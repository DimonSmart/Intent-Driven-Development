using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Runtime;
using Idd.Factory.Verification;

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

    [Theory]
    [InlineData(AgentTerminationKind.CommandTimeout)]
    [InlineData(AgentTerminationKind.IncompleteCommand)]
    public async Task ShellCommandFailureRetriesTheSameTaskWithDiagnosticBeforeContinuingBatch(AgentTerminationKind terminationKind)
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(_ => "# Task\n\nImplement A.\n\n# Task\n\nImplement B.");
        backend.EnqueueCommandFailure(terminationKind, "item_3 dotnet test did not complete");
        backend.Enqueue(invocation =>
        {
            Assert.Equal("W000001", invocation.WorkItemId);
            Assert.Contains("results are partial and must not be trusted", invocation.Input, StringComparison.Ordinal);
            Assert.Contains("item_3 dotnet test did not complete", invocation.Input, StringComparison.Ordinal);
            return "Diagnosed the hang and completed A.";
        });
        backend.Enqueue(_ => "Implemented B only after A completed.");
        backend.Enqueue(_ => "# Done");

        var outcome = await FactoryRuntimeTestHarness.CreateRuntime(temp.Path, backend)
            .RunRequestAsync("Complete A and B without trusting hung checks.", "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal(["W000001", "W000001", "W000002"],
            backend.Invocations.Where(invocation => invocation.Capability == "implementation").Select(invocation => invocation.WorkItemId));
        using var completed = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outcome.ResultDirectory!, "completed-work.json")));
        Assert.Equal(2, completed.RootElement.GetProperty("completed").GetArrayLength());
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
        Assert.Single(backend.Invocations, x => x.Capability == "implementation");
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
    public async Task InfrastructureDiagnosticPersistsReplaysAndResumesWithoutLosingEvidence()
    {
        using var temp = new TestWorkspace();
        var command = OperatingSystem.IsWindows()
            ? "if (Test-Path infra-ready.txt) { exit 0 }; Write-Output ('x' * 20000); [Console]::Error.WriteLine(('y' * 20000)); Start-Sleep -Seconds 10"
            : "if test -f infra-ready.txt; then exit 0; fi; yes x | head -c 20000; yes y | head -c 20000 >&2; sleep 10";
        temp.Write(".idd/verification.yaml", $$"""
            version: 1
            checks:
              infrastructure-check:
                run: {{command}}
                timeout: 3s
            default:
              use: []
            subtask:
              use:
                - infrastructure-check
            final:
              use: []
            """);
        var backend = new FakeAgentBackend();
        backend.Enqueue(_ => "# Task\n\nImplement the product change.");
        backend.Enqueue(_ =>
        {
            File.WriteAllText(Path.Combine(temp.Path, "product.txt"), "changed");
            return "Implemented the product change.";
        });
        backend.Enqueue(_ => "# Done");
        var runtime = FactoryRuntimeTestHarness.CreateRuntime(temp.Path, backend);

        var blocked = await runtime.RunRequestAsync("Exercise resumable verification diagnostics.", "test", default);

        Assert.Equal("VERIFICATION_INFRASTRUCTURE_FAILURE", blocked.FactoryOutcome);
        Assert.NotNull(blocked.Payload);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(blocked.Payload.Value.GetRawText()) <= 16 * 1024);
        Assert.Equal("infrastructure-check", blocked.Payload.Value.GetProperty("primaryCheckId").GetString());
        var check = Assert.Single(blocked.Payload.Value.GetProperty("checks").EnumerateArray());
        Assert.Equal("timeout", check.GetProperty("failureKind").GetString());
        Assert.Equal("execute", check.GetProperty("failureStage").GetString());
        Assert.Contains("infrastructure-check", check.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Contains("3 seconds", check.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.False(check.TryGetProperty("stage", out _));
        Assert.Equal(".idd/factory/current/verification/" + check.GetProperty("evidenceId").GetString() + ".json", check.GetProperty("evidencePath").GetString());
        Assert.StartsWith(".idd/factory/current/verification/", check.GetProperty("stdout").GetProperty("path").GetString(), StringComparison.Ordinal);
        Assert.StartsWith(".idd/factory/current/verification/", check.GetProperty("stderr").GetProperty("path").GetString(), StringComparison.Ordinal);
        Assert.True(check.GetProperty("stdout").GetProperty("truncated").GetBoolean());
        Assert.True(check.GetProperty("stderr").GetProperty("truncated").GetBoolean());
        var termination = check.GetProperty("termination");
        Assert.True(termination.GetProperty("requested").GetBoolean());
        Assert.True(termination.GetProperty("entireProcessTree").GetBoolean());
        Assert.True(termination.GetProperty("succeeded").GetBoolean());
        Assert.False(termination.TryGetProperty("attempted", out _));
        Assert.False(termination.TryGetProperty("gracePeriodExpired", out _));
        Assert.Contains("infrastructure-check", blocked.Reason, StringComparison.Ordinal);
        Assert.Contains("3 seconds", blocked.Reason, StringComparison.Ordinal);
        Assert.Contains("infrastructure-check", blocked.ResumeWhen, StringComparison.Ordinal);
        Assert.Contains("3 seconds", blocked.ResumeWhen, StringComparison.Ordinal);
        Assert.False(blocked.ResumeWhen!.Contains("restart", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("factory_continue", blocked.ResumeWhen, StringComparison.Ordinal);
        var state = await FactoryRuntimeTestHarness.LoadState(temp.Path);
        Assert.Equal(JsonSerializer.Serialize(blocked.Payload.Value), JsonSerializer.Serialize(state.Blocker!.Payload!.Value));
        Assert.True(state.PendingContinuation!.IsResumable);
        Assert.Single(state.Current!.VerificationEvidenceRefs);
        var status = await new FactoryStatusReader().ReadAsync(temp.Path, default);
        Assert.Equal(JsonSerializer.Serialize(blocked.Payload.Value), JsonSerializer.Serialize(status.Payload!.Value));

        File.WriteAllText(Path.Combine(temp.Path, "infra-ready.txt"), "ready");
        var completed = await runtime.ContinueAsync(default);

        Assert.Equal("COMPLETED", completed.FactoryOutcome);
        var finalState = JsonSerializer.Deserialize<FactoryState>(
            await File.ReadAllTextAsync(Path.Combine(completed.ResultDirectory!, "state.json")), FactoryJson.Options)!;
        Assert.Equal(2, Assert.Single(finalState.Completed).VerificationEvidenceRefs.Count);
    }

    [Fact]
    public void InfrastructureDiagnosticRepresentsMultipleEntriesAndBoundsMetadataAndTails()
    {
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var records = Enumerable.Range(0, 8).Select(index => new VerificationEvidence
        {
            SchemaVersion = 3,
            CheckId = $"check-{index}",
            EvidenceId = $"V{index:D35}",
            Status = "infrastructure-failure",
            StartedAt = now,
            FinishedAt = now.AddSeconds(1),
            DurationMilliseconds = 1000,
            TimeoutMilliseconds = 500,
            TimedOut = true,
            PrimaryFailure = new("timeout", "execute", new string('m', 4000)),
            Stdout = new($".idd/factory/current/verification/V{index:D35}.stdout.log", 20000, new string('o', 5000), true),
            Stderr = new($"verification/V{index:D35}.stderr.log", 20000, new string('e', 5000), true),
            EvidencePersisted = index != 7
        }).ToArray();

        var diagnostic = FactoryRuntime.CreateInfrastructureDiagnostic("VERIFICATION_INFRASTRUCTURE_FAILURE", "subtask", "W000001", records);
        var payload = FactoryRuntime.SerializeBoundedDiagnostic(diagnostic);

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(payload.GetRawText()) <= 16 * 1024);
        var checks = payload.GetProperty("checks").EnumerateArray().ToArray();
        Assert.Equal(records.Length, checks.Length);
        Assert.All(checks, check =>
        {
            Assert.Equal("execute", check.GetProperty("failureStage").GetString());
            Assert.True(check.GetProperty("stdout").GetProperty("truncated").GetBoolean());
            Assert.True(check.GetProperty("stderr").GetProperty("truncated").GetBoolean());
            Assert.StartsWith(".idd/factory/current/verification/", check.GetProperty("stdout").GetProperty("path").GetString(), StringComparison.Ordinal);
            Assert.StartsWith(".idd/factory/current/verification/", check.GetProperty("stderr").GetProperty("path").GetString(), StringComparison.Ordinal);
        });
        Assert.Equal(JsonValueKind.Null, checks[^1].GetProperty("evidencePath").ValueKind);
    }

    [Fact]
    public void InfrastructureDiagnosticCompactsAdversarialMetadataAndRetainsPrimaryWithoutThrowing()
    {
        var huge = string.Concat(Enumerable.Repeat("metadata-😀-", 5000));
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var records = Enumerable.Range(0, 200).Select(index => new VerificationEvidence
        {
            SchemaVersion = 3, CheckId = index == 0 ? "primary-" + huge : $"secondary-{index}-" + huge,
            EvidenceId = $"evidence-{index}-" + huge, Status = "infrastructure-failure", StartedAt = now, FinishedAt = now,
            PrimaryFailure = new(index == 0 ? "timeout" : "unknown", index == 0 ? "execute" : "capture-output", huge),
            TimeoutMilliseconds = 1234, TimedOut = index == 0,
            Stdout = new("verification/" + huge + ".stdout.log", 100000, huge, false),
            Stderr = new("verification/" + huge + ".stderr.log", 100000, huge, false),
            Termination = new(true, true, false, new("HugeException" + huge, huge, huge)),
            EvidencePersisted = true
        }).ToArray();

        var diagnostic = FactoryRuntime.CreateInfrastructureDiagnostic(huge, huge, huge, records);
        var payload = FactoryRuntime.SerializeBoundedDiagnostic(diagnostic);

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(payload.GetRawText()) <= 16 * 1024);
        Assert.True(payload.GetProperty("metadataTruncated").GetBoolean());
        Assert.True(payload.GetProperty("omittedCheckCount").GetInt32() > 0);
        var primary = payload.GetProperty("checks")[0];
        Assert.Equal("timeout", primary.GetProperty("failureKind").GetString());
        Assert.Equal("execute", primary.GetProperty("failureStage").GetString());
        Assert.True(primary.GetProperty("stdout").GetProperty("truncated").GetBoolean());
        Assert.True(primary.GetProperty("stderr").GetProperty("truncated").GetBoolean());
        var termination = primary.GetProperty("termination");
        Assert.True(termination.GetProperty("requested").GetBoolean());
        Assert.True(termination.GetProperty("entireProcessTree").GetBoolean());
        Assert.False(termination.GetProperty("succeeded").GetBoolean());
        Assert.True(termination.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task BaselineInfrastructureDiagnosticIsTerminalAndUsesCanonicalPayload()
    {
        using var temp = new TestWorkspace();
        var evidence = new VerificationEvidence
        {
            SchemaVersion = 3, CheckId = "repository-fallback", EvidenceId = "V00000000000000000000000000000000000",
            Status = "infrastructure-failure", StartedAt = DateTimeOffset.UtcNow, FinishedAt = DateTimeOffset.UtcNow,
            PrimaryFailure = new("process-start-failure", "start", "Shell could not start."), EvidencePersisted = false
        };
        var backend = new FakeAgentBackend();
        var runtime = FactoryRuntimeTestHarness.CreateRuntime(temp.Path, backend, verification: new BaselineFailureVerification(temp.Path, evidence));

        var outcome = await runtime.RunRequestAsync("Exercise baseline diagnostics.", "test", default);

        Assert.Equal("BASELINE_VERIFICATION_INFRASTRUCTURE_FAILURE", outcome.FactoryOutcome);
        Assert.Contains("cancel/restart", outcome.ResumeWhen, StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, Assert.Single(outcome.Payload!.Value.GetProperty("checks").EnumerateArray()).GetProperty("evidencePath").ValueKind);
        var state = await FactoryRuntimeTestHarness.LoadState(temp.Path);
        Assert.False(state.PendingContinuation!.IsResumable);
        Assert.Equal(JsonSerializer.Serialize(outcome.Payload), JsonSerializer.Serialize(state.Blocker!.Payload));
        var status = await new FactoryStatusReader().ReadAsync(temp.Path, default);
        Assert.Equal(JsonSerializer.Serialize(outcome.Payload), JsonSerializer.Serialize(status.Payload));
    }

    private sealed class BaselineFailureVerification(string workspace, VerificationEvidence evidence)
        : VerificationEngine(workspace, Path.Combine(workspace, ".idd", "factory", "current"))
    {
        public override Task<VerificationResult> RunContextAsync(string context, CancellationToken cancellationToken) =>
            Task.FromResult(new VerificationResult(VerificationStatus.InfrastructureFailure, [evidence]));
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
