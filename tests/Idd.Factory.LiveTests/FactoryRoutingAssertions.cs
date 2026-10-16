using System.Text.Json;
using System.Text.RegularExpressions;
using Idd.Factory.Report;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

internal static class FactoryRoutingAssertions
{
    internal static void VerifyUnavailableModel(FactoryRunReport report, CodexRollout root,
        string unavailableModel, string standardEffort, string pendingPlan)
    {
        Assert.Equal("blocked", report.Run.Result);
        Assert.Equal("blocked", report.Completion.DeclaredResult);
        Assert.True(!string.IsNullOrWhiteSpace(report.Run.Reason) &&
            report.Run.Reason.Contains(unavailableModel, StringComparison.Ordinal) &&
            Regex.IsMatch(report.Run.Reason, "host|rejected|unavailable|removed|not found|unsupported|cannot spawn",
                RegexOptions.IgnoreCase), "BLOCKED must explain the rejected model.");
        Assert.True(report.FactoryProjectState.CurrentRequestPresent);
        Assert.True(report.FactoryProjectState.CurrentPlanPresent);
        Assert.Contains("# Task", pendingPlan, StringComparison.Ordinal);
        Assert.Matches(@"(?s)#\s*ExecutionProfile\s*\r?\n\s*standard\b", pendingPlan);
        Assert.NotEqual("passed", report.Completion.ProjectVerification);
        Assert.Contains(report.Agents, agent => agent.Role == "planner" &&
            agent.PlannerOutcome == "Tasks");

        var spawns = InRun(root, report).ToArray();
        var workerSpawns = spawns.Where(IsFactoryWorkerSpawn).ToArray();
        Assert.NotEmpty(workerSpawns);
        Assert.Contains(workerSpawns, spawn =>
            spawn.RequestedModel == unavailableModel &&
            spawn.RequestedReasoningEffort == standardEffort &&
            spawn.SpawnOutcome == "rejected" &&
            (!string.IsNullOrWhiteSpace(spawn.RejectionCode) ||
             !string.IsNullOrWhiteSpace(spawn.RejectionMessage)) &&
            root.Events.Any(item => item.ToolName == "spawn_agent" &&
                item.ToolId == spawn.CallId && HasNoHistoryFork(item.ToolArguments)));

        // An additional worker with different (or unobservable) routing is a
        // potential fallback, even when no child rollout was produced.
        Assert.All(workerSpawns, spawn =>
        {
            Assert.Equal(unavailableModel, spawn.RequestedModel);
            Assert.Equal(standardEffort, spawn.RequestedReasoningEffort);
            Assert.Equal("rejected", spawn.SpawnOutcome);
            Assert.Empty(spawn.Children);
        });

        Assert.Empty(report.Tasks);
        Assert.DoesNotContain(report.Agents, agent => agent.Role == "worker");
        Assert.DoesNotContain(spawns, spawn => spawn.CorrelationConflict);
        foreach (var spawn in spawns.Where(spawn =>
            !IsFactoryWorkerSpawn(spawn) && !IsFactorySpawnForRole(spawn, "planner")))
            Assert.True(IsProvenUnrelated(spawn.Task),
                $"insufficient native evidence: spawn {spawn.CallId} may be a substitute worker.");

        // Unrelated nested agents are allowed, but a root child of uncertain
        // provenance cannot be silently excluded as a substitute worker.
        var unresolved = report.Agents.Where(agent => agent.Role == "unknown" &&
            agent.ParentThreadId == report.Run.RootThreadId);
        foreach (var agent in unresolved)
        {
            var matching = spawns.Where(spawn =>
                spawn.Children.Contains(agent.ThreadId, StringComparer.Ordinal)).ToArray();
            Assert.True(matching.Length == 1 &&
                !IsFactoryWorkerSpawn(matching[0]) &&
                IsProvenUnrelated(matching[0].Task),
                $"insufficient native evidence: unknown child {agent.ThreadId} could be a substitute worker.");
        }
    }

