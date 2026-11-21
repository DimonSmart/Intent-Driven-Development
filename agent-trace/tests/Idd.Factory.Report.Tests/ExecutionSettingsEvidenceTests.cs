using System.Text.Json;
using Idd.Factory.Report;
using Xunit;

namespace Idd.Factory.Report.Tests;

public sealed partial class FactoryReportTests
{
    [Theory]
    [InlineData("model-b", "low")]
    [InlineData("model-a", "high")]
    [InlineData("model-b", "high")]
    public void Report_PrefersWorkerContextAndDiagnosesConflictingSpawnSettings(string model, string effort)
    {
        var report = ExecutionEvidenceReport("model-a", "low", [WorkerContext(model, effort)]);
        var task = Assert.Single(report.Tasks);
        Assert.Equal(model, task.ActualModel);
        Assert.Equal(effort, task.ActualReasoningEffort);
        Assert.Equal("model-a", task.RequestedModel);
        Assert.Equal("low", task.RequestedReasoningEffort);
        var conflict = Assert.Single(report.Diagnostics, x => x.Code == "worker_spawn_settings_conflict");
        Assert.Equal("host-trace", conflict.Category);
        Assert.Contains(task.AgentThreadId, conflict.Message);
        Assert.Contains("model-a", conflict.Message);
        Assert.Contains(model, conflict.Message);
        Assert.Contains(effort, conflict.Message);
        Assert.Contains(report.Diagnostics, x => x.Code == "worker_execution_settings_mismatch");
        AssertSettingsMatchAgent(report, task);

        var markdown = Path.Combine(_root, "evidence-report.md");
        var json = Path.Combine(_root, "evidence-report.json");
        ReportWriters.WriteMarkdown(report, markdown, verbose: false);
        ReportWriters.WriteJson(report, json);
        using var console = new StringWriter();
        ReportWriters.WriteConsole(report, console, verbose: false);
        Assert.Contains(conflict.Message, File.ReadAllText(markdown));
        Assert.Contains("worker_spawn_settings_conflict", File.ReadAllText(json));
        Assert.Contains(conflict.Message, console.ToString());
    }

    [Fact]
    public void Report_UsesMatchingContextEvenWhenSpawnMetadataDisagree()
    {
        var report = ExecutionEvidenceReport("model-b", "high", [WorkerContext("model-a", "low")]);
        var task = Assert.Single(report.Tasks);
        Assert.Equal(task.RequestedModel, task.ActualModel);
        Assert.Equal(task.RequestedReasoningEffort, task.ActualReasoningEffort);
        Assert.Contains(report.Diagnostics, x => x.Code == "worker_spawn_settings_conflict");
        Assert.DoesNotContain(report.Diagnostics, x => x.Code == "worker_execution_settings_mismatch");
    }

    [Theory]
    [InlineData("model-b", "low", null, "low")]
    [InlineData("model-a", "high", "model-a", null)]
    [InlineData("model-b", "high", null, null)]
    public void Report_DoesNotResolveChangingContextsFromSpawnMetadata(
        string secondModel, string secondEffort, string? expectedModel, string? expectedEffort)
    {
        var report = ExecutionEvidenceReport("model-a", "low",
            [WorkerContext("model-a", "low"), WorkerContext(secondModel, secondEffort)]);
        var task = Assert.Single(report.Tasks);
        Assert.Equal(expectedModel, task.ActualModel);
        Assert.Equal(expectedEffort, task.ActualReasoningEffort);
        var conflict = Assert.Single(report.Diagnostics, x => x.Code == "worker_turn_context_settings_conflict");
        Assert.Contains(task.AgentThreadId, conflict.Message);
        Assert.Contains(secondModel, conflict.Message);
        Assert.Contains(secondEffort, conflict.Message);
        Assert.Contains(report.Diagnostics, x => x.Code == "worker_actual_settings_unavailable");
        Assert.DoesNotContain(report.Diagnostics, x => x.Code == "worker_execution_settings_mismatch");
        AssertSettingsMatchAgent(report, task);
    }

    [Fact]
    public void Report_DetectsChangingContextsWithoutSpawnSettings()
    {
        var report = ExecutionEvidenceReport(null, null,
        [
            new { timestamp = "2026-10-09T10:00:04Z", type = "event_msg",
                payload = new { type = "execution_settings", actual_model = "model-a", actual_reasoning_effort = "low" } },
            WorkerContext("model-a", "low"), WorkerContext("model-b", "high")
        ]);
        var task = Assert.Single(report.Tasks);
        Assert.Null(task.ActualModel);
        Assert.Null(task.ActualReasoningEffort);
        Assert.Contains(report.Diagnostics, x => x.Code == "worker_turn_context_settings_conflict");
        Assert.DoesNotContain(report.Diagnostics, x => x.Code == "worker_spawn_settings_conflict");
    }

    [Fact]
    public void Report_ChangingModelStillReportsStableReasoningMismatch()
    {
        var report = ExecutionEvidenceReport("model-a", "low",
            [WorkerContext("model-a", "high"), WorkerContext("model-b", "high")]);
        var task = Assert.Single(report.Tasks);
        Assert.Null(task.ActualModel);
        Assert.Equal("high", task.ActualReasoningEffort);
        Assert.Contains(report.Diagnostics, x => x.Code == "worker_turn_context_settings_conflict");
        Assert.Contains(report.Diagnostics, x => x.Code == "worker_execution_settings_mismatch");
    }

