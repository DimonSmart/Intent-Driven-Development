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
    public void AssertOrchestration_FailsForUnexpectedWorker()
    {
        var trace = ExpectedTrace();
        var agents = trace.Agents.Append(Node("impl-3", "root", "executor", null, DateTimeOffset.Parse("2026-01-01T00:00:10Z"))).ToArray();
        var assertions = AssertOrchestration(trace with { Agents = agents });

        Assert.True(assertions.HasFailuresIn("Orchestration failure"));
    }

    [Fact]
    public void AssertOrchestration_FailsWhenWorkerIsNested()
    {
        var trace = ExpectedTrace();
        var agents = trace.Agents.Select(agent => agent.ThreadId == "impl-1" ? agent with { ParentThreadId = "decomposer" } : agent).ToArray();
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
            Assert.Contains("Semantic subprocess workers: 4", report);
            Assert.Contains("Programmatic worker dispatches:", report);
            Assert.Contains("Completed semantic workers:", report);
            Assert.Contains("Platform collaboration spawns:", report);
            Assert.Contains("Root launcher input:", report);
            Assert.Contains("Semantic workers input:", report);
            Assert.Contains("Total Factory tokens:", report);
            Assert.Contains("Detailed efficiency diagnostics: efficiency.md / efficiency.json", report);
            Assert.DoesNotContain("Successfully spawned agents", report);
            Assert.True(File.Exists(workspace.EfficiencyJsonPath));
            Assert.True(File.Exists(workspace.EfficiencyMarkdownPath));
            Assert.Contains("Token usage by role", await File.ReadAllTextAsync(workspace.EfficiencyMarkdownPath));
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
            Node("plan-1", "root", "planner", null, start),
            Node("impl-1", "root", "executor", null, start.AddSeconds(1)),
            Node("impl-2", "root", "executor", null, start.AddSeconds(2)),
            Node("plan-2", "root", "planner", null, start.AddSeconds(3))
        ], []);
    }

    private static AgentTraceNode Node(string id, string? parent, string role, string? action, DateTimeOffset startedAt) =>
        new(id, parent, role, null, action, "completed", startedAt, startedAt.AddSeconds(1), 1000, 1, 1, null, null, null, null, null);
}
