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
        Assert.Equal(1, metrics.RootLevelSpawnedAgentCount);
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
        Assert.Equal(1, metrics.RootLevelSpawnedAgentCount);
    }

    [Fact]
    public void Analyze_StartedAndCompletedWaitEventsDoNotDoubleCount()
    {
        var metrics = AnalyzeLines(
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"spawn_1\",\"type\":\"collab_tool_call\",\"tool\":\"spawn_agent\",\"receiver_thread_ids\":[\"child_1\"],\"status\":\"completed\"}}",
            "{\"type\":\"item.started\",\"item\":{\"id\":\"wait_1\",\"type\":\"collab_tool_call\",\"tool\":\"wait\",\"status\":\"in_progress\"}}",
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"wait_1\",\"type\":\"collab_tool_call\",\"tool\":\"wait\",\"status\":\"completed\",\"agents_states\":{\"child_1\":{\"status\":\"completed\"}}}}");

        Assert.Equal(1, metrics.WaitAgentCallCount);
        Assert.Equal(1, metrics.CompletedChildAgentCount);
    }

    [Fact]
    public void Analyze_CompletedAgentsExcludeIdsThatWereNotSuccessfullySpawned()
    {
        var metrics = AnalyzeLines(
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"spawn_1\",\"type\":\"collab_tool_call\",\"tool\":\"spawn_agent\",\"receiver_thread_ids\":[\"child_1\"],\"status\":\"completed\"}}",
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"wait_1\",\"type\":\"collab_tool_call\",\"tool\":\"wait\",\"status\":\"completed\",\"agents_states\":{\"child_1\":{\"status\":\"completed\"},\"unspawned\":{\"status\":\"completed\"}}}}");

        Assert.Equal(1, metrics.CompletedChildAgentCount);
    }

    [Fact]
    public void Analyze_FailedSpawnIsCountedSeparately()
    {
        var metrics = AnalyzeLines("{\"type\":\"item.completed\",\"item\":{\"id\":\"item_1\",\"type\":\"collab_tool_call\",\"tool\":\"spawn_agent\",\"receiver_thread_ids\":[],\"status\":\"failed\",\"error\":{\"message\":\"capacity\"}}}");

        Assert.Equal(1, metrics.SpawnAgentCallCount);
        Assert.Equal(0, metrics.RootLevelSpawnedAgentCount);
        Assert.Equal(1, metrics.FailedSpawnAgentCallCount);
    }

    [Fact]
    public void Analyze_TraceWithoutSpawnEventsReturnsZero()
    {
        var metrics = AnalyzeLines("{\"type\":\"thread.started\",\"thread_id\":\"factory\"}", "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":1}}");

        Assert.Equal(0, metrics.SpawnAgentCallCount);
        Assert.Equal(0, metrics.RootLevelSpawnedAgentCount);
        Assert.Equal(0, metrics.FailedSpawnAgentCallCount);
    }

    [Fact]
    public void Analyze_CommandExecutionMentioningSpawnAgentIsIgnored()
    {
        var metrics = AnalyzeLines(
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"command_1\",\"type\":\"command_execution\",\"command\":\"Get-Content references/codex-dispatch.md\",\"aggregated_output\":\"Use spawn_agent with message only.\",\"status\":\"completed\"}}");

        Assert.Equal(0, metrics.ToolCallCount);
        Assert.Equal(0, metrics.SpawnAgentCallCount);
        Assert.Equal(0, metrics.RootLevelSpawnedAgentCount);
    }

    [Fact]
    public void Analyze_CloseAgentIsCountedWithoutChangingAgentMetrics()
    {
        var metrics = AnalyzeLines(
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"spawn_1\",\"type\":\"collab_tool_call\",\"tool\":\"spawn_agent\",\"receiver_thread_ids\":[\"child_1\"],\"status\":\"completed\"}}",
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"wait_1\",\"type\":\"collab_tool_call\",\"tool\":\"wait\",\"agents_states\":{\"child_1\":{\"status\":\"completed\"}},\"status\":\"completed\"}}",
            "{\"type\":\"item.started\",\"item\":{\"id\":\"close_1\",\"type\":\"collab_tool_call\",\"tool\":\"close_agent\",\"receiver_thread_ids\":[\"child_1\"],\"status\":\"in_progress\"}}",
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"close_1\",\"type\":\"collab_tool_call\",\"tool\":\"close_agent\",\"receiver_thread_ids\":[\"child_1\"],\"agents_states\":{\"child_1\":{\"status\":\"completed\"}},\"status\":\"completed\"}}");

        Assert.Equal(3, metrics.ToolCallCount);
        Assert.Equal(1, metrics.SpawnAgentCallCount);
        Assert.Equal(1, metrics.RootLevelSpawnedAgentCount);
        Assert.Equal(1, metrics.WaitAgentCallCount);
        Assert.Equal(1, metrics.CompletedChildAgentCount);
    }

    [Fact]
    public void Analyze_UnknownCollaborationItemIsInfrastructureError()
    {
        var exception = Assert.Throws<CodexJsonlAnalysisException>(() => AnalyzeLines("{\"type\":\"item.completed\",\"item\":{\"id\":\"item_1\",\"type\":\"collab_tool_call\",\"tool\":\"delegate_task\",\"status\":\"completed\"}}"));

        Assert.Contains("Unsupported collaboration tool", exception.Message);
    }

    [Fact]
    public void Analyze_CountsCompletedTurnsAndLatestCumulativeUsage()
    {
        var metrics = AnalyzeLines(
            "{\"type\":\"turn.started\"}",
            "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":100,\"cached_input_tokens\":60,\"output_tokens\":20,\"reasoning_output_tokens\":5}}",
            "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":150,\"cached_input_tokens\":80,\"output_tokens\":30,\"reasoning_output_tokens\":7}}");

        Assert.Equal(2, metrics.ModelTurnCount);
        Assert.Equal(150, metrics.InputTokens);
        Assert.Equal(80, metrics.CachedInputTokens);
        Assert.Equal(30, metrics.OutputTokens);
        Assert.Equal(7, metrics.ReasoningOutputTokens);
        Assert.Equal(180, metrics.TotalTokens);
    }

    [Fact]
    public void Analyze_CountsAdditionalCallTypesButNotResults()
    {
        var metrics = AnalyzeLines(
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"custom\",\"type\":\"custom_tool_call\",\"status\":\"completed\"}}",
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"shell\",\"type\":\"local_shell_call\",\"status\":\"completed\"}}",
            "{\"type\":\"item.completed\",\"item\":{\"call_id\":\"custom\",\"type\":\"custom_tool_call_output\",\"status\":\"completed\"}}");

        Assert.Equal(2, metrics.ToolCallCount);
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
