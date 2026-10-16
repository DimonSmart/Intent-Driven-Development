using System.Text.Json;
using Idd.Factory.Report;
using Xunit;

namespace Idd.Factory.Report.Tests;

public sealed partial class FactoryReportTests
{
    private const string OpaqueFactoryMessage = "gAAAAABopaque_encrypted_spawn_message==";
    private const string TestRootId = "10101010-1010-1010-1010-101010101010";
    private const string TestPlannerId = "20202020-2020-2020-2020-202020202020";
    private const string TestWorkerId = "30303030-3030-3030-3030-303030303030";

    [Fact]
    public void NativeIdentity_CorrelatesEncryptedPlannerAndWorkerByParentAndTaskName()
    {
        var (repo, home, sessions) = NativeIdentityWorkspace("encrypted-names");
        Write(Path.Combine(sessions, "root.jsonl"),
        [
            Session(TestRootId, repo),
            User("2026-10-16T10:00:00Z", "Use idd-factory-run."),
            NamedSpawn("2026-10-16T10:00:01Z", "p1", "idd_factory_planner_01"),
            NamedOutput("2026-10-16T10:00:02Z", "p1", "/root/idd_factory_planner_01"),
            NamedSpawn("2026-10-16T10:00:03Z", "w1", "idd_factory_worker_01", "model-a", "medium"),
            NamedOutput("2026-10-16T10:00:04Z", "w1", "/root/idd_factory_worker_01"),
            Assistant("2026-10-16T10:01:00Z", "Factory completed.")
        ]);
        Write(Path.Combine(sessions, "planner.jsonl"),
        [
            Session(TestPlannerId, repo, TestRootId, "/root/idd_factory_planner_01"),
            Assistant("2026-10-16T10:00:02Z", "# Task\nImplement ProductCode.\n# ExecutionProfile\nstandard")
        ]);
        Write(Path.Combine(sessions, "worker.jsonl"),
        [
            Session(TestWorkerId, repo, TestRootId, "/root/idd_factory_worker_01"),
            Assistant("2026-10-16T10:00:30Z", "Implemented ProductCode.")
        ]);

        var rollout = new CodexRolloutReader().Read(Path.Combine(sessions, "root.jsonl"));
        Assert.Equal("idd_factory_planner_01", rollout.SpawnRecords["p1"].RequestedTaskName);
        Assert.Equal("idd_factory_worker_01", rollout.SpawnRecords["w1"].RequestedTaskName);
        Assert.Equal("model-a", rollout.SpawnRecords["w1"].RequestedModel);
        Assert.Equal("medium", rollout.SpawnRecords["w1"].RequestedReasoningEffort);
        Assert.Null(rollout.SpawnRecords["w1"].ActualModel);
        Assert.Contains(rollout.Diagnostics, d => d.Code == "spawn_prompt_unreadable");

        var report = Assert.Single(new FactoryReportEngine().FindRuns(repo, home));
        Assert.Equal("planner", Assert.Single(report.Agents, a => a.ThreadId == TestPlannerId).Role);
        var worker = Assert.Single(report.Agents, a => a.ThreadId == TestWorkerId);
        Assert.Equal("worker", worker.Role);
        Assert.Equal("model-a", worker.RequestedModel);
        Assert.Null(worker.ActualModel);
        Assert.Equal("standard", worker.ExecutionProfile);
        Assert.Equal("Implement ProductCode.", worker.Task);
        Assert.DoesNotContain(report.Diagnostics, d => d.Code is "agent_role_conflict" or "spawn_child_identity_conflict");
    }

    [Theory]
    [InlineData(true, "rejected")]
    [InlineData(false, "unknown")]
    public void NativeIdentity_DistinguishesRejectedSpawnFromMissingChild(bool rejection, string expected)
    {
        var (repo, home, sessions) = NativeIdentityWorkspace("outcome-" + expected);
        var output = rejection
            ? JsonSerializer.Serialize(new { error = new { code = "model_not_found", message = "Model removed-model unavailable" } })
            : "{}";
        Write(Path.Combine(sessions, "root.jsonl"),
        [
            Session(TestRootId, repo),
            User("2026-10-16T10:00:00Z", "idd-factory-run"),
            NamedSpawn("2026-10-16T10:00:01Z", "w1", "idd_factory_worker_01", "removed-model", "medium"),
            NamedOutput("2026-10-16T10:00:02Z", "w1", null, output),
            Assistant("2026-10-16T10:00:03Z", "Factory blocked.")
        ]);
        var root = new CodexRolloutReader().Read(Path.Combine(sessions, "root.jsonl"));
        Assert.Equal(expected, root.SpawnRecords["w1"].SpawnOutcome);
        Assert.Empty(root.SpawnRecords["w1"].Children);
        Assert.Equal(rejection ? "model_not_found" : null, root.SpawnRecords["w1"].RejectionCode);
        var report = Assert.Single(new FactoryReportEngine().FindRuns(repo, home));
        Assert.Empty(report.Tasks);
        Assert.DoesNotContain(report.Agents, a => a.Role == "worker");
        Assert.Contains(report.Diagnostics, d => d.Code ==
            (rejection ? "native_spawn_rejected" : "spawn_child_unresolved"));
    }

