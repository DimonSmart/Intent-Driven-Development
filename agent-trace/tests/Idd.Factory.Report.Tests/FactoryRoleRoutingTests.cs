using Idd.Factory.Report;
using Xunit;

namespace Idd.Factory.Report.Tests;

public sealed partial class FactoryReportTests
{
    [Theory]
    [InlineData(false, false, "unknown")]
    [InlineData(false, false, "matching")]
    [InlineData(false, false, "mismatch")]
    [InlineData(false, true, "mismatch")]
    [InlineData(true, false, "mismatch")]
    [InlineData(true, true, "mismatch")]
    public void FactoryRoles_PreserveAssignmentAndExecutionSettings(
        bool legacy, bool initialInstructions, string actualSettings)
    {
        var repo = Path.Combine(_root, "repo-role-routing");
        var sessions = Path.Combine(_root, "codex-role-routing", "sessions");
        Directory.CreateDirectory(repo);
        Directory.CreateDirectory(sessions);
        const string rootId = "11111111-1111-1111-1111-111111111111";
        const string plannerId = "22222222-2222-2222-2222-222222222222";
        const string workerId = "33333333-3333-3333-3333-333333333333";
        const string helperId = "44444444-4444-4444-4444-444444444444";
        var taskText = initialInstructions
            ? "Implement Catalog refresh."
            : "Implement Catalog refresh.\n\nYou are the Factory planner.";
        var plannerInstructions = legacy
            ? "Use idd-factory-decompose-task."
            : "You are the Factory planner.";
        var workerInstructions = (legacy
            ? "Use idd-factory-execute-subtask."
            : "You are the Factory worker.") + "\n" +
            "# Task\nProtocol example to ignore.\n" +
            "--- Factory worker assignment ---\n# Task\n" + taskText +
            "\n# ExecutionProfile\neconomy\n# TaskRelatedIntent\nIDD-0001\n" +
            "--- End Factory worker assignment ---";
        var actualModel = actualSettings switch
        {
            "matching" => "model-a",
            "mismatch" => "model-b",
            _ => null
        };
        var actualEffort = actualSettings switch
        {
            "matching" => "low",
            "mismatch" => "high",
            _ => null
        };

        Write(Path.Combine(sessions, "root.jsonl"),
        [
            Session(rootId, repo),
            User("2026-10-08T10:00:00Z", "idd-factory-run"),
            SpawnCall("2026-10-08T10:00:01Z", "planner",
                initialInstructions ? "gAAAAABopaque_encrypted_spawn_message==" : plannerInstructions),
            SpawnOutput("2026-10-08T10:00:02Z", "planner", plannerId),
            SpawnCall("2026-10-08T10:00:03Z", "worker",
                initialInstructions ? "gAAAAABopaque_encrypted_spawn_message==" : workerInstructions, "model-a", "low"),
            SpawnOutput("2026-10-08T10:00:04Z", "worker", workerId, actualModel, actualEffort),
            SpawnCall("2026-10-08T10:00:05Z", "helper", "Explain the phrase: You are the Factory planner.", "model-a", "low"),
            SpawnOutput("2026-10-08T10:00:06Z", "helper", helperId, "model-b", "high"),
            Assistant("2026-10-08T10:01:00Z", "Factory completed.")
        ]);
        Write(Path.Combine(sessions, "planner.jsonl"),
        [
            Session(plannerId, repo, rootId),
            User("2026-10-08T10:00:02Z", initialInstructions ? plannerInstructions : "Factory child context."),
            Assistant("2026-10-08T10:00:02Z", "# Task\n" + taskText + "\n# ExecutionProfile\neconomy")
        ]);
        Write(Path.Combine(sessions, "worker.jsonl"),
        [
            Session(workerId, repo, rootId),
            User("2026-10-08T10:00:04Z", initialInstructions ? workerInstructions : "Factory child context."),
            Assistant("2026-10-08T10:00:50Z", "Implemented.")
        ]);
        Write(Path.Combine(sessions, "helper.jsonl"),
        [
            Session(helperId, repo, rootId),
            User("2026-10-08T10:00:06Z", "Explain the protocol."),
            Assistant("2026-10-08T10:00:40Z", "You are the Factory worker.")
        ]);

        var report = Assert.Single(new FactoryReportEngine().FindRuns(repo, Path.GetDirectoryName(sessions)!));
        Assert.Equal(1, report.Metrics.PlannerInvocations);
        Assert.Equal(1, report.Metrics.WorkerInvocations);
        Assert.Equal("planner", Assert.Single(report.Agents, x => x.ThreadId == plannerId).Role);
        Assert.Equal("unknown", Assert.Single(report.Agents, x => x.ThreadId == helperId).Role);
        var worker = Assert.Single(report.Agents, x => x.Role == "worker");
        var task = Assert.Single(report.Tasks);
        Assert.Equal(workerId, task.AgentThreadId);
        Assert.Equal(taskText, worker.Task);
        Assert.Equal(taskText, task.Text);
        Assert.Equal("economy", task.ExecutionProfile);
        Assert.Equal("model-a", task.RequestedModel);
        Assert.Equal("low", task.RequestedReasoningEffort);
        Assert.Equal(actualModel, task.ActualModel);
        Assert.Equal(actualEffort, task.ActualReasoningEffort);
        Assert.Equal(task.RequestedModel, worker.RequestedModel);
        Assert.Equal(task.RequestedReasoningEffort, worker.RequestedReasoningEffort);
        Assert.Equal(task.ActualModel, worker.ActualModel);
        Assert.Equal(task.ActualReasoningEffort, worker.ActualReasoningEffort);
        Assert.Equal(actualSettings == "mismatch", report.Diagnostics.Any(x =>
            x.Code == "worker_execution_settings_mismatch" && x.Message.Contains(workerId)));
        Assert.Equal(actualSettings == "unknown", report.Diagnostics.Any(x =>
            x.Code == "worker_actual_settings_unavailable" && x.Message.Contains(workerId)));
        Assert.DoesNotContain(report.Diagnostics, x =>
            x.Code == "worker_execution_settings_mismatch" && x.Message.Contains(helperId));
    }
}
