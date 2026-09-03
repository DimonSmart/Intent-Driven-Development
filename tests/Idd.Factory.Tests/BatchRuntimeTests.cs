using System.Text.Json;
using Idd.Factory.Domain;

namespace Idd.Factory.Tests;

public sealed class BatchRuntimeTests
{
    [Fact]
    public async Task RuntimeExecutesWholeBatchThenPlansAgainAndFinalizesWithoutReview()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(_ => "# Task\n\nImplement A.\n\n# Task\n\nImplement B.");
        backend.Enqueue(_ => "Implemented A in the current product.");
        backend.Enqueue(_ => "Implemented B and preserved the surrounding behavior.");
        backend.Enqueue(_ => "# Done");

        var outcome = await FactoryRuntimeTestHarness.CreateRuntime(temp.Path, backend)
            .RunRequestAsync("Complete A and B.", "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal(["planning", "implementation", "implementation", "planning"], backend.Invocations.Select(x => x.Capability));
        Assert.All(backend.Invocations.Where(x => x.Capability == "implementation"), x => Assert.Equal("executor", x.Role));
        Assert.DoesNotContain(backend.Invocations, x => x.Capability.Contains("review", StringComparison.Ordinal));
        Assert.Equal("Implement A.", (await File.ReadAllTextAsync(Path.Combine(outcome.ResultDirectory!, "work-items", "W000001", "contract.md"))).Trim());
        Assert.Contains("Implemented B", await File.ReadAllTextAsync(Path.Combine(outcome.ResultDirectory!, "attempts", "A000003", "semantic-result.md")));
        using var completed = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outcome.ResultDirectory!, "completed-work.json")));
        Assert.False(completed.RootElement.GetProperty("completed")[0].TryGetProperty("capability", out _));
    }

    [Fact]
    public async Task ExecutorDiscoveryDoesNotInterruptBatchAndOnlyNextPlannerCreatesWork()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(_ => "# Task\n\nImplement A.\n\n# Task\n\nImplement B.");
        backend.Enqueue(_ => "Implemented A. Discovered additional-work-required is needed for C, but did not create it.");
        backend.Enqueue(_ => "Implemented B against the latest repository state.");
        backend.Enqueue(_ => "# Task\n\nImplement C discovered by the previous batch.");
        backend.Enqueue(_ => "Implemented C.");
        backend.Enqueue(_ => "# Done");

        var outcome = await FactoryRuntimeTestHarness.CreateRuntime(temp.Path, backend)
            .RunRequestAsync("Complete the integrated change.", "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal(
            ["planning", "implementation", "implementation", "planning", "implementation", "planning"],
            backend.Invocations.Select(x => x.Capability));
        var contracts = Directory.GetFiles(Path.Combine(outcome.ResultDirectory!, "work-items"), "contract.md", SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.Ordinal)
            .Select(File.ReadAllText)
            .ToArray();
        Assert.Equal(3, contracts.Length);
        Assert.Contains("Implement C", contracts[2]);
    }

    [Fact]
    public async Task FailedFinalVerificationFeedsANewPlanningCycle()
    {
        using var temp = new TestWorkspace();
        var markerCheck = OperatingSystem.IsWindows()
            ? "if (Test-Path marker.txt) { exit 0 } else { exit 1 }"
            : "test -f marker.txt";
        temp.Write(".idd/verification.yaml", $$"""
            version: 1
            checks:
              final-check:
                run: {{markerCheck}}
            default:
              use: []
            final:
              use:
                - final-check
            """);
        var backend = new FakeAgentBackend();
        backend.Enqueue(_ => "# Done");
        backend.Enqueue(invocation =>
        {
            Assert.Contains("Strict final verification failed", invocation.Input);
            return "# Task\n\nCreate the missing marker required by integrated verification.";
        });
        backend.Enqueue(_ =>
        {
            File.WriteAllText(Path.Combine(temp.Path, "marker.txt"), "ready");
            return "Created the missing marker.";
        });
        backend.Enqueue(_ => "# Done");

        var outcome = await FactoryRuntimeTestHarness.CreateRuntime(temp.Path, backend)
            .RunRequestAsync("Produce a final-verifiable marker.", "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal(3, backend.Invocations.Count(x => x.Capability == "planning"));
        Assert.True(File.Exists(Path.Combine(temp.Path, "marker.txt")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\r\n  \r\n")]
    public async Task BlankPlannerOutputIsRejectedBeforeFinalVerification(string plannerOutput)
    {
        using var temp = new TestWorkspace();
        var finalCommand = OperatingSystem.IsWindows()
            ? "Set-Content -Path final-ran.txt -Value ran"
            : "touch final-ran.txt";
        temp.Write(".idd/verification.yaml", $$"""
            version: 1
            checks:
              final-sentinel:
                run: {{finalCommand}}
            default:
              use: []
            final:
              use:
                - final-sentinel
            """);
        var backend = new FakeAgentBackend();
        backend.Enqueue(_ => plannerOutput);

        var outcome = await FactoryRuntimeTestHarness.CreateRuntime(temp.Path, backend)
            .RunRequestAsync("Do not accept a missing planner conclusion.", "test", default);

        Assert.Equal("MALFORMED_PLANNER_OUTPUT", outcome.FactoryOutcome);
        Assert.False(File.Exists(Path.Combine(temp.Path, "final-ran.txt")));
        Assert.Single(backend.Invocations);
        Assert.Equal("planning", backend.Invocations[0].Capability);
        var state = await FactoryRuntimeTestHarness.LoadState(temp.Path);
        Assert.False(state.FinalVerificationPassed);
        Assert.Empty(state.Completed);
        Assert.Empty(state.Remaining);
    }

    [Fact]
    public async Task FailingRepositoryFallbackBaselineRequiresConfirmationBeforePlanning()
    {
        using var temp = new TestWorkspace();
        WriteRepositoryFallback(temp, 7);
        var backend = new FakeAgentBackend();

        var outcome = await FactoryRuntimeTestHarness.CreateRuntime(temp.Path, backend)
            .RunRequestAsync("Do not plan against a red repository without approval.", "test", default);

        Assert.Equal("VERIFICATION_CONFIRMATION_REQUIRED", outcome.FactoryOutcome);
        Assert.Contains("repository-fallback", outcome.Reason);
        Assert.Empty(backend.Invocations);
        var state = await FactoryRuntimeTestHarness.LoadState(temp.Path);
        Assert.False(state.RepositoryFallbackBaselineAccepted);
        Assert.Equal(FactoryRunStatus.Blocked, state.RunStatus);
        Assert.Equal("VERIFICATION_CONFIRMATION_REQUIRED", state.Blocker!.Code);
        Assert.NotNull(state.PendingContinuation);
        Assert.True(state.PendingContinuation!.IsResumable);
        Assert.Equal("baseline", state.PendingContinuation.VerificationContext);
        Assert.Equal(VerificationContinuationStage.AwaitingConfirmation, state.PendingContinuation.VerificationStage);
        Assert.Single(state.VerificationEvidenceRefs);
    }

    [Fact]
    public async Task AcceptedRepositoryFallbackBaselineDoesNotRetrySubtaskAndKeepsFinalStrict()
    {
        using var temp = new TestWorkspace();
        WriteRepositoryFallback(temp, 7);
        var backend = new FakeAgentBackend();
        backend.Enqueue(_ => "# Task\n\nImplement A.");
        backend.Enqueue(_ => "Implemented A without changing the known repository baseline.");
        backend.Enqueue(_ => "# Done");
        var runtime = FactoryRuntimeTestHarness.CreateRuntime(temp.Path, backend);

        var initial = await runtime.RunRequestAsync("Implement A in an already-red repository.", "test", default);
        Assert.Equal("VERIFICATION_CONFIRMATION_REQUIRED", initial.FactoryOutcome);

        var outcome = await runtime.ContinueAsync(default, VerificationConfirmation.Approve);

        Assert.Equal("FINAL_VERIFICATION_FAILED", outcome.FactoryOutcome);
        Assert.Equal(2, backend.Invocations.Count(x => x.Capability == "planning"));
        Assert.Single(backend.Invocations.Where(x => x.Capability == "implementation"));
        var state = await FactoryRuntimeTestHarness.LoadState(temp.Path);
        Assert.True(state.RepositoryFallbackBaselineAccepted);
        Assert.Single(state.Completed);
        Assert.Null(state.Current);
        Assert.Equal("final", state.PendingContinuation!.VerificationContext);
        Assert.Equal("FINAL_VERIFICATION_FAILED", state.Blocker!.Code);
    }

    [Fact]
    public async Task VerificationDrivenRetryWithoutWorkspaceChangesStopsImmediately()
    {
        using var temp = new TestWorkspace();
        temp.Write(".idd/verification.yaml", """
            version: 1
            checks:
              subtask-fail:
                run: exit 7
            default:
              use: []
            subtask:
              use:
                - subtask-fail
            final:
              use: []
            """);
        var backend = new FakeAgentBackend();
        backend.Enqueue(_ => "# Task\n\nImplement A.");
        backend.Enqueue(_ =>
        {
            File.WriteAllText(Path.Combine(temp.Path, "first-change.txt"), "changed");
            return "Initial implementation changed the workspace.";
        });
        backend.Enqueue(invocation =>
        {
            Assert.Contains("subtask-fail", invocation.Input);
            return "Retry found no additional workspace change to make.";
        });

        var outcome = await FactoryRuntimeTestHarness.CreateRuntime(temp.Path, backend)
            .RunRequestAsync("Implement A and verify it.", "test", default);

        Assert.Equal("VERIFICATION_RETRY_NO_PROGRESS", outcome.FactoryOutcome);
        Assert.Equal(2, backend.Invocations.Count(x => x.Capability == "implementation"));
        var state = await FactoryRuntimeTestHarness.LoadState(temp.Path);
        Assert.NotNull(state.Current);
        Assert.Equal(2, state.Current!.AttemptCount);
        Assert.Contains("first-change.txt", state.Current.ChangedPaths);
        Assert.Equal(CurrentWorkPhase.Blocked, state.CurrentPhase);
        Assert.Equal("VERIFICATION_RETRY_NO_PROGRESS", state.Blocker!.Code);
        Assert.NotNull(state.PendingContinuation);
        Assert.False(state.PendingContinuation!.IsResumable);
        Assert.Single(state.Current.VerificationEvidenceRefs);

        var retryAttempt = backend.Invocations.Last(x => x.Capability == "implementation").AttemptId;
        using var changes = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(temp.Path, ".idd", "factory", "current", "attempts", retryAttempt, "workspace-changes.json")));
        Assert.Equal(0, changes.RootElement.GetProperty("changedPaths").GetArrayLength());
    }

    [Fact]
    public async Task ExistingVerificationPolicySkipsRepositoryFallbackBaseline()
    {
        using var temp = new TestWorkspace();
        WriteRepositoryFallback(temp, 7);
        temp.Write(".idd/verification.yaml", "version: 1\nchecks: {}\ndefault:\n  use: []\n");
        var backend = new FakeAgentBackend();
        backend.Enqueue(_ => "# Done");

        var outcome = await FactoryRuntimeTestHarness.CreateRuntime(temp.Path, backend)
            .RunRequestAsync("Use the configured verification policy.", "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Single(backend.Invocations);
        Assert.Equal("planning", backend.Invocations[0].Capability);
    }

    private static void WriteRepositoryFallback(TestWorkspace temp, int exitCode)
    {
        if (OperatingSystem.IsWindows())
        {
            temp.Write("scripts/Check.ps1", $"exit {exitCode}\n");
            return;
        }

        var path = temp.Write("scripts/check.sh", $"#!/bin/sh\nexit {exitCode}\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
