using Idd.Factory.LiveTests.Infrastructure;
using Idd.Factory.LiveTests.Models;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class AgentTraceTests
{
    [Fact]
    public void CodexHomeLocator_UsesConfiguredHomeAndDoesNotCreateDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var configured = Path.Combine(root, "configured");
            Directory.CreateDirectory(Path.Combine(configured, "sessions"));
            Assert.Equal(Path.Combine(configured, "sessions"), new CodexHomeLocator(() => configured, () => "ignored").FindSessionsDirectory());
            var missing = Path.Combine(root, "missing");
            Assert.Null(new CodexHomeLocator(() => null, () => missing).FindSessionsDirectory());
            Assert.False(Directory.Exists(missing));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void JsonlAnalyzer_ReadsRootOnlyFromThreadStarted()
    {
        var path = Path.GetTempFileName();
        try { File.WriteAllText(path, "bad\n{\"type\":\"message\",\"thread_id\":\"wrong\"}\n{\"type\":\"thread.started\",\"thread_id\":\"root\"}\n"); Assert.Equal("root", CodexJsonlAnalyzer.TryReadRootThreadId(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Builder_UsesMetadataEdgesAndDispatchRoleAndWorkItem()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            Write(directory, "root", "{\"type\":\"session_meta\",\"payload\":{\"id\":\"root\",\"timestamp\":\"2026-01-01T00:00:00Z\"}}", "{\"type\":\"response_item\",\"payload\":{\"item\":{\"type\":\"collab_tool_call\",\"id\":\"spawn\",\"tool\":\"spawn_agent\",\"prompt\":\"Role:\\nfactory-step-coordinator\\nAction:\\nINITIALIZE\"}}}", "{\"type\":\"response_item\",\"payload\":{\"item\":{\"type\":\"collab_tool_call\",\"id\":\"spawn\",\"tool\":\"spawn_agent\",\"receiver_thread_ids\":[\"coordinator\"]}}}");
            Write(directory, "coordinator", "{\"type\":\"session_meta\",\"payload\":{\"id\":\"coordinator\",\"parent_thread_id\":\"root\"}}", "{\"type\":\"response_item\",\"payload\":{\"item\":{\"type\":\"message\",\"text\":\"unstructured child input\"}}}", "{\"timestamp\":\"2026-01-01T00:00:01Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"completed_at\":1767225601}} ");
            Write(directory, "implementer", "{\"type\":\"session_meta\",\"payload\":{\"id\":\"implementer\",\"parent_thread_id\":\"coordinator\"}}", "{\"type\":\"response_item\",\"payload\":{\"item\":{\"type\":\"message\",\"text\":\"Role:\\nimplementer\\n.idd/factory/current/001-code.active.md\"}}}");
            Write(directory, "foreign", "{\"type\":\"session_meta\",\"payload\":{\"id\":\"foreign\"}}");
            var trace = new AgentTraceBuilder().Build(directory, "root");
            Assert.Equal(["root", "coordinator", "implementer"], trace.Agents.Select(agent => agent.ThreadId));
            Assert.Equal("factory-step-coordinator", trace.Agents.Single(agent => agent.ThreadId == "coordinator").Role);
            Assert.Equal("INITIALIZE", trace.Agents.Single(agent => agent.ThreadId == "coordinator").Action);
            Assert.Equal("completed", trace.Agents.Single(agent => agent.ThreadId == "coordinator").Status);
            Assert.Equal("001-code", trace.Agents.Single(agent => agent.ThreadId == "implementer").WorkItem);
            Assert.Null(trace.Agents.Single(agent => agent.ThreadId == "implementer").TotalTokens);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Builder_InfersLeafRoleFromRoleReference()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            Write(directory, "root", "{\"type\":\"session_meta\",\"payload\":{\"id\":\"root\"}}");
            Write(directory, "leaf", "{\"type\":\"session_meta\",\"payload\":{\"id\":\"leaf\",\"parent_thread_id\":\"root\"}}", "{\"type\":\"response_item\",\"payload\":{\"item\":{\"type\":\"message\",\"text\":\"Read references/roles/checkpoint-reviewer.md and continue\"}}}");

            var leaf = new AgentTraceBuilder().Build(directory, "root").Agents.Single(agent => agent.ThreadId == "leaf");

            Assert.Equal("checkpoint-reviewer", leaf.Role);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ReportWriter_WritesStableEdgesAndEscapesLabels()
    {
        var trace = new AgentTrace(2, "root", [
            new("root", null, "factory-root", null, null, "completed", null, null, null, 1, 1, null, null, null, null, null),
            new("child", "root", "unknown [role] \"x\"", "a&b", null, "interrupted", null, null, null, 0, 0, null, null, null, null, null)
        ], []);
        var mermaid = AgentTraceReportWriter.WriteMermaid(trace);
        Assert.Contains("n1 --> n0", mermaid);
        Assert.Contains("<br/>", mermaid);
        Assert.DoesNotContain("&lt;br/&gt;", mermaid);
        Assert.Contains("unknown &#91;role&#93;", mermaid);
        Assert.Contains("&quot;x&quot;", mermaid);
        Assert.Contains("a&amp;b", mermaid);
        Assert.DoesNotContain("COMPLETED", mermaid);
        Assert.Contains("INTERRUPTED", mermaid);
        Assert.DoesNotContain("null", mermaid, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Builder_UsesLatestCumulativeTokensCompletedTurnsAndDistinctToolCalls()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            Write(directory, "root",
                "{\"type\":\"session_meta\",\"payload\":{\"id\":\"root\"}}",
                "{\"type\":\"response_item\",\"payload\":{\"item\":{\"type\":\"function_call\",\"id\":\"one\",\"name\":\"shell\"}}}",
                "{\"type\":\"response_item\",\"payload\":{\"item\":{\"type\":\"function_call_output\",\"call_id\":\"one\"}}}",
                "{\"type\":\"response_item\",\"payload\":{\"item\":{\"type\":\"custom_tool_call\",\"call_id\":\"two\"}}}",
                "{\"type\":\"response_item\",\"payload\":{\"item\":{\"type\":\"local_shell_call\",\"id\":\"three\"}}}",
                "{\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":100,\"cached_input_tokens\":40,\"output_tokens\":20,\"reasoning_output_tokens\":5}}}}",
                "{\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":150,\"cached_input_tokens\":50,\"output_tokens\":30,\"reasoning_output_tokens\":7}}}}",
                "{\"type\":\"event_msg\",\"payload\":{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":999,\"output_tokens\":999}}}",
                "{\"type\":\"event_msg\",\"payload\":{\"type\":\"turn.completed\"}}");

            var node = Assert.Single(new AgentTraceBuilder().Build(directory, "root").Agents);
            Assert.Equal(2, node.TurnCount);
            Assert.Equal(3, node.ToolCallCount);
            Assert.Equal(150, node.InputTokens);
            Assert.Equal(50, node.CachedInputTokens);
            Assert.Equal(30, node.OutputTokens);
            Assert.Equal(7, node.ReasoningOutputTokens);
            Assert.Equal(180, node.TotalTokens);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(950, "950 tok")]
    [InlineData(8100, "8.1k tok")]
    [InlineData(84317, "84.3k tok")]
    [InlineData(1200000, "1.2M tok")]
    public void ReportWriter_FormatsTokensCompactly(long tokens, string expected) => Assert.Equal(expected, AgentTraceReportWriter.FormatTokens(tokens));
    private static void Write(string directory, string name, params string[] lines) => File.WriteAllLines(Path.Combine(directory, name + ".jsonl"), lines);
}
