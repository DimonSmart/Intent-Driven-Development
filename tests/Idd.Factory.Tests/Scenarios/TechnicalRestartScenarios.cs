using System.Text.Json;
using Idd.Factory.Domain;

namespace Idd.Factory.Tests;

public sealed class TechnicalRestartScenarios
{
    [Fact]
    public async Task TechnicalRestartRunsWhenSemanticBudgetIsAlreadyFullyUsed()
    {
        const string task = "Implement A.";
        using var scenario = FactoryScenario.Create()
            .WithLimits(maxAttemptsPerTask: 1, maxTechnicalRestartsPerTask: 1)
            .Plan(task)
            .CommandFailure(AgentTerminationKind.CommandTimeout, "executor timed out")
            .Execute(task)
            .Done();

        var result = await scenario.Run();

        result.ShouldComplete();
        var invocations = WorkItemInvocations(result, "W000001");
        Assert.Equal(2, invocations.Length);
        Assert.All(invocations, x => Assert.Equal(1, x.SemanticAttemptNumber));
        Assert.Equal(0, invocations[0].TechnicalRestartNumber);
        Assert.Equal(1, invocations[1].TechnicalRestartNumber);
        Assert.Equal(WorkItemInvocationKind.TechnicalRestart, invocations[1].InvocationKind);
    }

    [Fact]
    public async Task TechnicalRestartBudgetExhaustionHasItsOwnTerminalBlocker()
    {
        const string task = "Implement A.";
        using var scenario = FactoryScenario.Create()
            .WithLimits(maxAttemptsPerTask: 4, maxTechnicalRestartsPerTask: 1)
            .Plan(task)
            .CommandFailure(AgentTerminationKind.CommandTimeout, "first timeout")
            .CommandFailure(AgentTerminationKind.CommandTimeout, "second timeout");

        var result = await scenario.Run();

        result.ShouldBeBlockedBy("TECHNICAL_RESTART_BUDGET_EXHAUSTED");
        Assert.Equal(1, result.State.Current!.SemanticAttemptCount);
        Assert.Equal(1, result.State.Current.TechnicalRestartCount);
        Assert.Equal(WorkItemInvocationKind.TechnicalRestart, result.State.Current.NextInvocationKind);
        Assert.Equal(2, result.State.Current.PriorTechnicalFailures.Count);
        Assert.Equal(2, WorkItemInvocations(result, "W000001").Length);
        Assert.NotNull(result.Outcome.Payload);
        var payload = result.Outcome.Payload!.Value;
        Assert.Equal("W000001", payload.GetProperty("workItemId").GetString());
        Assert.Equal("AGENT_COMMAND_TIMEOUT", payload.GetProperty("failureCode").GetString());
        Assert.Equal(1, payload.GetProperty("technicalRestartCount").GetInt32());
        Assert.Equal(1, payload.GetProperty("technicalRestartBudget").GetInt32());
        result.ShouldHaveContinuation(resumable: false);
    }

    [Fact]
    public async Task ZeroTechnicalRestartBudgetDisablesAutomaticRestart()
    {
        const string task = "Implement A.";
        using var scenario = FactoryScenario.Create()
            .WithLimits(maxTechnicalRestartsPerTask: 0)
            .Plan(task)
            .CommandFailure(AgentTerminationKind.IncompleteCommand, "command incomplete");

        var result = await scenario.Run();

        result.ShouldBeBlockedBy("TECHNICAL_RESTART_BUDGET_EXHAUSTED");
        Assert.Equal(1, result.State.Current!.SemanticAttemptCount);
        Assert.Equal(0, result.State.Current.TechnicalRestartCount);
        Assert.Single(WorkItemInvocations(result, "W000001"));
    }

    [Fact]
    public async Task TransportTerminationAfterTrustedCompleteResultDoesNotRestart()
    {
        const string task = "Implement A.";
        using var scenario = FactoryScenario.Create()
            .Plan(task)
            .TransportTerminationAfterResult(task)
            .Done();

        var result = await scenario.Run();

        result.ShouldComplete();
        var invocation = Assert.Single(WorkItemInvocations(result, "W000001"));
        Assert.Equal(WorkItemInvocationKind.Initial, invocation.InvocationKind);
        Assert.Equal(1, invocation.SemanticAttemptNumber);
        Assert.Equal(0, invocation.TechnicalRestartNumber);
    }

