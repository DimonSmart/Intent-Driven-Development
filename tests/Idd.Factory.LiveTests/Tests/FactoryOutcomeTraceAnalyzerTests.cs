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
            AgentMessage("Starting Factory runner bootstrap and verification."),
            Tool("spawn_agent"),
            Tool("wait"),
            AgentMessage(CompletedResponse),
            "{\"type\":\"turn.completed\"}");

        var outcome = Assert.Single(analysis.PublicFactoryOutcomes);
        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Empty(analysis.ActivityAfterOutcome);

        var assertions = AssertProtocol(analysis, "COMPLETED");
        Assert.All(assertions.Assertions, assertion => Assert.Equal("PASS", assertion.Status));
    }

    [Fact]
    public void Analyze_ReportsToolExecutionAfterTerminalOutcome()
    {
        var analysis = Analyze(
            AgentMessage(BlockedResponse),
            Tool("spawn_agent"));

        Assert.Equal(["spawn_agent"], analysis.ActivityAfterOutcome);

        var assertions = AssertProtocol(analysis, "BLOCKED");
        Assert.Contains(assertions.Assertions, assertion => assertion is { Name: "No activity after terminal outcome", Status: "FAIL" });
    }

    [Fact]
    public void Analyze_ReportsMultipleFactoryOutcomes()
    {
        var analysis = Analyze(
            AgentMessage(BlockedResponse),
            AgentMessage(CompletedResponse));

        Assert.Equal(2, analysis.PublicFactoryOutcomes.Count);
        Assert.Contains("agent_message", analysis.ActivityAfterOutcome);

        var assertions = AssertProtocol(analysis, "COMPLETED");
        Assert.Contains(assertions.Assertions, assertion => assertion is { Name: "Single terminal outcome", Status: "FAIL" });
        Assert.DoesNotContain(assertions.Assertions, assertion => assertion.Name == "Outcome consistency");
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

    [Fact]
    public void ProtocolAssertions_DoNotCheckConsistencyWhenOutcomeIsMissing()
    {
        var assertions = AssertProtocol(Analyze(AgentMessage("Factory is still running.")), "COMPLETED");

        Assert.Contains(assertions.Assertions, assertion => assertion is { Name: "Single terminal outcome", Status: "FAIL" });
        Assert.DoesNotContain(assertions.Assertions, assertion => assertion.Name == "Outcome consistency");
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

    private static EvalAssertionCollector AssertProtocol(FactoryOutcomeTraceAnalysis analysis, string finalOutcome)
    {
        var assertions = new EvalAssertionCollector();
        var response = new FactoryResponse(1, finalOutcome, finalOutcome == "COMPLETED" ? ".idd/factory/results/run/factory-result.json" : null, finalOutcome == "COMPLETED" ? null : "Stopped.");
        FactoryProtocolAssertions.Assert(assertions, analysis, new ExecutionResponseReadResult(response, null));
        return assertions;
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
