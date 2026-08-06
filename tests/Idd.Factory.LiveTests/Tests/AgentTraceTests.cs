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
            Write(directory, "root", "{\"type\":\"session_meta\",\"payload\":{\"id\":\"root\",\"timestamp\":\"2026-01-01T00:00:00Z\"}}", "{\"type\":\"response_item\",\"payload\":{\"item\":{\"type\":\"collab_tool_call\",\"id\":\"spawn\",\"tool\":\"spawn_agent\",\"receiver_thread_ids\":[\"coordinator\"]}}}");
            Write(directory, "coordinator", "{\"type\":\"session_meta\",\"payload\":{\"id\":\"coordinator\",\"parent_thread_id\":\"root\"}}", "{\"type\":\"response_item\",\"payload\":{\"item\":{\"type\":\"message\",\"text\":\"Role:\\nfactory-step-coordinator\"}}}");
            Write(directory, "implementer", "{\"type\":\"session_meta\",\"payload\":{\"id\":\"implementer\",\"parent_thread_id\":\"coordinator\"}}", "{\"type\":\"response_item\",\"payload\":{\"item\":{\"type\":\"message\",\"text\":\"Role:\\nimplementer\\n.idd/factory/current/001-code.active.md\"}}}");
            Write(directory, "foreign", "{\"type\":\"session_meta\",\"payload\":{\"id\":\"foreign\"}}");
            var trace = new AgentTraceBuilder().Build(directory, "root");
            Assert.Equal(["root", "coordinator", "implementer"], trace.Agents.Select(agent => agent.ThreadId));
            Assert.Equal("factory-step-coordinator", trace.Agents.Single(agent => agent.ThreadId == "coordinator").Role);
            Assert.Equal("001-code", trace.Agents.Single(agent => agent.ThreadId == "implementer").WorkItem);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ReportWriter_WritesStableEdgesAndEscapesLabels()
    {
        var trace = new AgentTrace(1, "root", [new("root", null, "factory-root", null, "completed", null, null, null, 1), new("child", "root", "unknown [role]", "a&b", "interrupted", null, null, null, null)], []);
        var mermaid = AgentTraceReportWriter.WriteMermaid(trace);
        Assert.Contains("n1 --> n0", mermaid); Assert.Contains("unknown &#91;role&#93;", mermaid); Assert.Contains("a&amp;b", mermaid); Assert.DoesNotContain("null", mermaid, StringComparison.OrdinalIgnoreCase);
    }
    private static void Write(string directory, string name, params string[] lines) => File.WriteAllLines(Path.Combine(directory, name + ".jsonl"), lines);
}
