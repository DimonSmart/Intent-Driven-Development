using System.Text.Json;
using Idd.Factory.LiveTests.Infrastructure;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class FactoryOutcomeTraceAnalyzerTests
{
    [Fact]
    public void Analyze_AcceptsOneOutcomeFollowedOnlyByRuntimeCompletion()
    {
        var analysis = Analyze(
            Tool("spawn_agent"),
            Tool("wait"),
            AgentMessage(CompletedResponse),
            "{\"type\":\"turn.completed\"}");

        var outcome = Assert.Single(analysis.PublicFactoryOutcomes);
        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Empty(analysis.ActivityAfterOutcome);
    }

    [Fact]
    public void Analyze_ReportsToolExecutionAfterTerminalOutcome()
    {
        var analysis = Analyze(
            AgentMessage(BlockedResponse),
            Tool("spawn_agent"));

        Assert.Equal(["spawn_agent"], analysis.ActivityAfterOutcome);
    }

    [Fact]
    public void Analyze_ReportsMultipleFactoryOutcomes()
    {
        var analysis = Analyze(
            AgentMessage(BlockedResponse),
            AgentMessage(CompletedResponse));

        Assert.Equal(2, analysis.PublicFactoryOutcomes.Count);
        Assert.Contains("agent_message", analysis.ActivityAfterOutcome);
    }

    [Fact]
    public void Analyze_DoesNotTreatProgressMessageAsFactoryOutcome()
    {
        var analysis = Analyze(AgentMessage("Factory progress: BLOCKED is not the final outcome."));

        Assert.Empty(analysis.PublicFactoryOutcomes);
    }

    [Fact]
    public void Analyze_DoesNotTreatInvalidJsonMessageAsFactoryOutcome()
    {
        var analysis = Analyze(AgentMessage("{ not valid JSON"));

        Assert.Empty(analysis.PublicFactoryOutcomes);
    }

    private static FactoryOutcomeTraceAnalysis Analyze(params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), $"factory-outcome-trace-{Guid.NewGuid():N}.jsonl");
        try
        {
            File.WriteAllLines(path, lines);
            return FactoryOutcomeTraceAnalyzer.Analyze(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string AgentMessage(string text) => JsonSerializer.Serialize(new
    {
        type = "item.completed",
        item = new { id = Guid.NewGuid().ToString("N"), type = "agent_message", text }
    });

    private static string Tool(string tool) => JsonSerializer.Serialize(new
    {
        type = "item.completed",
        item = new { id = Guid.NewGuid().ToString("N"), type = "collab_tool_call", tool, status = "completed" }
    });

    private const string CompletedResponse =
        "{\"schemaVersion\":1,\"factoryOutcome\":\"COMPLETED\",\"factoryResultPath\":\".idd/factory/results/run/factory-result.json\",\"reason\":null}";

    private const string BlockedResponse =
        "{\"schemaVersion\":1,\"factoryOutcome\":\"BLOCKED\",\"factoryResultPath\":null,\"reason\":\"Dispatch failed.\"}";
}
