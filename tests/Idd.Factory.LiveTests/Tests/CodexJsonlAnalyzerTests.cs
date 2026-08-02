using Idd.Factory.LiveTests.Infrastructure;
using Idd.Factory.LiveTests.Models;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class CodexJsonlAnalyzerTests
{
    [Fact]
    public void Analyze_RealSuccessfulProbeCountsOneSpawnOnce()
    {
        var metrics = CodexJsonlAnalyzer.Analyze(ProbeFixturePath, TimeSpan.FromMilliseconds(321));

        Assert.Equal(2, metrics.ToolCallCount);
        Assert.Equal(1, metrics.SpawnAgentCallCount);
        Assert.Equal(1, metrics.SpawnedAgentCount);
        Assert.Equal(0, metrics.FailedSpawnAgentCallCount);
        Assert.Equal(1, metrics.WaitAgentCallCount);
        Assert.Equal(1, metrics.CompletedChildAgentCount);
        Assert.Equal(321, metrics.WallTimeMs);
    }

    [Fact]
    public void Analyze_StartedAndCompletedEventsDoNotDoubleCount()
    {
        var metrics = AnalyzeLines(
            "{\"type\":\"item.started\",\"item\":{\"id\":\"item_1\",\"type\":\"collab_tool_call\",\"tool\":\"spawn_agent\",\"status\":\"in_progress\"}}",
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"item_1\",\"type\":\"collab_tool_call\",\"tool\":\"spawn_agent\",\"receiver_thread_ids\":[\"child_1\"],\"status\":\"completed\"}}");

        Assert.Equal(1, metrics.SpawnAgentCallCount);
        Assert.Equal(1, metrics.SpawnedAgentCount);
    }

    [Fact]
    public void Analyze_FailedSpawnIsCountedSeparately()
    {
        var metrics = AnalyzeLines("{\"type\":\"item.completed\",\"item\":{\"id\":\"item_1\",\"type\":\"collab_tool_call\",\"tool\":\"spawn_agent\",\"receiver_thread_ids\":[],\"status\":\"failed\",\"error\":{\"message\":\"capacity\"}}}");

        Assert.Equal(1, metrics.SpawnAgentCallCount);
        Assert.Equal(0, metrics.SpawnedAgentCount);
        Assert.Equal(1, metrics.FailedSpawnAgentCallCount);
    }

    [Fact]
    public void Analyze_TraceWithoutSpawnEventsReturnsZero()
    {
        var metrics = AnalyzeLines("{\"type\":\"thread.started\",\"thread_id\":\"factory\"}", "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":1}}");

        Assert.Equal(0, metrics.SpawnAgentCallCount);
        Assert.Equal(0, metrics.SpawnedAgentCount);
        Assert.Equal(0, metrics.FailedSpawnAgentCallCount);
    }

    [Fact]
    public void Analyze_UnknownCollaborationItemIsInfrastructureError()
    {
        var exception = Assert.Throws<CodexJsonlAnalysisException>(() => AnalyzeLines("{\"type\":\"item.completed\",\"item\":{\"id\":\"item_1\",\"type\":\"collab_tool_call\",\"tool\":\"delegate_task\",\"status\":\"completed\"}}"));

        Assert.Contains("Unsupported collaboration tool", exception.Message);
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

    private static string ProbeFixturePath => Path.Combine(RepositoryRootFinder.Find(), "tests", "Idd.Factory.LiveTests", "Tests", "Fixtures", "codex-0.146.0-subagent-telemetry-probe.jsonl");
}