    internal static void Verify(FactoryRunReport report,
        IReadOnlyDictionary<string, WorkerExecutionSettings> mappings)
    {
        LiveEvalSettings.ValidateProfiles(mappings);
        Assert.True(report.Tasks.Count >= 3,
            $"Expected three completed worker tasks, found {report.Tasks.Count}.");
        Assert.Equal(new[] { "standard", "strong", "economy" },
            report.Tasks.Take(3).Select(task => task.ExecutionProfile));
        Assert.Equal(3, report.Tasks.Take(3).Select(task => task.AgentThreadId)
            .Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(report.Tasks.Take(3), task =>
            string.IsNullOrWhiteSpace(task.AgentThreadId) || task.Status != "completed");

        foreach (var task in report.Tasks)
        {
            Assert.NotNull(task.ExecutionProfile);
            var expected = mappings[task.ExecutionProfile];
            Assert.Equal(expected.Model, task.RequestedModel);
            Assert.Equal(expected.ReasoningEffort, task.RequestedReasoningEffort);
            Assert.Equal(expected.Model, task.ActualModel);
            Assert.Equal(expected.ReasoningEffort, task.ActualReasoningEffort);
        }

        Assert.DoesNotContain(report.Diagnostics, diagnostic =>
            diagnostic.Code is "worker_execution_profile_mismatch" or "worker_execution_settings_mismatch"
                or "worker_actual_settings_unavailable" or "worker_turn_context_settings_conflict"
                or "worker_spawn_settings_conflict" or "agent_role_conflict"
                or "duplicate_spawn_task_name" or "spawn_child_identity_conflict"
                or "ambiguous_spawn_child");
    }

    internal static void VerifyNativeSpawns(FactoryRunReport report, CodexRollout root,
        IReadOnlyDictionary<string, WorkerExecutionSettings> mappings)
    {
        Verify(report, mappings);
        Assert.True(report.Completion.PlannerDone == true,
            "A successful Factory must end with a fresh planner # Done.");
        var spawns = InRun(root, report).ToArray();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var planners = report.Agents.Where(agent => agent.Role == "planner").ToArray();
        Assert.Contains(planners, planner => planner.PlannerOutcome == "Tasks");
        Assert.Contains(planners, planner => planner.PlannerOutcome == "Done");

        foreach (var agent in planners.Concat(report.Agents.Where(agent => agent.Role == "worker")))
        {
            var spawn = Assert.Single(spawns, spawn =>
                spawn.Children.Contains(agent.ThreadId, StringComparer.Ordinal) &&
                !spawn.CorrelationConflict);
            Assert.True(used.Add(spawn.CallId),
                $"A single spawn cannot account for more than one agent: {spawn.CallId}.");
            Assert.Equal("accepted", spawn.SpawnOutcome);
            Assert.True(IsFactorySpawnForRole(spawn, agent.Role),
                $"insufficient native evidence: {spawn.CallId} has no trusted {agent.Role} identity.");
            Assert.Contains(root.Events, item => item.ToolName == "spawn_agent" &&
                item.ToolId == spawn.CallId && HasNoHistoryFork(item.ToolArguments));
        }

        foreach (var task in report.Tasks.Take(3))
        {
            var spawn = Assert.Single(spawns, spawn =>
                spawn.Children.Contains(task.AgentThreadId, StringComparer.Ordinal) &&
                !spawn.CorrelationConflict);
            var expected = mappings[task.ExecutionProfile!];
            Assert.Equal(expected.Model, spawn.RequestedModel);
            Assert.Equal(expected.ReasoningEffort, spawn.RequestedReasoningEffort);
        }

        foreach (var spawn in spawns.Where(spawn => !used.Contains(spawn.CallId)))
            Assert.True(IsProvenUnrelated(spawn.Task),
                $"insufficient native evidence: unaccounted spawn {spawn.CallId} in the Factory invocation.");

        Assert.DoesNotContain(spawns, spawn => spawn.CorrelationConflict ||
            IsFactoryWorkerSpawn(spawn) && spawn.SpawnOutcome != "accepted");
    }

    internal static IEnumerable<SpawnRecord> InRun(CodexRollout root, FactoryRunReport report) =>
        root.SpawnRecords.Values.Where(spawn =>
            spawn.Timestamp is not null &&
            (report.Run.StartedAt is null || spawn.Timestamp >= report.Run.StartedAt) &&
            (report.Run.FinishedAt is null || spawn.Timestamp <= report.Run.FinishedAt));

    private static bool IsFactorySpawnForRole(SpawnRecord spawn, string role) =>
        role switch
        {
            "worker" => IsFactoryWorkerSpawn(spawn),
            "planner" => Regex.IsMatch(spawn.RequestedTaskName ?? "",
                    @"^idd_factory_planner_[0-9]+$") ||
                spawn.Task?.Contains("You are the Factory planner.", StringComparison.Ordinal) == true ||
                spawn.Task?.Contains("idd-factory-decompose-task", StringComparison.Ordinal) == true,
            _ => false
        };

    private static bool IsFactoryWorkerSpawn(SpawnRecord spawn) =>
        Regex.IsMatch(spawn.RequestedTaskName ?? "", @"^idd_factory_worker_[0-9]+$") ||
        spawn.Task?.Contains("idd-factory-execute-subtask", StringComparison.Ordinal) == true ||
        spawn.Task?.Contains("You are the Factory worker.", StringComparison.Ordinal) == true ||
        spawn.Task?.Contains("--- Factory worker assignment ---", StringComparison.Ordinal) == true;

    private static bool IsProvenUnrelated(string? instructions) =>
        instructions is not null &&
        (instructions.Contains("You are a helper.", StringComparison.Ordinal) ||
         instructions.Contains("Non-Factory helper:", StringComparison.Ordinal));

    private static bool HasNoHistoryFork(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return false;
        try
        {
            using var json = JsonDocument.Parse(arguments);
            return json.RootElement.ValueKind == JsonValueKind.Object &&
                json.RootElement.TryGetProperty("fork_turns", out var fork) &&
                fork.ValueKind == JsonValueKind.String && fork.ValueEquals("none");
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
