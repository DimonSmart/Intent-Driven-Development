using System.Text.Json;
using Idd.Factory.Domain;

namespace Idd.Factory.Tests;

public sealed class ExternalBackendBlockerScenarios
{
    [Theory]
    [InlineData("You've hit your usage limit", "AGENT_CAPACITY_UNAVAILABLE")]
    [InlineData("HTTP 429 / Too Many Requests", "AGENT_RATE_LIMITED")]
    [InlineData("login required", "AGENT_AUTHENTICATION_REQUIRED")]
    public async Task ExecutorExternalFailureBlocksAndContinueReplaysSameLogicalAttempt(
        string diagnostic,
        string expectedCode)
    {
        const string task = "Implement A.";
        using var scenario = FactoryScenario.Create()
            .Plan(task)
            .CommandFailure(AgentTerminationKind.TransportFailure, diagnostic)
            .Execute(task)
            .Done();

        var blocked = await scenario.Run();

        blocked.ShouldBeBlockedBy(expectedCode);
        blocked.ShouldHaveContinuation(resumable: true);
        Assert.Equal(SemanticOperationKind.WorkItemExecution, blocked.State.PendingContinuation!.Operation);
        Assert.Equal("W000001", blocked.State.PendingContinuation.WorkItemId);
        Assert.NotNull(blocked.State.PendingContinuation.OperationInput);
        Assert.Contains("factory_continue", blocked.Outcome.ResumeWhen, StringComparison.Ordinal);
        Assert.Equal(0, blocked.State.Current!.SemanticAttemptCount);
        Assert.Equal(0, blocked.State.Current.TechnicalRestartCount);
        Assert.Empty(blocked.State.Current.PriorTechnicalFailures);

        var first = Assert.Single(WorkItemInvocations(blocked));
        Assert.Equal(1, first.SemanticAttemptNumber);
        Assert.Equal(0, first.TechnicalRestartNumber);
        var payload = blocked.Outcome.Payload!.Value;
        Assert.Equal(first.AttemptId, payload.GetProperty("attemptId").GetString());
        Assert.Equal(expectedCode, payload.GetProperty("failureCode").GetString());
        var diagnosticReference = payload.GetProperty("diagnosticReference").GetString()!;
        Assert.Equal($"attempts/{first.AttemptId}/failure-diagnostic.json", diagnosticReference);
        Assert.True(File.Exists(Path.Combine(
            blocked.RunDirectory,
            diagnosticReference.Replace('/', Path.DirectorySeparatorChar))));
        Assert.DoesNotContain(
            "technical-restart-scheduled",
            File.ReadAllText(Path.Combine(blocked.RunDirectory, "events.jsonl")),
            StringComparison.Ordinal);

        var completed = await scenario.Continue();

        completed.ShouldComplete();
        var invocations = WorkItemInvocations(completed);
        Assert.Equal(2, invocations.Length);
        Assert.NotEqual(invocations[0].AttemptId, invocations[1].AttemptId);
        Assert.Equal(invocations[0].SemanticAttemptNumber, invocations[1].SemanticAttemptNumber);
        Assert.Equal(invocations[0].TechnicalRestartNumber, invocations[1].TechnicalRestartNumber);
        Assert.Equal(invocations[0].InvocationKind, invocations[1].InvocationKind);
    }

