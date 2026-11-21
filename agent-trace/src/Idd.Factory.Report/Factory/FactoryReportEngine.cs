using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Idd.Factory.Report;

public sealed class FactoryReportEngine
{
    private const string RunSkill = "idd-factory-run";
    private const string PlannerMarker = "You are the Factory planner.";
    private const string LegacyPlannerSkill = "idd-factory-decompose-task";
    private const string WorkerMarker = "You are the Factory worker.";
    private const string LegacyWorkerSkill = "idd-factory-execute-subtask";
    private const string WorkerAssignmentStart = "--- Factory worker assignment ---";
    private const string WorkerAssignmentEnd = "--- End Factory worker assignment ---";
    private readonly CodexRolloutReader _reader = new();

    public IReadOnlyList<FactoryRunReport> FindRuns(string repository, string codexHome, bool verbose = false)
    {
        var repo = NormalizePath(repository);
        var state = new CodexStateDbReader().Read(codexHome);
        var all = LoadRepositoryRollouts(repo, codexHome, state);
        var byId = all.Where(x => !string.IsNullOrWhiteSpace(x.ThreadId))
            .GroupBy(x => x.ThreadId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

        foreach (var child in all)
        {
            if (child.ParentThreadId is null && state.ParentByChild.TryGetValue(child.ThreadId, out var parent))
                child.ParentThreadId = parent;
        }

        foreach (var spawningRollout in all)
        {
            var spawns = spawningRollout.SpawnRecords.Values.ToArray();
            foreach (var duplicate in spawns.Where(x => x.RequestedTaskName is not null)
                         .GroupBy(x => (x.SenderThreadId ?? spawningRollout.ThreadId, x.RequestedTaskName))
                         .Where(x => x.Count() > 1))
            {
                foreach (var spawn in duplicate)
                    spawn.CorrelationConflict = true;
                spawningRollout.Diagnostics.Add(new Diagnostic
                {
                    Severity = "warning", Category = "host-trace", Code = "duplicate_spawn_task_name",
                    Message = $"Duplicate native task_name {duplicate.Key.RequestedTaskName} under parent {duplicate.Key.Item1}."
                });
            }

            foreach (var spawn in spawns)
            {
                var parent = spawn.SenderThreadId ?? spawningRollout.ThreadId;
                if (FactoryTaskNameRole(spawn.RequestedTaskName) is not null &&
                    spawn.ChildAgentPath?.StartsWith('/') == true &&
                    spawn.ChildAgentPath.Split('/').Last() != spawn.RequestedTaskName)
                {
                    spawn.CorrelationConflict = true;
                    spawningRollout.Diagnostics.Add(new Diagnostic
                    {
                        Severity = "warning", Category = "host-trace", Code = "spawn_child_identity_conflict",
                        Message = $"Spawn {spawn.CallId} requested {spawn.RequestedTaskName} but host returned {spawn.ChildAgentPath}."
                    });
                }

                if (!spawn.Children.Any() && spawn.SpawnOutcome != "rejected")
                {
                    var candidates = all.Where(child => child.ParentThreadId == parent &&
                        !string.IsNullOrWhiteSpace(child.AgentPath)).ToArray();
                    CodexRollout[] matches = [];
                    if (spawn.ChildAgentPath?.StartsWith('/') == true)
                        matches = candidates.Where(child => child.AgentPath == spawn.ChildAgentPath).ToArray();
                    else if (FactoryTaskNameRole(spawn.RequestedTaskName) is not null)
                        matches = candidates.Where(child => child.AgentPath is not null &&
                            child.AgentPath.Split('/').Last() == spawn.RequestedTaskName).ToArray();

                    if (matches.Length == 1 && !spawn.CorrelationConflict)
                    {
                        spawn.ChildThreadId = matches[0].ThreadId;
                        spawn.ChildThreadIds.Add(matches[0].ThreadId);
                    }
                    else if (matches.Length > 1)
                    {
                        spawn.CorrelationConflict = true;
                        spawningRollout.Diagnostics.Add(new Diagnostic
                        {
                            Severity = "warning", Category = "host-trace", Code = "ambiguous_spawn_child",
                            Message = $"Spawn {spawn.CallId} has {matches.Length} matching children under {parent}."
                        });
                    }
                }

                foreach (var childId in spawn.Children)
                {
                    if (!byId.TryGetValue(childId, out var child))
                        continue;

                    if (child.ParentThreadId is not null && child.ParentThreadId != parent ||
                        spawn.RequestedTaskName is not null && FactoryTaskNameRole(spawn.RequestedTaskName) is not null &&
                        child.AgentPath is not null && child.AgentPath.Split('/').Last() != spawn.RequestedTaskName ||
                        spawn.ChildAgentPath?.StartsWith('/') == true &&
                        child.AgentPath is not null && child.AgentPath != spawn.ChildAgentPath)
                    {
                        spawn.CorrelationConflict = true;
                        spawningRollout.Diagnostics.Add(new Diagnostic
                        {
                            Severity = "warning", Category = "host-trace", Code = "spawn_child_identity_conflict",
                            Message = $"Spawn {spawn.CallId} child {childId} conflicts with parent/name/path evidence."
                        });
                        continue;
                    }
                    child.ParentThreadId ??= parent;
                    if (!spawn.CorrelationConflict)
                        spawn.SpawnOutcome = "accepted";
                }
            }
        }

        var roots = all.Where(x => string.IsNullOrWhiteSpace(x.ParentThreadId)).ToArray();
        var reports = new List<FactoryRunReport>();

        foreach (var root in roots)
        {
            var starts = DetectRunStarts(root);
            for (var i = 0; i < starts.Count; i++)
            {
                var start = starts[i];
                var nextStartOrdinal = i + 1 < starts.Count ? starts[i + 1].Ordinal : long.MaxValue;
                reports.Add(BuildReport(repo, codexHome, root, i + 1, start, nextStartOrdinal, all, byId, state, verbose));
            }
        }

        return reports
            .OrderBy(x => x.Run.StartedAt ?? DateTimeOffset.MinValue)
            .ThenBy(x => x.Run.RootThreadId, StringComparer.Ordinal)
            .ThenBy(x => x.Run.RunIndex)
            .ToArray();
    }

    private List<CodexRollout> LoadRepositoryRollouts(string repo, string codexHome, CodexStateSnapshot state)
    {
        var paths = EnumerateRollouts(codexHome).Distinct(PathComparer()).ToArray();
        var candidates = new List<string>();

        foreach (var path in paths)
        {
            try
            {
                var meta = _reader.Read(path, metadataOnly: true);
                if (PathBelongsToRepository(meta.Cwd, repo))
                    candidates.Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                state.Diagnostics.Add(new Diagnostic
                {
                    Severity = "warning",
                    Category = "reporter",
                    Code = "rollout_metadata_unavailable",
                    Message = $"Could not read rollout metadata: {Path.GetFileName(path)}."
                });
            }
        }

        var result = new List<CodexRollout>();
        foreach (var path in candidates)
        {
            try
            {
                result.Add(_reader.Read(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                state.Diagnostics.Add(new Diagnostic
                {
                    Severity = "warning",
                    Category = "reporter",
                    Code = "missing_rollout",
                    Message = $"Could not read rollout {Path.GetFileName(path)}: {ex.Message}"
                });
            }
        }
        return result;
    }

    private static IEnumerable<string> EnumerateRollouts(string codexHome)
    {
        foreach (var dir in new[] { "sessions", "archived_sessions" })
        {
            var root = Path.Combine(codexHome, dir);
            if (!Directory.Exists(root))
                continue;
            foreach (var path in Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories))
                yield return path;
        }
    }

    private static List<CodexEvent> DetectRunStarts(CodexRollout root)
    {
        var explicitStarts = root.Events
            .Where(x => string.Equals(x.Role, "user", StringComparison.OrdinalIgnoreCase) && x.Contains(RunSkill))
            .ToList();
        if (explicitStarts.Count > 0)
            return explicitStarts;

        // A quoted marker in root dialogue is not proof of a Factory run.
        // The public Supervisor may be invoked implicitly by routing; its
        // original user request need not literally contain idd-factory-run.
        // In a nested trace a native Root spawn with the exact reserved
        // task_name is sufficient to find the run's derived start boundary.
        var fallback = root.Events.FirstOrDefault(x =>
            IsNativeSpawn(x) &&
            (HasUnambiguousFactoryRole(x.SpawnTask) ||
             x.ToolId is not null &&
             root.SpawnRecords.TryGetValue(x.ToolId, out var spawn) &&
             FactoryTaskNameRole(spawn.RequestedTaskName) == "root"));
        return fallback is null ? [] : [fallback];
    }

    private FactoryRunReport BuildReport(
        string repository,
        string codexHome,
        CodexRollout root,
        int runIndex,
        CodexEvent start,
        long nextStartOrdinal,
        IReadOnlyList<CodexRollout> all,
        IReadOnlyDictionary<string, CodexRollout> byId,
        CodexStateSnapshot state,
        bool verbose)
    {
        var segmentRootEvents = root.Events
            .Where(x => x.Ordinal >= start.Ordinal && x.Ordinal < nextStartOrdinal)
            .ToArray();

        var descendants = SelectDescendants(root, start.Timestamp, nextStartOrdinal == long.MaxValue ? null :
            root.Events.FirstOrDefault(x => x.Ordinal == nextStartOrdinal)?.Timestamp, all, byId, state);

        var diagnostics = new List<Diagnostic>();
        var agents = BuildAgents(root, segmentRootEvents, descendants, state, verbose, diagnostics);
        var rootCallIds = segmentRootEvents.Where(e => e.ToolName == "spawn_agent" && e.ToolId is not null)
            .Select(e => e.ToolId!).ToHashSet(StringComparer.Ordinal);
        var runSpawns = root.SpawnRecords.Values.Where(spawn => rootCallIds.Contains(spawn.CallId)).ToArray();
        var ambiguousTaskRouting = runSpawns.Any(spawn =>
            spawn.CorrelationConflict ||
            spawn.SpawnOutcome != "accepted" &&
                (FactoryTaskNameRole(spawn.RequestedTaskName) == "worker" ||
                 ExplicitFactoryRole(spawn.Task) == "worker" ||
                 LegacyFactoryRole(spawn.Task) == "worker" ||
                 spawn.RequestedTaskName is null && spawn.Task is null));
        if (ambiguousTaskRouting)
            diagnostics.Add(new Diagnostic
            {
                Severity = "warning", Category = "host-trace", Code = "worker_task_correlation_unavailable",
                Message = "Factory worker spawn identity or outcome is ambiguous; planner task order alone " +
                    "cannot establish worker task/profile routing."
            });
        else
            PopulateMissingWorkerTasks(agents, byId);
        var plannerAgents = agents.Where(x => x.Role == "planner").OrderBy(x => x.StartedAt).ToArray();
        var workerAgents = agents.Where(x => x.Role == "worker").OrderBy(x => x.StartedAt).ToArray();
        var latestPlanner = plannerAgents.LastOrDefault();
        var verificationPolicy = FinalVerificationEvidence.Load(repository, root.Cwd);

        var resultEvidence = new List<string>();
        var result = "unknown";
        string? resultReason = null;
        DateTimeOffset? end = null;
        string? endEvidence = null;
        var endConfidence = "unknown";
        CodexEvent? terminalEvent = null;

        var structuredResult = FindStructuredFactoryResult(segmentRootEvents);
        if (structuredResult is not null)
        {
            result = structuredResult.Status;
            resultReason = structuredResult.Reason;
            resultEvidence.Add("structured_final_response");
            end = structuredResult.Event.Timestamp;
            terminalEvent = structuredResult.Event;
            endEvidence = $"structured final response status={structuredResult.Status.ToUpperInvariant()}";
            endConfidence = "exact";
        }
        else if (latestPlanner?.PlannerOutcome == "Question")
        {
            result = "question";
            resultEvidence.Add("planner_question");
            end = latestPlanner.FinishedAt;
            endEvidence = "planner returned # Question";
            endConfidence = "derived";
        }
        else
        {
            var completion = segmentRootEvents.LastOrDefault(IsFactoryCompletion);
            if (completion is not null)
            {
                result = "completed";
                resultEvidence.Add("factory_completion");
                if (latestPlanner?.PlannerOutcome == "Done")
                    resultEvidence.Insert(0, "planner_done");
                end = completion.Timestamp;
                terminalEvent = completion;
                endEvidence = "explicit Factory completion event";
                endConfidence = "exact";
            }
            else if (latestPlanner?.PlannerOutcome == "Done")
            {
                var verification = verificationPolicy.Evaluate(segmentRootEvents, latestPlanner.PlannerOutcomeAt);
                if (verification.Result == "passed")
                {
                    result = "completed";
                    resultEvidence.Add("planner_done");
                    resultEvidence.Add("verification_success");
                    terminalEvent = verification.Observations.Last().Event;
                    end = terminalEvent.Timestamp;
                    endEvidence = "planner # Done followed by successful final verification evidence";
                    endConfidence = "derived";
                }
            }

            var blocked = segmentRootEvents.LastOrDefault(x =>
                x.Text?.Contains("Factory blocked", StringComparison.OrdinalIgnoreCase) == true);
            if (blocked is not null)
            {
                result = "blocked";
                resultEvidence.Clear();
                resultEvidence.Add("explicit_blocked");
                end = blocked.Timestamp;
                terminalEvent = blocked;
                endEvidence = "explicit Factory blocked event";
                endConfidence = "exact";
            }

            var interrupted = segmentRootEvents.LastOrDefault(IsInterruption);
            if (interrupted is not null)
            {
                result = "interrupted";
                resultEvidence.Clear();
                resultEvidence.Add("host_interruption");
                end = interrupted.Timestamp;
                terminalEvent = interrupted;
                endEvidence = "host interruption/cancellation evidence";
                endConfidence = "exact";
            }
        }

        if (result == "unknown")
        {
            var incompleteFactoryAgent = agents.FirstOrDefault(x =>
                x.Role is "planner" or "worker" &&
                byId.TryGetValue(x.ThreadId, out var childRollout) &&
                !HasTerminalEvidence(childRollout));
            if (incompleteFactoryAgent is not null)
            {
                result = "interrupted";
                resultEvidence.Add("child_without_completion");
                end = incompleteFactoryAgent.FinishedAt;
                endEvidence = $"{incompleteFactoryAgent.Role} child has no terminal completion evidence";
                endConfidence = "derived";
            }
        }

        if (end is null)
        {
            var owned = segmentRootEvents.Where(IsFactoryOwned).Select(x => x.Timestamp)
                .Concat(descendants.Select(x => x.FinishedAt))
                .Where(x => x is not null)
                .Max();
            if (owned is not null)
            {
                end = owned;
                endEvidence = "last deterministic Factory-owned event";
                endConfidence = "derived";
            }
        }

        if (end is not null)
        {
            agents.RemoveAll(x => x.StartedAt is not null && x.StartedAt > end);
            plannerAgents = agents.Where(x => x.Role == "planner").OrderBy(x => x.StartedAt).ToArray();
            workerAgents = agents.Where(x => x.Role == "worker").OrderBy(x => x.StartedAt).ToArray();
        }

        diagnostics.AddRange(state.Diagnostics);
        diagnostics.AddRange(root.Diagnostics);
        diagnostics.AddRange(descendants.SelectMany(x => x.Diagnostics));

        foreach (var rollout in descendants)
        {
            if (!HasTerminalEvidence(rollout))
            {
                diagnostics.Add(new Diagnostic
                {
                    Severity = "warning",
                    Category = "host-trace",
                    Code = "child_without_completion",
                    Message = $"Child thread {rollout.ThreadId} has no recognized completion event."
                });
            }
        }

        foreach (var unresolved in agents.Where(x => x.Role == "unknown"))
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = "info",
                Category = "reporter",
                Code = "agent_role_unresolved",
                Message = $"Could not determine the Factory role of child thread {unresolved.ThreadId} from explicit metadata or spawn instructions."
            });
        }

