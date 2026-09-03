using System.Text;
using System.Text.Json;
using Idd.Factory.LiveTests.Infrastructure;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class FactoryRuntimeTraceReaderTests
{
    [Fact]
    public void TryRead_UsesCurrentRuntimeEventsForInterruptedRun()
    {
        var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var current = Path.Combine(workspace, ".idd", "factory", "current");
        try
        {
            Directory.CreateDirectory(Path.Combine(current, "attempts", "A000001"));
            Directory.CreateDirectory(Path.Combine(current, "attempts", "A000002"));
            File.WriteAllLines(Path.Combine(current, "events.jsonl"),
            [
                Event("agent-dispatching", "A000001", "planner", null, "2026-01-01T00:00:00Z"),
                Event("agent-completed", "A000001", "planner", null, "2026-01-01T00:00:01Z"),
                Event("agent-dispatching", "A000002", "executor", "catalog", "2026-01-01T00:00:02Z")
            ]);
            File.WriteAllText(Path.Combine(current, "attempts", "A000002", "result.json"),
                JsonSerializer.Serialize(new { schemaVersion = 3, attemptId = "A000002", semanticResultPath = "semantic-result.md" }));
            File.WriteAllLines(Path.Combine(current, "attempts", "A000002", "stdout.log"),
            [
                "{\"type\":\"turn.started\"}",
                "{\"type\":\"item.started\",\"item\":{\"id\":\"tool-1\",\"type\":\"command_execution\",\"command\":\"Get-Content 'src/Catalog.cs'\",\"status\":\"in_progress\"}}",
                "{\"type\":\"item.completed\",\"item\":{\"id\":\"tool-1\",\"type\":\"command_execution\",\"command\":\"Get-Content 'src/Catalog.cs'\",\"aggregated_output\":\"source\",\"exit_code\":0,\"status\":\"completed\"}}",
                "{\"type\":\"item.started\",\"item\":{\"id\":\"tool-2\",\"type\":\"file_change\",\"status\":\"in_progress\"}}",
                "{\"type\":\"item.completed\",\"item\":{\"id\":\"tool-2\",\"type\":\"file_change\",\"status\":\"completed\"}}",
                "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":100,\"cached_input_tokens\":40,\"output_tokens\":20,\"reasoning_output_tokens\":5}}"
            ]);

            var trace = FactoryRuntimeTraceReader.TryRead(workspace, "root", processInterrupted: true)!;

            Assert.Equal(3, trace.Agents.Count);
            Assert.Equal("interrupted", trace.Agents.Single(agent => agent.ThreadId == "root").Status);
            var incomplete = trace.Agents.Single(agent => agent.ThreadId == "A000002");
            Assert.Equal("executor", incomplete.Role);
            Assert.Equal("catalog", incomplete.WorkItem);
            Assert.Equal("result-produced", incomplete.Status);
            Assert.Equal(2, incomplete.ToolCallCount);
            Assert.Equal(1, incomplete.FileReadCount);
            Assert.Equal(1, incomplete.UniqueFileReadCount);
            Assert.Equal(0, incomplete.RepeatedFileReadCount);
            Assert.Equal(["shell", "apply_patch"], incomplete.ToolCalls!.Select(call => call.Tool));
            Assert.Single(incomplete.TokenProgression!);
            Assert.Contains(trace.Diagnostics, diagnostic => diagnostic.Code == "RUNTIME_RESULT_NOT_RECORDED");
        }
        finally { if (Directory.Exists(workspace)) Directory.Delete(workspace, true); }
    }

    [Fact]
    public void TryRead_CountsBatchedReadOutputOnce()
    {
        var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var current = Path.Combine(workspace, ".idd", "factory", "current");
        const string output = "alpha\nbeta";
        try
        {
            Directory.CreateDirectory(Path.Combine(current, "attempts", "A000001"));
            File.WriteAllLines(Path.Combine(current, "events.jsonl"),
            [
                Event("agent-dispatching", "A000001", "planner", null, "2026-01-01T00:00:00Z"),
                Event("agent-completed", "A000001", "planner", null, "2026-01-01T00:00:01Z")
            ]);
            File.WriteAllLines(Path.Combine(current, "attempts", "A000001", "stdout.log"),
            [
                JsonSerializer.Serialize(new
                {
                    type = "item.completed",
                    item = new
                    {
                        id = "tool-1",
                        type = "command_execution",
                        command = "Get-Content 'a.md'; Get-Content 'b.md'",
                        aggregated_output = output,
                        exit_code = 0,
                        status = "completed"
                    }
                })
            ]);

            var trace = FactoryRuntimeTraceReader.TryRead(workspace, "root")!;
            var reviewer = trace.Agents.Single(agent => agent.ThreadId == "A000001");

            Assert.Equal(2, reviewer.FileReadCount);
            Assert.Equal(Encoding.UTF8.GetByteCount(output), reviewer.FileReadBytes);
        }
        finally { if (Directory.Exists(workspace)) Directory.Delete(workspace, true); }
    }

    [Fact]
    public void TryRead_UsesArchivedRuntimeEventsAfterFinalization()
    {
        var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var result = Path.Combine(workspace, ".idd", "factory", "results", "run-1");
        try
        {
            Directory.CreateDirectory(result);
            File.WriteAllText(Path.Combine(result, "events.jsonl"),
                Event("agent-dispatching", "A000001", "planner", null, "2026-01-01T00:00:00Z") + Environment.NewLine);

            var trace = FactoryRuntimeTraceReader.TryRead(workspace, "root")!;

            Assert.Equal("planner", trace.Agents.Single(agent => agent.ThreadId == "A000001").Role);
        }
        finally { if (Directory.Exists(workspace)) Directory.Delete(workspace, true); }
    }

    private static string Event(string type, string attemptId, string role, string? workItemId, string timestamp)
        => JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            timestamp,
            type,
            data = new { attemptId, role, workItemId }
        });
}