    [Fact]
    public async Task RepeatedExternalBlockersDoNotConsumeRetryBudgets()
    {
        const string task = "Implement A.";
        using var scenario = FactoryScenario.Create()
            .WithLimits(maxAttemptsPerTask: 1, maxTechnicalRestartsPerTask: 1)
            .Plan(task)
            .CommandFailure(AgentTerminationKind.TransportFailure, "quota exhausted")
            .CommandFailure(AgentTerminationKind.TransportFailure, "quota exhausted")
            .Execute(task)
            .Done();

        var firstBlock = await scenario.Run();
        firstBlock.ShouldBeBlockedBy("AGENT_CAPACITY_UNAVAILABLE");
        var secondBlock = await scenario.Continue();
        secondBlock.ShouldBeBlockedBy("AGENT_CAPACITY_UNAVAILABLE");
        Assert.Equal(0, secondBlock.State.Current!.SemanticAttemptCount);
        Assert.Equal(0, secondBlock.State.Current.TechnicalRestartCount);

        var completed = await scenario.Continue();

        completed.ShouldComplete();
        var invocations = WorkItemInvocations(completed);
        Assert.Equal(3, invocations.Length);
        Assert.Equal(3, invocations.Select(x => x.AttemptId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(invocations, x => Assert.Equal(1, x.SemanticAttemptNumber));
        Assert.All(invocations, x => Assert.Equal(0, x.TechnicalRestartNumber));
    }

    [Fact]
    public async Task ExternalBlockerDuringTechnicalRestartReplaysSameRestartNumber()
    {
        const string task = "Implement A.";
        using var scenario = FactoryScenario.Create()
            .WithLimits(maxAttemptsPerTask: 1, maxTechnicalRestartsPerTask: 1)
            .Plan(task)
            .CommandFailure(AgentTerminationKind.CommandTimeout, "executor timed out")
            .CommandFailure(AgentTerminationKind.TransportFailure, "quota exhausted")
            .Execute(task)
            .Done();

        var blocked = await scenario.Run();

        blocked.ShouldBeBlockedBy("AGENT_CAPACITY_UNAVAILABLE");
        Assert.Equal(1, blocked.State.Current!.SemanticAttemptCount);
        Assert.Equal(0, blocked.State.Current.TechnicalRestartCount);
        Assert.Equal(WorkItemInvocationKind.TechnicalRestart, blocked.State.Current.NextInvocationKind);
        var beforeContinue = WorkItemInvocations(blocked);
        Assert.Equal(2, beforeContinue.Length);
        Assert.Equal(1, beforeContinue[1].TechnicalRestartNumber);

        var completed = await scenario.Continue();

        completed.ShouldComplete();
        var invocations = WorkItemInvocations(completed);
        Assert.Equal(3, invocations.Length);
        Assert.Equal(1, invocations[2].SemanticAttemptNumber);
        Assert.Equal(1, invocations[2].TechnicalRestartNumber);
        Assert.Equal(WorkItemInvocationKind.TechnicalRestart, invocations[2].InvocationKind);
    }

    [Fact]
    public async Task ExternalBlockerDuringSemanticRetryReplaysSameSemanticAttemptNumber()
    {
        const string task = "Implement A.";
        const string retryProgressPath = "semantic-retry-recovered.txt";
        var verificationCommand = OperatingSystem.IsWindows()
            ? $"if (Test-Path {retryProgressPath}) {{ exit 0 }} else {{ exit 7 }}"
            : $"test -f {retryProgressPath}";
        using var scenario = FactoryScenario.Create();
        scenario.WithVerificationCheck("semantic-retry-external", verificationCommand)
            .Plan(task)
            .Execute(task, invocation =>
            {
                File.WriteAllText(Path.Combine(invocation.Workspace, "initial-change.txt"), "changed");
                return "Initial implementation changed the workspace.";
            })
            .CommandFailure(AgentTerminationKind.TransportFailure, "rate limit exceeded")
            .Execute(task, invocation =>
            {
                Assert.Equal(WorkItemInvocationKind.SemanticRetry, invocation.InvocationKind);
                Assert.Equal(2, invocation.SemanticAttemptNumber);
                Assert.Equal(0, invocation.TechnicalRestartNumber);
                File.WriteAllText(Path.Combine(invocation.Workspace, retryProgressPath), "ready");
                return "Semantic retry resumed after the external blocker.";
            })
            .Done();

        var blocked = await scenario.Run("Implement A and verify it.");

        blocked.ShouldBeBlockedBy("AGENT_RATE_LIMITED");
        Assert.Equal(1, blocked.State.Current!.SemanticAttemptCount);
        Assert.Equal(0, blocked.State.Current.TechnicalRestartCount);
        Assert.Equal(WorkItemInvocationKind.SemanticRetry, blocked.State.Current.NextInvocationKind);
        var beforeContinue = WorkItemInvocations(blocked);
        Assert.Equal(2, beforeContinue.Length);
        Assert.Equal(2, beforeContinue[1].SemanticAttemptNumber);
        Assert.Equal(WorkItemInvocationKind.SemanticRetry, beforeContinue[1].InvocationKind);

        var completed = await scenario.Continue();

        completed.ShouldComplete();
        var invocations = WorkItemInvocations(completed);
        Assert.Equal(3, invocations.Length);
        Assert.Equal(2, invocations[2].SemanticAttemptNumber);
        Assert.Equal(0, invocations[2].TechnicalRestartNumber);
        Assert.Equal(WorkItemInvocationKind.SemanticRetry, invocations[2].InvocationKind);
    }

    [Fact]
    public async Task PlannerExternalFailureBlocksWithoutConsumingPlanningCycle()
    {
        const string task = "Implement A.";
        using var scenario = FactoryScenario.Create()
            .CommandFailure(AgentTerminationKind.TransportFailure, "You've hit your usage limit")
            .Plan(task)
            .Execute(task)
            .Done();

        var blocked = await scenario.Run();

        blocked.ShouldBeBlockedBy("AGENT_CAPACITY_UNAVAILABLE");
        Assert.Equal(0, blocked.State.PlanningCycleCount);
        Assert.Null(blocked.State.Current);
        Assert.Equal(SemanticOperationKind.Planning, blocked.State.PendingContinuation!.Operation);
        var failedPlanning = Assert.Single(blocked.Invocations);
        var failedInput = failedPlanning.Input;

        var completed = await scenario.Continue();

        completed.ShouldComplete();
        var planningInvocations = completed.Invocations
            .Where(x => x.Capability == "planning")
            .ToArray();
        Assert.Equal(3, planningInvocations.Length);
        Assert.NotEqual(planningInvocations[0].AttemptId, planningInvocations[1].AttemptId);
        Assert.Equal(failedInput, planningInvocations[1].Input);
    }

    [Fact]
    public async Task PartialWorkspaceChangesSurviveExternalBlocker()
    {
        const string task = "Implement A.";
        const string changedPath = "src/partial-external.txt";
        using var scenario = FactoryScenario.Create()
            .Plan(task)
            .CommandFailure(
                AgentTerminationKind.TransportFailure,
                "quota exhausted",
                invocation =>
                {
                    var path = Path.Combine(
                        invocation.Workspace,
                        changedPath.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, "partial work");
                })
            .Execute(task, invocation =>
            {
                Assert.True(File.Exists(Path.Combine(
                    invocation.Workspace,
                    changedPath.Replace('/', Path.DirectorySeparatorChar))));
                return "Completed from current workspace.";
            })
            .Done();

        var blocked = await scenario.Run();

        blocked.ShouldBeBlockedBy("AGENT_CAPACITY_UNAVAILABLE");
        Assert.Contains(changedPath, blocked.State.Current!.ChangedPaths);
        Assert.Contains(changedPath, blocked.State.FactoryRunChangedPaths);

        var completed = await scenario.Continue();
        completed.ShouldComplete();
        Assert.Contains(changedPath, completed.State.Completed.Single().ChangedPaths);
    }

    [Fact]
    public async Task ExternalBlockerEventIsExplicitAndDoesNotScheduleTechnicalRestart()
    {
        using var scenario = FactoryScenario.Create()
            .Plan("Implement A.")
            .CommandFailure(AgentTerminationKind.TransportFailure, "quota exhausted");

        var blocked = await scenario.Run();
        blocked.ShouldBeBlockedBy("AGENT_CAPACITY_UNAVAILABLE");

        var eventTypes = File.ReadAllLines(Path.Combine(blocked.RunDirectory, "events.jsonl"))
            .Select(line => JsonDocument.Parse(line))
            .Select(document => document.RootElement.GetProperty("type").GetString())
            .ToArray();
        Assert.Contains("agent-external-blocker", eventTypes);
        Assert.DoesNotContain("technical-restart-scheduled", eventTypes);
    }

    private static AgentInvocation[] WorkItemInvocations(ScenarioResult result) =>
        result.Invocations
            .Where(x => x.WorkItemId == "W000001")
            .ToArray();
}