        foreach (var spawn in root.SpawnRecords.Values.Where(x =>
                     x.Timestamp >= start.Timestamp && (end is null || x.Timestamp <= end)))
        {
            if (spawn.SpawnOutcome == "rejected")
                diagnostics.Add(new Diagnostic
                {
                    Severity = "info", Category = "host-trace", Code = "native_spawn_rejected",
                    Message = $"Spawn {spawn.CallId} task_name={spawn.RequestedTaskName ?? "unavailable"} requested " +
                        $"{FormatExecutionSettings(spawn.RequestedModel, spawn.RequestedReasoningEffort)} " +
                        $"rejected: {spawn.RejectionCode ?? "host"} {spawn.RejectionMessage ?? "no message"}."
                });
            if (spawn.SpawnOutcome == "unknown" && !spawn.Children.Any())
                diagnostics.Add(new Diagnostic
                {
                    Severity = "info", Category = "host-trace", Code = "spawn_child_unresolved",
                    Message = $"Spawn {spawn.CallId} has no confirmed child or explicit host rejection."
                });
            if (spawn.ChildThreadId is not null && !byId.ContainsKey(spawn.ChildThreadId))
            {
                diagnostics.Add(new Diagnostic
                {
                    Severity = "warning",
                    Category = "host-trace",
                    Code = "spawn_without_discoverable_child",
                    Message = $"Spawned child {spawn.ChildThreadId} has no discoverable rollout."
                });
            }
        }