    [Fact]
    public async Task ArbitraryAgentProtocolFailureDoesNotBecomeTechnicalRestart()
    {
        const string task = "Implement A.";
        using var scenario = FactoryScenario.Create()
            .Plan(task)
            .MissingResult();

        var result = await scenario.Run();

        result.ShouldBeBlockedBy("MISSING_AGENT_RESULT");
        Assert.Equal(1, result.State.Current!.SemanticAttemptCount);
        Assert.Equal(0, result.State.Current.TechnicalRestartCount);
        Assert.Empty(result.State.Current.PriorTechnicalFailures);
        Assert.Single(WorkItemInvocations(result, "W000001"));
    }

    [Fact]
    public async Task FailedInvocationWorkspaceChangesSurviveAndAreAggregated()
    {
        const string task = "Implement A.";
        const string changedPath = "src/partial-change.txt";
        using var scenario = FactoryScenario.Create()
            .Plan(task)
            .CommandFailure(
                AgentTerminationKind.CommandTimeout,
                "timeout after editing",
                invocation =>
                {
                    var path = Path.Combine(invocation.Workspace, changedPath.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, "partial work");
                })
            .Execute(task, invocation =>
            {
                Assert.True(File.Exists(Path.Combine(
                    invocation.Workspace,
                    changedPath.Replace('/', Path.DirectorySeparatorChar))));
                return "Completed from the current workspace.";
            })
            .Done();

        var result = await scenario.Run();

        result.ShouldComplete();
        Assert.Contains(changedPath, result.State.Completed.Single().ChangedPaths);
        Assert.Contains(changedPath, result.State.FactoryRunChangedPaths);
    }

    [Fact]
    public async Task TechnicalFailureDiagnosticsAreBoundedAndNotTrustedAsPriorSemanticResult()
    {
        const string task = "Implement A.";
        using var scenario = FactoryScenario.Create()
            .Plan(task)
            .CommandFailure(AgentTerminationKind.TransportFailure, "transport broke before result")
            .Execute(task, invocation =>
            {
                Assert.Contains("AGENT_TRANSPORT_FAILURE", invocation.Input, StringComparison.Ordinal);
                Assert.Contains("Diagnostic:", invocation.Input, StringComparison.Ordinal);
                Assert.Contains("did not produce trusted semantic results", invocation.Input, StringComparison.Ordinal);
                return "Completed after technical restart.";
            })
            .Done();

        var result = await scenario.Run();

        result.ShouldComplete();
        var first = WorkItemInvocations(result, "W000001")[0];
        var diagnostic = $"attempts/{first.AttemptId}/stderr.log";
        Assert.True(File.Exists(Path.Combine(result.RunDirectory, diagnostic.Replace('/', Path.DirectorySeparatorChar))));
        Assert.DoesNotContain(
            $"attempts/{first.AttemptId}/semantic-result.md",
            result.State.Completed.Single().ResultRef ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TechnicalRestartEventStreamIsDistinctFromSemanticRetry()
    {
        const string task = "Implement A.";
        using var scenario = FactoryScenario.Create()
            .Plan(task)
            .CommandFailure(AgentTerminationKind.CommandTimeout, "timeout")
            .Execute(task)
            .Done();

        var result = await scenario.Run();

        result.ShouldComplete();
        var events = File.ReadAllLines(Path.Combine(result.RunDirectory, "events.jsonl"))
            .Select(line => JsonDocument.Parse(line))
            .ToArray();
        try
        {
            var eventTypes = events
                .Select(document => document.RootElement.GetProperty("type").GetString())
                .ToArray();
            Assert.Contains("technical-restart-scheduled", eventTypes);
            Assert.Contains("technical-restart-started", eventTypes);
            Assert.DoesNotContain("agent-command-failure-retry", eventTypes);
        }
        finally
        {
            foreach (var document in events)
                document.Dispose();
        }
    }

    private static AgentInvocation[] WorkItemInvocations(ScenarioResult result, string workItemId) =>
        result.Invocations
            .Where(x => x.WorkItemId == workItemId)
            .ToArray();
}
