using Idd.Factory.LiveTests.Infrastructure;
using Idd.Factory.LiveTests.Models;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class TwoStepCatalogFactoryOrchestrationTests
{
    [Fact]
    public void AssertOrchestration_PassesForTwoSuccessfulSpawnsAndCompletedAgents()
    {
        var metrics = AnalyzeLines(
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"spawn_1\",\"type\":\"collab_tool_call\",\"tool\":\"spawn_agent\",\"receiver_thread_ids\":[\"child_1\"],\"status\":\"completed\"}}",
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"spawn_2\",\"type\":\"collab_tool_call\",\"tool\":\"spawn_agent\",\"receiver_thread_ids\":[\"child_2\"],\"status\":\"completed\"}}",
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"wait_1\",\"type\":\"collab_tool_call\",\"tool\":\"wait\",\"status\":\"completed\",\"agents_states\":{\"child_1\":{\"status\":\"completed\"},\"child_2\":{\"status\":\"completed\"}}}}");
        var assertions = AssertOrchestration(metrics);

        Assert.False(assertions.HasFailuresIn("Orchestration failure"));
    }

    [Fact]
    public void AssertOrchestration_FailsWhenOnlyOneChildAgentCompleted()
    {
        var assertions = AssertOrchestration(new FactoryEvalMetrics { RootLevelSpawnedAgentCount = 2, CompletedChildAgentCount = 1 });

        Assert.True(assertions.HasFailuresIn("Orchestration failure"));
    }

    [Fact]
    public void AssertOrchestration_ReportsPrimaryFailureWithoutCascadingCompletionNoise()
    {
        var assertions = AssertOrchestration(new FactoryEvalMetrics());

        var exception = Assert.Throws<Xunit.Sdk.XunitException>(() => assertions.ThrowIfFailed("run"));
        Assert.Contains("Actual root-level spawned agents: 0", exception.Message);
        Assert.DoesNotContain("Actual completed agents: 0", exception.Message);
        Assert.Contains(Path.Combine("run", "report.md"), exception.Message);
    }

    [Fact]
    public void AssertOrchestration_DoesNotTreatWaitCallsAsFailure()
    {
        var assertions = AssertOrchestration(new FactoryEvalMetrics { RootLevelSpawnedAgentCount = 2, CompletedChildAgentCount = 2, WaitAgentCallCount = 3 });

        Assert.False(assertions.HasFailures);
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

    private static EvalAssertionCollector AssertOrchestration(FactoryEvalMetrics metrics)
    {
        var assertions = new EvalAssertionCollector();
        TwoStepCatalogFactoryEvalTests.AssertOrchestration(assertions, metrics);
        return assertions;
    }

    private static FactoryEvalMetrics AnalyzeLines(params string[] lines)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(path, lines);
            return CodexJsonlAnalyzer.Analyze(path, TimeSpan.Zero);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
