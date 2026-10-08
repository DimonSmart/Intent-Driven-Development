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
        Assert.Equal(2, rollout.Diagnostics.Count);
        Assert.All(rollout.Diagnostics, diagnostic => Assert.Equal("malformed_rollout_line", diagnostic.Code));
        Assert.Contains("final line may be truncated", rollout.Diagnostics[1].Message);
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
    public void Report_RecordsProfileAndRequestedSettingsWithoutInferringUnknownActualSettings()
    {
        var repo = Path.Combine(_root, "repo-routing");
        var codex = Path.Combine(_root, "codex-routing");
        var dir = Path.Combine(codex, "sessions");
        Directory.CreateDirectory(repo);
        Directory.CreateDirectory(dir);

        const string rootId = "01010101-0101-0101-0101-010101010101";
        const string plannerId = "02020202-0202-0202-0202-020202020202";
        const string economyId = "03030303-0303-0303-0303-030303030303";
        const string standardId = "04040404-0404-0404-0404-040404040404";
        const string strongId = "05050505-0505-0505-0505-050505050505";
        const string doneId = "06060606-0606-0606-0606-060606060606";

        Write(Path.Combine(dir, "root.jsonl"),
        [
            Session(rootId, repo),
            User("2026-09-23T10:00:00Z", "idd-factory-run"),
            SpawnCall("2026-09-23T10:00:01Z", "planner", "Use idd-factory-decompose-task."),
            SpawnOutput("2026-09-23T10:00:02Z", "planner", plannerId),
            SpawnCall("2026-09-23T10:00:03Z", "economy", "Use idd-factory-execute-subtask. Task: economy.", "model-a", "low"),
            SpawnOutput("2026-09-23T10:00:04Z", "economy", economyId),
            SpawnCall("2026-09-23T10:00:05Z", "standard", "Use idd-factory-execute-subtask. Task: standard.", "model-a", "medium"),
            SpawnOutput("2026-09-23T10:00:06Z", "standard", standardId),
            SpawnCall("2026-09-23T10:00:07Z", "strong", "Use idd-factory-execute-subtask. Task: strong.", "model-b", "high"),
            SpawnOutput("2026-09-23T10:00:08Z", "strong", strongId, "model-c", "high"),
            SpawnCall("2026-09-23T10:00:09Z", "done", "Use idd-factory-decompose-task."),
            SpawnOutput("2026-09-23T10:00:10Z", "done", doneId),
            Assistant("2026-09-23T10:00:20Z", "Factory completed.")
        ]);
        WriteChild(dir, "planner.jsonl", plannerId, rootId, repo,
            Assistant("2026-09-23T10:00:02Z", "# Task\neconomy\n\n# ExecutionProfile\neconomy\n\n# Task\nstandard\n\n# ExecutionProfile\nstandard\n\n# Task\nstrong\n\n# ExecutionProfile\nstrong"));
        WriteChild(dir, "economy.jsonl", economyId, rootId, repo, Assistant("2026-09-23T10:00:04Z", "done"));
        WriteChild(dir, "standard.jsonl", standardId, rootId, repo, Assistant("2026-09-23T10:00:06Z", "done"));
        WriteChild(dir, "strong.jsonl", strongId, rootId, repo, Assistant("2026-09-23T10:00:08Z", "done"));
        WriteChild(dir, "done.jsonl", doneId, rootId, repo, Assistant("2026-09-23T10:00:10Z", "# Done"));

        var report = Assert.Single(new FactoryReportEngine().FindRuns(repo, codex));
        Assert.Collection(report.Tasks.OrderBy(x => x.ExecutionProfile),
            economy =>
            {
                Assert.Equal("economy", economy.ExecutionProfile);
                Assert.Equal("model-a", economy.RequestedModel);
                Assert.Null(economy.ActualModel);
            },
            standard =>
            {
                Assert.Equal("standard", standard.ExecutionProfile);
                Assert.Equal("model-a", standard.RequestedModel);
                Assert.Equal("medium", standard.RequestedReasoningEffort);
            },
            strong =>
            {
                Assert.Equal("strong", strong.ExecutionProfile);
                Assert.Equal("model-b", strong.RequestedModel);
                Assert.Equal("model-c", strong.ActualModel);
            });
        Assert.Contains(report.Diagnostics, x => x.Code == "worker_execution_settings_mismatch");
    }

    [Fact]
    public void Report_PreservesPlannerProfileAndDiagnosesConflictingSpawnProfile()
    {
        var report = RoutingReport("# ExecutionProfile\neconomy", "strong", "unknown");
        var task = Assert.Single(report.Tasks);
        Assert.Equal("economy", task.ExecutionProfile);
        Assert.Equal("strong", task.SpawnExecutionProfile);
        var agent = Assert.Single(report.Agents, x => x.Role == "worker");
        Assert.Equal(task.ExecutionProfile, agent.ExecutionProfile);
        Assert.Equal(task.SpawnExecutionProfile, agent.SpawnExecutionProfile);
        Assert.Contains(report.Diagnostics, x => x.Code == "worker_execution_profile_mismatch");
        var markdown = Path.Combine(_root, "routing-report.md");
        ReportWriters.WriteMarkdown(report, markdown, verbose: false);
        Assert.Contains("Execution profile: economy", File.ReadAllText(markdown));
        Assert.Contains("Spawn execution profile: strong", File.ReadAllText(markdown));
    }

    [Theory]
    [InlineData("")]
    [InlineData("# ExecutionProfile\neconomy\n\n# ExecutionProfile\nstrong")]
    [InlineData("# ExecutionProfile\nunknown")]
    public void Report_DoesNotReplaceMissingOrMalformedPlannerProfileWithSpawnProfile(string metadata)
    {
        var report = RoutingReport(metadata, "strong", "unknown");
        var task = Assert.Single(report.Tasks);
        Assert.Null(task.ExecutionProfile);
        Assert.Equal("strong", task.SpawnExecutionProfile);
        Assert.Contains(report.Diagnostics, x => x.Code == "worker_execution_profile_unavailable");
    }

    [Theory]
    [InlineData("nested")]
    [InlineData("output-event")]
    [InlineData("native-event")]
    [InlineData("child-event")]
    [InlineData("turn-context")]
    public void Report_RecognizesExplicitActualSettingsAndReportsMismatch(string settingsLocation)
    {
        var report = RoutingReport("# ExecutionProfile\neconomy", "economy", settingsLocation);
        var task = Assert.Single(report.Tasks);
        Assert.Equal("model-a", task.RequestedModel);
        Assert.Equal("low", task.RequestedReasoningEffort);
        Assert.Equal("model-b", task.ActualModel);
        Assert.Equal("high", task.ActualReasoningEffort);
        Assert.Contains(report.Diagnostics, x => x.Code == "worker_execution_settings_mismatch");
        Assert.DoesNotContain(report.Diagnostics, x => x.Code == "worker_execution_profile_mismatch");
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("grandchild-event")]
    [InlineData("foreign-context")]
    public void Report_DoesNotInferWorkerSettingsFromDefaultsOrGrandchildSpawns(string settingsLocation)
    {
        var report = RoutingReport("# ExecutionProfile\neconomy", "economy", settingsLocation);
        var task = Assert.Single(report.Tasks);
        Assert.Null(task.ActualModel);
        Assert.Null(task.ActualReasoningEffort);
        Assert.DoesNotContain(report.Diagnostics, x => x.Code == "worker_execution_settings_mismatch");
        Assert.Contains(report.Diagnostics, x => x.Code == "worker_actual_settings_unavailable");
    }

    [Theory]
    [InlineData("model-only-context", "reasoning effort")]
    [InlineData("invalid-context", "model and reasoning effort")]
    public void Report_ExplainsMissingActualSettingsInEveryFormat(string settingsLocation, string missing)
    {
        var report = RoutingReport("# ExecutionProfile\neconomy", "economy", settingsLocation);
        var diagnostic = Assert.Single(report.Diagnostics, x => x.Code == "worker_actual_settings_unavailable");
        Assert.Contains("actual " + missing, diagnostic.Message);
        Assert.Contains(report.Tasks[0].AgentThreadId, diagnostic.Message);
        if (settingsLocation == "invalid-context")
            Assert.Equal(2, report.Diagnostics.Count(x => x.Code == "invalid_turn_context_setting"));

        var markdown = Path.Combine(_root, "missing-settings.md");
        var json = Path.Combine(_root, "missing-settings.json");
        ReportWriters.WriteMarkdown(report, markdown, verbose: false);
        ReportWriters.WriteJson(report, json);
        using var console = new StringWriter();
        ReportWriters.WriteConsole(report, console, verbose: false);
        Assert.Contains(diagnostic.Message, File.ReadAllText(markdown));
        Assert.Contains("worker_actual_settings_unavailable", File.ReadAllText(json));
        Assert.Contains(diagnostic.Message, console.ToString());
    }

    [Fact]
    public void Report_DiagnosesReasoningMismatchEvenWhenTheModelMatches()
    {
        var report = RoutingReport("# ExecutionProfile\neconomy", "economy", "reasoning-only");
        var task = Assert.Single(report.Tasks);
        Assert.Equal(task.RequestedModel, task.ActualModel);
        Assert.Equal("high", task.ActualReasoningEffort);
        Assert.Contains(report.Diagnostics, x => x.Code == "worker_execution_settings_mismatch");
    }

    [Fact]
    public void Report_DoesNotDiagnoseMatchingActualSettings()
    {
        var report = RoutingReport("# ExecutionProfile\neconomy", "economy", "matching");
        var task = Assert.Single(report.Tasks);
        Assert.Equal(task.RequestedModel, task.ActualModel);
        Assert.Equal(task.RequestedReasoningEffort, task.ActualReasoningEffort);
        Assert.DoesNotContain(report.Diagnostics, x => x.Code == "worker_execution_settings_mismatch");
    }

    [Fact]
    public void Report_AssociatesOpaqueSpawnArgumentsWithPlannerProfilesUsingChildMetadata()
    {
        var report = RoutingReport("# ExecutionProfile\neconomy", "economy", "unknown", opaqueSpawn: true);
        var task = Assert.Single(report.Tasks);
        Assert.Equal("economy", task.ExecutionProfile);
        Assert.Null(task.SpawnExecutionProfile);
        Assert.Equal("model-a", task.RequestedModel);
        Assert.Equal("low", task.RequestedReasoningEffort);
        Assert.DoesNotContain(report.Diagnostics, x => x.Code == "agent_role_unresolved");
        Assert.Equal("Implement A.", task.Text);
        Assert.Contains(report.Diagnostics, x => x.Code == "spawn_prompt_unreadable");
    }

    [Fact]
    public void Report_CountsResponseItemSpawnsAlongsideNativeCommands()
    {
        var report = RoutingReport("# ExecutionProfile\neconomy", "economy", "mixed-native");
        Assert.Equal(2, report.Metrics.Tools.SpawnAgentCalls);
        Assert.Equal(1, report.Metrics.Tools.Commands);
    }

    [Fact]
    public void Report_ResolvesNativeTaskNameOutputToChildThreadAndKeepsRequestedSettings()
    {
        var report = RoutingReport("# ExecutionProfile\neconomy", "economy", "path-output", opaqueSpawn: true);
        var task = Assert.Single(report.Tasks);
        Assert.Equal("economy", task.ExecutionProfile);
        Assert.Equal("33333333-3333-3333-3333-333333333333", task.AgentThreadId);
        Assert.Equal("model-a", task.RequestedModel);
        Assert.Equal("low", task.RequestedReasoningEffort);
    }

    private FactoryRunReport RoutingReport(string plannerMetadata, string spawnProfile, string settingsLocation,
        bool opaqueSpawn = false)
    {
        var repo = Path.Combine(_root, "repo-routing-regression");
        var codex = Path.Combine(_root, "codex-routing-regression");
        var dir = Path.Combine(codex, "sessions");
        Directory.CreateDirectory(repo);
        Directory.CreateDirectory(dir);
        const string rootId = "11111111-1111-1111-1111-111111111111";
        const string plannerId = "22222222-2222-2222-2222-222222222222";
        const string workerId = "33333333-3333-3333-3333-333333333333";
        var prompt = opaqueSpawn ? "gAAAAABopaque_encrypted_spawn_message==" :
            $"Use idd-factory-execute-subtask. Task: Implement A.\nExecutionProfile: {spawnProfile}";
        var lines = new List<object>
        {
            Session(rootId, repo),
            User("2026-09-23T10:00:00Z", "idd-factory-run"),
            new { timestamp = "2026-09-23T10:00:00Z", type = "turn_context",
                payload = new { model = "root-default", effort = "medium" } },
            SpawnCall("2026-09-23T10:00:01Z", "planner",
                opaqueSpawn ? "opaque encrypted planner message" : "Use idd-factory-decompose-task."),
            SpawnOutput("2026-09-23T10:00:02Z", "planner", plannerId),
            SpawnCall("2026-09-23T10:00:03Z", "worker", prompt, "model-a", "low")
        };
        lines.Add(settingsLocation switch
        {
            "nested" => SpawnOutput("2026-09-23T10:00:04Z", "worker", workerId, "model-b", "high"),
            "reasoning-only" => SpawnOutput("2026-09-23T10:00:04Z", "worker", workerId, "model-a", "high"),
            "matching" => SpawnOutput("2026-09-23T10:00:04Z", "worker", workerId, "model-a", "low"),
            "path-output" => new
            {
                timestamp = "2026-09-23T10:00:04Z", type = "response_item",
                payload = new
                {
                    type = "function_call_output", call_id = "worker",
                    output = JsonSerializer.Serialize(new { task_name = "/root/worker" })
                }
            },
            "output-event" => new
            {
                timestamp = "2026-09-23T10:00:04Z", type = "response_item",
                payload = new
                {
                    type = "function_call_output", call_id = "worker",
                    output = $"spawned child thread {workerId}",
                    actual_model = "model-b", actual_reasoning_effort = "high"
                }
            },
            "native-event" => new
            {
                timestamp = "2026-09-23T10:00:04Z", type = "item.completed",
                item = new
                {
                    id = "worker", type = "collab_tool_call", tool = "spawn_agent", prompt,
                    receiver_thread_ids = new[] { workerId }, status = "completed",
                    resolved_model = "model-b", resolved_reasoning_effort = "high"
                }
            },
            _ => SpawnOutput("2026-09-23T10:00:04Z", "worker", workerId)
        });
        if (settingsLocation == "mixed-native")
            lines.Add(NativeCommandCompleted("2026-09-23T10:00:15Z", "check", "dotnet test", 0));
        lines.Add(Assistant("2026-09-23T10:00:20Z", "Factory completed."));
        Write(Path.Combine(dir, "root.jsonl"), lines);
        Write(Path.Combine(dir, "planner.jsonl"),
        [
            Session(plannerId, repo, rootId, "/root/planner"),
            Assistant("2026-09-23T10:00:02Z", $"# Task\nImplement A.\n\n{plannerMetadata}")
        ]);
        var workerEvents = new List<object> { Session(workerId, repo, rootId, "/root/worker") };
        if (settingsLocation == "turn-context")
            workerEvents.Add(new
            {
                timestamp = "2026-09-23T10:00:05Z", type = "turn_context",
                payload = new { model = "model-b", effort = "high" }
            });
        if (settingsLocation == "foreign-context")
            workerEvents.Add(new
            {
                timestamp = "2026-09-23T10:00:05Z", type = "turn_context",
                payload = new { thread_id = "44444444-4444-4444-4444-444444444444", model = "model-b", effort = "high" }
            });
        if (settingsLocation == "model-only-context")
            workerEvents.Add(new
            {
                timestamp = "2026-09-23T10:00:05Z", type = "turn_context",
                payload = new { model = "model-a", collaboration_mode = new { model = "nested-default", effort = "high" } }
            });
        if (settingsLocation == "invalid-context")
            workerEvents.Add(new
            {
                timestamp = "2026-09-23T10:00:05Z", type = "turn_context",
                payload = new { model = 42, effort = "" }
            });
        if (settingsLocation == "child-event")
            workerEvents.Add(new
            {
                timestamp = "2026-09-23T10:00:05Z", type = "event_msg",
                payload = new { type = "execution_settings", resolved_model = "model-b", resolved_reasoning_effort = "high" }
            });
        if (settingsLocation == "grandchild-event")
        {
            workerEvents.Add(SpawnCall("2026-09-23T10:00:05Z", "grandchild", "Research helper."));
            workerEvents.Add(SpawnOutput("2026-09-23T10:00:06Z", "grandchild",
                "44444444-4444-4444-4444-444444444444", "model-b", "high"));
        }
        workerEvents.Add(Assistant("2026-09-23T10:00:10Z", "done"));
        Write(Path.Combine(dir, "worker.jsonl"), workerEvents);
        if (settingsLocation == "path-output")
            Write(Path.Combine(dir, "unrelated-worker.jsonl"),
            [
                Session("55555555-5555-5555-5555-555555555555", repo,
                    "66666666-6666-6666-6666-666666666666", "/root/worker"),
                Assistant("2026-09-23T10:00:10Z", "Unrelated worker with the same path.")
            ]);
        return Assert.Single(new FactoryReportEngine().FindRuns(repo, codex));
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
    public void Report_FindsRolloutWhenRepositoryPathUsesSymlink()
    {
        if (OperatingSystem.IsWindows())
            return;

        var repository = Path.Combine(_root, "physical-repository");
        var aliasedRepository = Path.Combine(_root, "repository-alias");
        var codex = Path.Combine(_root, "codex-symlink");
        Directory.CreateDirectory(repository);
        Directory.CreateSymbolicLink(aliasedRepository, repository);
        var sessions = Path.Combine(codex, "sessions");
        Directory.CreateDirectory(sessions);

        Write(Path.Combine(sessions, "rollout.jsonl"),
        [
            Session("12121212-1212-1212-1212-121212121212", aliasedRepository),
            User("2026-09-23T10:00:00Z", "idd-factory-run"),
            Assistant("2026-09-23T10:01:00Z", "Factory completed.")
        ]);

        var report = Assert.Single(new FactoryReportEngine().FindRuns(repository, codex));

        Assert.Equal("completed", report.Run.Result);
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
        Assert.Equal(3, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(JsonValueKind.Null,
            document.RootElement.GetProperty("metrics").GetProperty("tokens").GetProperty("inputTokens").ValueKind);
    }

    [Fact]
    public void MarkdownWriter_UsesSingleHeadingsAndIncludesFullTaskText()
    {
        var path = Path.Combine(_root, "report.md");
        var task = "Implement the catalog value type with canonicalization and duplicate detection.";
        var report = new FactoryRunReport
        {
            Agents =
            [
                new AgentReport
                {
                    ThreadId = "worker",
                    Role = "worker",
                    Task = task,
                    TaskTitle = "Implement the catalog value type"
                }
            ],
            Tasks =
            [
                new TaskReport
                {
                    Number = 1,
                    AgentThreadId = "worker",
                    Text = task,
                    Status = "completed"
                }
            ]
        };

        ReportWriters.WriteMarkdown(report, path, verbose: false);
        var markdown = File.ReadAllText(path);

        Assert.Equal(1, markdown.Split("## Tasks", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, markdown.Split("## Token accounting", StringSplitOptions.None).Length - 1);
        Assert.Contains("```text", markdown);
        Assert.Contains(task, markdown);
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
    public void Report_UsesPlannerTaskAndTerminalWorkerResultAfterExploratoryFailure()
    {
        var repo = Path.Combine(_root, "repo-worker-completed");
        var codex = Path.Combine(_root, "codex-worker-completed");
        Directory.CreateDirectory(repo);
        var dir = Path.Combine(codex, "sessions");
        Directory.CreateDirectory(dir);

        const string rootId = "14141414-1414-1414-1414-141414141414";
        const string plannerId = "24242424-2424-2424-2424-242424242424";
        const string workerId = "34343434-3434-3434-3434-343434343434";

        Write(Path.Combine(dir, "root.jsonl"),
        [
            Session(rootId, repo),
            User("2026-09-23T10:00:00Z", "idd-factory-run"),
            SpawnCall("2026-09-23T10:00:01Z", "p1", "Use idd-factory-decompose-task."),
            SpawnOutput("2026-09-23T10:00:02Z", "p1", plannerId),
            SpawnCall("2026-09-23T10:00:03Z", "w1", "Use idd-factory-execute-subtask."),
            SpawnOutput("2026-09-23T10:00:04Z", "w1", workerId),
            Assistant("2026-09-23T10:01:00Z", "Factory completed.")
        ]);

        Write(Path.Combine(dir, "planner.jsonl"),
        [
            Session(plannerId, repo, rootId),
            Assistant("2026-09-23T10:00:10Z", "# Task\nImplement the catalog value type.\n\n# ExecutionProfile\nstandard")
        ]);

        Write(Path.Combine(dir, "worker.jsonl"),
        [
            Session(workerId, repo, rootId),
            NativeCommandCompleted("2026-09-23T10:00:20Z", "probe", "dotnet test", 1),
            Assistant("2026-09-23T10:00:30Z", "Implemented the catalog value type.")
        ]);

        var report = Assert.Single(new FactoryReportEngine().FindRuns(repo, codex));

        var task = Assert.Single(report.Tasks);
        Assert.Equal("Implement the catalog value type.", task.Text);
        Assert.Equal("completed", task.Status);
        Assert.Equal(1, report.Metrics.Tools.FailedCommands);
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
        var subagent = Assert.Single(report.Agents, x => x.ThreadId == subagentId);

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

    private static void WriteChild(string dir, string file, string id, string parent, string cwd, params object[] events)
    {
        Write(Path.Combine(dir, file),
        [
            Session(id, cwd, parent),
            User("2026-09-23T10:00:06Z", "Factory child context"),
            .. events
        ]);
    }

    private static void Write(string path, IEnumerable<object> lines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Concat(lines.Select(Line)), Encoding.UTF8);
    }

    private static string Line(object value) => JsonSerializer.Serialize(value) + "\n";

    private static object Session(string id, string cwd, string? parent = null, string? agentPath = null) => new
    {
        timestamp = "2026-09-23T09:00:00Z",
        type = "session_meta",
        payload = new { id, cwd, parent_thread_id = parent, agent_path = agentPath }
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

    private static object SpawnCall(string timestamp, string callId, string task, string? model = null, string? reasoningEffort = null) => new
    {
        timestamp,
        type = "response_item",
        payload = new
        {
            type = "function_call",
            name = "spawn_agent",
            call_id = callId,
            arguments = JsonSerializer.Serialize(new { message = task, model, reasoning_effort = reasoningEffort })
        }
    };

    private static object SpawnOutput(string timestamp, string callId, string childId, string? actualModel = null, string? actualReasoningEffort = null) => new
    {
        timestamp,
        type = "response_item",
        payload = new
        {
            type = "function_call_output",
            call_id = callId,
            output = actualModel is null
                ? $"spawned child thread {childId}"
                : JsonSerializer.Serialize(new { childThreadId = childId, actual_model = actualModel, actual_reasoning_effort = actualReasoningEffort })
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
