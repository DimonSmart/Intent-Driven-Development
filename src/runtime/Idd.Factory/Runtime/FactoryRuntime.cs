using System.Text.Json;
using Idd.Factory.Agents;
using Idd.Factory.Domain;
using Idd.Factory.Finalization;
using Idd.Factory.Persistence;
using Idd.Factory.State;
using Idd.Factory.Telemetry;
using Idd.Factory.Verification;
using Idd.Factory.Workflow;

namespace Idd.Factory.Runtime;

public sealed class FactoryRuntime(
    string workspace,
    string pluginRoot,
    WorkflowDefinition workflow,
    IFactoryStateStore stateStore,
    AgentExecutor agentExecutor,
    VerificationEngine verification,
    WorkspaceFingerprinter fingerprinter,
    FactoryEventWriter events,
    IClock clock)
{
    private readonly string currentDirectory = Path.Combine(workspace, ".idd", "factory", "current");
    private readonly IReadOnlyDictionary<string, WorkflowStepDefinition> steps = workflow.Steps.ToDictionary(x => x.Id, StringComparer.Ordinal);

    public async Task<FactoryCliOutcome> RunAsync(string requestPath, string methodologyVersion, CancellationToken cancellationToken)
        => await RunRequestAsync(await File.ReadAllTextAsync(requestPath, cancellationToken), methodologyVersion, cancellationToken);

    public async Task<FactoryCliOutcome> RunRequestAsync(string request, string methodologyVersion, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request)) throw new ArgumentException("Factory request cannot be empty.", nameof(request));
        DetectLegacyState();
        if (await stateStore.LoadAsync(cancellationToken) is not null)
            return new("RUN_EXISTS", "unknown", "Use continue or cancel for the existing Factory run.");
        Directory.CreateDirectory(currentDirectory); Directory.CreateDirectory(Path.Combine(currentDirectory, "work-items")); Directory.CreateDirectory(Path.Combine(currentDirectory, "attempts"));
        await File.WriteAllTextAsync(Path.Combine(currentDirectory, "request.md"), request, cancellationToken);
        var state = new FactoryState
        {
            MethodologyVersion = methodologyVersion, RuntimeVersion = Version(), RunId = Guid.NewGuid().ToString("N"), Revision = 0,
            CurrentWorkflowStep = workflow.Steps[0].Id, WorkflowName = workflow.Name, WorkflowHash = workflow.Hash,
            RequestPath = "request.md", BaselineRevision = fingerprinter.Compute(workspace)
        };
        await stateStore.CreateAsync(state, cancellationToken); await events.WriteAsync(state.RunId, "run-created", new { workflow.Name, workflow.Hash }, cancellationToken);
        return await ExecuteLoopAsync(state, cancellationToken);
    }

    public async Task<FactoryCliOutcome> ContinueAsync(CancellationToken cancellationToken, string? answerPath = null)
    {
        DetectLegacyState();
        var state = await stateStore.LoadAsync(cancellationToken) ?? throw new FactoryStateException("MISSING_FACTORY_STATE", "No Factory run exists.");
        if (state.WorkflowHash != workflow.Hash) return new("WORKFLOW_CHANGED", state.RunId, "Restore the workflow used to start this run or cancel and restart.");
        if (state.RunStatus == FactoryRunStatus.Cancelled) return new("CANCELLED", state.RunId);
        await ReconcileAsync(state, cancellationToken);
        if (state.Blocker?.Code == "NEEDS_CLARIFICATION" && answerPath is null)
            return new("NEEDS_CLARIFICATION", state.RunId, state.Blocker.Reason, state.Blocker.ResumeWhen);
        if (answerPath is not null) await RecordClarificationAsync(state, answerPath, cancellationToken);
        state.RunStatus = FactoryRunStatus.Running; await SaveAsync(state, cancellationToken);
        return await ExecuteLoopAsync(state, cancellationToken);
    }

    public async Task<FactoryCliOutcome> CancelAsync(CancellationToken cancellationToken)
    {
        var state = await stateStore.LoadAsync(cancellationToken) ?? throw new FactoryStateException("MISSING_FACTORY_STATE", "No Factory run exists.");
        state.RunStatus = FactoryRunStatus.Cancelled; state.Blocker = new("CANCELLED", "The user cancelled the run.", "Start a new Factory run.");
        foreach (var item in state.WorkItems.Where(x => x.Status is not WorkItemStatus.Completed and not WorkItemStatus.Superseded)) item.Status = WorkItemStatus.Cancelled;
        await SaveAsync(state, cancellationToken); await events.WriteAsync(state.RunId, "run-cancelled", new { state.CurrentAttemptId }, cancellationToken);
        return new("CANCELLED", state.RunId, "Product changes and Factory diagnostics were preserved.");
    }

    private async Task<FactoryCliOutcome> ExecuteLoopAsync(FactoryState state, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!steps.TryGetValue(state.CurrentWorkflowStep, out var step)) throw new WorkflowException("MISSING_WORKFLOW_STEP", state.CurrentWorkflowStep);
            await events.WriteAsync(state.RunId, "workflow-step-started", new { step.Id, step.Uses }, cancellationToken);
            string outcome;
            try { outcome = step.Uses switch
            {
                "factory.decompose" => await DecomposeAsync(state, step, cancellationToken),
                "factory.intent" => await IntentGateAsync(state, cancellationToken),
                "factory.execute" => await ExecuteWorkAsync(state, step, cancellationToken),
                "factory.replan" => await ReplanAsync(state, step, cancellationToken),
                "factory.final-review" => await FinalReviewAsync(state, step, cancellationToken),
                "factory.finalize" => "finalized",
                _ => throw new WorkflowException("UNKNOWN_WORKFLOW_HANDLER", step.Uses)
            }; }
            catch (AgentProtocolException exception) { return await StopAsync(state, exception.Code, exception.Message, "Continue to retry within the configured attempt budget.", cancellationToken); }
            catch (VerificationException exception) { return await StopAsync(state, exception.Code, exception.Message, "Fix the verification failure, then continue.", cancellationToken); }

            if (step.Uses == "factory.finalize")
            {
                var directory = await new FinalizeHandler(workspace).FinalizeAsync(state, cancellationToken);
                return new("COMPLETED", state.RunId, ResultDirectory: directory);
            }
            if (!step.Transitions.TryGetValue(outcome, out var target))
                throw new WorkflowException("UNROUTED_WORKFLOW_OUTCOME", $"Step {step.Id} does not route {outcome}.");
            await events.WriteAsync(state.RunId, "workflow-step-finished", new { step.Id, outcome, target }, cancellationToken);
            if (target == "$stop") return await StopForOutcomeAsync(state, outcome, cancellationToken);
            state.CurrentWorkflowStep = target; await SaveAsync(state, cancellationToken);
        }
    }

    private async Task<string> DecomposeAsync(FactoryState state, WorkflowStepDefinition step, CancellationToken cancellationToken)
    {
        var request = await File.ReadAllTextAsync(Path.Combine(currentDirectory, state.RequestPath), cancellationToken);
        var result = await InvokeAsync(state, step.Agent!, null, $"Original Factory request:\n{request}\n\nRecorded clarifications:\n{await ReadClarificationsAsync(state, cancellationToken)}\n\nReturn a complete ordered decomposition.", cancellationToken);
        if (result.Outcome != "ready") return result.Outcome;
        if (result.Payload is not { } payload || !payload.TryGetProperty("workItems", out var items) || items.ValueKind != JsonValueKind.Array)
            throw new AgentProtocolException("MALFORMED_AGENT_RESULT", "Decomposer ready result requires payload.workItems.");
        foreach (var node in items.EnumerateArray()) AddWorkItem(state, node);
        ValidateDecomposition(state);
        foreach (var item in state.WorkItems.Where(x => x.Dependencies.Count == 0)) item.Status = WorkItemStatus.Ready;
        await SaveAsync(state, cancellationToken); return "ready";
    }

    private async Task<string> ExecuteWorkAsync(FactoryState state, WorkflowStepDefinition step, CancellationToken cancellationToken)
    {
        PromoteReady(state); var item = state.WorkItems.OrderBy(x => x.Sequence).FirstOrDefault(x => x.Status == WorkItemStatus.Ready);
        if (item is null) return state.WorkItems.All(x => x.Status is WorkItemStatus.Completed or WorkItemStatus.Superseded) ? "exhausted" : "blocked";
        var role = item.Kind == WorkItemKind.ReviewCheckpoint ? step.Handlers["review-checkpoint"] : step.Handlers["subtask"];
        item.Status = WorkItemStatus.Dispatching; await SaveAsync(state, cancellationToken); item.Status = WorkItemStatus.Running; await SaveAsync(state, cancellationToken);
        var contract = await File.ReadAllTextAsync(Path.Combine(currentDirectory, item.ContractPath), cancellationToken);
        var result = await InvokeAsync(state, role, item, $"Work item contract:\n{contract}", cancellationToken);
        item.LastResultRef = Path.GetRelativePath(currentDirectory, Path.Combine(currentDirectory, "attempts", item.CurrentAttemptId!, "result.json")).Replace('\\', '/');
        if (result.Outcome is "needs-replan" or "intent-required" or "blocked") { item.Status = result.Outcome == "blocked" ? WorkItemStatus.Blocked : WorkItemStatus.Ready; await SaveAsync(state, cancellationToken); return result.Outcome; }
        if (result.Outcome == "needs-fix") { InsertCorrection(state, item, result.Payload); await SaveAsync(state, cancellationToken); return "advanced"; }
        if (result.Outcome is not "completed" and not "approved") throw new AgentProtocolException("UNSUPPORTED_AGENT_OUTCOME", result.Outcome);
        item.Status = WorkItemStatus.AwaitingVerification; await SaveAsync(state, cancellationToken);
        var evidence = await verification.RunAsync(item.VerificationCheckIds, cancellationToken);
        foreach (var record in evidence) { var path = $"verification/{record.EvidenceId}.json"; item.VerificationEvidenceRefs.Add(path); state.VerificationEvidenceRefs.Add(path); }
        item.Status = WorkItemStatus.Completed; item.CurrentAttemptId = null; await SaveAsync(state, cancellationToken); PromoteReady(state); await SaveAsync(state, cancellationToken);
        return "advanced";
    }

    private async Task<string> ReplanAsync(FactoryState state, WorkflowStepDefinition step, CancellationToken cancellationToken)
    {
        if (state.ReplanCount >= workflow.Limits.MaxReplans) return "blocked";
        var request = await File.ReadAllTextAsync(Path.Combine(currentDirectory, state.RequestPath), cancellationToken);
        var ready = state.WorkItems.Where(x => x.Status is WorkItemStatus.Ready or WorkItemStatus.Planned).Select(x => new { x.Id, x.Sequence, x.Kind, x.ContractPath });
        var result = await InvokeAsync(state, step.Agent!, null, $"Original request:\n{request}\n\nRecorded clarifications:\n{await ReadClarificationsAsync(state, cancellationToken)}\n\nMutable remaining work:\n{JsonSerializer.Serialize(ready)}", cancellationToken);
        if (result.Outcome != "replan-proposed") return result.Outcome;
        ApplyReplan(state, result.Payload); state.ReplanCount++; await SaveAsync(state, cancellationToken); return "applied";
    }

    private async Task<string> FinalReviewAsync(FactoryState state, WorkflowStepDefinition step, CancellationToken cancellationToken)
    {
        var evidence = await verification.RunContextAsync("final", cancellationToken);
        foreach (var record in evidence)
        {
            var path = $"verification/{record.EvidenceId}.json";
            if (!state.VerificationEvidenceRefs.Contains(path, StringComparer.Ordinal)) state.VerificationEvidenceRefs.Add(path);
        }
        await SaveAsync(state, cancellationToken);
        var request = await File.ReadAllTextAsync(Path.Combine(currentDirectory, state.RequestPath), cancellationToken);
        var result = await InvokeAsync(state, step.Agent!, null, $"Original request:\n{request}\n\nCompleted work:\n{JsonSerializer.Serialize(state.WorkItems.Select(x => new { x.Id, x.Kind, x.ContractPath, x.LastResultRef }))}\n\nAuthoritative final verification evidence:\n{JsonSerializer.Serialize(evidence)}", cancellationToken);
        state.FinalReview = new(result.Outcome, $"attempts/{result.AttemptId}/result.json", (state.FinalReview?.AttemptCount ?? 0) + 1);
        if (result.Outcome == "needs-fix")
        {
            if (state.CorrectiveCycleCount >= workflow.Limits.MaxCorrectiveCycles) return "blocked";
            InsertCorrection(state, null, result.Payload); await SaveAsync(state, cancellationToken);
        }
        else await SaveAsync(state, cancellationToken);
        return result.Outcome;
    }

    private async Task<string> IntentGateAsync(FactoryState state, CancellationToken cancellationToken)
    {
        var currentHash = HashIntent();
        if (state.IntentSnapshotHash is null)
        {
            state.IntentSnapshotHash = currentHash;
            state.Blocker = new("INTENT_REQUIRED", "Factory requires the existing IDD intent workflow before semantic work can continue.", "Update the required durable intent, then run continue.");
            await SaveAsync(state, cancellationToken); return "blocked";
        }
        if (state.IntentSnapshotHash == currentHash) return "blocked";
        state.IntentSnapshotHash = null; state.Blocker = null; await SaveAsync(state, cancellationToken); return "completed";
    }

    private async Task<AgentResultEnvelope> InvokeAsync(FactoryState state, string role, WorkItemState? item, string input, CancellationToken cancellationToken)
    {
        if (state.CurrentAttemptId is { } persistedAttempt)
        {
            var directory = Path.Combine(currentDirectory, "attempts", persistedAttempt);
            var invocationPath = Path.Combine(directory, "invocation.json"); var persistedResultPath = Path.Combine(directory, "result.json");
            if (File.Exists(invocationPath) && File.Exists(persistedResultPath))
            {
                var persistedInvocation = JsonSerializer.Deserialize<AgentInvocation>(await File.ReadAllTextAsync(invocationPath, cancellationToken), FactoryJson.Options)
                    ?? throw new AgentProtocolException("UNKNOWN_ATTEMPT", $"Attempt {persistedAttempt} has no valid invocation.");
                if (persistedInvocation.Role != role || persistedInvocation.WorkItemId != item?.Id)
                    throw new AgentProtocolException("UNKNOWN_ATTEMPT", $"Attempt {persistedAttempt} does not belong to the current semantic operation.");
                var persistedResult = JsonSerializer.Deserialize<AgentResultEnvelope>(await File.ReadAllTextAsync(persistedResultPath, cancellationToken), FactoryJson.Options);
                var validated = new AgentResultValidator().Validate(persistedInvocation, persistedResult);
                state.CurrentAttemptId = null; await SaveAsync(state, cancellationToken);
                await events.WriteAsync(state.RunId, "agent-result-reused", new { attemptId = persistedAttempt, role }, cancellationToken);
                return validated;
            }
        }
        var attemptId = $"A{++state.AttemptSequence:000000}"; state.CurrentAttemptId = attemptId;
        if (item is not null) { item.CurrentAttemptId = attemptId; item.AttemptCount++; if (item.AttemptCount > workflow.Limits.MaxAgentAttempts) throw new AgentProtocolException("RETRY_BUDGET_EXHAUSTED", $"{item.Id} exhausted its agent attempt budget."); }
        await SaveAsync(state, cancellationToken);
        var attemptDirectory = Path.Combine(currentDirectory, "attempts", attemptId); Directory.CreateDirectory(attemptDirectory);
        var resultPath = Path.Combine(attemptDirectory, "result.json");
        var prompt = BuildPrompt(role, state, attemptId, resultPath, input);
        var invocation = new AgentInvocation { RunId = state.RunId, AttemptId = attemptId, Role = role, WorkItemId = item?.Id, Workspace = workspace, ResultPath = resultPath, Prompt = prompt, StartedAt = clock.UtcNow, WorkspaceFingerprint = fingerprinter.Compute(workspace), SkillReferences = RoleReferences(role) };
        await events.WriteAsync(state.RunId, "agent-dispatching", new { attemptId, role, workItemId = item?.Id }, cancellationToken);
        var result = await agentExecutor.ExecuteAsync(invocation, cancellationToken);
        state.CurrentAttemptId = null; await SaveAsync(state, cancellationToken);
        await events.WriteAsync(state.RunId, "agent-completed", new { attemptId, role, result.Outcome, metrics = result.Metrics }, cancellationToken); return result;
    }

    private string BuildPrompt(string role, FactoryState state, string attemptId, string resultPath, string input)
    {
        var references = RoleReferences(role).Where(File.Exists).Select(File.ReadAllText);
        return $"You are the IDD Factory semantic role `{role}` in a fresh isolated context.\n\n{string.Join("\n\n", references)}\n\n{input}\n\nReturn only one JSON object as your final response. The backend captures that response as {resultPath}; do not create or edit that file yourself. Required envelope: protocolVersion=1, runId={state.RunId}, attemptId={attemptId}, role={role}, outcome=<role outcome>, reason=<optional>, payload=<role data>. Do not mutate .idd/factory/current or .idd/intent. stdout is diagnostic only.";
    }

    private IReadOnlyList<string> RoleReferences(string role)
    {
        var skill = role switch { "task-decomposer" => "idd-factory-decompose-task", "implementer" => "idd-factory-execute-subtask", "checkpoint-reviewer" => "idd-factory-review-checkpoint", "final-reviewer" => "idd-factory-review-task", "factory-replanner" => "idd-factory-replan", _ => throw new InvalidOperationException(role) };
        return [Path.Combine(pluginRoot, "skills", skill, "SKILL.md"), Path.Combine(pluginRoot, "skills", skill, "references", "roles", role + ".md")];
    }

    private void AddWorkItem(FactoryState state, JsonElement node)
    {
        var id = node.GetProperty("id").GetString() ?? throw new AgentProtocolException("INVALID_DECOMPOSITION", "Work item id missing.");
        var kindText = node.GetProperty("kind").GetString(); var kind = kindText switch { "subtask" => WorkItemKind.Subtask, "review-checkpoint" => WorkItemKind.ReviewCheckpoint, "corrective-subtask" => WorkItemKind.CorrectiveSubtask, _ => throw new AgentProtocolException("INVALID_DECOMPOSITION", $"Unknown kind {kindText}.") };
        var sequence = node.TryGetProperty("sequence", out var sequenceNode) ? sequenceNode.GetInt32() : state.WorkItems.Count + 1;
        var file = $"{sequence:000}-{Slug(id)}.md"; var relative = $"work-items/{file}";
        var contract = node.TryGetProperty("contractMarkdown", out var contractNode) ? contractNode.GetString() : null;
        if (string.IsNullOrWhiteSpace(contract)) throw new AgentProtocolException("INVALID_DECOMPOSITION", $"{id} lacks contractMarkdown.");
        File.WriteAllText(Path.Combine(currentDirectory, relative), contract);
        state.WorkItems.Add(new WorkItemState { Id = id, Sequence = sequence, Kind = kind, ContractPath = relative,
            Dependencies = Strings(node, "dependencies"), CoveredWorkItems = Strings(node, "coveredWorkItems"), VerificationCheckIds = Strings(node, "verificationCheckIds") });
    }

    private void ValidateDecomposition(FactoryState state)
    {
        new FactoryStateValidator().Validate(state);
        var sequences = state.WorkItems.Select(x => x.Sequence).Order().ToArray();
        if (!sequences.SequenceEqual(Enumerable.Range(1, sequences.Length))) throw new AgentProtocolException("INVALID_DECOMPOSITION", "Work-item sequences must be positive, contiguous, and gap-free.");
        verification.ValidateCheckIds(state.WorkItems.SelectMany(x => x.VerificationCheckIds));
        var completed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in state.WorkItems.OrderBy(x => x.Sequence))
        {
            if (item.Dependencies.Any(id => !completed.Contains(id))) throw new AgentProtocolException("INVALID_DECOMPOSITION", $"Dependencies for {item.Id} cannot be satisfied in sequence order.");
            completed.Add(item.Id);
        }
        foreach (var checkpoint in state.WorkItems.Where(x => x.Kind == WorkItemKind.ReviewCheckpoint))
        {
            if (checkpoint.CoveredWorkItems.Count == 0 || checkpoint.CoveredWorkItems.Any(id => state.WorkItems.Single(x => x.Id == id).Sequence >= checkpoint.Sequence))
                throw new AgentProtocolException("INVALID_DECOMPOSITION", $"Checkpoint {checkpoint.Id} has invalid coverage.");
        }
    }

    private void PromoteReady(FactoryState state)
    {
        foreach (var item in state.WorkItems.Where(x => x.Status == WorkItemStatus.Planned && x.Dependencies.All(id => state.WorkItems.Single(x => x.Id == id).Status == WorkItemStatus.Completed))) item.Status = WorkItemStatus.Ready;
    }

    private void InsertCorrection(FactoryState state, WorkItemState? review, JsonElement? payload)
    {
        if (state.CorrectiveCycleCount >= workflow.Limits.MaxCorrectiveCycles) throw new AgentProtocolException("CORRECTIVE_BUDGET_EXHAUSTED", "Corrective cycle budget exhausted.");
        if (payload is not { } data || !data.TryGetProperty("correctiveSubtask", out var correction)) throw new AgentProtocolException("MALFORMED_AGENT_RESULT", "needs-fix requires payload.correctiveSubtask.");
        var max = state.WorkItems.Count == 0 ? 0 : state.WorkItems.Max(x => x.Sequence); var id = correction.TryGetProperty("id", out var idNode) ? idNode.GetString()! : $"correction-{state.CorrectiveCycleCount + 1}";
        var contract = correction.GetProperty("contractMarkdown").GetString() ?? throw new AgentProtocolException("MALFORMED_AGENT_RESULT", "Correction contract missing.");
        var relative = $"work-items/{max + 1:000}-{Slug(id)}.md"; File.WriteAllText(Path.Combine(currentDirectory, relative), contract);
        var dependencies = review?.CoveredWorkItems.ToList() ?? state.WorkItems.Where(x => x.Status == WorkItemStatus.Completed).Select(x => x.Id).ToList();
        var item = new WorkItemState { Id = id, Sequence = max + 1, Kind = WorkItemKind.CorrectiveSubtask, Status = WorkItemStatus.Ready, ContractPath = relative, Dependencies = dependencies, VerificationCheckIds = Strings(correction, "verificationCheckIds") };
        state.WorkItems.Add(item); state.CorrectiveCycleCount++;
        if (review is not null) { review.Status = WorkItemStatus.Planned; review.Dependencies.Add(id); review.CoveredWorkItems.Add(id); }
    }

    private void ApplyReplan(FactoryState state, JsonElement? payload)
    {
        if (payload is not { } data || !data.TryGetProperty("operations", out var operations) || operations.ValueKind != JsonValueKind.Array) throw new AgentProtocolException("INVALID_REPLAN", "Replan proposal requires operations.");
        foreach (var operation in operations.EnumerateArray())
        {
            var kind = operation.GetProperty("kind").GetString();
            if (kind == "insert-subtask") AddWorkItem(state, operation.GetProperty("subtask"));
            else if (kind == "supersede-ready-subtask") { var item = MutableItem(state, operation.GetProperty("id").GetString()!); item.Status = WorkItemStatus.Superseded; }
            else if (kind == "replace-ready-subtask") { var old = MutableItem(state, operation.GetProperty("id").GetString()!); old.Status = WorkItemStatus.Superseded; AddWorkItem(state, operation.GetProperty("subtask")); }
            else if (kind == "reorder-ready-work") ReorderReady(state, Strings(operation, "workItemIds"));
            else if (kind == "update-run-context") File.WriteAllText(Path.Combine(currentDirectory, "run-context.md"), operation.GetProperty("content").GetString() ?? "");
            else if (kind == "update-checkpoint-coverage") { var checkpoint = MutableItem(state, operation.GetProperty("id").GetString()!); if (checkpoint.Kind != WorkItemKind.ReviewCheckpoint) throw new AgentProtocolException("INVALID_REPLAN", $"{checkpoint.Id} is not a checkpoint."); checkpoint.CoveredWorkItems.Clear(); checkpoint.CoveredWorkItems.AddRange(Strings(operation, "coveredWorkItems")); }
            else if (kind == "insert-checkpoint") AddWorkItem(state, operation.GetProperty("checkpoint"));
            else if (kind == "remove-unused-ready-checkpoint") { var checkpoint = MutableItem(state, operation.GetProperty("id").GetString()!); if (checkpoint.Kind != WorkItemKind.ReviewCheckpoint) throw new AgentProtocolException("INVALID_REPLAN", $"{checkpoint.Id} is not a checkpoint."); checkpoint.Status = WorkItemStatus.Superseded; }
            else throw new AgentProtocolException("INVALID_REPLAN", $"Unsupported or unsafe replan operation {kind}.");
        }
        ValidateDecomposition(state); PromoteReady(state);
    }

    private static WorkItemState MutableItem(FactoryState state, string id)
    { var item = state.WorkItems.SingleOrDefault(x => x.Id == id) ?? throw new AgentProtocolException("INVALID_REPLAN", $"Unknown work item {id}."); if (item.Status is not WorkItemStatus.Ready and not WorkItemStatus.Planned) throw new AgentProtocolException("INVALID_REPLAN", $"Only ready work may change: {id}."); return item; }
    private static void ReorderReady(FactoryState state, IReadOnlyList<string> ids)
    {
        var mutable = state.WorkItems.Where(x => x.Status is WorkItemStatus.Ready or WorkItemStatus.Planned).OrderBy(x => x.Sequence).ToArray();
        if (ids.Count != mutable.Length || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count || ids.Any(id => !mutable.Any(x => x.Id == id))) throw new AgentProtocolException("INVALID_REPLAN", "reorder-ready-work must name every mutable work item exactly once.");
        var slots = mutable.Select(x => x.Sequence).Order().ToArray();
        for (var index = 0; index < ids.Count; index++) mutable.Single(x => x.Id == ids[index]).Sequence = slots[index];
    }
    private async Task SaveAsync(FactoryState state, CancellationToken token) { var revision = state.Revision; await stateStore.SaveAsync(state, revision, token); }
    private async Task ReconcileAsync(FactoryState state, CancellationToken token)
    {
        if (state.CurrentAttemptId is not { } attemptId) return;
        var directory = Path.Combine(currentDirectory, "attempts", attemptId); var invocationPath = Path.Combine(directory, "invocation.json");
        if (!File.Exists(invocationPath)) throw new AgentProtocolException("UNKNOWN_ATTEMPT", $"Persisted attempt {attemptId} has no invocation artifact.");
        var invocation = JsonSerializer.Deserialize<AgentInvocation>(await File.ReadAllTextAsync(invocationPath, token), FactoryJson.Options)
            ?? throw new AgentProtocolException("UNKNOWN_ATTEMPT", $"Persisted attempt {attemptId} is malformed.");
        if (invocation.RunId != state.RunId || invocation.AttemptId != attemptId) throw new AgentProtocolException("UNKNOWN_ATTEMPT", $"Persisted attempt {attemptId} identity is invalid.");
        var item = invocation.WorkItemId is null ? null : state.WorkItems.SingleOrDefault(x => x.Id == invocation.WorkItemId)
            ?? throw new AgentProtocolException("UNKNOWN_ATTEMPT", $"Attempt {attemptId} references unknown work.");
        if (File.Exists(Path.Combine(directory, "result.json")))
        {
            if (item is not null && item.Status is WorkItemStatus.Dispatching or WorkItemStatus.Running) item.Status = WorkItemStatus.Ready;
            await SaveAsync(state, token); return;
        }
        state.CurrentAttemptId = null;
        if (item is not null) { item.CurrentAttemptId = null; if (item.Status is WorkItemStatus.Dispatching or WorkItemStatus.Running) item.Status = WorkItemStatus.Ready; }
        await SaveAsync(state, token); await events.WriteAsync(state.RunId, "agent-attempt-interrupted", new { attemptId }, token);
    }
    private async Task<FactoryCliOutcome> StopForOutcomeAsync(FactoryState state, string outcome, CancellationToken token) => await StopAsync(state, state.Blocker?.Code ?? outcome.ToUpperInvariant().Replace('-', '_'), state.Blocker?.Reason ?? $"Workflow stopped with {outcome}.", state.Blocker?.ResumeWhen ?? "Resolve the reported condition and continue.", token);
    private async Task<FactoryCliOutcome> StopAsync(FactoryState state, string code, string reason, string resume, CancellationToken token) { state.RunStatus = FactoryRunStatus.Blocked; state.Blocker = new(code, reason, resume); await SaveAsync(state, token); await events.WriteAsync(state.RunId, "run-blocked", new { code, reason, resume }, token); return new(code, state.RunId, reason, resume); }
    private void DetectLegacyState() { if (!Directory.Exists(currentDirectory)) return; if (Directory.EnumerateFiles(currentDirectory, "*.ready.md").Concat(Directory.EnumerateFiles(currentDirectory, "*.active.md")).Concat(Directory.EnumerateFiles(currentDirectory, "*.completed.md")).Concat(Directory.EnumerateFiles(currentDirectory, "*.blocked.md")).Any()) throw new FactoryStateException("LEGACY_FACTORY_STATE", "Finish with the previous Factory version or cancel/restart with the new runtime."); }
    private async Task RecordClarificationAsync(FactoryState state, string sourcePath, CancellationToken token)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Clarification answer file was not found.", sourcePath);
        var directory = Path.Combine(currentDirectory, "clarifications"); Directory.CreateDirectory(directory);
        var relative = $"clarifications/Q{state.ClarificationRefs.Count + 1:00000}.md"; await File.WriteAllTextAsync(Path.Combine(currentDirectory, relative), await File.ReadAllTextAsync(sourcePath, token), token);
        state.ClarificationRefs.Add(relative); state.Blocker = null; await SaveAsync(state, token);
    }
    private async Task<string> ReadClarificationsAsync(FactoryState state, CancellationToken token) => string.Join("\n\n", await Task.WhenAll(state.ClarificationRefs.Select(path => File.ReadAllTextAsync(Path.Combine(currentDirectory, path), token))));
    private string HashIntent()
    {
        var root = Path.Combine(workspace, ".idd", "intent"); if (!Directory.Exists(root)) return "missing";
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        foreach (var path in Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal)) { hash.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(root, path).Replace('\\', '/') + "\0")); hash.AppendData(File.ReadAllBytes(path)); }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    private static List<string> Strings(JsonElement node, string name) => node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(x => x.GetString()!).Where(x => !string.IsNullOrWhiteSpace(x)).ToList() : [];
    private static string Slug(string value) { var chars = value.ToLowerInvariant().Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-').ToArray(); return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries)); }
    private static string Version() => typeof(FactoryRuntime).Assembly.GetName().Version?.ToString() ?? "0.0.0";
}