    [Fact]
    public void Report_RepeatedMatchingContextsDoNotCreateConflicts()
    {
        var report = ExecutionEvidenceReport("model-a", "low",
            [WorkerContext("model-a", "low"), WorkerContext("model-a", "low")]);
        var task = Assert.Single(report.Tasks);
        Assert.Equal("model-a", task.ActualModel);
        Assert.Equal("low", task.ActualReasoningEffort);
        Assert.DoesNotContain(report.Diagnostics, x => x.Code is
            "worker_spawn_settings_conflict" or "worker_turn_context_settings_conflict" or
            "worker_actual_settings_unavailable" or "worker_execution_settings_mismatch");
    }

    [Fact]
    public void Report_ContextTakesPriorityOverEarlierWorkerSettingsEvent()
    {
        var report = ExecutionEvidenceReport(null, null,
        [
            new { timestamp = "2026-10-09T10:00:04Z", type = "event_msg",
                payload = new { type = "execution_settings", actual_model = "model-a", actual_reasoning_effort = "low" } },
            WorkerContext("model-b", "high")
        ]);
        var task = Assert.Single(report.Tasks);
        Assert.Equal("model-b", task.ActualModel);
        Assert.Equal("high", task.ActualReasoningEffort);
    }

    [Fact]
    public void Report_ForeignContextsDoNotOverrideOrConflictWithWorkerSettings()
    {
        var report = ExecutionEvidenceReport("model-a", "low",
        [
            WorkerContext("model-b", "high", "44444444-4444-4444-4444-444444444444"),
            WorkerContext("model-a", "low")
        ]);
        var task = Assert.Single(report.Tasks);
        Assert.Equal("model-a", task.ActualModel);
        Assert.Equal("low", task.ActualReasoningEffort);
        Assert.DoesNotContain(report.Diagnostics, x => x.Code is
            "worker_spawn_settings_conflict" or "worker_turn_context_settings_conflict");
    }

    [Fact]
    public void Report_MissingContextFieldCanUseExplicitSpawnEvidence()
    {
        var report = ExecutionEvidenceReport("model-a", "low", [WorkerContext("model-a", null)]);
        var task = Assert.Single(report.Tasks);
        Assert.Equal("model-a", task.ActualModel);
        Assert.Equal("low", task.ActualReasoningEffort);
        Assert.DoesNotContain(report.Diagnostics, x => x.Code is
            "worker_spawn_settings_conflict" or "worker_actual_settings_unavailable");
    }

    private FactoryRunReport ExecutionEvidenceReport(string? spawnModel, string? spawnEffort,
        IReadOnlyList<object> workerSettings)
    {
        var repo = Path.Combine(_root, "repo-evidence");
        var codex = Path.Combine(_root, "codex-evidence");
        var sessions = Path.Combine(codex, "sessions");
        Directory.CreateDirectory(repo);
        Directory.CreateDirectory(sessions);
        const string rootId = "11111111-1111-1111-1111-111111111111";
        const string plannerId = "22222222-2222-2222-2222-222222222222";
        const string workerId = "33333333-3333-3333-3333-333333333333";
        Write(Path.Combine(sessions, "root.jsonl"),
        [
            Session(rootId, repo),
            User("2026-10-09T10:00:00Z", "idd-factory-run"),
            SpawnCall("2026-10-09T10:00:01Z", "planner", "You are the Factory planner."),
            SpawnOutput("2026-10-09T10:00:02Z", "planner", plannerId),
            SpawnCall("2026-10-09T10:00:03Z", "worker", """
                You are the Factory worker.
                --- Factory worker assignment ---
                # Task
                Implement A.
                --- End Factory worker assignment ---
                """, "model-a", "low"),
            new
            {
                timestamp = "2026-10-09T10:00:04Z", type = "response_item",
                payload = new
                {
                    type = "function_call_output", call_id = "worker",
                    output = JsonSerializer.Serialize(new
                    {
                        childThreadId = workerId,
                        resolved_model = spawnModel,
                        resolved_reasoning_effort = spawnEffort
                    })
                }
            },
            Assistant("2026-10-09T10:01:00Z", "Factory completed.")
        ]);
        WriteChild(sessions, "planner.jsonl", plannerId, rootId, repo,
            Assistant("2026-10-09T10:00:02Z", "# Task\nImplement A.\n# ExecutionProfile\neconomy"));
        Write(Path.Combine(sessions, "worker.jsonl"),
            new[] { Session(workerId, repo, rootId) }.Concat(workerSettings).Append(
                Assistant("2026-10-09T10:00:50Z", "Implemented.")));
        return Assert.Single(new FactoryReportEngine().FindRuns(repo, codex));
    }

    private static object WorkerContext(string? model, string? effort, string? threadId = null)
    {
        var payload = new Dictionary<string, string>();
        if (model is not null) payload["model"] = model;
        if (effort is not null) payload["effort"] = effort;
        if (threadId is not null) payload["thread_id"] = threadId;
        return new { timestamp = "2026-10-09T10:00:05Z", type = "turn_context", payload };
    }

    private static void AssertSettingsMatchAgent(FactoryRunReport report, TaskReport task)
    {
        var worker = Assert.Single(report.Agents, x => x.ThreadId == task.AgentThreadId);
        Assert.Equal(task.ActualModel, worker.ActualModel);
        Assert.Equal(task.ActualReasoningEffort, worker.ActualReasoningEffort);
    }
}