    [Theory]
    [InlineData("role")]
    [InlineData("output-path")]
    [InlineData("requested-path")]
    [InlineData("duplicate-name")]
    public void NativeIdentity_ConflictingIdentityFailsClosed(string conflict)
    {
        var (repo, home, sessions) = NativeIdentityWorkspace("conflict-" + conflict);
        var requestedName = conflict == "requested-path" ? "idd_factory_worker_02" : "idd_factory_worker_01";
        var outputPath = conflict == "output-path" ? "/root/idd_factory_worker_02" : "/root/idd_factory_worker_01";
        Write(Path.Combine(sessions, "root.jsonl"),
        [
            Session(TestRootId, repo),
            User("2026-10-16T10:00:00Z", "idd-factory-run"),
            NamedSpawn("2026-10-16T10:00:01Z", "w1", requestedName),
            NamedOutput("2026-10-16T10:00:02Z", "w1", outputPath),
            .. (conflict == "duplicate-name"
                ? new object[] { NamedSpawn("2026-10-16T10:00:03Z", "w2", requestedName) }
                : Array.Empty<object>()),
            Assistant("2026-10-16T10:01:00Z", "Factory completed.")
        ]);
        Write(Path.Combine(sessions, "worker.jsonl"),
        [
            Session(TestWorkerId, repo, TestRootId, "/root/idd_factory_worker_01",
                conflict == "role" ? "factory-planner" : null),
            Assistant("2026-10-16T10:00:30Z", "Done.")
        ]);
        var report = Assert.Single(new FactoryReportEngine().FindRuns(repo, home));
        Assert.Equal("unknown", Assert.Single(report.Agents, a => a.ThreadId == TestWorkerId).Role);
        Assert.Contains(report.Diagnostics, d => d.Code is "agent_role_conflict"
            or "spawn_child_identity_conflict" or "duplicate_spawn_task_name");
        Assert.Empty(report.Tasks);
    }

    [Fact]
    public void NativeIdentity_DoesNotUseSimilarPathFromAnotherParent()
    {
        var (repo, home, sessions) = NativeIdentityWorkspace("foreign-parent");
        const string foreignParent = "40404040-4040-4040-4040-404040404040";
        Write(Path.Combine(sessions, "root.jsonl"),
        [
            Session(TestRootId, repo),
            User("2026-10-16T10:00:00Z", "idd-factory-run"),
            NamedSpawn("2026-10-16T10:00:01Z", "w1", "idd_factory_worker_01"),
            Assistant("2026-10-16T10:01:00Z", "Factory blocked.")
        ]);
        Write(Path.Combine(sessions, "foreign.jsonl"),
        [
            Session(TestWorkerId, repo, foreignParent, "/other-root/idd_factory_worker_01"),
            Assistant("2026-10-16T10:00:30Z", "Done.")
        ]);
        var report = Assert.Single(new FactoryReportEngine().FindRuns(repo, home));
        Assert.Empty(report.Tasks);
        Assert.DoesNotContain(report.Agents, a => a.ThreadId == TestWorkerId);
    }

    [Fact]
    public void NativeIdentity_MergesStartedCompletedAndResponseOutputForOneCall()
    {
        var path = Path.Combine(_root, "native-call.jsonl");
        Write(path,
        [
            Session(TestRootId, _root),
            NativeNamedEvent("2026-10-16T10:00:01Z", "item_started", "s1"),
            NativeNamedEvent("2026-10-16T10:00:02Z", "item_completed", "s1"),
            NamedOutput("2026-10-16T10:00:03Z", "s1", "/root/idd_factory_worker_01")
        ]);
        var rollout = new CodexRolloutReader().Read(path);
        var spawn = Assert.Single(rollout.SpawnRecords.Values);
        Assert.Equal("idd_factory_worker_01", spawn.RequestedTaskName);
        Assert.Equal("model-a", spawn.RequestedModel);
        Assert.Equal("medium", spawn.RequestedReasoningEffort);
        Assert.Equal("/root/idd_factory_worker_01", spawn.ChildAgentPath);
    }

