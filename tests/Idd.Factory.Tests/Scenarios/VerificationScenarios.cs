using Idd.Factory.Domain;
using Idd.Factory.Verification;

namespace Idd.Factory.Tests;

public sealed class VerificationScenarios
{
    [Fact]
    public async Task FailedFinalVerificationStartsAnotherPlanningCycle()
    {
        var markerCheck = OperatingSystem.IsWindows()
            ? "if (Test-Path marker.txt) { exit 0 } else { exit 1 }"
            : "test -f marker.txt";
        const string task = "Create the missing marker required by integrated verification.";
        using var scenario = FactoryScenario.Create();
        scenario.WithVerificationCheck("final-check", markerCheck, "final")
            .Done()
            .Planner(invocation =>
            {
                Assert.Contains("Strict final verification failed", invocation.Input, StringComparison.Ordinal);
                return $"# Task\n\n{task}";
            })
            .Execute(task, _ =>
            {
                File.WriteAllText(Path.Combine(scenario.WorkspacePath, "marker.txt"), "ready");
                return "Created the missing marker.";
            })
            .Done();

        var result = await scenario.Run("Produce a final-verifiable marker.");

        result.ShouldComplete();
        result.ShouldExecute(task);
        result.ShouldHavePlanningCycles(3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\r\n  \r\n")]
    public async Task BlankPlannerOutputStopsBeforeFinalVerification(string plannerOutput)
    {
        var finalCommand = OperatingSystem.IsWindows()
            ? "Set-Content -Path final-ran.txt -Value ran"
            : "touch final-ran.txt";
        using var scenario = FactoryScenario.Create()
            .WithVerificationCheck("final-sentinel", finalCommand, "final")
            .Planner(plannerOutput);

        var result = await scenario.Run("Do not accept a missing planner conclusion.");

        result.ShouldStopWith("MALFORMED_PLANNER_OUTPUT");
        result.ShouldHavePlanningCycles(1);
        Assert.False(File.Exists(Path.Combine(scenario.WorkspacePath, "final-ran.txt")));
        Assert.False(result.State.FinalVerificationPassed);
    }

    [Fact]
    public async Task FailingRepositoryFallbackRequiresConfirmationBeforePlanning()
    {
        using var scenario = FactoryScenario.Create().WithRepositoryFallback(7);

        var result = await scenario.Run("Do not plan against a red repository without approval.");

        result.ShouldBeBlockedBy("VERIFICATION_CONFIRMATION_REQUIRED");
        result.ShouldHaveNoAgentCalls();
        result.ShouldHaveContinuation(resumable: true, context: "baseline");
        result.ShouldHaveEvidence("repository-fallback");
        Assert.False(result.State.RepositoryFallbackBaselineAccepted);
    }

    [Fact]
    public async Task AcceptedRedBaselineKeepsFinalVerificationStrict()
    {
        const string task = "Implement A.";
        using var scenario = FactoryScenario.Create()
            .WithRepositoryFallback(7)
            .Plan(task)
            .Execute(task)
            .Done();

        var initial = await scenario.Run("Implement A in an already-red repository.");
        initial.ShouldBeBlockedBy("VERIFICATION_CONFIRMATION_REQUIRED");

        var result = await scenario.Continue(VerificationConfirmation.Approve);

        result.ShouldBeBlockedBy("FINAL_VERIFICATION_FAILED");
        result.ShouldExecute(task);
        Assert.True(result.State.RepositoryFallbackBaselineAccepted);
        Assert.Null(result.State.Current);
        result.ShouldHaveContinuation(resumable: false, context: "final");
    }

    [Fact]
    public async Task VerificationRetryWithoutWorkspaceChangesStopsImmediately()
    {
        const string task = "Implement A.";
        using var scenario = FactoryScenario.Create();
        scenario.WithVerificationCheck("subtask-fail", "exit 7")
            .Plan(task)
            .Execute(task, _ =>
            {
                File.WriteAllText(Path.Combine(scenario.WorkspacePath, "first-change.txt"), "changed");
                return "Initial implementation changed the workspace.";
            })
            .Execute(task, invocation =>
            {
                Assert.Contains("subtask-fail", invocation.Input, StringComparison.Ordinal);
                return "Retry found no additional workspace change to make.";
            });

        var result = await scenario.Run("Implement A and verify it.");

        result.ShouldBeBlockedBy("VERIFICATION_RETRY_NO_PROGRESS");
        result.ShouldHaveAttemptCount("W000001", 2);
        result.ShouldHaveEvidence("subtask-fail");
        result.ShouldHaveContinuation(resumable: false);
        Assert.Contains("first-change.txt", result.State.Current!.ChangedPaths);
    }

    [Fact]
    public async Task VerificationInfrastructureFailureIsResumableAndKeepsEvidence()
    {
        const string task = "Implement the product change.";
        using var scenario = FactoryScenario.Create();
        var verification = new ResumableInfrastructureVerification(scenario.WorkspacePath);
        scenario.WithVerification(verification)
            .Plan(task)
            .Execute(task, _ =>
            {
                File.WriteAllText(Path.Combine(scenario.WorkspacePath, "product.txt"), "changed");
                return "Implemented the product change.";
            })
            .Done();

        var blocked = await scenario.Run("Exercise resumable verification diagnostics.");
        blocked.ShouldBeBlockedBy("VERIFICATION_INFRASTRUCTURE_FAILURE");
        blocked.ShouldHaveContinuation(resumable: true, context: "subtask");
        blocked.ShouldHaveEvidence("infrastructure-check");

        var completed = await scenario.Continue();
        completed.ShouldComplete();
        completed.ShouldExecute(task);
    }

    [Fact]
    public async Task BaselineInfrastructureFailureIsTerminal()
    {
        using var scenario = FactoryScenario.Create();
        scenario.WithVerification(new BaselineInfrastructureVerification(scenario.WorkspacePath));

        var result = await scenario.Run("Exercise baseline diagnostics.");

        result.ShouldBeBlockedBy("BASELINE_VERIFICATION_INFRASTRUCTURE_FAILURE");
        result.ShouldHaveNoAgentCalls();
        result.ShouldHaveContinuation(resumable: false);
        Assert.Contains("No verification evidence JSON was persisted", result.Outcome.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfiguredPolicySkipsRepositoryFallbackBaseline()
    {
        using var scenario = FactoryScenario.Create()
            .WithRepositoryFallback(7)
            .WithVerification(VerificationPolicyFixture.Empty())
            .Done();

        var result = await scenario.Run("Use the configured verification policy.");

        result.ShouldComplete();
        result.ShouldHavePlanningCycles(1);
    }

    private sealed class ResumableInfrastructureVerification(string workspace)
        : VerificationEngine(workspace, Path.Combine(workspace, ".idd", "factory", "current"))
    {
        private bool subtaskFailed;

        public override Task<VerificationResult> RunContextAsync(string context, CancellationToken cancellationToken) =>
            Task.FromResult(new VerificationResult(VerificationStatus.NoChecks, []));

        public override Task<VerificationResult> RunContextAsync(
            string context,
            IEnumerable<string> changedPaths,
            CancellationToken cancellationToken)
        {
            if (context != "subtask" || subtaskFailed)
                return Task.FromResult(new VerificationResult(VerificationStatus.NoChecks, []));

            subtaskFailed = true;
            var evidence = InfrastructureEvidence("infrastructure-check", persisted: true);
            var directory = Path.Combine(workspace, ".idd", "factory", "current", "verification");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, evidence.EvidenceId + ".json"),
                System.Text.Json.JsonSerializer.Serialize(evidence, FactoryJson.Options));
            return Task.FromResult(new VerificationResult(VerificationStatus.InfrastructureFailure, [evidence]));
        }
    }

    private sealed class BaselineInfrastructureVerification(string workspace)
        : VerificationEngine(workspace, Path.Combine(workspace, ".idd", "factory", "current"))
    {
        public override Task<VerificationResult> RunContextAsync(string context, CancellationToken cancellationToken) =>
            Task.FromResult(new VerificationResult(
                VerificationStatus.InfrastructureFailure,
                [InfrastructureEvidence("repository-fallback", persisted: false)]));
    }

    private static VerificationEvidence InfrastructureEvidence(string checkId, bool persisted) => new()
    {
        SchemaVersion = 3,
        EvidenceId = "V-test-infrastructure-failure",
        CheckId = checkId,
        CheckDefinitionHash = "test",
        StartedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        FinishedAt = DateTimeOffset.Parse("2026-01-01T00:00:01Z"),
        Status = "infrastructure-failure",
        PrimaryFailure = new("process-start-failure", "start", "Shell could not start."),
        EvidencePersisted = persisted
    };
}
