using System.Text.Json;
using Idd.Factory.Report;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class FactoryNativeEvidenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(),
        "idd-native-evidence-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void FourTaskPlanExecutesInsideOneRootWithTargetThree()
    {
        var (supervisor, roots, children) = Build();
        FactoryNativeEvidence.VerifyRoots(supervisor, roots);
        Assert.Equal(4, FactoryNativeEvidence.VerifyPlans(roots[0], children));
    }

    [Theory]
    [InlineData("missing-worker-terminal")]
    [InlineData("missing-planner-terminal")]
    [InlineData("unconfirmed-wait")]
    [InlineData("wrong-wait-identity")]
    [InlineData("overlapping-workers")]
    [InlineData("unfinished-plan")]
    [InlineData("intermediate-task-only")]
    [InlineData("malformed-rollout")]
    [InlineData("conflicting-child-identity")]
    public void UnprovenOrContradictoryWorkerEvidenceIsRejected(string fault)
    {
        var (_, roots, children) = Build(fault);
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            FactoryNativeEvidence.VerifyPlans(roots[0], children));
    }

    [Fact]
    public void SupervisorCannotSpawnNextRootBeforeConfirmedPreviousWait()
    {
        var (supervisor, roots, _) = Build("overlapping-roots");
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            FactoryNativeEvidence.VerifyRoots(supervisor, roots));
    }

    private (CodexRollout Supervisor, CodexRollout[] Roots, CodexRollout[] Children)
        Build(string? fault = null)
    {
        Directory.CreateDirectory(_directory);
        const string supervisorId = "11111111-0000-4000-8000-000000000001";
        const string rootId = "11111111-0000-4000-8000-000000000002";
        const string nextRootId = "11111111-0000-4000-8000-000000000003";
        const string plannerId = "11111111-0000-4000-8000-000000000004";
        var workerIds = Enumerable.Range(5, 4)
            .Select(index => $"11111111-0000-4000-8000-{index:000000000000}").ToArray();
        var assignments = Enumerable.Range(1, 4)
            .Select(index => $"# Task\nImplement Step{index:00}\n# ExecutionProfile\nstandard")
            .ToArray();
        var plan = string.Join("\n\n", assignments);
        var hasSecondRoot = fault == "overlapping-roots";
        var supervisorLines = new List<string> { Session(supervisorId) };
        Spawn(supervisorLines, "root-1", "idd_factory_root_b0001", rootId,
            "Use Root SKILL.md; max_workers_per_batch = 3");

        var rootLines = new List<string> { Session(rootId, supervisorId,
            "/root/idd_factory_root_b0001") };
        Spawn(rootLines, "planner", "idd_factory_planner_b0001_0001", plannerId,
            "Read installed Planner SKILL.md");
        Wait(rootLines, "planner-wait", plannerId);

        var children = new List<CodexRollout>();
        var plannerLines = new List<string>
        {
            Session(plannerId, rootId, "/root/idd_factory_root_b0001/idd_factory_planner_b0001_0001"),
            Message(plan)
        };
        if (fault == "intermediate-task-only")
            plannerLines.Add(Message("# Done"));
        if (fault != "missing-planner-terminal")
            plannerLines.Add(Complete());
        children.Add(Read(plannerId, plannerLines));

        var count = fault == "unfinished-plan" ? 3 : 4;
        for (var i = 0; i < count; i++)
        {
            var id = workerIds[i];
            var name = $"idd_factory_worker_b0001_{i + 1:0000}";
            var prompt = "Read installed Worker SKILL.md\n" +
                "--- Factory worker assignment ---\n" + assignments[i] + "\n" +
                "--- End Factory worker assignment ---";
            Spawn(rootLines, $"worker-{i + 1}", name,
                fault == "conflicting-child-identity" && i == 0 ? workerIds[1] : id,
                prompt);
            // An overlapping worker is spawned before the previous wait completes.
            if (fault != "overlapping-workers" || i != 0)
            {
                if (fault == "overlapping-workers" && i == 1)
                    Wait(rootLines, "wait-worker-1", workerIds[0]);
                Wait(rootLines, $"wait-worker-{i + 1}", id,
                    fault == "unconfirmed-wait" && i == 0 ? "running" : "completed",
                    fault == "wrong-wait-identity" && i == 0 ? workerIds[1] : null);
            }
            var workerLines = new List<string>
            {
                Session(id, rootId, $"/root/idd_factory_root_b0001/{name}"),
                Message("Worker finished.")
            };
            if (fault != "missing-worker-terminal" || i != 0)
                workerLines.Add(Complete());
            children.Add(Read(id, workerLines));
        }

        if (!hasSecondRoot)
        {
            const string donePlannerId = "11111111-0000-4000-8000-000000000009";
            const string doneName = "idd_factory_planner_b0001_0002";
            Spawn(rootLines, "planner-done", doneName, donePlannerId,
                "Read installed Planner SKILL.md");
            Wait(rootLines, "planner-done-wait", donePlannerId);
            children.Add(Read(donePlannerId,
            [
                Session(donePlannerId, rootId, "/root/idd_factory_root_b0001/" + doneName),
                Message("# Done"),
                Complete()
            ]));
        }

        rootLines.Add(Message(hasSecondRoot
            ? "Status: CONTINUE\nCompletedWorkers: 4\nReason: BATCH_LIMIT"
            : $"Status: COMPLETED\nCompletedWorkers: {count}\nReason: DONE"));
        rootLines.Add(Complete());
        if (fault == "malformed-rollout")
            rootLines.Add("{truncated");
        var roots = new List<CodexRollout> { Read(rootId, rootLines) };

        if (hasSecondRoot)
        {
            Spawn(supervisorLines, "root-2", "idd_factory_root_b0002", nextRootId,
                "Use Root SKILL.md; max_workers_per_batch = 3");
            Wait(supervisorLines, "wait-root-1", rootId);
            Wait(supervisorLines, "wait-root-2", nextRootId);
            roots.Add(Read(nextRootId,
            [
                Session(nextRootId, supervisorId, "/root/idd_factory_root_b0002"),
                Message("Status: COMPLETED\nCompletedWorkers: 0\nReason: DONE"),
                Complete()
            ]));
        }
        else
            Wait(supervisorLines, "wait-root-1", rootId);
        supervisorLines.Add(Message("Factory completed."));
        supervisorLines.Add(Complete());
        return (Read(supervisorId, supervisorLines), roots.ToArray(), children.ToArray());
    }

    private CodexRollout Read(string id, IEnumerable<string> lines)
    {
        var path = Path.Combine(_directory, id + ".jsonl");
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        return new CodexRolloutReader().Read(path);
    }

    private static void Spawn(List<string> lines, string callId, string name,
        string childId, string prompt)
    {
        lines.Add(Record("response_item", new
        {
            type = "function_call", name = "spawn_agent", call_id = callId,
            arguments = JsonSerializer.Serialize(new
            {
                task_name = name, fork_turns = "none", message = prompt
            })
        }));
        lines.Add(Record("response_item", new
        {
            type = "function_call_output", call_id = callId,
            output = JsonSerializer.Serialize(new { agent_id = childId })
        }));
    }

    private static void Wait(List<string> lines, string callId, string childId,
        string status = "completed", string? reportedChildId = null)
    {
        lines.Add(Record("response_item", new
        {
            type = "function_call", name = "wait_agent", call_id = callId,
            arguments = JsonSerializer.Serialize(new { agent_ids = new[] { childId } })
        }));
        lines.Add(Record("response_item", new
        {
            type = "function_call_output", call_id = callId,
            output = JsonSerializer.Serialize(new
            {
                agent_id = reportedChildId ?? childId, status
            })
        }));
    }

    private static string Session(string id, string? parent = null, string? path = null) =>
        Record("session_meta", new
        {
            id, parent_thread_id = parent, agent_path = path
        });

    private static string Message(string text) => Record("response_item", new
    {
        type = "message", role = "assistant",
        content = new[] { new { type = "output_text", text } }
    });

    private static string Complete() =>
        Record("event_msg", new { type = "task_complete" });

    private static string Record(string type, object payload) =>
        JsonSerializer.Serialize(new { type, payload });

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }
}