        var stateInfo = ReadFactoryProjectState(repository);
        if (result == "completed" && stateInfo.CurrentRequestPresent)
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = "warning",
                Category = "factory",
                Code = "completed_with_active_request",
                Message = "Factory result is COMPLETED but .idd/factory/current/request.md still exists."
            });
        }

        if (workerAgents.Length == 0 && !agents.Any(x => x.Role == "unknown"))
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = "warning",
                Category = "factory",
                Code = "factory_run_without_worker",
                Message = "Factory run contains no recognized worker agent."
            });
        }

        if (endConfidence != "exact")
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = "info",
                Category = "reporter",
                Code = "factory_boundary_not_exact",
                Message = "Factory end boundary was derived rather than read from an explicit terminal event."
            });
        }

        var nested = agents.Any(x => x.Role == "root" &&
            x.ParentThreadId == root.ThreadId && x.BatchOrdinal is not null);
        var rootAgent = BuildRootAgent(root, start, end, terminalEvent, segmentRootEvents, verbose, diagnostics);
        if (nested)
            rootAgent.Role = "supervisor";
        agents.Insert(0, rootAgent);

        var batches = BuildBatchReports(agents, byId);
        if (nested)
            AddBatchDiagnostics(batches, agents, diagnostics);
        AssignSequences(agents);
        var tasks = workerAgents.Select((x, index) =>
        {
            var rollout = byId.GetValueOrDefault(x.ThreadId);
            var status = rollout is null
                ? "unknown"
                : HasTerminalEvidence(rollout)
                    ? "completed"
                    : HasThreadFailure(rollout)
                        ? "failed"
                        : "interrupted";
            return new TaskReport
            {
                Number = index + 1,
                AgentThreadId = x.ThreadId,
                BatchOrdinal = x.BatchOrdinal,
                Text = x.Task,
                ExecutionProfile = x.ExecutionProfile,
                SpawnExecutionProfile = x.SpawnExecutionProfile,
                RequestedModel = x.RequestedModel,
                RequestedReasoningEffort = x.RequestedReasoningEffort,
                ActualModel = x.ActualModel,
                ActualReasoningEffort = x.ActualReasoningEffort,
                Status = status,
                DurationMilliseconds = x.DurationMilliseconds
            };
        }).ToList();

        foreach (var task in tasks)
        {
            if (task.RequestedModel is null || task.RequestedReasoningEffort is null)
                diagnostics.Add(new Diagnostic
                {
                    Severity = "info", Category = "reporter", Code = "worker_requested_settings_unspecified",
                    Message = $"Worker {task.AgentThreadId} has no recorded explicit {(task.RequestedModel is null ? "model" : "reasoning effort")} override{(task.RequestedModel is null && task.RequestedReasoningEffort is null ? " or reasoning effort override" : "")}. This may be intentional inheritance; the reporter does not infer requested overrides from project policy."
                });
            if (task.ExecutionProfile is null)
                diagnostics.Add(new Diagnostic
                {
                    Severity = "warning", Category = "reporter", Code = "worker_execution_profile_unavailable",
                    Message = $"Could not determine the execution profile for worker {task.AgentThreadId}: no matching planner task was readable, or its explicit ExecutionProfile was empty, repeated, or unknown."
                });
            if (string.IsNullOrWhiteSpace(task.Text))
                diagnostics.Add(new Diagnostic
                {
                    Severity = "warning", Category = "reporter", Code = "worker_task_unavailable",
                    Message = $"Could not read the task for worker {task.AgentThreadId} from spawn instructions or matching planner output."
                });
            if (task.ActualModel is null || task.ActualReasoningEffort is null)
            {
                var missing = new List<string>();
                if (task.ActualModel is null) missing.Add("model");
                if (task.ActualReasoningEffort is null) missing.Add("reasoning effort");
                diagnostics.Add(new Diagnostic
                {
                    Severity = "warning", Category = "reporter", Code = "worker_actual_settings_unavailable",
                    Message = $"Could not determine actual {string.Join(" and ", missing)} for worker {task.AgentThreadId}: its own turn_context and explicit resolved/actual settings contain missing or conflicting evidence. Requested settings and root defaults are not evidence of actual settings; routing could not be fully verified."
                });
            }
        }

        foreach (var task in tasks.Where(x => x.ExecutionProfile is not null &&
                     x.SpawnExecutionProfile is not null && x.ExecutionProfile != x.SpawnExecutionProfile))
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = "warning",
                Category = "factory",
                Code = "worker_execution_profile_mismatch",
                Message = $"Planner assigned {task.ExecutionProfile} to worker {task.AgentThreadId}, but its spawn prompt declares {task.SpawnExecutionProfile}."
            });
        }

        foreach (var task in tasks.Where(HasConfirmedExecutionSettingsMismatch))
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = "warning",
                Category = "factory",
                Code = "worker_execution_settings_mismatch",
                Message = $"Worker {task.AgentThreadId} requested {FormatExecutionSettings(task.RequestedModel, task.RequestedReasoningEffort)} but the trace reports {FormatExecutionSettings(task.ActualModel, task.ActualReasoningEffort)}."
            });
        }

        var rootReportedTokens = ComputeRootReportedTokens(root.Events, root.ThreadId, end, terminalEvent);
        var tokenMetrics = AggregateTokens(agents, out var tokenMethod, out var tokenComplete);

        var tokenStatus = tokenMethod == "overlap-unknown"
            ? "overlap-unknown"
            : tokenComplete ? "complete" : "unavailable";
        if (tokenStatus == "overlap-unknown")
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = "info",
                Category = "reporter",
                Code = "token_aggregation_overlap_unknown",
                Message = "Root and child token accounting may overlap; aggregate Total is intentionally unavailable."
            });
        }

        var tools = AggregateTools(agents);
        if (tools.ToolBatches is null)
            diagnostics.Add(new Diagnostic
            {
                Severity = "info", Category = "reporter", Code = "tool_batches_unavailable",
                Message = "Could not calculate tool batches: the trace lacks complete, reliable started/completed intervals for tool calls."
            });
        foreach (var agent in agents)
        {
            var missing = new List<string>();
            if (agent.StartedAt is null) missing.Add("start timestamp");
            if (agent.FinishedAt is null) missing.Add("finish timestamp");
            if (agent.Tokens.InputTokens is null) missing.Add("input tokens");
            if (agent.Tokens.CachedInputTokens is null) missing.Add("cached input tokens");
            if (agent.Tokens.OutputTokens is null) missing.Add("output tokens");
            if (missing.Count > 0)
                diagnostics.Add(new Diagnostic
                {
                    Severity = "info", Category = "reporter", Code = "agent_data_unavailable",
                    Message = $"Could not determine {string.Join(", ", missing)} for {agent.Role} thread {agent.ThreadId} from the available trace."
                });
        }
        foreach (var failed in tools.FailedCommandItems)
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = "warning",
                Category = "factory",
                Code = "failed_command",
                Message = $"Command failed in {failed.Agent}: exit/status {failed.ExitCode?.ToString() ?? failed.Status ?? "unknown"}."
            });
        }

        AddTopologyDiagnostics(agents, diagnostics);
        var finalizingBatch = batches.LastOrDefault(item => item.Status == "COMPLETED");
        var finalizingRollout = finalizingBatch is null ? null :
            byId.GetValueOrDefault(finalizingBatch.RootThreadId);
        var completionEvents = nested && finalizingRollout is not null
            ? finalizingRollout.Events.ToArray() : segmentRootEvents;
        var completionTerminal = nested && finalizingRollout is not null
            ? completionEvents.LastOrDefault(item =>
                item.Role == "assistant" && !string.IsNullOrWhiteSpace(item.Text))
            : terminalEvent;

        // In a nested run, a Supervisor's own "Factory completed" prose/JSON
        // cannot override missing, ambiguous or conflicting internal Root
        // terminal evidence. The completed run must have a contiguous chain of
        // native Roots ending in a strict COMPLETED Root terminal.
        var nestedTerminalConfirmed = false;
        if (nested)
        {
            var orderedBatches = batches.OrderBy(batch => batch.BatchOrdinal).ToArray();
            // Task names may already be reserved in a reused host session.
            // Require a positive, contiguous native ordinal sequence, not an
            // unconditional first batch of 1.
            var firstBatch = orderedBatches.FirstOrDefault()?.BatchOrdinal;
            var validRotationChain = firstBatch is > 0 &&
                firstBatch.Value <= int.MaxValue - orderedBatches.Length + 1 &&
                orderedBatches.Select(batch => batch.BatchOrdinal)
                    .SequenceEqual(Enumerable.Range(firstBatch.Value, orderedBatches.Length)
                        .Select(number => (int?)number)) &&
                orderedBatches.Take(orderedBatches.Length - 1)
                    .All(batch => batch.Status == "CONTINUE" &&
                        batch.CompletedWorkers is > 0 &&
                        batch.Reason == "BATCH_LIMIT");
            var lastBatch = orderedBatches.LastOrDefault();
            var nestedResult = validRotationChain ? lastBatch?.Status switch
            {
                "COMPLETED" => "completed",
                "BLOCKED" => "blocked",
                "NEEDS_INPUT" => "question",
                _ => "unknown"
            } : "unknown";
            nestedTerminalConfirmed = nestedResult != "unknown";
            if (!validRotationChain)
                diagnostics.Add(new Diagnostic
                {
                    Severity = "warning", Category = "factory", Code = "batch_rotation_chain_invalid",
                    Message = "Nested Factory batch ordinals, native terminal results or CONTINUE checkpoints are missing/contradictory. The run result cannot be trusted."
                });
            if (result is "completed" or "blocked" or "question" &&
                nestedTerminalConfirmed && result != nestedResult)
                diagnostics.Add(new Diagnostic
                {
                    Severity = "warning", Category = "host-trace", Code = "supervisor_root_terminal_conflict",
                    Message = $"Supervisor declared {result}, but final nested Factory Root declared {nestedResult}. The run result remains unknown."
                });
            else if (result is "completed" or "blocked" or "question" &&
                     !nestedTerminalConfirmed)
                diagnostics.Add(new Diagnostic
                {
                    Severity = "warning", Category = "host-trace", Code = "supervisor_completion_unverified",
                    Message = "Supervisor reported a final status without a complete confirmed native Root rotation chain."
                });

            var conflict = result is "completed" or "blocked" or "question" &&
                nestedTerminalConfirmed && result != nestedResult;
            result = nestedTerminalConfirmed && !conflict ? nestedResult : "unknown";
            resultReason = result == "unknown" ? null : lastBatch?.Reason;
            resultEvidence.Clear();
            if (result != "unknown")
                resultEvidence.Add("native_internal_root_terminal");
        }

        var completionInfo = BuildCompletion(
            agents,
            completionEvents,
            result,
            nested ? nestedTerminalConfirmed && result != "unknown"
                : structuredResult is not null,
            completionTerminal,
            verificationPolicy,
            diagnostics);
        if (nested && result == "completed" &&
            (completionInfo.PlannerDone != true ||
             completionInfo.ProjectVerification != "passed" ||
             completionInfo.ProtocolValidation != "ok"))
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = "warning", Category = "factory",
                Code = "nested_completed_without_verified_final_gate",
                Message = "The final native Factory Root declared COMPLETED, but a fresh final Planner # Done and successful project verification were not independently established. The reporter will not attest completed execution."
            });
            result = "unknown";
            resultReason = null;
            resultEvidence.Clear();
        }
        if (completionInfo.ProjectVerification == "unavailable")
            diagnostics.Add(new Diagnostic
            {
                Severity = "info", Category = "reporter", Code = "project_verification_unavailable",
                Message = "Could not determine final project verification: no recognized command/result evidence was found after a final planner # Done. Absence of this evidence does not mean verification was not configured."
            });
        if (completionInfo.DeclaredResult == "unavailable")
            diagnostics.Add(new Diagnostic
            {
                Severity = "info", Category = "reporter", Code = "declared_result_unavailable",
                Message = "Could not read an explicit structured Factory result; any derived run result is reported separately."
            });
        if (completionInfo.PlannerDone is null)
            diagnostics.Add(new Diagnostic
            {
                Severity = "warning", Category = "reporter", Code = "planner_done_unavailable",
                Message = "Could not establish the final planner # Done ordering relative to worker completion from the available trace."
            });

        if (completionInfo.DeclaredResult == "completed" &&
            workerAgents.Length > 0 &&
            completionInfo.PlannerDone == false)
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = "warning",
                Category = "factory",
                Code = "completed_without_planner_done",
                Message = "Factory reported COMPLETED, but no final fresh planner '# Done' evidence was found."
            });
        }

        var timeline = BuildTimeline(start, end, result, agents, tools, completionInfo);

        var maxDepth = ComputeMaximumDepth(agents);
        var otherAgents = agents.Count(x => x.Role is "subagent" or "unknown" && x.ThreadId != root.ThreadId);
        var plannerWall = SumDuration(agents.Where(x => x.Role == "planner"));
        var workerWall = SumDuration(agents.Where(x => x.Role == "worker"));
        var sumAgent = SumDuration(agents.Where(x => x.ThreadId != root.ThreadId));

        var capabilities = new CodexCapabilities
        {
            StateDb = state.DatabaseAvailable,
            SpawnEdges = state.SpawnEdgesAvailable,
            AgentRole = descendants.Any(x => !string.IsNullOrWhiteSpace(x.AgentRoleHint)),
            TokenAccounting = agents.Any(x => x.Tokens.Available) ? tokenMethod : "unavailable"
        };

        return new FactoryRunReport
        {
            Repository = repository,
            Host = new HostInfo
            {
                CodexHome = verbose ? codexHome : null,
                Capabilities = capabilities
            },
            Run = new RunInfo
            {
                RootThreadId = root.ThreadId,
                SupervisorThreadId = nested ? root.ThreadId : null,
                RunIndex = runIndex,
                StartedAt = start.Timestamp,
                FinishedAt = end,
                StartBoundary = new BoundaryInfo
                {
                    Confidence = start.Role == "user" && start.Contains(RunSkill) ? "exact" : "derived",
                    Evidence = start.Role == "user" && start.Contains(RunSkill)
                        ? "explicit idd-factory-run invocation/reference"
                        : "Factory planner/worker evidence"
                },
                EndBoundary = new BoundaryInfo { Confidence = endConfidence, Evidence = endEvidence },
                Result = result,
                Reason = resultReason,
                ResultEvidence = resultEvidence
            },
            Agents = agents,
            Tasks = tasks,
            Batches = batches,
            Metrics = new ReportMetrics
            {
                Tokens = tokenMetrics,
                RootReportedTokens = rootReportedTokens,
                TokenAggregationMethod = tokenMethod,
                TokenAggregationStatus = tokenStatus,
                TokenAggregationComplete = tokenComplete,
                Tools = tools,
                DurationMilliseconds = start.Timestamp is not null && end is not null
                    ? (long)(end.Value - start.Timestamp.Value).TotalMilliseconds
                    : null,
                TotalPlannerWallTimeMilliseconds = plannerWall,
                TotalWorkerWallTimeMilliseconds = workerWall,
                SumAgentDurationsMilliseconds = sumAgent,
                PlannerInvocations = plannerAgents.Length,
                FactoryRootInvocations = batches.Count,
                SupervisorInvocations = nested ? 1 : 0,
                WorkerInvocations = workerAgents.Length,
                OtherChildAgents = otherAgents,
                MaximumDepth = maxDepth
            },
            Timeline = timeline,
            Diagnostics = diagnostics
                .GroupBy(x => (x.Category, x.Code, x.Message))
                .Select(x => x.First())
                .ToList(),
            FactoryProjectState = stateInfo,
            Completion = completionInfo
        };
    }

    private static List<FactoryBatchReport> BuildBatchReports(
        IReadOnlyList<AgentReport> agents,
        IReadOnlyDictionary<string, CodexRollout> rollouts)
    {
        var reports = new List<FactoryBatchReport>();
        foreach (var root in agents.Where(agent => agent.Role == "root" &&
                     agent.ParentThreadId is not null && agent.BatchOrdinal is not null)
                     .OrderBy(agent => agent.BatchOrdinal).ThenBy(agent => agent.StartedAt))
        {
            var status = "unavailable";
            int? completed = null;
            string? reason = null;
            var evidence = "no confirmed final native child message";
            var candidates = root.ParentThreadId is not null &&
                rollouts.TryGetValue(root.ParentThreadId, out var supervisor)
                    ? supervisor.SpawnRecords.Values.Where(spawn =>
                        !spawn.CorrelationConflict &&
                        spawn.RequestedTaskName == root.NativeTaskName).Take(2).ToArray()
                    : [];
            var effectiveLimit = candidates.Length == 1
                ? FactoryBatchTrace.ParseBatchLimit(candidates[0].Task) : null;
            if (rollouts.TryGetValue(root.ThreadId, out var rollout))
            {
                var finished = rollout.Events.Any(item => item.Type is
                    "turn.completed" or "turn_completed" or "task_complete");
                var lastAssistant = rollout.Events.LastOrDefault(item =>
                    item.Role == "assistant" && !string.IsNullOrWhiteSpace(item.Text));
                if (finished && lastAssistant is not null && effectiveLimit is > 0 &&
                    FactoryBatchTrace.TryParseTerminal(lastAssistant.Text, effectiveLimit, out var terminal))
                {
                    status = terminal!.Status;
                    completed = terminal.CompletedWorkers;
                    reason = terminal.Reason;
                    evidence = "completed native child turn with strict terminal fields";
                }
                else
                    evidence = effectiveLimit is null
                        ? "no unambiguous effective max_workers_per_batch in the native Root assignment"
                        : finished
                            ? "native turn ended without valid terminal fields for effective batch limit"
                            : "native Root turn completion unavailable";
            }
            reports.Add(new FactoryBatchReport
            {
                BatchOrdinal = root.BatchOrdinal,
                RootThreadId = root.ThreadId,
                NativeTaskName = root.NativeTaskName,
                EffectiveWorkerLimit = effectiveLimit,
                Status = status,
                CompletedWorkers = completed,
                Reason = reason,
                TerminalEvidence = evidence
            });
        }
        return reports;
    }

    private static void AddBatchDiagnostics(
        IReadOnlyList<FactoryBatchReport> batches,
        IReadOnlyList<AgentReport> agents,
        List<Diagnostic> diagnostics)
    {
        foreach (var duplicate in batches.Where(item => item.BatchOrdinal is not null)
                     .GroupBy(item => item.BatchOrdinal).Where(group => group.Count() > 1))
            diagnostics.Add(new Diagnostic
            {
                Severity = "warning", Category = "host-trace", Code = "duplicate_factory_batch",
                Message = $"Multiple native Factory Roots use batch ordinal {duplicate.Key}."
            });

        for (var i = 0; i < batches.Count; i++)
        {
            var batch = batches[i];
            var workers = agents.Where(agent => agent.Role == "worker" &&
                agent.ParentThreadId == batch.RootThreadId).ToArray();
            if (batch.Status == "unavailable")
                diagnostics.Add(new Diagnostic
                {
                    Severity = "warning", Category = "host-trace", Code = "batch_terminal_unavailable",
                    Message = $"Factory Root {batch.RootThreadId}: {batch.TerminalEvidence}."
                });
            if (batch.CompletedWorkers is int count && count > workers.Length)
                diagnostics.Add(new Diagnostic
                {
                    Severity = "warning", Category = "factory", Code = "batch_worker_count_unverified",
                    Message = $"Factory Root {batch.RootThreadId} declares {count} confirmed Workers, but only {workers.Length} native Worker children were discovered. Successful durable checkpoints cannot be inferred from the message."
                });
            if (i + 1 < batches.Count && batch.Status != "CONTINUE")
                diagnostics.Add(new Diagnostic
                {
                    Severity = "warning", Category = "factory", Code = "batch_rotation_without_continue",
                    Message = $"A later Root followed batch {batch.BatchOrdinal} without a confirmed CONTINUE terminal."
                });
            foreach (var worker in workers.Where(worker => worker.BatchOrdinal != batch.BatchOrdinal))
                diagnostics.Add(new Diagnostic
                {
                    Severity = "warning", Category = "host-trace", Code = "worker_batch_identity_conflict",
                    Message = $"Worker {worker.ThreadId} has batch {worker.BatchOrdinal?.ToString() ?? "unavailable"} but native parent belongs to batch {batch.BatchOrdinal}."
                });
        }
    }

    private static bool HasConfirmedExecutionSettingsMismatch(TaskReport task) =>
        (!string.IsNullOrWhiteSpace(task.RequestedModel) &&
         !string.IsNullOrWhiteSpace(task.ActualModel) &&
         !string.Equals(task.RequestedModel, task.ActualModel, StringComparison.Ordinal)) ||
        (!string.IsNullOrWhiteSpace(task.RequestedReasoningEffort) &&
         !string.IsNullOrWhiteSpace(task.ActualReasoningEffort) &&
         !string.Equals(task.RequestedReasoningEffort, task.ActualReasoningEffort, StringComparison.Ordinal));

    private static string FormatExecutionSettings(string? model, string? reasoning) =>
        $"model={model ?? "unknown"}, reasoning={reasoning ?? "unknown"}";

    private static List<CodexRollout> SelectDescendants(
        CodexRollout root,
        DateTimeOffset? runStart,
        DateTimeOffset? nextRunStart,
        IReadOnlyList<CodexRollout> all,
        IReadOnlyDictionary<string, CodexRollout> byId,
        CodexStateSnapshot state)
    {
        var parentMap = new Dictionary<string, string>(state.ParentByChild, StringComparer.Ordinal);
        foreach (var rollout in all)
        {
            if (!string.IsNullOrWhiteSpace(rollout.ParentThreadId))
                parentMap[rollout.ThreadId] = rollout.ParentThreadId!;
        }
        foreach (var spawningRollout in all)
        {
            foreach (var spawn in spawningRollout.SpawnRecords.Values)
            {
                if (spawn.CorrelationConflict)
                    continue;
                foreach (var childId in spawn.Children)
                    parentMap.TryAdd(childId, spawn.SenderThreadId ?? spawningRollout.ThreadId);
            }
        }

        var result = new List<CodexRollout>();
        var queue = new Queue<string>();
        queue.Enqueue(root.ThreadId);
        var seen = new HashSet<string>(StringComparer.Ordinal) { root.ThreadId };

        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            foreach (var edge in parentMap.Where(x => x.Value == parent))
            {
                if (!seen.Add(edge.Key))
                    continue;
                queue.Enqueue(edge.Key);
                if (!byId.TryGetValue(edge.Key, out var child))
                    continue;
                child.ParentThreadId ??= parent;
                var childActivityStart = ActivityStartedAt(child);
                if (runStart is not null && childActivityStart is not null && childActivityStart < runStart)
                    continue;
                if (nextRunStart is not null && childActivityStart is not null && childActivityStart >= nextRunStart)
                    continue;
                result.Add(child);
            }
        }

        return result;
    }

    private static DateTimeOffset? ActivityStartedAt(CodexRollout rollout) =>
        rollout.Events.FirstOrDefault(x =>
            x.Type != "session_meta" &&
            x.Usage is null)?.Timestamp ?? rollout.StartedAt;

    private static DateTimeOffset? ExecutionFinishedAt(CodexRollout rollout) =>
        rollout.Events
            .Where(x => x.Type != "session_meta" && x.Usage is null)
            .Select(x => x.Timestamp)
            .Where(x => x is not null)
            .LastOrDefault() ?? rollout.StartedAt;

    private static List<AgentReport> BuildAgents(
        CodexRollout root,
        IReadOnlyList<CodexEvent> rootEvents,
        IReadOnlyList<CodexRollout> descendants,
        CodexStateSnapshot state,
        bool verbose,
        List<Diagnostic> diagnostics)
    {
        var agents = new List<AgentReport>();
        var spawningRollouts = new[] { root }.Concat(descendants).ToArray();

        foreach (var child in descendants.OrderBy(ActivityStartedAt).ThenBy(x => x.ThreadId, StringComparer.Ordinal))
        {
            var related = spawningRollouts.SelectMany(rollout => rollout.SpawnRecords.Values)
                .Where(record => record.Children.Contains(child.ThreadId, StringComparer.Ordinal)).ToArray();
            var spawn = FindSpawnRecord(spawningRollouts, child.ThreadId);
            var uncertain = related.Length > 1 || related.Any(record => record.CorrelationConflict);
            var role = uncertain ? "unknown" : ClassifyRole(root, child, spawn, diagnostics);
            if (uncertain)
                diagnostics.Add(new Diagnostic
                {
                    Severity = "warning", Category = "factory", Code = "agent_role_conflict",
                    Message = $"Child {child.ThreadId} has conflicting or non-unique native spawn identity."
                });
            var task = role == "worker" ? ExtractFactoryTask(spawn?.Task) : null;
            var (outcome, outcomeAt) = role == "planner" ? PlannerOutcome(child) : (null, null);
            var actualSettings = ChildExecutionSettings(child, spawn, role == "worker" ? diagnostics : null);
            var tokens = ComputeWholeThreadTokens(child, out var authoritativeTokens);
            agents.Add(new AgentReport
            {
                ThreadId = child.ThreadId,
                ParentThreadId = child.ParentThreadId ?? state.ParentByChild.GetValueOrDefault(child.ThreadId),
                Role = role,
                NativeTaskName = spawn?.RequestedTaskName,
                BatchOrdinal = FactoryBatchTrace.ParseName(spawn?.RequestedTaskName) is { BatchOrdinal: > 0 } batch
                    ? batch.BatchOrdinal : null,
                StartedAt = spawn?.Timestamp ?? ActivityStartedAt(child),
                FinishedAt = ExecutionFinishedAt(child),
                Task = task,
                TaskTitle = TaskTitle(task),
                SpawnExecutionProfile = spawn?.ExecutionProfile,
                RequestedModel = spawn?.RequestedModel,
                RequestedReasoningEffort = spawn?.RequestedReasoningEffort,
                ActualModel = actualSettings.Model,
                ActualReasoningEffort = actualSettings.ReasoningEffort,
                PlannerOutcome = outcome,
                PlannerOutcomeAt = outcomeAt,
                Tokens = tokens,
                TokensAuthoritative = authoritativeTokens,
                Tools = AnalyzeTools(child.Events, role),
                RolloutPath = verbose ? child.Path : null
            });
        }
        return agents;
    }

    private static (string? Model, string? ReasoningEffort) ChildExecutionSettings(
        CodexRollout child, SpawnRecord? spawn, List<Diagnostic>? diagnostics)
    {
        // Tool events describe invoked operations, including grandchildren, not this worker.
        var settings = child.Events.Where(x => x.ToolId is null &&
            (x.ThreadId is null || x.ThreadId == child.ThreadId)).ToArray();
        var contexts = settings.Where(x => x.Type == "turn_context").ToArray();
        var models = contexts.Select(x => x.ActualModel).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        var efforts = contexts.Select(x => x.ActualReasoningEffort).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        var contextEvidence = string.Join("; ", contexts.Select(x =>
            $"turn_context at line {x.Ordinal}: {FormatExecutionSettings(x.ActualModel, x.ActualReasoningEffort)}"));

        if (models.Length > 1 || efforts.Length > 1)
            diagnostics?.Add(new Diagnostic
            {
                Severity = "warning", Category = "host-trace", Code = "worker_turn_context_settings_conflict",
                Message = $"Worker {child.ThreadId} has differing turn_context settings: {contextEvidence}. Conflicting fields remain unknown; spawn metadata cannot resolve changes between turns, so routing could not be fully verified."
            });

        if (contexts.Any(x =>
                SettingsDisagree(spawn?.ActualModel, x.ActualModel) ||
                SettingsDisagree(spawn?.ActualReasoningEffort, x.ActualReasoningEffort)))
            diagnostics?.Add(new Diagnostic
            {
                Severity = "warning", Category = "host-trace", Code = "worker_spawn_settings_conflict",
                Message = $"Worker {child.ThreadId} spawn metadata reports {FormatExecutionSettings(spawn?.ActualModel, spawn?.ActualReasoningEffort)}, but its own context disagrees: {contextEvidence}. The worker's turn_context takes precedence; conflicting values across turns remain unknown."
            });

        // A changing field must not fall back to spawn metadata or an earlier event.
        // Stable context fields are primary; other explicit evidence fills absent fields only.
        return (models.Length > 1 ? null : models.SingleOrDefault() ?? spawn?.ActualModel ??
                settings.FirstOrDefault(x => x.ActualModel is not null)?.ActualModel,
            efforts.Length > 1 ? null : efforts.SingleOrDefault() ?? spawn?.ActualReasoningEffort ??
                settings.FirstOrDefault(x => x.ActualReasoningEffort is not null)?.ActualReasoningEffort);
    }

    private static bool SettingsDisagree(string? first, string? second) =>
        first is not null && second is not null && !string.Equals(first, second, StringComparison.Ordinal);

    private static AgentReport BuildRootAgent(
        CodexRollout root,
        CodexEvent start,
        DateTimeOffset? end,
        CodexEvent? terminalEvent,
        IReadOnlyList<CodexEvent> segmentEvents,
        bool verbose,
        List<Diagnostic> diagnostics)
    {
        var tokens = ComputeSegmentTokens(root.Events, root.ThreadId, start, end, terminalEvent,
            out var authoritativeTokens, out var unavailableReason);
        if (unavailableReason is not null)
            diagnostics.Add(new Diagnostic
            {
                Severity = "info", Category = "reporter",
                Code = unavailableReason,
                Message = $"Could not establish root Factory Run token usage for {root.ThreadId}: " +
                    (unavailableReason switch
                    {
                        "root_start_baseline_unavailable" => "no reliable cumulative baseline before Factory.",
                        "root_terminal_usage_unavailable" => "no reliable cumulative counter at the Factory terminal boundary.",
                        "root_terminal_usage_correlation_unavailable" => "a later usage record cannot be proven to belong to the terminal turn.",
                        _ => "the native counters are incomplete or inconsistent."
                    })
            });
        return new AgentReport
        {
            ThreadId = root.ThreadId,
            Role = "root",
            StartedAt = start.Timestamp,
            FinishedAt = end,
            Tokens = tokens,
            TokensAuthoritative = authoritativeTokens,
            Tools = AnalyzeTools(
                segmentEvents.Where(x =>
                    (start.Timestamp is null || x.Timestamp is null || x.Timestamp >= start.Timestamp) &&
                    (end is null || x.Timestamp is null || x.Timestamp <= end || Correlates(x, terminalEvent))),
                "Factory root"),
            RolloutPath = verbose ? root.Path : null
        };
    }

    private static string ClassifyRole(CodexRollout root, CodexRollout child,
        SpawnRecord? spawn, List<Diagnostic> diagnostics)
    {
        var nativeRole = TrustedFactoryRole(child.AgentRoleHint);
        var namedRole = FactoryTaskNameRole(spawn?.RequestedTaskName);
        var initialInstructions = child.Events
            .Where(x => string.Equals(x.Role, "user", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(x.Role, "system", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Text)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

        var roleSources = new[]
        {
            nativeRole, namedRole,
            ExplicitFactoryRole(spawn?.Task), ExplicitFactoryRole(initialInstructions),
            LegacyFactoryRole(spawn?.Task), LegacyFactoryRole(initialInstructions)
        }.Where(x => x is not null).Distinct(StringComparer.Ordinal).ToArray();

        var invalidPath = spawn is not null && (
            spawn.CorrelationConflict ||
            namedRole is not null && child.AgentPath is not null &&
                child.AgentPath.Split('/').Last() != spawn.RequestedTaskName ||
            spawn.ChildAgentPath?.StartsWith('/') == true &&
                child.AgentPath is not null && spawn.ChildAgentPath != child.AgentPath);

        if (invalidPath || roleSources.Length > 1 || roleSources.Contains("ambiguous"))
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = "warning", Category = "factory", Code = "agent_role_conflict",
                Message = $"Child {child.ThreadId} has incompatible role or identity evidence: " +
                    $"agent_role={child.AgentRoleHint ?? "unavailable"}, " +
                    $"requested task_name={spawn?.RequestedTaskName ?? "unavailable"}, " +
                    $"child agent_path={child.AgentPath ?? "unavailable"}, " +
                    $"spawn output path={spawn?.ChildAgentPath ?? "unavailable"}, " +
                    $"roles={string.Join(",", roleSources)}."
            });
            return "unknown";
        }

        return roleSources.SingleOrDefault() ?? FallbackRole(root, child);
    }

    private static string FallbackRole(CodexRollout root, CodexRollout child) =>
        child.ParentThreadId is not null && child.ParentThreadId != root.ThreadId
            ? "subagent" : "unknown";

    private static string? FactoryTaskNameRole(string? name)
    {
        if (name is null)
            return null;
        return FactoryBatchTrace.ParseName(name)?.Role;
    }

    private static string? TrustedFactoryRole(string? hint) =>
        hint?.Trim().ToLowerInvariant() switch
        {
            "planner" or "factory-planner" or "factory_planner" or "idd-factory-planner" => "planner",
            "worker" or "factory-worker" or "factory_worker" or "idd-factory-worker" => "worker",
            "factory-root" or "factory_root" or "idd-factory-root" => "root",
            _ => null
        };

    private static SpawnRecord? FindSpawnRecord(IEnumerable<CodexRollout> rollouts, string childId)
    {
        var matches = rollouts.SelectMany(rollout => rollout.SpawnRecords.Values)
            .Where(record => record.Children.Contains(childId, StringComparer.Ordinal)).ToArray();
        return matches.Length == 1 && !matches[0].CorrelationConflict ? matches[0] : null;
    }

    private static string? ExplicitFactoryRole(string? instructions)
    {
        var preamble = BeforeWorkerAssignment(instructions);
        var planner = HasRoleLine(preamble, PlannerMarker);
        var worker = HasRoleLine(preamble, WorkerMarker);
        return planner && worker ? "ambiguous" : planner ? "planner" : worker ? "worker" : null;
    }

    private static string? LegacyFactoryRole(string? instructions)
    {
        var preamble = BeforeWorkerAssignment(instructions);
        var planner = HasFactoryMarker(preamble, LegacyPlannerSkill);
        var worker = HasFactoryMarker(preamble, LegacyWorkerSkill);
        return planner && worker ? "ambiguous" : planner ? "planner" : worker ? "worker" : null;
    }

    private static bool HasUnambiguousFactoryRole(string? instructions)
    {
        var explicitRole = ExplicitFactoryRole(instructions);
        if (explicitRole is "planner" or "worker")
            return true;
        return explicitRole is null && LegacyFactoryRole(instructions) is "planner" or "worker";
    }

    private static string BeforeWorkerAssignment(string? instructions)
    {
        if (string.IsNullOrWhiteSpace(instructions))
            return "";
        var boundary = instructions.IndexOf(WorkerAssignmentStart, StringComparison.OrdinalIgnoreCase);
        return boundary < 0 ? instructions : instructions[..boundary];
    }

    private static bool HasRoleLine(string? text, string marker) =>
        !string.IsNullOrWhiteSpace(text) &&
        Regex.IsMatch(text, @"(?m)^\s*" + Regex.Escape(marker) + @"(?:\s|$)", RegexOptions.IgnoreCase);

    private static bool HasFactoryMarker(string? text, string marker) =>
        !string.IsNullOrWhiteSpace(text) &&
        Regex.IsMatch(text, @"(?im)^\s*(?:use|invoke|run|follow)\s+" +
            Regex.Escape(marker) + @"(?:\s|[.,;:]|$)");

    private static bool IsNativeSpawn(CodexEvent e) =>
        e.ToolName?.Contains("spawn_agent", StringComparison.OrdinalIgnoreCase) == true;

    private static (string? Outcome, DateTimeOffset? Timestamp) PlannerOutcome(CodexRollout rollout)
    {
        foreach (var e in rollout.Events
                     .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                     .Reverse())
        {
            if (Regex.IsMatch(e.Text!, @"(?m)^#\s*Question\s*$", RegexOptions.IgnoreCase))
                return ("Question", e.Timestamp);
            if (Regex.IsMatch(e.Text!, @"(?m)^#\s*Done\s*$", RegexOptions.IgnoreCase))
                return ("Done", e.Timestamp);
            if (Regex.IsMatch(e.Text!, @"(?m)^(?:#\s*Task\s*$|##\s*Task\s+\d+:)", RegexOptions.IgnoreCase))
                return ("Tasks", e.Timestamp);
        }
        return ("Unknown", null);
    }

    private static string? ExtractFactoryTask(string? spawnTask)
    {
        if (string.IsNullOrWhiteSpace(spawnTask))
            return null;

        var text = spawnTask.Replace("\r\n", "\n");
        var start = text.IndexOf(WorkerAssignmentStart, StringComparison.OrdinalIgnoreCase);
        if (start >= 0)
        {
            var begin = start + WorkerAssignmentStart.Length;
            var end = text.IndexOf(WorkerAssignmentEnd, begin, StringComparison.OrdinalIgnoreCase);
            if (end < 0)
                return null;

            // Never extract a task from role instructions or Engineering Guardrails.
            var assignment = text[begin..end];
            var heading = Regex.Match(assignment,
                @"(?ims)^\s*#\s*Task\s*$\s*(?<task>.*?)(?=^\s*#\s*(?:Task|ExecutionProfile|TaskRelatedIntent|TaskRelatedEngineering|AlwaysEngineering)\s*$|\z)");
            if (!heading.Success)
                return null;
            var task = heading.Groups["task"].Value.Trim();
            return string.IsNullOrWhiteSpace(task) ? null : BoundAssignmentTask(task, 1000);
        }

        // Modern spawn instructions without a complete boundary are ambiguous.
        if (HasRoleLine(text, WorkerMarker))
            return null;

        // Retain historical inline task extraction for the public worker skill.
        var taskMatch = Regex.Match(text, @"(?im)(?:^|\s)Task\s*:\s*(.+)$");
        if (taskMatch.Success)
            return Bound(taskMatch.Groups[1].Value, 1000);

        var headingLegacy = Regex.Match(text, @"(?im)^\s*#\s*Task\s*$");
        if (headingLegacy.Success)
        {
            var remainder = text[(headingLegacy.Index + headingLegacy.Length)..].Trim();
            if (!string.IsNullOrWhiteSpace(remainder))
                return Bound(remainder, 1000);
        }

        var lines = text.Split('\n')
            .Select(x => x.Trim())
            .Where(x => x.Length > 0 &&
                        !x.Contains(LegacyWorkerSkill, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return lines.Length == 0 ? null : Bound(string.Join(" ", lines), 1000);
    }

    private static string BoundAssignmentTask(string task, int max) =>
        task.Length <= max ? task : task[..max] + " [truncated]";

    private static string? TaskTitle(string? task)
    {
        if (string.IsNullOrWhiteSpace(task))
            return null;
        var first = task.Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .FirstOrDefault(x => x.Length > 0);
        return string.IsNullOrWhiteSpace(first) ? null : Bound(first, 80);
    }

    private sealed record PlannedTask(string Text, string? ExecutionProfile);

    private static string? ExtractPlannerExecutionProfile(string text)
    {
        var headings = Regex.Matches(text,
            @"(?im)^[ \t]*#[ \t]*ExecutionProfile[ \t]*\r?$[ \t\r\n]*(?<value>[^\r\n]*)");
        var inline = Regex.Matches(text,
            @"(?im)^ExecutionProfile:\s*(?<value>[^\r\n]*)$");
        // Legacy output with no section is standard. Any explicit malformed
        // or repeated profile remains unknown instead of being inferred.
        if (headings.Count + inline.Count == 0)
            return "standard";
        if (headings.Count + inline.Count != 1)
            return null;
        var value = (headings.Count == 1 ? headings[0] : inline[0]).Groups["value"].Value.Trim();
        return value is "economy" or "standard" or "strong" ? value : null;
    }

    private static void PopulateMissingWorkerTasks(
        IReadOnlyList<AgentReport> agents,
        IReadOnlyDictionary<string, CodexRollout> rollouts)
    {
        var pending = new Queue<PlannedTask>();
        foreach (var agent in agents.OrderBy(x => x.StartedAt).ThenBy(x => x.ThreadId, StringComparer.Ordinal))
        {
            // A fully validated plan.md batch can legitimately span successive
            // fresh Roots. Keep the pending Planner-to-Worker queue across
            // boundaries; ONLY a new real Planner result resets it. This avoids
            // losing ExecutionProfile provenance in subsequent Root batches.
            if (agent.Role == "planner" && rollouts.TryGetValue(agent.ThreadId, out var planner))
            {
                pending.Clear();
                foreach (var task in ExtractPlannerTasks(planner))
                    pending.Enqueue(task);
                continue;
            }

            if (agent.Role != "worker" || pending.Count == 0)
                continue;

            var plannedTask = pending.Dequeue();
            if (string.IsNullOrWhiteSpace(agent.Task))
            {
                agent.Task = plannedTask.Text;
                agent.TaskTitle = TaskTitle(plannedTask.Text);
            }
            agent.ExecutionProfile = plannedTask.ExecutionProfile;
        }
    }

    private static IReadOnlyList<PlannedTask> ExtractPlannerTasks(CodexRollout rollout)
    {
        var text = rollout.Events
            .Where(x => string.Equals(x.Role, "assistant", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(x.Text) &&
                        Regex.IsMatch(x.Text!, @"(?m)^(?:\s*#\s*Task\s*$|##\s*Task\s+\d+:)", RegexOptions.IgnoreCase))
            .Select(x => x.Text!)
            .LastOrDefault();
        if (text is null)
            return [];

        // Accept the two bounded, explicit planner layouts observed in native
        // traces, without guessing profiles from models or root commentary.
        var canonical = Regex.IsMatch(text, @"(?m)^#\s*Task\s*$", RegexOptions.IgnoreCase);
        var sections = canonical
            ? Regex.Matches(text, @"(?ms)^#\s*Task\s*$\s*(?<section>.*?)(?=^#\s*Task\s*$|\z)",
                RegexOptions.IgnoreCase)
            : Regex.Matches(text, @"(?ms)^##\s*Task\s+\d+:\s*(?<section>.*?)(?=^##\s*Task\s+\d+:|\z)",
                RegexOptions.IgnoreCase);
        return sections.Select(match =>
            {
                var section = match.Groups["section"].Value;
                var task = Regex.Split(section,
                        @"(?m)^(?:\s*#\s*(?:ExecutionProfile|TaskRelatedIntent|TaskRelatedEngineering)\s*$|(?:ExecutionProfile|TaskRelatedIntent):)",
                        RegexOptions.IgnoreCase)[0];
                return new PlannedTask(Bound(task.Trim(), 1000), ExtractPlannerExecutionProfile(section));
            })
            .Where(task => !string.IsNullOrWhiteSpace(task.Text))
            .ToArray();
    }


    private static bool HasCompleteTokens(TokenMetrics? usage) =>
        usage is not null &&
        usage.InputTokens is >= 0 &&
        usage.CachedInputTokens is >= 0 &&
        usage.OutputTokens is >= 0 &&
        usage.CachedInputTokens <= usage.InputTokens &&
        usage.NewInputTokens == usage.InputTokens - usage.CachedInputTokens;

    private static bool BelongsToThread(CodexEvent e, string threadId) =>
        !e.TurnIdentityConflict && (e.ThreadId is null || e.ThreadId == threadId);

    private static TokenMetrics ComputeWholeThreadTokens(CodexRollout rollout, out bool authoritative)
    {
        authoritative = false;
        var threadUsage = rollout.Events.LastOrDefault(x =>
            x.Usage is not null && x.UsageScope == "thread" &&
            BelongsToThread(x, rollout.ThreadId));
        if (threadUsage?.Usage is not null)
        {
            authoritative = HasCompleteTokens(threadUsage.Usage);
            return threadUsage.Usage;
        }

        var usageEvents = rollout.Events.Where(x => x.Usage is not null &&
            BelongsToThread(x, rollout.ThreadId)).ToArray();
        var cumulative = usageEvents.LastOrDefault(x => x.UsageIsCumulative);
        if (cumulative?.Usage is not null)
            return cumulative.Usage;

        // Legacy turn counters are not authoritative independent thread totals.
        var perTurn = usageEvents.Where(x => !x.UsageIsCumulative).Select(x => x.Usage!).ToArray();
        return perTurn.Length == 0 ? new TokenMetrics() : TokenMetrics.Sum(perTurn);
    }

    private static bool HasFreshSessionPrelude(
        IReadOnlyList<CodexEvent> events, CodexEvent start)
    {
        // Only a complete, recognized session prelude proves zero prior model usage.
        // Unknown events, earlier user turns and truncated sessions fail closed.
        if (events.Count == 0 || events[0].Type != "session_meta" ||
            events[0].Ordinal >= start.Ordinal || start.TurnIdentityConflict)
            return false;

        foreach (var e in events.Where(x => x.Ordinal < start.Ordinal))
        {
            if (!e.IsInitialization || e.Usage is not null || e.TurnIdentityConflict)
                return false;
            if (e.TurnId is not null &&
                (start.TurnId is null || e.TurnId != start.TurnId))
                return false;
        }
        return true;
    }

    private static bool Correlates(CodexEvent usage, CodexEvent? terminal, string? threadId = null)
    {
        if (terminal is null || usage.Usage is null ||
            usage.TurnIdentityConflict || terminal.TurnIdentityConflict)
            return false;

        if (threadId is not null &&
            (usage.ThreadId is not null && usage.ThreadId != threadId ||
             terminal.ThreadId is not null && terminal.ThreadId != threadId))
            return false;
        if (usage.ThreadId is not null && terminal.ThreadId is not null &&
            usage.ThreadId != terminal.ThreadId)
            return false;

        static bool Same(string? a, string? b) =>
            !string.IsNullOrWhiteSpace(a) &&
            string.Equals(a, b, StringComparison.Ordinal);

        // Shared root_turn_id does not identify a child turn when both native
        // turn_ids are present and different.
        if (usage.TurnId is not null && terminal.TurnId is not null)
            return Same(usage.TurnId, terminal.TurnId);

        if (Same(usage.ResponseId, terminal.ResponseId))
            return true;
        // Root-turn fallback is safe only when neither event supplies a
        // competing turn identity.
        return usage.TurnId is null && terminal.TurnId is null &&
               Same(usage.RootTurnId, terminal.RootTurnId);
    }

    private static bool HasAmbiguousTerminalUsage(
        IReadOnlyList<CodexEvent> events, string threadId, CodexEvent? terminal) =>
        terminal is not null && events.Any(x =>
            x.Ordinal > terminal.Ordinal &&
            x.UsageScope == "thread" && x.Usage is not null &&
            (x.ThreadId is null || x.ThreadId == threadId) &&
            !Correlates(x, terminal, threadId) &&
            (terminal.TurnIdentityConflict || x.TurnIdentityConflict ||
             terminal.TurnId is null || x.TurnId is null));

    private static bool AtTerminalBoundary(CodexEvent usage, DateTimeOffset end, CodexEvent? terminal, string threadId)
    {
        if (!BelongsToThread(usage, threadId))
            return false;
        if (terminal is not null && usage.Ordinal > terminal.Ordinal)
            return Correlates(usage, terminal, threadId);
        return usage.Timestamp <= end;
    }

    private static TokenMetrics ComputeSegmentTokens(
        IReadOnlyList<CodexEvent> events,
        string threadId,
        CodexEvent start,
        DateTimeOffset? end,
        CodexEvent? terminalEvent,
        out bool authoritative,
        out string? unavailableReason)
    {
        authoritative = false;
        unavailableReason = null;
        if (start.Timestamp is null || end is null)
        {
            unavailableReason = "root_terminal_usage_unavailable";
            return new TokenMetrics();
        }

        var native = events.Where(x => x.UsageScope == "thread" &&
            x.Usage is not null && BelongsToThread(x, threadId)).ToArray();
        if (native.Length > 0)
        {
            var before = native.LastOrDefault(x => x.Ordinal < start.Ordinal &&
                x.Timestamp < start.Timestamp);
            TokenMetrics? baseline = before?.Usage;
            if (baseline is null && HasFreshSessionPrelude(events, start))
                baseline = new TokenMetrics
                {
                    InputTokens = 0, CachedInputTokens = 0, NewInputTokens = 0, OutputTokens = 0
                };

            if (!HasCompleteTokens(baseline))
            {
                unavailableReason = "root_start_baseline_unavailable";
                return new TokenMetrics();
            }

            if (HasAmbiguousTerminalUsage(events, threadId, terminalEvent))
            {
                unavailableReason = "root_terminal_usage_correlation_unavailable";
                return new TokenMetrics();
            }

            var finish = native.LastOrDefault(x => AtTerminalBoundary(x, end.Value, terminalEvent, threadId));
            if (finish?.Usage is null)
            {
                unavailableReason = native.Any(x => x.Ordinal > (terminalEvent?.Ordinal ?? long.MaxValue))
                    ? "root_terminal_usage_correlation_unavailable" : "root_terminal_usage_unavailable";
                return new TokenMetrics();
            }

            var delta = TokenMetrics.Subtract(finish.Usage, baseline!);
            if (!HasCompleteTokens(delta))
            {
                unavailableReason = "root_token_counters_inconsistent";
                return delta;
            }
            authoritative = true;
            return delta;
        }

        // Legacy cumulative counters may still yield a segment value, but
        // without native per-thread identity they cannot establish aggregation.
        var cumulative = events.Where(x => x.UsageIsCumulative &&
            x.Usage is not null && BelongsToThread(x, threadId)).ToArray();
        if (cumulative.Length > 0)
        {
            var before = cumulative.LastOrDefault(x => x.Timestamp < start.Timestamp);
            var finish = cumulative.LastOrDefault(x =>
                AtTerminalBoundary(x, end.Value, terminalEvent, threadId));
            if (before?.Usage is not null && finish?.Usage is not null)
                return TokenMetrics.Subtract(finish.Usage, before.Usage);
            unavailableReason = before?.Usage is null
                ? "root_start_baseline_unavailable" : "root_terminal_usage_unavailable";
            return new TokenMetrics();
        }

        var perTurn = events.Where(x => !x.UsageIsCumulative && x.Usage is not null &&
                x.Timestamp >= start.Timestamp &&
                AtTerminalBoundary(x, end.Value, terminalEvent, threadId))
            .Select(x => x.Usage!)
            .ToArray();
        if (perTurn.Length == 0)
            unavailableReason = "root_terminal_usage_unavailable";
        return perTurn.Length == 0 ? new TokenMetrics() : TokenMetrics.Sum(perTurn);
    }

    private static TokenMetrics ComputeRootReportedTokens(
        IReadOnlyList<CodexEvent> events,
        string threadId,
        DateTimeOffset? end,
        CodexEvent? terminalEvent)
    {
        if (end is null || HasAmbiguousTerminalUsage(events, threadId, terminalEvent))
            return new TokenMetrics();
        var eligible = events.Where(x => x.Usage is not null &&
            AtTerminalBoundary(x, end.Value, terminalEvent, threadId)).ToArray();
        // A cumulative root-reported metric must never be synthesized from
        // independent turn counters or multiple representations of one record.
        return eligible.LastOrDefault(x => x.UsageScope == "thread")?.Usage ??
               eligible.LastOrDefault(x => x.UsageIsCumulative)?.Usage ??
               new TokenMetrics();
    }

    private static ToolMetrics AnalyzeTools(IEnumerable<CodexEvent> source, string agent)
    {
        var events = source.OrderBy(x => x.Ordinal).ToArray();
        var calls = new Dictionary<string, ToolCallState>(StringComparer.Ordinal);
        var wrapperIds = new HashSet<string>(StringComparer.Ordinal);
        var anonymous = 0;

        foreach (var e in events)
        {
            if (e.ToolPhase is null)
                continue;

            var id = e.ToolId ?? "anonymous-" + ++anonymous;
            if (!calls.TryGetValue(id, out var call))
            {
                call = new ToolCallState { Id = id };
                calls[id] = call;
            }

            if (!string.IsNullOrWhiteSpace(e.ToolName))
                call.Name = e.ToolName;
            if (!string.IsNullOrWhiteSpace(e.ToolArguments))
                call.Arguments = e.ToolArguments;
            if (!string.IsNullOrWhiteSpace(e.ToolOutput))
                call.Output = e.ToolOutput;
            call.Timestamp ??= e.Timestamp;
            call.Status = e.Status ?? call.Status;
            call.ExitCode = e.ExitCode ?? call.ExitCode;
            call.IsNative |= e.IsNativeOperation;
            call.IsWrapper |= e.IsHostWrapper;
            if (e.IsHostWrapper)
                wrapperIds.Add(id);

            if (e.ToolPhase == "started")
            {
                call.StartedAt ??= e.Timestamp;
                call.HadLifecycle = true;
            }
            else if (e.ToolPhase == "completed")
            {
                call.CompletedAt = e.Timestamp ?? call.CompletedAt;
                call.HadLifecycle = true;
            }
        }

        var hasNative = calls.Values.Any(x => x.IsNative);
        // Some hosts emit native command events but only response-item spawn calls.
        // Keep those collaboration calls; call IDs already deduplicate native wrappers.
        var semantic = (hasNative
                ? calls.Values.Where(x => x.IsNative || x.Name is "spawn_agent" or "wait_agent")
                : calls.Values)
            .ToArray();

        var metrics = new ToolMetrics
        {
            ToolCalls = calls.Count,
            NativeOperations = calls.Values.Count(x => x.IsNative),
            HostWrapperCalls = wrapperIds.Count,
            ToolBatches = ComputeToolBatches(semantic)
        };

        foreach (var call in semantic)
        {
            var name = call.Name ?? "";
            var outputChars = call.Output?.Length ?? 0;
            metrics.ToolOutputCharacters += outputChars;

            var failed = IsFailed(call);
            if (failed)
                metrics.FailedToolOutputCharacters += outputChars;

            if (IsCommandName(name))
            {
                metrics.Commands++;
                if (failed)
                {
                    metrics.FailedCommands++;
                    metrics.FailedCommandItems.Add(new FailedCommand
                    {
                        Agent = agent,
                        Timestamp = call.CompletedAt ?? call.Timestamp,
                        ExitCode = call.ExitCode,
                        Status = call.Status,
                        Command = Bound(ExtractCommand(call.Arguments), 500)
                    });
                }
            }
            else if (name == "file_change")
            {
                metrics.FileChanges++;
                metrics.FileOperations++;
            }
            else if (name == "spawn_agent")
                metrics.SpawnAgentCalls++;
            else if (name == "wait_agent")
                metrics.WaitAgentCalls++;
            else if (name.Contains("search", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("find", StringComparison.OrdinalIgnoreCase))
                metrics.SearchOperations++;
            else if (name.Contains("file", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("read", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("write", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("patch", StringComparison.OrdinalIgnoreCase))
                metrics.FileOperations++;
            else
                metrics.OtherToolCalls++;
        }

        return metrics;
    }

    private static long? ComputeToolBatches(IEnumerable<ToolCallState> source)
    {
        var calls = source.ToArray();
        if (calls.Length == 0)
            return 0;
        if (calls.Any(x => x.StartedAt is null || x.CompletedAt is null))
            return null;

        var intervals = calls
            .OrderBy(x => x.StartedAt)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();

        long batches = 0;
        DateTimeOffset? activeEnd = null;
        foreach (var call in intervals)
        {
            if (activeEnd is null || call.StartedAt!.Value > activeEnd.Value)
            {
                batches++;
                activeEnd = call.CompletedAt;
            }
            else if (call.CompletedAt!.Value > activeEnd.Value)
            {
                activeEnd = call.CompletedAt;
            }
        }
        return batches;
    }

    private static bool IsFailed(ToolCallState call) =>
        call.ExitCode is not null && call.ExitCode != 0 ||
        call.Status is not null && call.Status is "failed" or "error" or "declined";

    private static bool IsCommand(CodexEvent e) => IsCommandName(e.ToolName ?? "");

    private static bool IsCommandName(string name) =>
        name.Equals("command_execution", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("exec_command", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("write_stdin", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("local_shell", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractCommand(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments) ||
            string.Equals(arguments.Trim(), "null", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(arguments);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return arguments;

            foreach (var name in new[] { "cmd", "command", "input" })
            {
                if (doc.RootElement.TryGetProperty(name, out var value) &&
                    value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
                    return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
            }
        }
        catch (JsonException)
        {
        }
        return arguments;
    }

    private static StructuredFactoryResult? FindStructuredFactoryResult(IEnumerable<CodexEvent> events)
    {
        foreach (var e in events.Reverse())
        {
            if (TryParseStructuredFactoryResult(e, out var status, out var reason))
                return new StructuredFactoryResult(status, reason, e);
        }
        return null;
    }

    private static bool TryParseStructuredFactoryResult(
        CodexEvent e,
        out string status,
        out string? reason)
    {
        status = "";
        reason = null;

        if (!string.Equals(e.Role, "assistant", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(e.Text))
            return false;

        var text = e.Text.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = text.IndexOf('\n');
            var closingFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine >= 0 && closingFence > firstNewLine)
                text = text[(firstNewLine + 1)..closingFence].Trim();
        }

        if (!text.StartsWith("{", StringComparison.Ordinal) ||
            !text.EndsWith("}", StringComparison.Ordinal))
            return false;

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            string? rawStatus = null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name.Equals("status", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                    rawStatus = property.Value.GetString();
                else if (property.Name.Equals("reason", StringComparison.OrdinalIgnoreCase) &&
                         property.Value.ValueKind == JsonValueKind.String)
                    reason = property.Value.GetString();
            }

            status = rawStatus?.Trim().ToUpperInvariant() switch
            {
                "COMPLETED" => "completed",
                "INTERRUPTED" => "interrupted",
                "BLOCKED" => "blocked",
                "QUESTION" => "question",
                _ => ""
            };
            return status.Length > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsFactoryCompletion(CodexEvent e)
    {
        var text = e.Text;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        return text.Contains("Factory completed", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Factory run completed", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Factory is complete", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInterruption(CodexEvent e)
    {
        var type = e.Type;
        var status = e.Status;
        return type.Contains("cancel", StringComparison.OrdinalIgnoreCase) ||
               type.Contains("interrupt", StringComparison.OrdinalIgnoreCase) ||
               status is "cancelled" or "canceled" or "interrupted";
    }

    private static bool IsFactoryOwned(CodexEvent e) =>
        (string.Equals(e.Role, "user", StringComparison.OrdinalIgnoreCase) && e.Contains(RunSkill)) ||
        IsNativeSpawn(e) ||
        e.ToolName?.Contains("wait_agent", StringComparison.OrdinalIgnoreCase) == true ||
        IsFactoryCompletion(e) ||
        TryParseStructuredFactoryResult(e, out _, out _);

    private static bool HasTerminalEvidence(CodexRollout rollout) =>
        CodexRolloutReader.HasConfirmedTerminal(rollout);

    private static bool HasThreadFailure(CodexRollout? rollout) =>
        rollout?.Events.Any(x =>
            x.Type.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            x.Status is "failed" or "error") == true;

    private static TokenMetrics AggregateTokens(
        IReadOnlyList<AgentReport> agents,
        out string method,
        out bool complete)
    {
        var distinct = agents.GroupBy(x => x.ThreadId, StringComparer.Ordinal).Select(x => x.First()).ToArray();
        if (distinct.Length == 0)
        {
            complete = false;
            method = "unavailable";
            return new TokenMetrics();
        }

        if (agents.GroupBy(x => x.ThreadId, StringComparer.Ordinal).Any(x => x.Count() != 1) ||
            distinct.Any(x => !x.TokensAuthoritative || !HasCompleteTokens(x.Tokens)))
        {
            complete = false;
            method = distinct.Any(x => x.Tokens.Available) ? "overlap-unknown" : "unavailable";
            return new TokenMetrics();
        }

        complete = true;
        method = "per-thread";
        return TokenMetrics.Sum(distinct.Select(x => x.Tokens));
    }

    private static ToolMetrics AggregateTools(IEnumerable<AgentReport> agents)
    {
        var total = new ToolMetrics();
        long? batches = 0;
        foreach (var x in agents)
        {
            total.ToolCalls += x.Tools.ToolCalls;
            total.NativeOperations += x.Tools.NativeOperations;
            total.HostWrapperCalls += x.Tools.HostWrapperCalls;
            batches = batches is null || x.Tools.ToolBatches is null ? null : batches + x.Tools.ToolBatches;
            total.Commands += x.Tools.Commands;
            total.FailedCommands += x.Tools.FailedCommands;
            total.FileChanges += x.Tools.FileChanges;
            total.FileOperations += x.Tools.FileOperations;
            total.SearchOperations += x.Tools.SearchOperations;
            total.SpawnAgentCalls += x.Tools.SpawnAgentCalls;
            total.WaitAgentCalls += x.Tools.WaitAgentCalls;
            total.OtherToolCalls += x.Tools.OtherToolCalls;
            total.ToolOutputCharacters += x.Tools.ToolOutputCharacters;
            total.FailedToolOutputCharacters += x.Tools.FailedToolOutputCharacters;
            total.FailedCommandItems.AddRange(x.Tools.FailedCommandItems);
        }
        total.ToolBatches = batches;
        return total;
    }

    private static CompletionReport BuildCompletion(
        IReadOnlyList<AgentReport> agents,
        IReadOnlyList<CodexEvent> rootEvents,
        string result,
        bool declaredResult,
        CodexEvent? terminalEvent,
        FinalVerificationEvidence policy,
        List<Diagnostic> diagnostics)
    {
        var planners = agents.Where(x => x.Role == "planner").ToArray();
        var workers = agents.Where(x => x.Role == "worker").ToArray();
        var latestPlanner = planners.OrderBy(x => x.StartedAt).LastOrDefault();
        var done = latestPlanner?.PlannerOutcome == "Done" ? latestPlanner : null;

        bool? plannerDone;
        if (done is null)
        {
            plannerDone = false;
        }
        else if (workers.Length == 0)
        {
            plannerDone = true;
        }
        else
        {
            var workerFinishes = workers.Select(x => x.FinishedAt).Where(x => x is not null).Select(x => x!.Value).ToArray();
            var lastWorker = workerFinishes.Length == 0 ? (DateTimeOffset?)null : workerFinishes.Max();
            plannerDone = lastWorker is null || done.PlannerOutcomeAt is null
                ? null
                : done.PlannerOutcomeAt >= lastWorker;
        }

        var terminalAt = terminalEvent?.Timestamp;
        var verification = plannerDone == true
            ? policy.Evaluate(rootEvents, done?.PlannerOutcomeAt, terminalAt).Result : "unavailable";
        if (policy.Error is not null)
            diagnostics.Add(new Diagnostic
            {
                Severity = "warning", Category = "reporter", Code = "verification_policy_unavailable",
                Message = $"Cannot establish configured final checks: {policy.Error} No fallback was used."
            });

        var firstDone = planners.Where(x => x.PlannerOutcome == "Done")
            .Select(x => x.PlannerOutcomeAt).Where(x => x is not null).OrderBy(x => x).FirstOrDefault();
        var observations = policy.Evaluate(rootEvents, firstDone, terminalAt).Observations;
        var recoveryMissing = false;
        foreach (var failure in observations.Where(x => x.Result == "failed"))
        {
            var nextCheck = observations.FirstOrDefault(x => x.Event.Timestamp > failure.Event.Timestamp);
            var recoveryPlanner = planners.FirstOrDefault(x => x.StartedAt > failure.Event.Timestamp &&
                (nextCheck is null || x.StartedAt < nextCheck.Event.Timestamp));
            if (recoveryPlanner is not null) continue;
            recoveryMissing = true;
            diagnostics.Add(new Diagnostic
            {
                Severity = "warning", Category = "factory", Code = "verification_failure_without_fresh_planner",
                Message = $"Final check {failure.Check} failed at {failure.Event.Timestamp:O}, but no fresh planner was observed before verification resumed or the run ended."
            });
        }

        var declared = declaredResult ? result : "unavailable";
        var validation = declared == "completed"
            ? plannerDone != true || verification == "failed" || recoveryMissing ? "warning" : "ok"
            : declaredResult ? "ok" : "unavailable";
        if (declared == "completed" && policy.HasPolicy && verification != "passed")
        {
            validation = "warning";
            diagnostics.Add(new Diagnostic
            {
                Severity = "warning", Category = "factory", Code = "completed_without_final_verification",
                Message = "Factory declared COMPLETED without conclusive successful evidence for every configured final check."
            });
        }

        return new CompletionReport
        {
            PlannerDone = plannerDone,
            ProjectVerification = verification,
            DeclaredResult = declared,
            ProtocolValidation = validation
        };
    }

    private static void AddTopologyDiagnostics(
        IReadOnlyList<AgentReport> agents,
        List<Diagnostic> diagnostics)
    {
        var byId = agents.ToDictionary(x => x.ThreadId, StringComparer.Ordinal);
        foreach (var agent in agents.Where(x => x.ParentThreadId is not null))
        {
            if (agent.ParentThreadId is not null && !byId.ContainsKey(agent.ParentThreadId))
            {
                diagnostics.Add(new Diagnostic
                {
                    Severity = "warning",
                    Category = "reporter",
                    Code = "agent_parent_missing",
                    Message = $"Agent {agent.ThreadId} references missing parent {agent.ParentThreadId}."
                });
            }

            var current = agent;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (seen.Add(current.ThreadId) &&
                   current.ParentThreadId is not null &&
                   byId.TryGetValue(current.ParentThreadId, out var parent))
            {
                current = parent;
            }

            if (current.ParentThreadId is not null &&
                byId.TryGetValue(current.ParentThreadId, out var repeatedParent) &&
                seen.Contains(repeatedParent.ThreadId))
            {
                diagnostics.Add(new Diagnostic
                {
                    Severity = "warning",
                    Category = "reporter",
                    Code = "agent_topology_cycle",
                    Message = $"Agent topology contains a cycle involving {agent.ThreadId}."
                });
            }
        }
    }

    private static List<TimelineEvent> BuildTimeline(
        CodexEvent start,
        DateTimeOffset? end,
        string result,
        IEnumerable<AgentReport> agentsSource,
        ToolMetrics tools,
        CompletionReport completion)
    {
        var agents = agentsSource.ToArray();
        var events = new List<TimelineEvent>
        {
            new() { Timestamp = start.Timestamp, Ordinal = start.Ordinal, Kind = "factory-start", Text = "Factory run started" }
        };

        var ordinal = start.Ordinal + 1;
        foreach (var agent in agents.Where(x => x.ParentThreadId is not null)
                     .OrderBy(x => x.StartedAt)
                     .ThenBy(x => x.ThreadId, StringComparer.Ordinal))
        {
            var label = AgentLabel(agent, agents);
            var task = agent.Role == "worker" && !string.IsNullOrWhiteSpace(agent.TaskTitle)
                ? ": " + agent.TaskTitle
                : "";
            events.Add(new TimelineEvent
            {
                Timestamp = agent.StartedAt,
                Ordinal = ordinal++,
                Kind = $"{agent.Role}-start",
                Text = $"{label} started{task}"
            });
            if (agent.Role == "planner" && agent.PlannerOutcome is not null)
            {
                events.Add(new TimelineEvent
                {
                    Timestamp = agent.PlannerOutcomeAt ?? agent.FinishedAt,
                    Ordinal = ordinal++,
                    Kind = "planner-outcome",
                    Text = $"{label} -> {agent.PlannerOutcome}"
                });
            }
            else
            {
                events.Add(new TimelineEvent
                {
                    Timestamp = agent.FinishedAt,
                    Ordinal = ordinal++,
                    Kind = $"{agent.Role}-finish",
                    Text = $"{label} completed"
                });
            }
        }

        foreach (var failed in tools.FailedCommandItems)
        {
            events.Add(new TimelineEvent
            {
                Timestamp = failed.Timestamp,
                Ordinal = ordinal++,
                Kind = "failed-command",
                Text = $"Failed command in {failed.Agent}"
            });
        }

        if (completion.ProjectVerification is "passed" or "failed")
        {
            events.Add(new TimelineEvent
            {
                Timestamp = end,
                Ordinal = long.MaxValue - 1,
                Kind = "project-verification",
                Text = $"Project verification {completion.ProjectVerification}"
            });
        }

        events.Add(new TimelineEvent
        {
            Timestamp = end,
            Ordinal = long.MaxValue,
            Kind = "factory-result",
            Text = completion.DeclaredResult != "unavailable"
                ? $"Factory declared result: {completion.DeclaredResult.ToUpperInvariant()}"
                : $"Factory result: {result.ToUpperInvariant()}"
        });

        return events.OrderBy(x => x.Timestamp ?? DateTimeOffset.MaxValue).ThenBy(x => x.Ordinal).ToList();
    }

    private static void AssignSequences(List<AgentReport> agents)
    {
        foreach (var group in agents.Where(x => x.Role != "root").GroupBy(x => x.Role))
        {
            var n = 0;
            foreach (var agent in group.OrderBy(x => x.StartedAt))
                agent.Sequence = ++n;
        }
    }

    private static string AgentLabel(AgentReport agent, IReadOnlyCollection<AgentReport> agents)
    {
        if (agent.Role == "root")
            return "Factory root";
        if (agent.Role == "planner" && agents.Count(x => x.Role == "planner") == 1)
            return "Planner";
        return $"{char.ToUpperInvariant(agent.Role[0])}{agent.Role[1..]} #{agent.Sequence}";
    }

    private static int ComputeMaximumDepth(IReadOnlyList<AgentReport> agents)
    {
        var map = agents.ToDictionary(x => x.ThreadId, StringComparer.Ordinal);
        var max = 0;
        foreach (var agent in agents)
        {
            var depth = 0;
            var current = agent;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (current.ParentThreadId is not null && seen.Add(current.ThreadId) &&
                   map.TryGetValue(current.ParentThreadId, out var parent))
            {
                depth++;
                current = parent;
            }
            max = Math.Max(max, depth);
        }
        return max;
    }

    private static long? SumDuration(IEnumerable<AgentReport> agents)
    {
        var values = agents.Select(x => x.DurationMilliseconds).Where(x => x is not null).Select(x => x!.Value).ToArray();
        return values.Length == 0 ? null : values.Sum();
    }

    private static FactoryProjectState ReadFactoryProjectState(string repository)
    {
        var current = Path.Combine(repository, ".idd", "factory", "current");
        var results = Path.Combine(repository, ".idd", "factory", "results");
        bool Has(string name) => File.Exists(Path.Combine(current, name));
        return new FactoryProjectState
        {
            CurrentRequestPresent = Has("request.md"),
            CurrentPlanPresent = Has("plan.md"),
            CurrentCompletedPresent = Has("completed.md"),
            CurrentAnswersPresent = Has("answers.md"),
            CurrentQuestionPresent = Has("question.md"),
            VerificationFailurePresent = Has("verification-failure.md"),
            ArchivedResultPresent = Directory.Exists(results) &&
                                    Directory.EnumerateFileSystemEntries(results).Any()
        };
    }

    private static string Bound(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value ?? "";
        var normalized = Regex.Replace(value, @"\s+", " ").Trim();
        return normalized.Length <= max ? normalized : normalized[..max] + " [truncated]";
    }

    private static bool PathBelongsToRepository(string? cwd, string repository)
    {
        if (string.IsNullOrWhiteSpace(cwd))
            return false;
        try
        {
            var normalizedCwd = NormalizePath(cwd);
            var normalizedRepository = NormalizePath(repository);
            if (PathComparer().Equals(normalizedCwd, normalizedRepository))
                return true;

            var relative = Path.GetRelativePath(normalizedRepository, normalizedCwd);
            if (Path.IsPathRooted(relative) || relative == "..")
                return false;

            var parentPrefix = ".." + Path.DirectorySeparatorChar;
            var alternateParentPrefix = ".." + Path.AltDirectorySeparatorChar;
            return !relative.StartsWith(parentPrefix, PathComparison()) &&
                   !relative.StartsWith(alternateParentPrefix, PathComparison());
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizePath(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile)
            path = uri.LocalPath;

        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return OperatingSystem.IsWindows() ? full : ResolveExistingSymlinks(full);
    }

    private static string ResolveExistingSymlinks(string path)
    {
        try
        {
            var start = new ProcessStartInfo("realpath")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            start.ArgumentList.Add(path);
            using var process = Process.Start(start);
            if (process is null)
                return path;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output)
                ? output.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : path;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            return path;
        }
    }

    private static StringComparer PathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison PathComparison() =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed record StructuredFactoryResult(string Status, string? Reason, CodexEvent Event);

    private sealed class ToolCallState
    {
        public string Id { get; init; } = "";
        public string? Name { get; set; }
        public string? Arguments { get; set; }
        public string? Output { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public string? Status { get; set; }
        public int? ExitCode { get; set; }
        public bool HadLifecycle { get; set; }
        public bool IsNative { get; set; }
        public bool IsWrapper { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
    }
}
