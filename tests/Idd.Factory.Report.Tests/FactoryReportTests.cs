using System.Text;
using System.Text.Json;
using Idd.Factory.Report;
using Xunit;

namespace Idd.Factory.Report.Tests;

public sealed class FactoryReportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "idd-factory-report-tests-" + Guid.NewGuid().ToString("N"));

    public FactoryReportTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void RolloutReader_ToleratesMalformedMiddleAndTrailingPartialLine()
    {
        var path = Path.Combine(_root, "rollout.jsonl");
        File.WriteAllText(path,
            Line(Session("11111111-1111-1111-1111-111111111111", _root)) +
            "{ malformed }\n" +
            Line(User("2026-09-23T10:00:00Z", "use idd-factory-run")) +
            "{ partial",
            Encoding.UTF8);

        var rollout = new CodexRolloutReader().Read(path);

        Assert.Equal("11111111-1111-1111-1111-111111111111", rollout.ThreadId);
        Assert.Single(rollout.Diagnostics);
        Assert.Equal("malformed_rollout_line", rollout.Diagnostics[0].Code);
        Assert.Contains(rollout.Events, e => e.Role == "user" && e.Text!.Contains("idd-factory-run"));
    }

    [Fact]
    public void Report_SeparatesFactorySegmentFromLongLivedRootThread()
    {
        var repo = Path.Combine(_root, "repo");
        var codex = Path.Combine(_root, "codex");
        Directory.CreateDirectory(repo);
        var sessionDir = Path.Combine(codex, "sessions", "2026", "09", "23");
        Directory.CreateDirectory(sessionDir);

        const string rootId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        const string planner1 = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
        const string worker1 = "cccccccc-cccc-cccc-cccc-cccccccccccc";
        const string worker2 = "dddddddd-dddd-dddd-dddd-dddddddddddd";
        const string planner2 = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee";

        var rootLines = new[]
        {
            Session(rootId, repo),
            User("2026-09-23T09:59:00Z", "ordinary pre-factory work"),
            Tokens("2026-09-23T09:59:30Z", 100, 40, 10),
            User("2026-09-23T10:00:00Z", "Run $idd-factory-run for the requested change."),
            SpawnCall("2026-09-23T10:00:05Z", "p1", "Use idd-factory-decompose-task and return the current batch."),
            SpawnOutput("2026-09-23T10:00:06Z", "p1", planner1),
            SpawnCall("2026-09-23T10:01:00Z", "w1", "Use idd-factory-execute-subtask. Task: implement A."),
            SpawnOutput("2026-09-23T10:01:01Z", "w1", worker1),
            SpawnCall("2026-09-23T10:02:00Z", "w2", "Use idd-factory-execute-subtask. Task: implement B."),
            SpawnOutput("2026-09-23T10:02:01Z", "w2", worker2),
            SpawnCall("2026-09-23T10:03:00Z", "p2", "Use idd-factory-decompose-task and decide what remains."),
            SpawnOutput("2026-09-23T10:03:01Z", "p2", planner2),
            Tokens("2026-09-23T10:03:30Z", 260, 90, 50),
            CommandStarted("2026-09-23T10:04:00Z", "verify", "dotnet test"),
            CommandCompleted("2026-09-23T10:04:30Z", "verify", "dotnet test", 0),
            Assistant("2026-09-23T10:04:31Z", "Factory completed."),
            CommandStarted("2026-09-23T10:04:40Z", "post", "dotnet build unrelated"),
            CommandCompleted("2026-09-23T10:04:50Z", "post", "dotnet build unrelated", 1),
            User("2026-09-23T10:05:00Z", "ordinary post-factory work"),
            Tokens("2026-09-23T10:05:30Z", 999, 400, 200)
        };
        Write(Path.Combine(sessionDir, "rollout-root.jsonl"), rootLines);

        WriteChild(sessionDir, "planner-1.jsonl", planner1, rootId, repo,
            Assistant("2026-09-23T10:00:07Z", "# Task\nImplement A\n\n# Task\nImplement B"));
        WriteChild(sessionDir, "worker-1.jsonl", worker1, rootId, repo,
            Assistant("2026-09-23T10:01:50Z", "Implemented A."));
        WriteChild(sessionDir, "worker-2.jsonl", worker2, rootId, repo,
            Assistant("2026-09-23T10:02:50Z", "Implemented B."));
        WriteChild(sessionDir, "planner-2.jsonl", planner2, rootId, repo,
            Assistant("2026-09-23T10:03:20Z", "# Done"));

        var report = Assert.Single(new FactoryReportEngine().FindRuns(repo, codex));

        Assert.Equal("completed", report.Run.Result);
        Assert.Equal(2, report.Metrics.PlannerInvocations);
        Assert.Equal(2, report.Metrics.WorkerInvocations);
        Assert.Equal(2, report.Tasks.Count);
        Assert.Equal(160, report.Agents.Single(x => x.Role == "root").Tokens.InputTokens);
        Assert.Equal(50, report.Agents.Single(x => x.Role == "root").Tokens.CachedInputTokens);
        Assert.Equal(110, report.Agents.Single(x => x.Role == "root").Tokens.NewInputTokens);
        Assert.Equal(40, report.Agents.Single(x => x.Role == "root").Tokens.OutputTokens);
        Assert.Equal(DateTimeOffset.Parse("2026-09-23T10:04:31Z"), report.Run.FinishedAt);
        Assert.Equal(1, report.Metrics.Tools.Commands);
        Assert.Equal(0, report.Metrics.Tools.FailedCommands);
        Assert.DoesNotContain(report.Timeline, x => x.Timestamp >= DateTimeOffset.Parse("2026-09-23T10:04:40Z"));
    }

    [Fact]
    public void Report_DistinguishesMultipleRunsInOneThread()
    {
        var repo = Path.Combine(_root, "repo-multiple");
        var codex = Path.Combine(_root, "codex-multiple");
        Directory.CreateDirectory(repo);
        var dir = Path.Combine(codex, "sessions", "2026", "09", "23");
        Directory.CreateDirectory(dir);

        const string rootId = "11111111-2222-3333-4444-555555555555";
        Write(Path.Combine(dir, "rollout.jsonl"),
        [
            Session(rootId, repo),
            User("2026-09-23T08:00:00Z", "idd-factory-run first"),
            Assistant("2026-09-23T08:01:00Z", "Factory completed."),
            User("2026-09-23T09:00:00Z", "idd-factory-run second"),
            Assistant("2026-09-23T09:02:00Z", "Factory completed.")
        ]);

        var reports = new FactoryReportEngine().FindRuns(repo, codex);

        Assert.Equal(2, reports.Count);
        Assert.Equal(1, reports[0].Run.RunIndex);
        Assert.Equal(2, reports[1].Run.RunIndex);
        Assert.Equal(DateTimeOffset.Parse("2026-09-23T08:00:00Z"), reports[0].Run.StartedAt);
        Assert.Equal(DateTimeOffset.Parse("2026-09-23T09:00:00Z"), reports[1].Run.StartedAt);
    }

    [Fact]
    public void ToolMetrics_DeduplicateLifecycleAndCountOverlappingBatch()
    {
        var path = Path.Combine(_root, "tools.jsonl");
        Write(path,
        [
            Session("99999999-9999-9999-9999-999999999999", _root),
            ToolStarted("2026-09-23T10:00:00Z", "a", "mcp_tool_call", "search"),
            ToolStarted("2026-09-23T10:00:01Z", "b", "command_execution", "exec_command"),
            ToolCompleted("2026-09-23T10:00:02Z", "a", "mcp_tool_call", "search", 0),
            ToolCompleted("2026-09-23T10:00:03Z", "b", "command_execution", "exec_command", 1)
        ]);

        var rollout = new CodexRolloutReader().Read(path);
        var method = typeof(FactoryReportEngine).GetMethod("FindRuns");
        Assert.NotNull(method);

        // Exercise the same event semantics through a minimal Factory run.
        var repo = Path.Combine(_root, "repo-tools");
        var codex = Path.Combine(_root, "codex-tools");
        Directory.CreateDirectory(repo);
        var dir = Path.Combine(codex, "sessions");
        Directory.CreateDirectory(dir);
        Write(Path.Combine(dir, "run.jsonl"),
        [
            Session("88888888-8888-8888-8888-888888888888", repo),
            User("2026-09-23T10:00:00Z", "idd-factory-run"),
            ToolStarted("2026-09-23T10:00:01Z", "a", "mcp_tool_call", "search"),
            ToolStarted("2026-09-23T10:00:02Z", "b", "command_execution", "exec_command"),
            ToolCompleted("2026-09-23T10:00:03Z", "a", "mcp_tool_call", "search", 0),
            ToolCompleted("2026-09-23T10:00:04Z", "b", "command_execution", "exec_command", 1),
            Assistant("2026-09-23T10:00:05Z", "Factory completed.")
        ]);

        var report = Assert.Single(new FactoryReportEngine().FindRuns(repo, codex));
        var root = report.Agents.Single(x => x.Role == "root");
        Assert.Equal(2, root.Tools.ToolCalls);
        Assert.Equal(1, root.Tools.ToolBatches);
        Assert.Equal(1, root.Tools.Commands);
        Assert.Equal(1, root.Tools.FailedCommands);
    }

    [Fact]
    public void JsonWriter_UsesVersionedSchemaAndNullForUnavailableMetrics()
    {
        var path = Path.Combine(_root, "report.json");
        var report = new FactoryRunReport
        {
            Repository = _root,
            Run = new RunInfo { RootThreadId = "thread", RunIndex = 1 }
        };

        ReportWriters.WriteJson(report, path);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(JsonValueKind.Null,
            document.RootElement.GetProperty("metrics").GetProperty("tokens").GetProperty("inputTokens").ValueKind);
    }

    [Fact]
    public void CodexHomeLocator_UsesExplicitThenEnvironment()
    {
        var explicitPath = Path.Combine(_root, "explicit");
        Assert.Equal(Path.GetFullPath(explicitPath), CodexHomeLocator.Resolve(explicitPath));

        var previous = Environment.GetEnvironmentVariable("CODEX_HOME");
        try
        {
            var envPath = Path.Combine(_root, "env");
            Environment.SetEnvironmentVariable("CODEX_HOME", envPath);
            Assert.Equal(Path.GetFullPath(envPath), CodexHomeLocator.Resolve(null));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previous);
        }
    }


    [Fact]
    public void Report_MarksWorkerWithoutTerminalEvidenceAsInterrupted()
    {
        var repo = Path.Combine(_root, "repo-interrupted");
        var codex = Path.Combine(_root, "codex-interrupted");
        Directory.CreateDirectory(repo);
        var dir = Path.Combine(codex, "sessions");
        Directory.CreateDirectory(dir);

        const string rootId = "12121212-1212-1212-1212-121212121212";
        const string workerId = "34343434-3434-3434-3434-343434343434";

        Write(Path.Combine(dir, "root.jsonl"),
        [
            Session(rootId, repo),
            User("2026-09-23T10:00:00Z", "idd-factory-run"),
            SpawnCall("2026-09-23T10:00:01Z", "w1", "Use idd-factory-execute-subtask. Task: incomplete work."),
            SpawnOutput("2026-09-23T10:00:02Z", "w1", workerId)
        ]);

        Write(Path.Combine(dir, "worker.jsonl"),
        [
            Session(workerId, repo, rootId),
            User("2026-09-23T10:00:03Z", "Factory child context")
        ]);

        var report = Assert.Single(new FactoryReportEngine().FindRuns(repo, codex));

        Assert.Equal("interrupted", report.Run.Result);
        Assert.Contains("child_without_completion", report.Run.ResultEvidence);
        Assert.Single(report.Tasks);
        Assert.Equal("interrupted", report.Tasks[0].Status);
    }

    [Fact]
    public void Report_ReconstructsNestedSpawnLinkageWithoutChildParentMetadata()
    {
        var repo = Path.Combine(_root, "repo-nested");
        var codex = Path.Combine(_root, "codex-nested");
        Directory.CreateDirectory(repo);
        var dir = Path.Combine(codex, "sessions");
        Directory.CreateDirectory(dir);

        const string rootId = "45454545-4545-4545-4545-454545454545";
        const string workerId = "56565656-5656-5656-5656-565656565656";
        const string subagentId = "67676767-6767-6767-6767-676767676767";

        Write(Path.Combine(dir, "root.jsonl"),
        [
            Session(rootId, repo),
            User("2026-09-23T10:00:00Z", "idd-factory-run"),
            SpawnCall("2026-09-23T10:00:01Z", "w1", "Use idd-factory-execute-subtask. Task: implementation."),
            SpawnOutput("2026-09-23T10:00:02Z", "w1", workerId),
            Assistant("2026-09-23T10:02:00Z", "Factory completed.")
        ]);

        Write(Path.Combine(dir, "worker.jsonl"),
        [
            Session(workerId, repo, rootId),
            User("2026-09-23T10:00:03Z", "Use idd-factory-execute-subtask."),
            SpawnCall("2026-09-23T10:00:10Z", "sub1", "Inspect the implementation independently."),
            SpawnOutput("2026-09-23T10:00:11Z", "sub1", subagentId),
            Assistant("2026-09-23T10:01:30Z", "Worker completed.")
        ]);

        Write(Path.Combine(dir, "subagent.jsonl"),
        [
            Session(subagentId, repo),
            User("2026-09-23T10:00:12Z", "Inspect the implementation independently."),
            Assistant("2026-09-23T10:01:00Z", "Inspection complete.")
        ]);

        var report = Assert.Single(new FactoryReportEngine().FindRuns(repo, codex));
        var subagent = Assert.Single(report.Agents.Where(x => x.ThreadId == subagentId));

        Assert.Equal("subagent", subagent.Role);
        Assert.Equal(workerId, subagent.ParentThreadId);
        Assert.Equal(2, report.Metrics.MaximumDepth);
    }

    [Fact]
    public void Report_MatchesSessionCwdInsideRepository()
    {
        var repo = Path.Combine(_root, "repo-subdir");
        var cwd = Path.Combine(repo, "src", "feature");
        var codex = Path.Combine(_root, "codex-subdir");
        Directory.CreateDirectory(cwd);
        var dir = Path.Combine(codex, "sessions");
        Directory.CreateDirectory(dir);

        Write(Path.Combine(dir, "root.jsonl"),
        [
            Session("78787878-7878-7878-7878-787878787878", cwd),
            User("2026-09-23T10:00:00Z", "idd-factory-run"),
            Assistant("2026-09-23T10:00:30Z", "Factory completed.")
        ]);

        var report = Assert.Single(new FactoryReportEngine().FindRuns(repo, codex));
        Assert.Equal("completed", report.Run.Result);
    }

    [Fact]
    public void RealNativeFactoryTrace_IsReportedCorrectly()
    {
        var repo = Path.Combine(_root, "repo-native");
        var codex = Path.Combine(_root, "codex-native");
        Directory.CreateDirectory(repo);
        var dir = Path.Combine(codex, "sessions", "2026", "09", "23");
        Directory.CreateDirectory(dir);

        const string rootId = "10101010-1010-1010-1010-101010101010";
        const string planner1 = "20202020-2020-2020-2020-202020202020";
        const string worker1 = "30303030-3030-3030-3030-303030303030";
        const string worker2 = "40404040-4040-4040-4040-404040404040";
        const string planner2 = "50505050-5050-5050-5050-505050505050";

        Write(Path.Combine(dir, "root.jsonl"),
        [
            Session(rootId, repo),
            User("2026-09-23T10:00:00Z", "Run idd-factory-run for the requested change."),
            NativeSpawn("2026-09-23T10:00:01Z", "p1", "Use idd-factory-decompose-task and return the next task.", planner1),
            NativeSpawn("2026-09-23T10:00:10Z", "w1", "Use idd-factory-execute-subtask. Task: Implement MiniCatalog.ProductCode.", worker1),
            NativeSpawn("2026-09-23T10:01:10Z", "w2", "Use idd-factory-execute-subtask. Task: Update MiniCatalog.Catalog.", worker2),
            NativeSpawn("2026-09-23T10:02:10Z", "p2", "Use idd-factory-decompose-task and decide what remains.", planner2),
            NativeCommandCompleted("2026-09-23T10:03:00Z", "cmd-failed", "type missing-SKILL.md", 1),
            NativeCommandCompleted("2026-09-23T10:03:10Z", "cmd-ok", "dotnet test", 0),
            Tokens("2026-09-23T10:03:20Z", 901767, 813568, 3477),
            Assistant("2026-09-23T10:03:30Z", "{\"status\":\"COMPLETED\",\"reason\":\"Implemented ProductCode and Catalog\"}")
        ]);

        Write(Path.Combine(dir, "planner-1.jsonl"),
        [
            Session(planner1, repo),
            User("2026-09-23T10:00:02Z", "Factory context mentions idd-factory-decompose-task and idd-factory-execute-subtask."),
            Assistant("2026-09-23T10:00:05Z", "# Task\nImplement MiniCatalog.ProductCode")
        ]);

        Write(Path.Combine(dir, "worker-1.jsonl"),
        [
            Session(worker1, repo),
            User("2026-09-23T10:00:11Z", "Inherited Factory context mentions idd-factory-decompose-task too."),
            Tokens("2026-09-23T10:00:50Z", 1200, 700, 90),
            Assistant("2026-09-23T10:01:00Z", "Implemented ProductCode.")
        ]);

        Write(Path.Combine(dir, "worker-2.jsonl"),
        [
            Session(worker2, repo),
            User("2026-09-23T10:01:11Z", "Inherited Factory context mentions idd-factory-decompose-task too."),
            Tokens("2026-09-23T10:01:50Z", 900, 400, 70),
            Assistant("2026-09-23T10:02:00Z", "Updated Catalog.")
        ]);

        Write(Path.Combine(dir, "planner-2.jsonl"),
        [
            Session(planner2, repo),
            User("2026-09-23T10:02:11Z", "Factory planner context."),
            Assistant("2026-09-23T10:02:30Z", "# Done")
        ]);

        var report = Assert.Single(new FactoryReportEngine().FindRuns(repo, codex));

        Assert.Equal("completed", report.Run.Result);
        Assert.Equal("Implemented ProductCode and Catalog", report.Run.Reason);
        Assert.Equal("exact", report.Run.EndBoundary.Confidence);
        Assert.Contains("structured_final_response", report.Run.ResultEvidence);
        Assert.Equal(2, report.Metrics.PlannerInvocations);
        Assert.Equal(2, report.Metrics.WorkerInvocations);
        Assert.Equal(2, report.Tasks.Count);
        Assert.All(report.Tasks, task => Assert.Equal("completed", task.Status));
        Assert.Contains("ProductCode", report.Tasks[0].Text);
        Assert.Contains("Catalog", report.Tasks[1].Text);
        Assert.All(report.Tasks, task => Assert.NotNull(task.DurationMilliseconds));
        Assert.Equal(2, report.Metrics.Tools.Commands);
        Assert.Equal(1, report.Metrics.Tools.FailedCommands);
        Assert.Equal(901767, report.Metrics.RootReportedTokens.InputTokens);
        Assert.Equal(813568, report.Metrics.RootReportedTokens.CachedInputTokens);
        Assert.Equal(88199, report.Metrics.RootReportedTokens.NewInputTokens);
        Assert.Equal(3477, report.Metrics.RootReportedTokens.OutputTokens);
        Assert.Equal("overlap-unknown", report.Metrics.TokenAggregationStatus);
        Assert.False(report.Metrics.Tokens.Available);
        Assert.Contains(report.Diagnostics, x => x.Category == "factory" && x.Code == "failed_command");
        Assert.Contains(report.Diagnostics, x => x.Category == "reporter" && x.Code == "token_aggregation_overlap_unknown");
        Assert.DoesNotContain(report.Diagnostics, x => x.Code == "factory_run_without_worker");
        Assert.DoesNotContain(report.Diagnostics, x => x.Code == "factory_boundary_not_exact");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static void WriteChild(string dir, string file, string id, string parent, string cwd, object finalEvent)
    {
        Write(Path.Combine(dir, file),
        [
            Session(id, cwd, parent),
            User("2026-09-23T10:00:06Z", "Factory child context"),
            finalEvent
        ]);
    }

    private static void Write(string path, IEnumerable<object> lines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Concat(lines.Select(Line)), Encoding.UTF8);
    }

    private static string Line(object value) => JsonSerializer.Serialize(value) + "\n";

    private static object Session(string id, string cwd, string? parent = null) => new
    {
        timestamp = "2026-09-23T09:00:00Z",
        type = "session_meta",
        payload = new { id, cwd, parent_thread_id = parent }
    };

    private static object User(string timestamp, string text) => Message(timestamp, "user", "input_text", text);
    private static object Assistant(string timestamp, string text) => Message(timestamp, "assistant", "output_text", text);

    private static object Message(string timestamp, string role, string contentType, string text) => new
    {
        timestamp,
        type = "response_item",
        payload = new
        {
            type = "message",
            role,
            content = new[] { new { type = contentType, text } }
        }
    };

    private static object Tokens(string timestamp, long input, long cached, long output) => new
    {
        timestamp,
        type = "event_msg",
        payload = new
        {
            type = "token_count",
            info = new
            {
                total_token_usage = new
                {
                    input_tokens = input,
                    cached_input_tokens = cached,
                    output_tokens = output
                }
            }
        }
    };

    private static object SpawnCall(string timestamp, string callId, string task) => new
    {
        timestamp,
        type = "response_item",
        payload = new
        {
            type = "function_call",
            name = "spawn_agent",
            call_id = callId,
            arguments = JsonSerializer.Serialize(new { message = task })
        }
    };

    private static object SpawnOutput(string timestamp, string callId, string childId) => new
    {
        timestamp,
        type = "response_item",
        payload = new
        {
            type = "function_call_output",
            call_id = callId,
            output = $"spawned child thread {childId}"
        }
    };

    private static object NativeSpawn(string timestamp, string id, string prompt, string childId) => new
    {
        timestamp,
        type = "item.completed",
        item = new
        {
            id,
            type = "collab_tool_call",
            tool = "spawn_agent",
            prompt,
            receiver_thread_ids = new[] { childId },
            status = "completed"
        }
    };

    private static object NativeCommandCompleted(string timestamp, string id, string command, int exitCode) => new
    {
        timestamp,
        type = "item.completed",
        item = new
        {
            id,
            type = "command_execution",
            command,
            exit_code = exitCode,
            status = exitCode == 0 ? "completed" : "failed",
            aggregated_output = exitCode == 0 ? "ok" : "failed"
        }
    };

    private static object CommandStarted(string timestamp, string id, string command) =>
        ToolStarted(timestamp, id, "command_execution", "exec_command", command);

    private static object CommandCompleted(string timestamp, string id, string command, int exitCode) =>
        ToolCompleted(timestamp, id, "command_execution", "exec_command", exitCode, command);

    private static object ToolStarted(string timestamp, string id, string type, string name, string? command = null) => new
    {
        timestamp,
        type = "item.started",
        item = new { id, type, name, command }
    };

    private static object ToolCompleted(string timestamp, string id, string type, string name, int exitCode, string? command = null) => new
    {
        timestamp,
        type = "item.completed",
        item = new { id, type, name, command, exit_code = exitCode, status = exitCode == 0 ? "completed" : "failed" }
    };
}
