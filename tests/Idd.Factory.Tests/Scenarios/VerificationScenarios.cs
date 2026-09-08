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
        result.ShouldHaveContinuation(resumable: true, context: "final");
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
        var starts = 0;
        var hooks = new VerificationRuntimeHooks
        {
            StartProcess = info => starts++ == 0
                ? throw new System.ComponentModel.Win32Exception("simulated infrastructure failure")
                : System.Diagnostics.Process.Start(info)
        };
        scenario.WithVerification(VerificationPolicyFixture.SingleCheck("infrastructure-check", "exit 0", "subtask"))
            .WithVerification(new VerificationEngine(
                scenario.WorkspacePath,
                Path.Combine(scenario.WorkspacePath, ".idd", "factory", "current"),
                hooks))
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
        var hooks = new VerificationRuntimeHooks
        {
            StartProcess = _ => throw new System.ComponentModel.Win32Exception("simulated infrastructure failure")
        };
        scenario.WithRepositoryFallback(0)
            .WithVerification(new VerificationEngine(
                scenario.WorkspacePath,
                Path.Combine(scenario.WorkspacePath, ".idd", "factory", "current"),
                hooks));

        var result = await scenario.Run("Exercise baseline diagnostics.");

        result.ShouldBeBlockedBy("BASELINE_VERIFICATION_INFRASTRUCTURE_FAILURE");
        result.ShouldHaveNoAgentCalls();
        result.ShouldHaveContinuation(resumable: false);
        result.ShouldHaveEvidence("repository-fallback");
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
}
