using System.Text.Json;
using Idd.Factory.Report;
using Xunit;

namespace Idd.Factory.Report.Tests;

/// <summary>
/// Deterministic read-only native-rollout fixture: a Supervisor and distinct
/// Root contexts; tests both legacy 3+3+3+1 and plan-safe 4+4+2
/// sequential Worker children. Uses real Codex
/// session_meta, spawn-agent call and task_complete event shapes.
/// </summary>
public sealed class FactoryNestedTopologyReportTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(21, false)]
    [InlineData(41, true)]
    public void TenWorkerNestedTracePreservesLegacyAndPlanSafeBatchAttribution(int firstOrdinal, bool planBoundaryRotation)
    {
        var directory = Path.Combine(Path.GetTempPath(), "idd-report-nested-" + Guid.NewGuid().ToString("N"));
        try
        {
            var home = Path.Combine(directory, "home");
            Directory.CreateDirectory(Path.Combine(home, "sessions"));
            var rootCount = planBoundaryRotation ? 3 : 4;
            var ids = Enumerable.Range(1, 1 + rootCount + 10 + (planBoundaryRotation ? 3 : 1))
                .Select(index => $"11111111-0000-4000-8000-{index:000000000000}")
                .ToArray();
            var supervisor = ids[0];
            var roots = ids.Skip(1).Take(rootCount).ToArray();
            var workers = ids.Skip(1 + rootCount).Take(10).ToArray();
            var plannerIds = ids.Skip(11 + rootCount)
                .Take(planBoundaryRotation ? 3 : 1).ToArray();
            var start = DateTimeOffset.Parse("2026-11-18T10:00:00Z");

            string Session(int slot) => Path.Combine(home, "sessions", $"rollout-{slot:00}.jsonl");
            var supervisorLines = new List<string>
            {
                SessionMeta(start, supervisor, directory),
                UserMessage(start.AddSeconds(1), "Implement the ten approved features in this repository.")
            };

            var workerCursor = 0;
            for (var index = 0; index < rootCount; index++)
            {
                var batch = firstOrdinal + index;
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
                if (index == 0 || planBoundaryRotation)
                {
                    var plannerId = plannerIds[planBoundaryRotation ? index : 0];
                    var plannerName = $"idd_factory_planner_b{batch:0000}_0001";
                    var plannerTime = batchTime.AddSeconds(3);
                    Spawn(rootLines, plannerTime, "planner-1", plannerName, plannerId, null);
                    var tasks = string.Join("\n",
                        Enumerable.Range(planBoundaryRotation ? index * 4 + 1 : 1,
                            planBoundaryRotation ? (index == 2 ? 2 : 4) : 10).Select(step =>
                            $"# Task\nImplement Step{step:00}\n# ExecutionProfile\nstandard\n"));
                    File.WriteAllLines(Session(300 + index), new[]
                    {
                        SessionMeta(plannerTime.AddSeconds(1), plannerId, directory, rootId,
                            $"/root/{rootName}/{plannerName}"),
                        UserMessage(plannerTime.AddSeconds(2), "Plan ten independent Tasks."),
                        AssistantMessage(plannerTime.AddSeconds(4), tasks),
                        Complete(plannerTime.AddSeconds(5)),
                        Usage(plannerTime.AddSeconds(6), plannerId, 10, 1, 3)
                    });
                }

                var count = planBoundaryRotation ? (index == 2 ? 2 : 4) : (index == 3 ? 1 : 3);
                for (var i = 0; i < count; i++)
                {
                    var workerId = workers[workerCursor++];
                    var taskName = $"idd_factory_worker_b{batch:0000}_{i + 1:0000}";
                    var workerTime = batchTime.AddSeconds(10 + 40 * i);
                    Spawn(rootLines, workerTime, $"worker-{batch}-{i + 1}", taskName, workerId,
                        "standard", workerCursor);
                    File.WriteAllLines(Session(200 + workerCursor), new[]
                    {
                        SessionMeta(workerTime.AddSeconds(1), workerId, directory, rootId,
                            $"/root/{rootName}/{taskName}"),
                        UserMessage(workerTime.AddSeconds(2), "Implement one bounded Task."),
                        AssistantMessage(workerTime.AddSeconds(14), "Worker done."),
                        Complete(workerTime.AddSeconds(15)),
                        Usage(workerTime.AddSeconds(16), workerId, 10, 1, 3)
                    });
                }

                var terminal = index == rootCount - 1
                    ? $"Status: COMPLETED\nCompletedWorkers: {count}\nReason: DONE"
                    : $"Status: CONTINUE\nCompletedWorkers: {count}\nReason: BATCH_LIMIT";
                rootLines.Add(AssistantMessage(batchTime.AddSeconds(150), terminal));
                rootLines.Add(Complete(batchTime.AddSeconds(151)));
                rootLines.Add(Usage(batchTime.AddSeconds(152), rootId, 30, 5, 8));
                File.WriteAllLines(Session(100 + index), rootLines);
            }
            supervisorLines.Add(AssistantMessage(start.AddMinutes(45), "Factory completed."));
            supervisorLines.Add(Complete(start.AddMinutes(45).AddSeconds(1)));
            supervisorLines.Add(Usage(start.AddMinutes(45).AddSeconds(2), supervisor, 20, 1, 4));
            File.WriteAllLines(Session(0), supervisorLines);

            var report = Assert.Single(new FactoryReportEngine().FindRuns(directory, home));
            Assert.Equal(1, report.Metrics.SupervisorInvocations);
            Assert.Equal(rootCount, report.Metrics.FactoryRootInvocations);
            Assert.Equal(10, report.Metrics.WorkerInvocations);
            Assert.Equal("supervisor", Assert.Single(report.Agents, agent =>
                agent.ThreadId == supervisor).Role);
            Assert.Equal(rootCount, report.Agents.Count(agent => agent.Role == "root"));
            Assert.Equal(10, report.Agents.Count(agent => agent.Role == "worker"));
            Assert.Equal(10, report.Tasks.Count);
            Assert.All(report.Tasks, task => Assert.Equal("standard", task.ExecutionProfile));
            Assert.True(report.Tasks.Select(task => task.Text?.Trim())
                .SequenceEqual(Enumerable.Range(1, 10)
                    .Select(number => $"Implement Step{number:00}")));
            Assert.True(report.Batches.Select(batch => batch.BatchOrdinal)
                .SequenceEqual(Enumerable.Range(firstOrdinal, rootCount)
                    .Select(number => (int?)number)));
            Assert.True(report.Batches.Select(batch => batch.Status)
                .SequenceEqual(Enumerable.Repeat("CONTINUE", rootCount - 1).Append("COMPLETED")));
            Assert.True(report.Batches.Select(batch => batch.CompletedWorkers)
                .SequenceEqual(planBoundaryRotation
                    ? new int?[] { 4, 4, 2 } : new int?[] { 3, 3, 3, 1 }));
            Assert.All(report.Batches, batch =>
                Assert.Equal(3, batch.EffectiveWorkerLimit));
            if (planBoundaryRotation)
            {
                Assert.Equal(3, report.Metrics.PlannerInvocations);
                Assert.All(report.Agents.Where(agent => agent.Role == "planner"), agent =>
                    Assert.Contains(roots, root => agent.ParentThreadId == root));
                Assert.Equal(new[] { 4, 4, 2 },
                    report.Tasks.GroupBy(task => task.BatchOrdinal)
                        .OrderBy(group => group.Key).Select(group => group.Count()).ToArray());
            }
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
            // The fixture does not contain a trusted invocation-start token
            // baseline for the Supervisor. Do not manufacture an additive total
            // from the child's independent thread counters.
            Assert.Equal("overlap-unknown", report.Metrics.TokenAggregationMethod);
            Assert.False(report.Metrics.TokenAggregationComplete);
            Assert.Null(report.Metrics.Tokens.InputTokens);
            Assert.Null(report.Metrics.Tokens.OutputTokens);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("3,4,2", 8, "9,0")]
    [InlineData("4", 3, "4,0")]
    [InlineData("4,4,2", 3, "4,4,2")]
    [InlineData("3,4,3", 3, "3,4,3,0")]
    [InlineData("4,4", 8, "8,0")]
    [InlineData("1,1,1,1", 3, "3,1")]
    [InlineData("2,2,2,2,2", 8, "8,2")]
    [InlineData("6", 3, "6,0")]
    public void NormalRotationTargetsTheNextCompletePlanNotAnExactWorkerCount(
        string planSizes, int target, string expectedRootCounts)
    {
        var expected = expectedRootCounts.Split(',').Select(int.Parse).ToArray();
        var actual = new List<int>();
        var currentRoot = 0;
        foreach (var plan in planSizes.Split(',').Select(int.Parse))
        {
            currentRoot += plan;
            // Even an already-persisted six-Task legacy plan must finish.
            if (currentRoot < target) continue;
            Assert.True(FactoryBatchTrace.TryParseTerminal(
                $"Status: CONTINUE\nCompletedWorkers: {currentRoot}\nReason: BATCH_LIMIT",
                target, out _));
            actual.Add(currentRoot);
            currentRoot = 0;
        }

        // Planner # Done closes the current Root without another rotation if
        // its checkpoint target was not reached; otherwise it needs the
        // new fresh Root created by the last BATCH_LIMIT.
        Assert.True(FactoryBatchTrace.TryParseTerminal(
            $"Status: COMPLETED\nCompletedWorkers: {currentRoot}\nReason: DONE",
            target, out _));
        actual.Add(currentRoot);
        Assert.Equal(expected, actual);
    }

    private static void Spawn(List<string> lines, DateTimeOffset timestamp,
        string callId, string taskName, string childId, string? profile, int? step = null)
    {
        var args = JsonSerializer.Serialize(new
        {
            task_name = taskName,
            fork_turns = "none",
            message = taskName.StartsWith("idd_factory_root_", StringComparison.Ordinal)
                ? "Load installed Root SKILL.md; max_workers_per_batch = 3"
                : profile is null
                    ? "Read installed Planner SKILL.md"
                    : $"Read installed Worker SKILL.md\n--- Factory worker assignment ---\n# Task\nImplement Step{step:00}\n# ExecutionProfile\n{profile}\n--- End Factory worker assignment ---"
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