    [Fact]
    public void NativeIdentity_ArbitraryReferencesDoNotGrantFactoryRole()
    {
        var (repo, home, sessions) = NativeIdentityWorkspace("unrelated-role");
        Write(Path.Combine(sessions, "root.jsonl"),
        [
            Session(TestRootId, repo),
            User("2026-10-16T10:00:00Z", "idd-factory-run"),
            SpawnCall("2026-10-16T10:00:01Z", "helper", "Explain how a worker and planner differ."),
            SpawnOutput("2026-10-16T10:00:02Z", "helper", TestWorkerId),
            Assistant("2026-10-16T10:01:00Z", "Factory completed.")
        ]);
        Write(Path.Combine(sessions, "helper.jsonl"),
        [
            Session(TestWorkerId, repo, TestRootId),
            Assistant("2026-10-16T10:00:30Z", "The worker and planner are examples.")
        ]);
        var report = Assert.Single(new FactoryReportEngine().FindRuns(repo, home));
        Assert.Equal("unknown", Assert.Single(report.Agents, a => a.ThreadId == TestWorkerId).Role);
    }

    [Fact]
    public void NativeIdentity_ExtraRejectedWorkerPreventsGuessingPlannerTaskOrder()
    {
        var (repo, home, sessions) = NativeIdentityWorkspace("ambiguous-profile");
        Write(Path.Combine(sessions, "root.jsonl"),
        [
            Session(TestRootId, repo),
            User("2026-10-16T10:00:00Z", "idd-factory-run"),
            NamedSpawn("2026-10-16T10:00:01Z", "p1", "idd_factory_planner_01"),
            NamedOutput("2026-10-16T10:00:02Z", "p1", "/root/idd_factory_planner_01"),
            NamedSpawn("2026-10-16T10:00:03Z", "rejected", "idd_factory_worker_01", "removed-model", "medium"),
            NamedOutput("2026-10-16T10:00:04Z", "rejected", null,
                JsonSerializer.Serialize(new { error = new { code = "model_not_found", message = "Model rejected" } })),
            NamedSpawn("2026-10-16T10:00:05Z", "w2", "idd_factory_worker_02", "model-b", "medium"),
            NamedOutput("2026-10-16T10:00:06Z", "w2", "/root/idd_factory_worker_02"),
            Assistant("2026-10-16T10:01:00Z", "Factory completed.")
        ]);
        Write(Path.Combine(sessions, "planner.jsonl"),
        [
            Session(TestPlannerId, repo, TestRootId, "/root/idd_factory_planner_01"),
            Assistant("2026-10-16T10:00:02Z",
                "# Task\nProductCode task\n# ExecutionProfile\nstandard\n# Task\nCatalog task\n# ExecutionProfile\nstrong")
        ]);
        Write(Path.Combine(sessions, "worker.jsonl"),
        [
            Session(TestWorkerId, repo, TestRootId, "/root/idd_factory_worker_02"),
            Assistant("2026-10-16T10:00:30Z", "Completed.")
        ]);

        var report = Assert.Single(new FactoryReportEngine().FindRuns(repo, home));
        var task = Assert.Single(report.Tasks);
        Assert.Null(task.ExecutionProfile);
        Assert.Contains(report.Diagnostics, diagnostic => diagnostic.Code == "worker_task_correlation_unavailable");
    }

    private (string Repo, string Home, string Sessions) NativeIdentityWorkspace(string name)
    {
        var repo = Path.Combine(_root, name, "repo");
        var home = Path.Combine(_root, name, "home");
        var sessions = Path.Combine(home, "sessions");
        Directory.CreateDirectory(repo);
        Directory.CreateDirectory(sessions);
        return (repo, home, sessions);
    }

    private static object NamedSpawn(string timestamp, string id, string taskName,
        string? model = null, string? reasoning = null) => new
    {
        timestamp, type = "response_item",
        payload = new
        {
            type = "function_call", name = "spawn_agent", call_id = id,
            arguments = JsonSerializer.Serialize(new
            {
                message = OpaqueFactoryMessage, task_name = taskName, fork_turns = "none",
                model, reasoning_effort = reasoning
            })
        }
    };

    private static object NamedOutput(string timestamp, string id, string? path, string? json = null) => new
    {
        timestamp, type = "response_item",
        payload = new
        {
            type = "function_call_output", call_id = id,
            output = json ?? JsonSerializer.Serialize(new { task_name = path })
        }
    };

    private static object NativeNamedEvent(string timestamp, string phase, string id) => new
    {
        timestamp, type = "event_msg",
        payload = new
        {
            type = phase,
            item = new
            {
                id, type = "CollabAgentToolCall", tool = "spawn_agent",
                task_name = "idd_factory_worker_01", message = OpaqueFactoryMessage,
                model = "model-a", reasoning_effort = "medium", fork_turns = "none"
            }
        }
    };
}
