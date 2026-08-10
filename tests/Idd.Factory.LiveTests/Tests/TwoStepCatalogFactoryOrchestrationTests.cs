using Idd.Factory.LiveTests.Infrastructure;
using Idd.Factory.LiveTests.Models;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class TwoStepCatalogFactoryOrchestrationTests
{
    [Fact]
    public void AssertOrchestration_PassesForExpectedSemanticTopology()
    {
        var assertions = AssertOrchestration(ExpectedTrace());

        Assert.False(assertions.HasFailuresIn("Orchestration failure"));
    }

    [Fact]
    public void AssertOrchestration_FailsForPhaseSpecificAction()
    {
        var trace = ExpectedTrace();
        var agents = trace.Agents.Select(agent => agent.ThreadId == "final" ? agent with { Action = "FINAL REVIEW" } : agent).ToArray();
        var assertions = AssertOrchestration(trace with { Agents = agents });

        Assert.True(assertions.HasFailuresIn("Orchestration failure"));
    }

    [Fact]
    public void AssertOrchestration_FailsWhenWorkerRunsUnderRoot()
    {
        var trace = ExpectedTrace();
        var agents = trace.Agents.Select(agent => agent.ThreadId == "impl-1" ? agent with { ParentThreadId = "root" } : agent).ToArray();
        var assertions = AssertOrchestration(trace with { Agents = agents });

        Assert.True(assertions.HasFailuresIn("Orchestration failure"));
    }

    [Fact]
    public void AssertOrchestration_ReportsMissingTraceAsPrimaryFailure()
    {
        var assertions = AssertOrchestration(new AgentTrace(2, null, [], []));

        var exception = Assert.Throws<Xunit.Sdk.XunitException>(() => assertions.ThrowIfFailed("run"));
        Assert.Contains("no root trace was available", exception.Message);
        Assert.Contains(Path.Combine("run", "report.md"), exception.Message);
    }

    [Fact]
    public async Task EvalReport_DistinguishesRootLevelAndTotalSpawnedAgents()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var workspace = new FactoryEvalWorkspace(directory, directory, directory, directory, directory);
            var result = new FactoryEvalResult { RunDirectory = directory, Outcome = "PASS" };
            var metrics = new FactoryEvalMetrics { RootLevelSpawnedAgentCount = 2, TotalSpawnedAgentCount = 4 };

            await new EvalAssertionCollector().WriteAsync(workspace, result, metrics, new(null, "not expected"), new(2, null, [], []));

            var report = await File.ReadAllTextAsync(Path.Combine(directory, "report.md"));
            Assert.Contains("Root-level spawned agents: 2", report);
            Assert.Contains("Total spawned agents: 4", report);
            Assert.DoesNotContain("Successfully spawned agents", report);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static EvalAssertionCollector AssertOrchestration(AgentTrace trace)
    {
        var assertions = new EvalAssertionCollector();
        TwoStepCatalogFactoryEvalTests.AssertOrchestration(assertions, trace);
        return assertions;
    }

    private static AgentTrace ExpectedTrace()
    {
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        return new AgentTrace(2, "root", [
            Node("root", null, "factory-root", null, start),
            Node("decomposer", "root", "task-decomposer", null, start),
            Node("initialize", "root", "factory-step-coordinator", "INITIALIZE", start.AddSeconds(1)),
            Node("implementation-1", "root", "factory-step-coordinator", "CONTINUE", start.AddSeconds(2)),
            Node("impl-1", "implementation-1", "implementer", null, start.AddSeconds(3)),
            Node("implementation-2", "root", "factory-step-coordinator", "CONTINUE", start.AddSeconds(4)),
            Node("impl-2", "implementation-2", "implementer", null, start.AddSeconds(5)),
            Node("checkpoint", "root", "factory-step-coordinator", "CONTINUE", start.AddSeconds(6)),
            Node("checkpoint-review", "checkpoint", "checkpoint-reviewer", null, start.AddSeconds(7)),
            Node("final", "root", "factory-step-coordinator", "CONTINUE", start.AddSeconds(8)),
            Node("final-review", "final", "final-reviewer", null, start.AddSeconds(9))
        ], []);
    }

    private static AgentTraceNode Node(string id, string? parent, string role, string? action, DateTimeOffset startedAt) =>
        new(id, parent, role, null, action, "completed", startedAt, startedAt.AddSeconds(1), 1000, 1, 1, null, null, null, null, null);
}
