using System.Text.Json;
using Idd.Factory.Report;
using Xunit;

namespace Idd.Factory.Report.Tests;

/// <summary>
/// Deterministic read-only native-rollout fixture: a Supervisor and four distinct
/// Root contexts, with 3+3+3+1 sequential Worker children. Uses real Codex
/// session_meta, spawn-agent call and task_complete event shapes.
/// </summary>
public sealed class FactoryNestedTopologyReportTests
{
    [Fact]
    public void TenWorkerNestedTraceKeepsAllFourRootsAndWorkerBatchAttribution()
    {
        var directory = Path.Combine(Path.GetTempPath(), "idd-report-nested-" + Guid.NewGuid().ToString("N"));
        try
        {
            var home = Path.Combine(directory, "home");
            Directory.CreateDirectory(Path.Combine(home, "sessions"));
            var ids = Enumerable.Range(1, 15)
                .Select(index => $"11111111-0000-4000-8000-{index:000000000000}")
                .ToArray();
            var supervisor = ids[0];
            var roots = ids.Skip(1).Take(4).ToArray();
            var workers = ids.Skip(5).ToArray();
            var start = DateTimeOffset.Parse("2026-11-18T10:00:00Z");

            string Session(int slot) => Path.Combine(home, "sessions", $"rollout-{slot:00}.jsonl");
            var supervisorLines = new List<string>
            {
                SessionMeta(start, supervisor, directory),
                UserMessage(start.AddSeconds(1), "Invoke idd-factory-run now.")
            };

            var workerCursor = 0;
            for (var index = 0; index < 4; index++)
            {
                var batch = index + 1;
                var rootId = roots[index];
                var rootName = $"idd_factory_root_b{batch:0000}";
                var batchTime = start.AddMinutes(1 + index * 10);
                Spawn(supervisorLines, batchTime, $"root-{batch}", rootName, rootId, null);
                var rootLines = new List<string>
                {
                    SessionMeta(batchTime.AddSeconds(1), rootId, directory,
                        supervisor, $"/root/{rootName}"),
                    UserMessage(batchTime.AddSeconds(2),
                        "Read installed Factory Root Skill; continue the saved request.")
                };
                var count = index == 3 ? 1 : 3;
                for (var i = 0; i < count; i++)
                {
                    var workerId = workers[workerCursor++];
                    var taskName = $"idd_factory_worker_b{batch:0000}_{i + 1:0000}";
                    var workerTime = batchTime.AddSeconds(10 + 40 * i);
                    Spawn(rootLines, workerTime, $"worker-{batch}-{i + 1}", taskName, workerId, "standard");
                    File.WriteAllLines(Session(5 + workerCursor), new[]
                    {
                        SessionMeta(workerTime.AddSeconds(1), workerId, directory, rootId,
                            $"/root/{rootName}/{taskName}"),
                        UserMessage(workerTime.AddSeconds(2), "Implement one bounded Task."),
                        AssistantMessage(workerTime.AddSeconds(14), "Worker done."),
                        Complete(workerTime.AddSeconds(15)),
                        Usage(workerTime.AddSeconds(16), workerId, 10, 1, 3)
                    });
                }

                var terminal = index == 3
                    ? "Status: COMPLETED\nCompletedWorkers: 1\nReason: DONE"
                    : "Status: CONTINUE\nCompletedWorkers: 3\nReason: BATCH_LIMIT";
                rootLines.Add(AssistantMessage(batchTime.AddSeconds(150), terminal));
                rootLines.Add(Complete(batchTime.AddSeconds(151)));
                rootLines.Add(Usage(batchTime.AddSeconds(152), rootId, 30, 5, 8));
                File.WriteAllLines(Session(1 + batch), rootLines);
            }
            supervisorLines.Add(AssistantMessage(start.AddMinutes(45), "Factory completed."));
            supervisorLines.Add(Complete(start.AddMinutes(45).AddSeconds(1)));
            supervisorLines.Add(Usage(start.AddMinutes(45).AddSeconds(2), supervisor, 20, 1, 4));
            File.WriteAllLines(Session(0), supervisorLines);

            var report = Assert.Single(new FactoryReportEngine().FindRuns(directory, home));
            Assert.Equal(1, report.Metrics.SupervisorInvocations);
            Assert.Equal(4, report.Metrics.FactoryRootInvocations);
            Assert.Equal(10, report.Metrics.WorkerInvocations);
            Assert.Equal("supervisor", Assert.Single(report.Agents, agent =>
                agent.ThreadId == supervisor).Role);
            Assert.Equal(4, report.Agents.Count(agent => agent.Role == "root"));
            Assert.Equal(10, report.Agents.Count(agent => agent.Role == "worker"));
            Assert.Equal(10, report.Tasks.Count);
            Assert.True(report.Batches.Select(batch => batch.BatchOrdinal)
                .SequenceEqual(new int?[] { 1, 2, 3, 4 }));
            Assert.True(report.Batches.Select(batch => batch.Status)
                .SequenceEqual(new[] { "CONTINUE", "CONTINUE", "CONTINUE", "COMPLETED" }));
            Assert.True(report.Batches.Select(batch => batch.CompletedWorkers)
                .SequenceEqual(new int?[] { 3, 3, 3, 1 }));
            Assert.All(report.Batches, batch =>
                Assert.Equal(3, batch.EffectiveWorkerLimit));
            Assert.All(report.Agents.Where(agent => agent.Role == "worker"), agent =>
            {
                var parent = Assert.Single(report.Agents,
                    item => item.ThreadId == agent.ParentThreadId);
                Assert.Equal("root", parent.Role);
                Assert.Equal(parent.BatchOrdinal, agent.BatchOrdinal);
            });
            Assert.DoesNotContain(report.Diagnostics, item => item.Code is
                "agent_role_conflict" or "worker_batch_identity_conflict" or
                "batch_rotation_without_continue" or "duplicate_factory_batch");
            Assert.Equal(report.Agents.Count, report.Agents.Select(agent => agent.ThreadId)
                .Distinct(StringComparer.Ordinal).Count());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ExactBoundaryEightAndNineRequireSeparateFinalizingRoot()
    {
        foreach (var (workerCount, limit, expectedRoots) in new[]
                 { (8, 8, 2), (9, 3, 4) })
        {
            var full = workerCount / limit;
            Assert.Equal(expectedRoots, full + 1);
            var remaining = workerCount;
            for (var root = 0; root < full; root++)
            {
                var completed = Math.Min(limit, remaining);
                remaining -= completed;
                Assert.True(FactoryBatchTrace.TryParseTerminal(
                    $"Status: CONTINUE\nCompletedWorkers: {completed}\nReason: BATCH_LIMIT",
                    limit, out var result));
                Assert.Equal("CONTINUE", result!.Status);
            }
            Assert.Equal(0, remaining);
            Assert.True(FactoryBatchTrace.TryParseTerminal(
                "Status: COMPLETED\nCompletedWorkers: 0\nReason: DONE",
                limit, out var final));
            Assert.Equal("COMPLETED", final!.Status);
        }
    }

    private static void Spawn(List<string> lines, DateTimeOffset timestamp,
        string callId, string taskName, string childId, string? profile)
    {
        var args = JsonSerializer.Serialize(new
        {
            task_name = taskName,
            fork_turns = "none",
            message = profile is null
                ? "Load installed Root SKILL.md; max_workers_per_batch = 3"
                : $"Read installed Worker Skill; ExecutionProfile: {profile}"
        });
        lines.Add(Json(timestamp, "response_item", new
        {
            type = "function_call", name = "spawn_agent", call_id = callId, arguments = args
        }));
        lines.Add(Json(timestamp.AddSeconds(1), "response_item", new
        {
            type = "function_call_output", call_id = callId,
            output = JsonSerializer.Serialize(new { agent_id = childId, task_name = taskName })
        }));
    }

    private static string SessionMeta(DateTimeOffset timestamp, string id,
        string cwd, string? parent = null, string? agentPath = null) =>
        Json(timestamp, "session_meta", new Dictionary<string, object?>
        {
            ["id"] = id,
            ["cwd"] = cwd,
            ["parent_thread_id"] = parent,
            ["source"] = parent is null ? "exec" :
                new { subagent = new { thread_spawn = new
                {
                    parent_thread_id = parent, agent_path = agentPath, depth = 1
                } } }
        });

    private static string UserMessage(DateTimeOffset timestamp, string content) =>
        Json(timestamp, "response_item", new
        {
            type = "message", role = "user",
            content = new[] { new { type = "input_text", text = content } }
        });

    private static string AssistantMessage(DateTimeOffset timestamp, string content) =>
        Json(timestamp, "response_item", new
        {
            type = "message", role = "assistant",
            content = new[] { new { type = "output_text", text = content } }
        });

    private static string Complete(DateTimeOffset timestamp) =>
        Json(timestamp, "event_msg", new { type = "task_complete" });

    private static string Usage(DateTimeOffset timestamp, string threadId,
        long input, long cached, long output) =>
        Json(timestamp, "token_usage_record", new
        {
            thread_id = threadId,
            thread_token_usage = new
            {
                input_tokens = input, cached_input_tokens = cached,
                output_tokens = output
            }
        });

    private static string Json(DateTimeOffset timestamp, string type, object payload) =>
        JsonSerializer.Serialize(new { timestamp, type, payload });
}
