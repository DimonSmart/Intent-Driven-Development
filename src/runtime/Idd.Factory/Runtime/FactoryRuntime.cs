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
    WorkflowDefinition workflow,
    IFactoryStateStore stateStore,
    AgentExecutor agentExecutor,
    VerificationEngine verification,
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
            RequestPath = "request.md"
        };
        await stateStore.CreateAsync(state, cancellationToken); await events.WriteAsync(state.RunId, "run-created", new { workflow.Name, workflow.Hash }, cancellationToken);
        return await ExecuteLoopAsync(state, cancellationToken);
    }

    public async Task<FactoryCliOutcome> ContinueAsync(CancellationToken cancellationToken, string? answerPath = null, VerificationConfirmation confirmation = VerificationConfirmation.None, bool? verificationPassed = null)
    {
        DetectLegacyState();
        var state = await stateStore.LoadAsync(cancellationToken) ?? throw new FactoryStateException("MISSING_FACTORY_STATE", "No Factory run exists.");
        if (state.WorkflowHash != workflow.Hash) return new("WORKFLOW_CHANGED", state.RunId, "Restore the workflow used to start this run or cancel and restart.");
        if (state.RunStatus == FactoryRunStatus.Cancelled) return new("CANCELLED", state.RunId);
        await ReconcileAsync(state, cancellationToken);
        if (state.PendingContinuation is { } persistedContinuation)
            state.CurrentWorkflowStep = persistedContinuation.WorkflowStep;
        if (state.PendingContinuation is { IsResumable: false })
            return new(state.Blocker?.Code ?? "TERMINAL_STOP", state.RunId, state.Blocker?.Reason, state.Blocker?.ResumeWhen, Payload: state.Blocker?.Payload);
        if (state.PendingContinuation is { VerificationStage: VerificationContinuationStage.AwaitingConfirmation or VerificationContinuationStage.AwaitingManualResult } pending)
        {
            if (pending.VerificationStage == VerificationContinuationStage.AwaitingConfirmation && confirmation == VerificationConfirmation.None)
                return new("VERIFICATION_CONFIRMATION_REQUIRED", state.RunId, state.Blocker?.Reason, state.Blocker?.ResumeWhen, Payload: state.Blocker?.Payload);
            if (pending.VerificationStage == VerificationContinuationStage.AwaitingManualResult && verificationPassed is null)
                return new("VERIFICATION_RESULT_REQUIRED", state.RunId, state.Blocker?.Reason, state.Blocker?.ResumeWhen, Payload: state.Blocker?.Payload);
            var session = state.PendingVerificationSession ?? throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Pending verification action has no persisted verification session.");
            try
            {
                var item = pending.WorkItemId is null ? null : state.WorkItems.Single(x => x.Id == pending.WorkItemId);
                if (pending.VerificationStage == VerificationContinuationStage.AwaitingConfirmation && confirmation == VerificationConfirmation.Decline)
                {
                    var declined = await verification.DeclineCheckAsync(session.PendingCheckId!, session.PendingCheckDefinitionHash, session.PolicyHash, cancellationToken);
                    RecordEvidence(state, item, declined.Evidence);
                    state.PendingVerificationSession = null;
                    state.Blocker = new("VERIFICATION_DECLINED", $"Confirmation for check {session.PendingCheckId} was explicitly declined.", "Start a new Factory run when this verification can be approved.");
                    state.PendingContinuation = new(ContinuationKind.Terminal, pending.WorkflowStep, pending.WorkItemId, pending.VerificationContext, "VERIFICATION_DECLINED", false,
                        VerificationCheckId: session.PendingCheckId);
                    if (item is not null) item.Status = WorkItemStatus.Blocked;
                    await SaveAsync(state, cancellationToken);
                    return new("VERIFICATION_DECLINED", state.RunId, state.Blocker.Reason, state.Blocker.ResumeWhen);
                }
                var resolved = await verification.RunCheckAsync(session.PendingCheckId!, confirmation == VerificationConfirmation.Approve, verificationPassed, session.PendingCheckDefinitionHash, session.PolicyHash, cancellationToken);
                RecordEvidence(state, item, resolved.Evidence);
                if (resolved.Status == VerificationStatus.Failed)
                {
                    var outcome = await HandlePersistedVerificationFailureAsync(state, item, session.Context, resolved, cancellationToken);
                    var resumed = await ResumePendingVerificationActionAsync(state, pending, outcome, cancellationToken);
                    if (resumed is not null) return resumed;
                }
                else
                {
                session = session with { NextCheckIndex = session.NextCheckIndex + 1, CompletedCheckIds = session.CompletedCheckIds.Concat([session.PendingCheckId!]).ToList(), EvidenceRefs = session.EvidenceRefs.Concat(resolved.Evidence.Select(x => $"verification/{x.EvidenceId}.json")).ToList(), PendingCheckId = null, PendingCheckDefinitionHash = null, Stage = VerificationContinuationStage.ExecuteCheck };
                state.PendingVerificationSession = session;
                state.PendingContinuation = pending with { VerificationStage = VerificationContinuationStage.ExecuteCheck };
                state.Blocker = null;
                await SaveAsync(state, cancellationToken);
                }
            }
            catch (VerificationException exception)
            {
                state.Blocker = new(exception.Code, exception.Message, "Resolve the verification configuration and request confirmation again.");
                await SaveAsync(state, cancellationToken);
                return new(exception.Code, state.RunId, exception.Message, state.Blocker.ResumeWhen);
            }
        }
        if (state.Blocker?.Code == "NEEDS_CLARIFICATION" && answerPath is null)
            return new("NEEDS_CLARIFICATION", state.RunId, state.Blocker.Reason, state.Blocker.ResumeWhen, Payload: state.Blocker.Payload);
        if (answerPath is not null) await RecordClarificationAsync(state, answerPath, cancellationToken);
        if (state.PendingContinuation is { Kind: ContinuationKind.VerificationGate } continuation)
        {
            var resumed = await ResumeVerificationAsync(state, continuation, cancellationToken);
            if (resumed is not null) return resumed;
        }
        if (state.PendingContinuation is { Kind: ContinuationKind.IntentGate })
        {
            state.RunStatus = FactoryRunStatus.Running;
            await SaveAsync(state, cancellationToken);
            return await ExecuteLoopAsync(state, cancellationToken);
        }
        if (state.PendingContinuation is { Kind: ContinuationKind.SemanticInvocation } semantic)
        {
            try
            {
                var resumed = await ResumeSemanticOperationAsync(state, semantic, cancellationToken);
                if (resumed is not null) return resumed;
            }
            catch (AgentProtocolException exception)
            {
                return await StopForAgentProtocolExceptionAsync(state, exception, cancellationToken);
            }
        }
        state.PendingContinuation = null;
        state.Blocker = null;
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
            catch (AgentProtocolException exception)
            {
                var resume = exception.Code.EndsWith("_BUDGET_EXHAUSTED", StringComparison.Ordinal)
                    ? "The configured budget is exhausted. Cancel and restart with a workflow that provides sufficient budget; continue cannot add budget to the current run."
                    : "Continue to retry within the configured attempt budget.";
                return await StopAsync(state, exception.Code, exception.Message, resume, cancellationToken);
            }
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
            if (outcome == "needs-replan")
            {
                PersistReplanContinuation(state, step.Id);
                await SaveAsync(state, cancellationToken);
                return await ExecuteLoopAsync(state, cancellationToken);
            }
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
        var candidate = CloneState(state);
        var contracts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in items.EnumerateArray()) AddWorkItem(candidate, node, contracts);
        ValidateDecomposition(candidate);
        foreach (var item in candidate.WorkItems.Where(x => x.Dependencies.Count == 0)) item.Status = WorkItemStatus.Ready;
        ApplyCandidate(state, candidate);
        WriteContracts(contracts);
        await SaveAsync(state, cancellationToken); return "ready";
    }

    private async Task<string> ExecuteWorkAsync(FactoryState state, WorkflowStepDefinition step, CancellationToken cancellationToken)
    {
        PromoteReady(state); var item = state.WorkItems.OrderBy(x => x.Sequence).FirstOrDefault(x => x.Status is WorkItemStatus.Ready or WorkItemStatus.AwaitingReview);
        if (item is null) return state.WorkItems.All(x => x.Status is WorkItemStatus.Completed or WorkItemStatus.Superseded) ? "exhausted" : "blocked";
        var role = item.Kind == WorkItemKind.ReviewCheckpoint ? step.Handlers["review-checkpoint"] : step.Handlers["subtask"];
        var resumeAfterCheckpointGate = item.Status == WorkItemStatus.AwaitingReview;
        item.Status = WorkItemStatus.Dispatching; await SaveAsync(state, cancellationToken); item.Status = WorkItemStatus.Running; await SaveAsync(state, cancellationToken);
        var contract = await File.ReadAllTextAsync(Path.Combine(currentDirectory, item.ContractPath), cancellationToken);
        if (item.Kind == WorkItemKind.ReviewCheckpoint && !resumeAfterCheckpointGate)
        {
            var gateOutcome = await VerifyWorkItemGateAsync(state, item, "checkpoint", contract, cancellationToken);
            if (gateOutcome != "passed") return gateOutcome;
        }
        var evidenceRefs = item.VerificationEvidenceRefs.Count == 0 ? "none" : string.Join("\n", item.VerificationEvidenceRefs);
        var result = await InvokeAsync(state, role, item, item.Kind == WorkItemKind.ReviewCheckpoint
            ? $"Work item contract:\n{contract}\n\nAuthoritative Runtime verification evidence references:\n{evidenceRefs}"
            : $"Work item contract:\n{contract}", cancellationToken);
        item.LastResultRef = $"attempts/{result.AttemptId}/result.json";
        if (result.Outcome is "needs-replan" or "intent-required" or "blocked")
        {
            item.Status = result.Outcome == "blocked" && item.Kind == WorkItemKind.ReviewCheckpoint
                ? WorkItemStatus.AwaitingReview
                : result.Outcome == "blocked" ? WorkItemStatus.Blocked : WorkItemStatus.Ready;
            await SaveAsync(state, cancellationToken); return result.Outcome;
        }
        if (result.Outcome == "needs-fix") { InsertCorrection(state, item, result.Payload); await SaveAsync(state, cancellationToken); return "advanced"; }
        if (result.Outcome is not "completed" and not "approved") throw new AgentProtocolException("UNSUPPORTED_AGENT_OUTCOME", result.Outcome);
        if (item.Kind != WorkItemKind.ReviewCheckpoint)
        {
            item.Status = WorkItemStatus.AwaitingVerification; await SaveAsync(state, cancellationToken);
            var gateOutcome = await VerifyWorkItemGateAsync(state, item, "subtask", contract, cancellationToken);
            if (gateOutcome != "passed") return gateOutcome;
        }
        else { item.Status = WorkItemStatus.AwaitingVerification; await SaveAsync(state, cancellationToken); }
        item.Status = WorkItemStatus.Completed; item.CurrentAttemptId = null; await SaveAsync(state, cancellationToken); PromoteReady(state); await SaveAsync(state, cancellationToken);
        return "advanced";
    }

    private async Task<string> ReplanAsync(FactoryState state, WorkflowStepDefinition step, CancellationToken cancellationToken)
    {
        if (state.ReplanCount >= workflow.Limits.MaxReplans)
            throw new AgentProtocolException("REPLAN_BUDGET_EXHAUSTED", "Replan budget exhausted.");
        var request = await File.ReadAllTextAsync(Path.Combine(currentDirectory, state.RequestPath), cancellationToken);
        var ready = state.WorkItems.Where(x => x.Status is WorkItemStatus.Ready or WorkItemStatus.Planned).Select(x => new { x.Id, x.Sequence, x.Kind, x.ContractPath });
        var completed = state.WorkItems.Where(x => x.Status == WorkItemStatus.Completed).Select(x => new { x.Id, x.Kind, x.ContractPath, x.LastResultRef });
        var trigger = state.PendingReplanTrigger ?? throw new AgentProtocolException("MISSING_REPLAN_TRIGGER", "Replan requires a persisted needs-replan trigger.");
        var result = await InvokeAsync(state, step.Agent!, null, $"Original request:\n{request}\n\nRecorded clarifications:\n{await ReadClarificationsAsync(state, cancellationToken)}\n\nReplan trigger:\n{JsonSerializer.Serialize(trigger)}\n\nMutable remaining work:\n{JsonSerializer.Serialize(ready)}\n\nCompleted work context:\n{JsonSerializer.Serialize(completed)}", cancellationToken);
        if (result.Outcome != "replan-proposed") return result.Outcome;
        ApplyReplan(state, result.Payload); state.ReplanCount++; state.PendingReplanTrigger = null;
        state.PendingVerificationSession = null;
        state.PendingContinuation = null;
        state.Blocker = null;
        await SaveAsync(state, cancellationToken); return "applied";
    }

    private async Task<string> FinalReviewAsync(FactoryState state, WorkflowStepDefinition step, CancellationToken cancellationToken)
    {
        if (!state.FinalVerificationPassed)
        {
            var gateOutcome = await VerifyFinalGateAsync(state, cancellationToken);
            if (gateOutcome != "passed") return gateOutcome;
            state.FinalVerificationPassed = true;
            await SaveAsync(state, cancellationToken);
        }
        var request = await File.ReadAllTextAsync(Path.Combine(currentDirectory, state.RequestPath), cancellationToken);
        var result = await InvokeAsync(state, step.Agent!, null, $"Original request:\n{request}\n\nCompleted work:\n{JsonSerializer.Serialize(state.WorkItems.Select(x => new { x.Id, x.Kind, x.ContractPath, x.LastResultRef }))}\n\nAuthoritative final verification evidence references:\n{string.Join("\n", state.VerificationEvidenceRefs)}", cancellationToken);
        state.FinalReview = new(result.Outcome, $"attempts/{result.AttemptId}/result.json", (state.FinalReview?.AttemptCount ?? 0) + 1);
        if (result.Outcome == "needs-fix")
        {
            InsertCorrection(state, null, result.Payload); await SaveAsync(state, cancellationToken);
        }
        else
        {
            if (result.Outcome is not ("approved" or "blocked")) state.FinalVerificationPassed = false;
            await SaveAsync(state, cancellationToken);
        }
        return result.Outcome;
    }

    private async Task<string> VerifyWorkItemGateAsync(FactoryState state, WorkItemState item, string context, string contract, CancellationToken cancellationToken)
    {
        return await RunPersistedVerificationAsync(state, item, context, cancellationToken);
        /*
        while (true)
        {
            await events.WriteAsync(state.RunId, "verification-started", new { verificationContext = context, workItemId = item.Id, verificationFixAttempt = item.VerificationFixAttemptCount }, cancellationToken);
            VerificationResult result;
            try
            {
                result = context == "subtask"
                    ? await verification.RunSubtaskAsync(item.VerificationCheckIds, cancellationToken)
                    : await verification.RunContextAsync(context, cancellationToken);
            }
            catch (VerificationException exception)
            {
                return await BlockForVerificationExceptionAsync(state, item, context, exception, cancellationToken);
            }
            RecordEvidence(state, item, result.Evidence);
            await events.WriteAsync(state.RunId, "verification-completed", new { verificationContext = context, verificationStatus = result.Status.ToString(), verificationFixAttempt = item.VerificationFixAttemptCount }, cancellationToken);
            await SaveAsync(state, cancellationToken);
            if (result.Passed || result.Status == VerificationStatus.NoChecks)
            {
                state.PendingContinuation = null;
                state.Blocker = null;
                await SaveAsync(state, cancellationToken);
                return "passed";
            }
            if (result.Status != VerificationStatus.Failed)
                return await BlockForVerificationAsync(state, item, context, result, cancellationToken);
            if (item.VerificationFixAttemptCount >= workflow.Limits.MaxVerificationFixAttempts)
                return await BlockForVerificationAsync(state, item, context, result, cancellationToken);
            item.VerificationFixAttemptCount++;
            var failed = FailureSummary(result);
            var scope = context == "subtask"
                ? $"work item {item.Id}\n{contract}"
                : $"checkpoint {item.Id}\ncovered work items: {string.Join(", ", item.CoveredWorkItems)}\nFix only problems preventing this checkpoint verification from passing.";
            var repairInput = $"Mode:\nverification-fix\n\nScope:\n{scope}\n\nFailed authoritative checks:\n{failed}";
            PersistVerificationFixContinuation(state, item, context, "verification-fix", repairInput);
            state.Blocker = null;
            await SaveAsync(state, cancellationToken);
            var repair = await InvokeAsync(state, "implementer", item, repairInput, cancellationToken,
                context == "subtask" ? SemanticOperationKind.SubtaskVerificationFix : SemanticOperationKind.CheckpointVerificationFix, repairInput);
            if (repair.Outcome != "completed")
            {
                var outcome = PrepareRepairOutcome(item, repair.Outcome);
                HandleVerificationFixOutcomeContinuation(state, item, context, repair, repairInput);
                await SaveAsync(state, cancellationToken); return outcome;
            }
            HandleVerificationFixOutcomeContinuation(state, item, context, repair, repairInput);
            await SaveAsync(state, cancellationToken);
        }
        */
    }

    private async Task<string> VerifyFinalGateAsync(FactoryState state, CancellationToken cancellationToken)
    {
        return await RunPersistedVerificationAsync(state, null, "final", cancellationToken);
        /*
        while (true)
        {
            await events.WriteAsync(state.RunId, "verification-started", new { verificationContext = "final", verificationFixAttempt = state.FinalVerificationFixAttemptCount }, cancellationToken);
            VerificationResult result;
            try { result = await verification.RunContextAsync("final", cancellationToken); }
            catch (VerificationException exception)
            {
                return await BlockForVerificationExceptionAsync(state, null, "final", exception, cancellationToken);
            }
            RecordEvidence(state, null, result.Evidence);
            await events.WriteAsync(state.RunId, "verification-completed", new { verificationContext = "final", verificationStatus = result.Status.ToString(), verificationFixAttempt = state.FinalVerificationFixAttemptCount }, cancellationToken);
            await SaveAsync(state, cancellationToken);
            if (result.Passed || result.Status == VerificationStatus.NoChecks)
            {
                state.PendingContinuation = null;
                state.Blocker = null;
                await SaveAsync(state, cancellationToken);
                return "passed";
            }
            if (result.Status != VerificationStatus.Failed || state.FinalVerificationFixAttemptCount >= workflow.Limits.MaxVerificationFixAttempts)
                return await BlockForVerificationAsync(state, null, "final", result, cancellationToken);
            state.FinalVerificationFixAttemptCount++;
            var repairInput = $"Mode:\nverification-fix\n\nScope:\nfinal Factory run verification.\nFix only implementation defects required for the failed final checks.\nDo not introduce new product behavior or change durable intent.\n\nFailed authoritative checks:\n{FailureSummary(result)}";
            PersistVerificationFixContinuation(state, null, "final", "verification-fix", repairInput);
            state.Blocker = null;
            await SaveAsync(state, cancellationToken);
            var repair = await InvokeAsync(state, "implementer", null, repairInput, cancellationToken,
                SemanticOperationKind.FinalVerificationFix, repairInput);
            if (repair.Outcome != "completed")
            {
                HandleVerificationFixOutcomeContinuation(state, null, "final", repair, repairInput);
                await SaveAsync(state, cancellationToken);
                return repair.Outcome;
            }
            HandleVerificationFixOutcomeContinuation(state, null, "final", repair, repairInput);
            await SaveAsync(state, cancellationToken);
        }
        */
    }

    private async Task<string> RunPersistedVerificationAsync(FactoryState state, WorkItemState? item, string context, CancellationToken cancellationToken)
    {
        var session = state.PendingVerificationSession;
        if (session is null || session.Context != context || session.WorkItemId != item?.Id)
        {
            var changedPaths = ChangedPathsFor(state, item, context);
            var selection = await verification.ResolveContextAsync(context, changedPaths, cancellationToken);
            if (context == "subtask" && item is not null && item.VerificationCheckIds.Count > 0 && selection.CheckIds.Count > 0 && !item.VerificationCheckIds.SequenceEqual(selection.CheckIds, StringComparer.Ordinal))
            {
                state.PendingReplanTrigger = new("verification", item.Id, "", "The work item changed paths require a different verification selection.", null, item.VerificationEvidenceRefs.ToList());
                state.PendingVerificationSession = null;
                return "needs-replan";
            }
            session = new(context, item?.Id, selection.CheckIds.ToList(), changedPaths.ToList(), 0, [], [], null, null, selection.PolicyHash, VerificationContinuationStage.ExecuteCheck);
            state.PendingVerificationSession = session;
            await SaveAsync(state, cancellationToken);
        }
        if (session.CheckIds.Count == 0)
        {
            if (session.PolicyHash == "not-configured")
            {
                VerificationResult fallback;
                try { fallback = await verification.RunContextAsync(context, session.ChangedPaths, cancellationToken); }
                catch (VerificationException exception) { return await BlockForVerificationExceptionAsync(state, item, context, exception, cancellationToken); }
                RecordEvidence(state, item, fallback.Evidence);
                if (fallback.Status == VerificationStatus.Failed) return await HandlePersistedVerificationFailureAsync(state, item, context, fallback, cancellationToken);
                if (fallback.Status is not (VerificationStatus.Passed or VerificationStatus.NoChecks)) return await BlockForVerificationAsync(state, item, context, fallback, cancellationToken);
            }
            state.PendingVerificationSession = null;
            state.PendingContinuation = null;
            state.Blocker = null;
            await SaveAsync(state, cancellationToken);
            return "passed";
        }
        while (session.NextCheckIndex < session.CheckIds.Count)
        {
            var checkId = session.CheckIds[session.NextCheckIndex];
            var definitionHash = await verification.GetCheckDefinitionHashAsync(checkId, cancellationToken);
            VerificationResult result;
            try { result = await verification.RunCheckAsync(checkId, false, null, definitionHash, session.PolicyHash, cancellationToken); }
            catch (VerificationException exception) { return await BlockForVerificationExceptionAsync(state, item, context, exception, cancellationToken); }
            RecordEvidence(state, item, result.Evidence);
            if (result.Status is VerificationStatus.ConfirmationRequired or VerificationStatus.ResultRequired)
            {
                state.PendingVerificationSession = session with { PendingCheckId = checkId, PendingCheckDefinitionHash = definitionHash,
                    Stage = result.Status == VerificationStatus.ConfirmationRequired ? VerificationContinuationStage.AwaitingConfirmation : VerificationContinuationStage.AwaitingManualResult };
                return await BlockForVerificationAsync(state, item, context, result, cancellationToken);
            }
            if (result.Status == VerificationStatus.Failed) return await HandlePersistedVerificationFailureAsync(state, item, context, result, cancellationToken);
            if (result.Status != VerificationStatus.Passed) return await BlockForVerificationAsync(state, item, context, result, cancellationToken);
            session = session with { NextCheckIndex = session.NextCheckIndex + 1, CompletedCheckIds = session.CompletedCheckIds.Concat([checkId]).ToList(), EvidenceRefs = session.EvidenceRefs.Concat(result.Evidence.Select(x => $"verification/{x.EvidenceId}.json")).ToList() };
            state.PendingVerificationSession = session;
            await SaveAsync(state, cancellationToken);
        }
        state.PendingVerificationSession = null;
        state.PendingContinuation = null;
        state.Blocker = null;
        await SaveAsync(state, cancellationToken);
        return "passed";
    }

    private async Task<string> HandlePersistedVerificationFailureAsync(FactoryState state, WorkItemState? item, string context, VerificationResult result, CancellationToken cancellationToken)
    {
        var attempts = item?.VerificationFixAttemptCount ?? state.FinalVerificationFixAttemptCount;
        if (attempts >= workflow.Limits.MaxVerificationFixAttempts) return await BlockForVerificationAsync(state, item, context, result, cancellationToken);
        if (item is not null) item.VerificationFixAttemptCount++; else state.FinalVerificationFixAttemptCount++;
        var input = $"Mode:\nverification-fix\n\nScope:\n{context} verification.\n\nFailed authoritative checks:\n{FailureSummary(result)}";
        state.PendingVerificationSession = null;
        PersistVerificationFixContinuation(state, item, context, "verification-fix", input);
        await SaveAsync(state, cancellationToken);
        var repair = await InvokeAsync(state, "implementer", item, input, cancellationToken,
            context == "subtask" ? SemanticOperationKind.SubtaskVerificationFix : context == "checkpoint" ? SemanticOperationKind.CheckpointVerificationFix : SemanticOperationKind.FinalVerificationFix, input);
        if (repair.Outcome != "completed") { if (item is not null) PrepareRepairOutcome(item, repair.Outcome); HandleVerificationFixOutcomeContinuation(state, item, context, repair, input); await SaveAsync(state, cancellationToken); return repair.Outcome; }
        HandleVerificationFixOutcomeContinuation(state, item, context, repair, input);
        await SaveAsync(state, cancellationToken);
        return await RunPersistedVerificationAsync(state, item, context, cancellationToken);
    }

    private static IReadOnlyList<string> ChangedPathsFor(FactoryState state, WorkItemState? item, string context) => context switch
    {
        "subtask" => item?.ChangedPaths ?? [],
        "checkpoint" => state.WorkItems.Where(x => item!.CoveredWorkItems.Contains(x.Id, StringComparer.Ordinal)).SelectMany(x => x.ChangedPaths)
            .Concat(item!.ChangedPaths).Distinct(StringComparer.Ordinal).ToArray(),
        "final" => state.FactoryRunChangedPaths,
        _ => []
    };

    private void RecordEvidence(FactoryState state, WorkItemState? item, IEnumerable<VerificationEvidence> evidence)
    {
        foreach (var record in evidence)
        {
            var path = $"verification/{record.EvidenceId}.json";
            if (item is not null && !item.VerificationEvidenceRefs.Contains(path, StringComparer.Ordinal)) item.VerificationEvidenceRefs.Add(path);
            if (!state.VerificationEvidenceRefs.Contains(path, StringComparer.Ordinal)) state.VerificationEvidenceRefs.Add(path);
        }
    }

    private static string PrepareRepairOutcome(WorkItemState item, string outcome)
    {
        item.Status = outcome == "blocked" ? WorkItemStatus.Blocked : WorkItemStatus.Ready;
        item.CurrentAttemptId = null;
        return outcome;
    }

    private async Task<string> BlockForVerificationAsync(FactoryState state, WorkItemState? item, string context, VerificationResult result, CancellationToken cancellationToken)
    {
        var code = result.Status switch
        {
            VerificationStatus.ConfirmationRequired => "VERIFICATION_CONFIRMATION_REQUIRED",
            VerificationStatus.ResultRequired => "VERIFICATION_RESULT_REQUIRED",
            VerificationStatus.InfrastructureFailure => "VERIFICATION_INFRASTRUCTURE_FAILURE",
            _ => "VERIFICATION_FIX_BUDGET_EXHAUSTED"
        };
        var reason = result.Status switch
        {
            VerificationStatus.ConfirmationRequired => $"Check {result.PendingCheckId} in {context} requires explicit confirmation before running: {result.PendingCommand}",
            VerificationStatus.ResultRequired => $"Manual check {result.PendingCheckId} in {context} requires a passed or failed result: {result.PendingInstructions}",
            _ => $"{context} verification ended as {result.Status}: {FailureSummary(result)}"
        };
        var terminal = code.EndsWith("_BUDGET_EXHAUSTED", StringComparison.Ordinal);
        state.Blocker = new(code, reason, terminal
            ? "The configured budget is exhausted. Cancel and restart with a workflow that provides sufficient budget; continue cannot add budget to the current run."
            : result.Status == VerificationStatus.ConfirmationRequired ? "Run continue with --confirmation approve to execute this exact check, or --confirmation decline to terminate without running it."
            : result.Status == VerificationStatus.ResultRequired ? "Run continue with --verification-result passed or failed for this exact check."
            : "Resolve the reported verification condition, then continue.");
        var stage = result.Status == VerificationStatus.ConfirmationRequired ? VerificationContinuationStage.AwaitingConfirmation
            : result.Status == VerificationStatus.ResultRequired ? VerificationContinuationStage.AwaitingManualResult : VerificationContinuationStage.ExecuteCheck;
        state.PendingContinuation = new(terminal ? ContinuationKind.Terminal : ContinuationKind.VerificationGate, state.CurrentWorkflowStep, item?.Id, context, code, !terminal,
            VerificationCheckId: result.PendingCheckId, VerificationStage: stage);
        if (item is not null) item.Status = WorkItemStatus.Blocked;
        await SaveAsync(state, cancellationToken);
        return "blocked";
    }

    private void PersistVerificationFixContinuation(FactoryState state, WorkItemState? item, string context, string outcome, string input)
    {
        var operation = context switch
        {
            "subtask" => SemanticOperationKind.SubtaskVerificationFix,
            "checkpoint" => SemanticOperationKind.CheckpointVerificationFix,
            "final" => SemanticOperationKind.FinalVerificationFix,
            _ => throw new ArgumentOutOfRangeException(nameof(context))
        };
        state.PendingContinuation = new(ContinuationKind.SemanticInvocation, state.CurrentWorkflowStep, item?.Id, context,
            outcome.ToUpperInvariant().Replace('-', '_'), true, operation, input);
    }

    private void HandleVerificationFixOutcomeContinuation(FactoryState state, WorkItemState? item, string context, AgentResultEnvelope repair, string input)
    {
        switch (repair.Outcome)
        {
            case "completed":
                state.PendingVerificationSession = null;
                PersistVerificationGateContinuation(state, item, context);
                return;
            case "needs-replan":
                state.PendingVerificationSession = null;
                PersistReplanContinuation(state, state.CurrentWorkflowStep);
                return;
            case "intent-required":
                state.PendingVerificationSession = null;
                return;
            default:
                PersistVerificationFixContinuation(state, item, context, repair.Outcome, input);
                return;
        }
    }

    private void PersistVerificationGateContinuation(FactoryState state, WorkItemState? item, string context) =>
        state.PendingContinuation = new(ContinuationKind.VerificationGate, state.CurrentWorkflowStep, item?.Id, context,
            "VERIFICATION_GATE", true);

    private async Task<string> BlockForVerificationExceptionAsync(FactoryState state, WorkItemState? item, string context, VerificationException exception, CancellationToken cancellationToken)
    {
        state.Blocker = new(exception.Code, exception.Message, "Fix the verification failure, then continue.");
        state.PendingContinuation = new(ContinuationKind.VerificationGate, state.CurrentWorkflowStep, item?.Id, context, exception.Code, true);
        if (item is not null) item.Status = WorkItemStatus.Blocked;
        await SaveAsync(state, cancellationToken);
        return "blocked";
    }

    private static string FailureSummary(VerificationResult result) => string.Join("\n", result.Evidence.Where(x => x.Status != "passed").Select(x =>
        $"- {x.CheckId}: status={x.Status}, exitCode={x.ExitCode}, evidence=verification/{x.EvidenceId}.json\n  {Bounded(x.Output)}"));

    private static string Bounded(string value)
    {
        const int limit = 2000;
        var normalized = value.Replace("\r\n", "\n").Trim();
        return normalized.Length <= limit ? normalized : normalized[..limit] + "\n[truncated; read full evidence artifact]";
    }

    private async Task<string> IntentGateAsync(FactoryState state, CancellationToken cancellationToken)
    {
        var currentHash = HashIntent();
        if (state.IntentSnapshotHash is null)
        {
            state.IntentSnapshotHash = currentHash;
            var semanticBlocker = state.Blocker?.Code == "INTENT_REQUIRED" ? state.Blocker : null;
            state.Blocker = new(
                "INTENT_REQUIRED",
                semanticBlocker?.Reason ?? "Factory requires durable intent decisions before semantic work can continue.",
                "Update the listed durable intent decisions in .idd/intent, then run continue.",
                semanticBlocker?.Payload);
            await SaveAsync(state, cancellationToken); return "blocked";
        }
        if (state.IntentSnapshotHash == currentHash) return "blocked";
        state.IntentSnapshotHash = null; state.PendingContinuation = null; state.Blocker = null; await SaveAsync(state, cancellationToken); return "completed";
    }

    private async Task<AgentResultEnvelope> InvokeAsync(FactoryState state, string role, WorkItemState? item, string input, CancellationToken cancellationToken,
        SemanticOperationKind? continuationOperation = null, string? continuationInput = null)
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
                await RecoverWorkspaceChangesAsync(state, item, persistedInvocation, cancellationToken);
                var persistedResult = JsonSerializer.Deserialize<AgentResultEnvelope>(await File.ReadAllTextAsync(persistedResultPath, cancellationToken), FactoryJson.Options);
                var validated = new AgentResultValidator().Validate(persistedInvocation, persistedResult);
                CaptureSemanticOutcome(state, role, item, validated, continuationOperation, continuationInput); state.CurrentAttemptId = null; await SaveAsync(state, cancellationToken);
                await events.WriteAsync(state.RunId, "agent-result-reused", new { attemptId = persistedAttempt, role }, cancellationToken);
                return validated;
            }
        }
        var isVerificationFix = continuationOperation is SemanticOperationKind.SubtaskVerificationFix or SemanticOperationKind.CheckpointVerificationFix or SemanticOperationKind.FinalVerificationFix;
        if (!isVerificationFix && item is not null && item.Kind != WorkItemKind.ReviewCheckpoint && item.AttemptCount >= workflow.Limits.MaxAgentAttempts)
        {
            item.Status = WorkItemStatus.Blocked;
            item.CurrentAttemptId = null;
            throw new AgentProtocolException("RETRY_BUDGET_EXHAUSTED", $"{item.Id} exhausted its agent attempt budget.");
        }
        var attemptId = $"A{++state.AttemptSequence:000000}"; state.CurrentAttemptId = attemptId;
        if (item is not null) { item.CurrentAttemptId = attemptId; if (!isVerificationFix) item.AttemptCount++; }
        await SaveAsync(state, cancellationToken);
        var attemptDirectory = Path.Combine(currentDirectory, "attempts", attemptId); Directory.CreateDirectory(attemptDirectory);
        var resultPath = Path.Combine(attemptDirectory, "result.json");
        var agentContract = FactoryAgentCatalog.Resolve(role);
        var invocation = new AgentInvocation
        {
            RunId = state.RunId,
            AttemptId = attemptId,
            Role = agentContract.Role,
            WorkItemId = item?.Id,
            Workspace = workspace,
            ResultPath = resultPath,
            SkillName = agentContract.SkillName,
            ExecutionProfile = agentContract.ExecutionProfile,
            Input = input,
            StartedAt = clock.UtcNow
        };
        await WriteJsonAtomicallyAsync(Path.Combine(attemptDirectory, "invocation.json"), invocation, cancellationToken);
        if (agentContract.ExecutionProfile == AgentExecutionProfile.WorkspaceWrite)
            await PersistWorkspaceSnapshotAsync(state.RunId, attemptDirectory, cancellationToken);
        await events.WriteAsync(state.RunId, "agent-dispatching", new { attemptId, role, workItemId = item?.Id }, cancellationToken);
        AgentExecutionResult execution;
        try
        {
            execution = await agentExecutor.ExecuteAsync(invocation, cancellationToken);
        }
        finally
        {
            if (agentContract.ExecutionProfile == AgentExecutionProfile.WorkspaceWrite)
                await RecoverWorkspaceChangesAsync(state, item, invocation, CancellationToken.None);
        }
        var result = execution.Result;
        CaptureSemanticOutcome(state, role, item, result, continuationOperation, continuationInput); state.CurrentAttemptId = null; await SaveAsync(state, cancellationToken);
        await events.WriteAsync(state.RunId, "agent-completed", new
        {
            attemptId,
            role,
            result.Outcome,
            metrics = result.Metrics,
            termination = new
            {
                execution.Process.TerminationKind,
                execution.Process.CompleteResultObserved,
                execution.Process.KillRequired,
                execution.Process.ExitCode
            }
        }, cancellationToken);
        return result;
    }

    private async Task PersistWorkspaceSnapshotAsync(string runId, string attemptDirectory, CancellationToken cancellationToken)
    {
        var snapshot = await SnapshotWorkspaceAsync(runId, cancellationToken);
        await WriteJsonAtomicallyAsync(Path.Combine(attemptDirectory, "workspace-before.json"), new WorkspaceSnapshotArtifact(1, snapshot), cancellationToken);
    }

    private async Task RecoverWorkspaceChangesAsync(FactoryState state, WorkItemState? item, AgentInvocation invocation, CancellationToken cancellationToken)
    {
        if (invocation.ExecutionProfile != AgentExecutionProfile.WorkspaceWrite) return;
        var attemptDirectory = Path.GetDirectoryName(invocation.ResultPath)!;
        var changesPath = Path.Combine(attemptDirectory, "workspace-changes.json");
        WorkspaceChangesArtifact? changes = null;
        if (File.Exists(changesPath))
        {
            changes = JsonSerializer.Deserialize<WorkspaceChangesArtifact>(await File.ReadAllTextAsync(changesPath, cancellationToken), FactoryJson.Options)
                ?? throw new AgentProtocolException("MALFORMED_WORKSPACE_CHANGES", $"Attempt {invocation.AttemptId} has malformed workspace changes.");
        }
        else
        {
            var beforePath = Path.Combine(attemptDirectory, "workspace-before.json");
            if (!File.Exists(beforePath)) return;
            var before = JsonSerializer.Deserialize<WorkspaceSnapshotArtifact>(await File.ReadAllTextAsync(beforePath, cancellationToken), FactoryJson.Options)
                ?? throw new AgentProtocolException("MALFORMED_WORKSPACE_SNAPSHOT", $"Attempt {invocation.AttemptId} has malformed workspace baseline.");
            var after = await SnapshotWorkspaceAsync(state.RunId, cancellationToken);
            var changedPaths = after.Where(pair => !before.Files.TryGetValue(pair.Key, out var prior) || prior != pair.Value).Select(pair => pair.Key)
                .Concat(before.Files.Keys.Where(path => !after.ContainsKey(path)))
                .Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToList();
            changes = new(1, changedPaths);
            await WriteJsonAtomicallyAsync(changesPath, changes, cancellationToken);
        }
        if (item is not null)
            foreach (var path in changes.ChangedPaths)
                if (!item.ChangedPaths.Contains(path, StringComparer.Ordinal)) item.ChangedPaths.Add(path);
        foreach (var path in changes.ChangedPaths)
            if (!state.FactoryRunChangedPaths.Contains(path, StringComparer.Ordinal)) state.FactoryRunChangedPaths.Add(path);
    }

    private async Task<SortedDictionary<string, string>> SnapshotWorkspaceAsync(string runId, CancellationToken cancellationToken)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(workspace);
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] directories;
            string[] files;
            try
            {
                directories = Directory.GetDirectories(directory);
                files = Directory.GetFiles(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                await events.WriteAsync(runId, "workspace-snapshot-directory-skipped", new { path = RelativePath(directory), exception = exception.GetType().Name }, CancellationToken.None);
                continue;
            }
            foreach (var child in directories.OrderByDescending(path => path, StringComparer.Ordinal))
            {
                var relative = RelativePath(child);
                if (IsOperationalArtifact(relative)) continue;
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { await events.WriteAsync(runId, "workspace-snapshot-directory-skipped", new { path = relative, exception = exception.GetType().Name }, CancellationToken.None); }
            }
            foreach (var path in files.OrderBy(path => path, StringComparer.Ordinal))
            {
                var relative = RelativePath(path);
                if (IsOperationalArtifact(relative)) continue;
                try { result.Add(relative, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path, cancellationToken)))); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { await events.WriteAsync(runId, "workspace-snapshot-file-skipped", new { path = relative, exception = exception.GetType().Name }, CancellationToken.None); }
            }
        }
        return result;
    }

    private string RelativePath(string path) => Path.GetRelativePath(workspace, path).Replace('\\', '/');

    private static bool IsOperationalArtifact(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2 && segments[0].Equals(".idd", StringComparison.OrdinalIgnoreCase) && segments[1].Equals("factory", StringComparison.OrdinalIgnoreCase)) return true;
        return segments.Any(segment => segment.Equals(".git", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".vs", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".idea", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".vscode", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("TestResults", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task WriteJsonAtomicallyAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, FactoryJson.Options), cancellationToken);
        File.Move(temporary, path, true);
    }

    private sealed record WorkspaceSnapshotArtifact(int SchemaVersion, SortedDictionary<string, string> Files);
    private sealed record WorkspaceChangesArtifact(int SchemaVersion, List<string> ChangedPaths);

    private void CaptureSemanticOutcome(FactoryState state, string role, WorkItemState? item, AgentResultEnvelope result,
        SemanticOperationKind? continuationOperation = null, string? continuationInput = null)
    {
        if (result.Outcome == "needs-replan")
        {
            var evidence = item?.VerificationEvidenceRefs ?? state.VerificationEvidenceRefs;
            state.PendingReplanTrigger = new(role, item?.Id, $"attempts/{result.AttemptId}/result.json", result.Reason, result.Payload?.Clone(), evidence.ToList());
        }
        else if (result.Outcome == "intent-required" && state.PendingReplanTrigger is null && IntentRequiredLeadsToReplan(state))
        {
            var evidence = item?.VerificationEvidenceRefs ?? state.VerificationEvidenceRefs;
            state.PendingReplanTrigger = new(role, item?.Id, $"attempts/{result.AttemptId}/result.json", result.Reason, result.Payload?.Clone(), evidence.ToList());
        }
        if (result.Outcome is not ("needs-clarification" or "focused-handoff" or "blocked" or "intent-required")) return;
        if (result.Outcome == "intent-required") IntentRequiredPayload.Validate(result.Payload);
        var code = result.Outcome.ToUpperInvariant().Replace('-', '_');
        var reason = string.IsNullOrWhiteSpace(result.Reason)
            ? result.Outcome == "intent-required"
                ? "Durable product intent is missing decisions required for semantic work."
                : $"Workflow stopped with {result.Outcome}."
            : result.Reason;
        var resumeWhen = result.Outcome switch
        {
            "needs-clarification" => "Provide the requested clarification and continue.",
            "intent-required" => "Update the listed durable intent decisions in .idd/intent, then run continue.",
            _ => "Resolve the reported condition and continue."
        };
        state.Blocker = new(code, reason, resumeWhen, result.Payload?.Clone());
        var kind = result.Outcome switch
        {
            "needs-clarification" => ContinuationKind.Clarification,
            "intent-required" => ContinuationKind.IntentGate,
            _ => ContinuationKind.SemanticInvocation
        };
        var operation = continuationOperation ?? SemanticOperationFor(role, item);
        state.PendingContinuation = new(kind, ContinuationWorkflowStep(state, result.Outcome), item?.Id, VerificationContextFor(operation), code, true,
            operation, continuationInput);
    }

    private string ContinuationWorkflowStep(FactoryState state, string outcome)
    {
        if (outcome != "intent-required") return state.CurrentWorkflowStep;
        var step = steps[state.CurrentWorkflowStep];
        return step.Transitions.TryGetValue(outcome, out var target) && target != "$stop"
            ? target
            : throw new WorkflowException("UNROUTED_WORKFLOW_OUTCOME", $"Step {step.Id} does not route {outcome} to a resumable intent gate.");
    }

    private bool IntentRequiredLeadsToReplan(FactoryState state)
    {
        var source = steps[state.CurrentWorkflowStep];
        if (!source.Transitions.TryGetValue("intent-required", out var intentStepId) || !steps.TryGetValue(intentStepId, out var intentStep)) return false;
        return intentStep.Uses == "factory.intent" && intentStep.Transitions.TryGetValue("completed", out var target) &&
            steps.TryGetValue(target, out var targetStep) && targetStep.Uses == "factory.replan";
    }

    private SemanticOperationKind SemanticOperationForStep(string workflowStep) =>
        steps.TryGetValue(workflowStep, out var step) ? step.Uses switch
        {
            "factory.decompose" => SemanticOperationKind.Decomposition,
            "factory.replan" => SemanticOperationKind.Replan,
            "factory.final-review" => SemanticOperationKind.FinalReview,
            "factory.execute" => SemanticOperationKind.ExecuteWork,
            _ => throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Workflow step does not identify a resumable operation.")
        } : SemanticOperationKind.None;

    private static SemanticOperationKind SemanticOperationFor(string role, WorkItemState? item) => role switch
    {
        "task-decomposer" => SemanticOperationKind.Decomposition,
        "factory-replanner" => SemanticOperationKind.Replan,
        "checkpoint-reviewer" => SemanticOperationKind.CheckpointReview,
        "final-reviewer" => SemanticOperationKind.FinalReview,
        "implementer" when item is not null => SemanticOperationKind.PrimarySubtaskImplementation,
        _ => SemanticOperationKind.None
    };

    private static string? VerificationContextFor(SemanticOperationKind operation) => operation switch
    {
        SemanticOperationKind.SubtaskVerificationFix => "subtask",
        SemanticOperationKind.CheckpointVerificationFix => "checkpoint",
        SemanticOperationKind.FinalVerificationFix => "final",
        _ => null
    };

    private void AddWorkItem(FactoryState state, JsonElement node, IDictionary<string, string>? contracts = null)
    {
        var id = node.GetProperty("id").GetString() ?? throw new AgentProtocolException("INVALID_DECOMPOSITION", "Work item id missing.");
        var kindText = node.GetProperty("kind").GetString(); var kind = kindText switch { "subtask" => WorkItemKind.Subtask, "review-checkpoint" => WorkItemKind.ReviewCheckpoint, "corrective-subtask" => WorkItemKind.CorrectiveSubtask, _ => throw new AgentProtocolException("INVALID_DECOMPOSITION", $"Unknown kind {kindText}.") };
        var sequence = node.TryGetProperty("sequence", out var sequenceNode) ? sequenceNode.GetInt32() : state.WorkItems.Count + 1;
        var file = $"{sequence:000}-{Slug(id)}.md"; var relative = $"work-items/{file}";
        var contract = node.TryGetProperty("contractMarkdown", out var contractNode) ? contractNode.GetString() : null;
        if (string.IsNullOrWhiteSpace(contract)) throw new AgentProtocolException("INVALID_DECOMPOSITION", $"{id} lacks contractMarkdown.");
        contracts?.Add(relative, contract);
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

    private static FactoryState CloneState(FactoryState state) =>
        JsonSerializer.Deserialize<FactoryState>(JsonSerializer.Serialize(state, FactoryJson.Options), FactoryJson.Options)
        ?? throw new InvalidOperationException("Cannot clone Factory state.");

    private static void ApplyCandidate(FactoryState state, FactoryState candidate)
    {
        state.WorkItems.Clear();
        state.WorkItems.AddRange(candidate.WorkItems);
    }

    private void WriteContracts(IReadOnlyDictionary<string, string> contracts)
    {
        foreach (var (relative, content) in contracts)
            File.WriteAllText(Path.Combine(currentDirectory, relative), content);
    }

    private void ValidateRuntimeGraph(FactoryState state)
    {
        new FactoryStateValidator().Validate(state);
        verification.ValidateCheckIds(state.WorkItems.SelectMany(x => x.VerificationCheckIds));
        if (state.WorkItems.Any(x => x.Status is WorkItemStatus.Dispatching or WorkItemStatus.Running && x.CurrentAttemptId is null))
            throw new AgentProtocolException("INVALID_RUNTIME_GRAPH", "Active work items require an attempt identity.");
        var items = state.WorkItems.ToDictionary(x => x.Id, StringComparer.Ordinal);
        foreach (var item in state.WorkItems.Where(IsRemainingWork))
        foreach (var dependencyId in item.Dependencies)
        {
            var dependency = items[dependencyId];
            if (dependency.Status is WorkItemStatus.Superseded or WorkItemStatus.Cancelled or WorkItemStatus.Failed)
                throw new AgentProtocolException("INVALID_RUNTIME_GRAPH", $"Remaining work item {item.Id} depends on {dependencyId}, which can no longer complete.");
        }
    }

    private static bool IsRemainingWork(WorkItemState item) => item.Status is not (WorkItemStatus.Completed or WorkItemStatus.Superseded or WorkItemStatus.Cancelled or WorkItemStatus.Failed);

    private void PromoteReady(FactoryState state)
    {
        foreach (var item in state.WorkItems.Where(x => x.Status == WorkItemStatus.Planned && x.Dependencies.All(id => state.WorkItems.Single(x => x.Id == id).Status == WorkItemStatus.Completed))) item.Status = WorkItemStatus.Ready;
    }

    private void InsertCorrection(FactoryState state, WorkItemState? review, JsonElement? payload)
    {
        if (state.CorrectiveCycleCount >= workflow.Limits.MaxCorrectiveCycles) throw new AgentProtocolException("CORRECTIVE_BUDGET_EXHAUSTED", "Corrective cycle budget exhausted.");
        if (payload is not { } data || !data.TryGetProperty("correctiveSubtask", out var correction)) throw new AgentProtocolException("MALFORMED_AGENT_RESULT", "needs-fix requires payload.correctiveSubtask.");
        var candidate = CloneState(state);
        var max = candidate.WorkItems.Count == 0 ? 0 : candidate.WorkItems.Max(x => x.Sequence); var id = correction.TryGetProperty("id", out var idNode) ? idNode.GetString() : $"correction-{state.CorrectiveCycleCount + 1}";
        if (string.IsNullOrWhiteSpace(id) || candidate.WorkItems.Any(x => x.Id == id)) throw new AgentProtocolException("MALFORMED_AGENT_RESULT", "Correction ID must be non-empty and unique.");
        var contract = correction.TryGetProperty("contractMarkdown", out var contractNode) ? contractNode.GetString() : null;
        if (string.IsNullOrWhiteSpace(contract)) throw new AgentProtocolException("MALFORMED_AGENT_RESULT", "Correction contract missing.");
        var relative = $"work-items/{max + 1:000}-{Slug(id)}.md";
        var dependencies = review?.CoveredWorkItems.ToList() ?? candidate.WorkItems.Where(x => x.Status == WorkItemStatus.Completed).Select(x => x.Id).ToList();
        var item = new WorkItemState { Id = id, Sequence = max + 1, Kind = WorkItemKind.CorrectiveSubtask, Status = WorkItemStatus.Ready, ContractPath = relative, Dependencies = dependencies, VerificationCheckIds = Strings(correction, "verificationCheckIds") };
        candidate.WorkItems.Add(item); candidate.CorrectiveCycleCount++;
        var candidateReview = review is null ? null : candidate.WorkItems.Single(x => x.Id == review.Id);
        if (candidateReview is not null)
        {
            candidateReview.Status = WorkItemStatus.Planned;
            candidateReview.CurrentAttemptId = null;
            candidateReview.VerificationFixAttemptCount = 0;
            candidateReview.Dependencies.Add(id);
            candidateReview.CoveredWorkItems.Add(id);
        }
        else { candidate.FinalVerificationFixAttemptCount = 0; candidate.FinalVerificationPassed = false; }
        ValidateRuntimeGraph(candidate);
        ApplyCandidate(state, candidate);
        state.CorrectiveCycleCount = candidate.CorrectiveCycleCount;
        state.FinalVerificationFixAttemptCount = candidate.FinalVerificationFixAttemptCount;
        state.FinalVerificationPassed = candidate.FinalVerificationPassed;
        File.WriteAllText(Path.Combine(currentDirectory, relative), contract);
    }

    private void ApplyReplan(FactoryState state, JsonElement? payload)
    {
        var candidate = CloneState(state);
        var contracts = new Dictionary<string, string>(StringComparer.Ordinal);
        string? runContext = null;
        ApplyReplanOperations(candidate, payload, contracts, ref runContext);
        PromoteReady(candidate);
        ValidateRuntimeGraph(candidate);
        ApplyCandidate(state, candidate);
        WriteContracts(contracts);
        if (runContext is not null) File.WriteAllText(Path.Combine(currentDirectory, "run-context.md"), runContext);
    }

    private void ApplyReplanOperations(FactoryState state, JsonElement? payload, IDictionary<string, string> contracts, ref string? runContext)
    {
        if (payload is not { } data || !data.TryGetProperty("operations", out var operations) || operations.ValueKind != JsonValueKind.Array) throw new AgentProtocolException("INVALID_REPLAN", "Replan proposal requires operations.");
        foreach (var operation in operations.EnumerateArray())
        {
            var kind = operation.GetProperty("kind").GetString();
            if (kind == "insert-subtask") AddWorkItem(state, operation.GetProperty("subtask"), contracts);
            else if (kind == "supersede-ready-subtask") { var item = MutableItem(state, operation.GetProperty("id").GetString()!); item.Status = WorkItemStatus.Superseded; }
            else if (kind == "replace-ready-subtask") { var old = MutableItem(state, operation.GetProperty("id").GetString()!); old.Status = WorkItemStatus.Superseded; AddWorkItem(state, operation.GetProperty("subtask"), contracts); }
            else if (kind == "reorder-ready-work") ReorderReady(state, Strings(operation, "workItemIds"));
            else if (kind == "update-run-context") runContext = operation.GetProperty("content").GetString() ?? "";
            else if (kind == "update-checkpoint-coverage") { var checkpoint = MutableItem(state, operation.GetProperty("id").GetString()!); if (checkpoint.Kind != WorkItemKind.ReviewCheckpoint) throw new AgentProtocolException("INVALID_REPLAN", $"{checkpoint.Id} is not a checkpoint."); checkpoint.CoveredWorkItems.Clear(); checkpoint.CoveredWorkItems.AddRange(Strings(operation, "coveredWorkItems")); }
            else if (kind == "insert-checkpoint") AddWorkItem(state, operation.GetProperty("checkpoint"), contracts);
            else if (kind == "remove-unused-ready-checkpoint") { var checkpoint = MutableItem(state, operation.GetProperty("id").GetString()!); if (checkpoint.Kind != WorkItemKind.ReviewCheckpoint) throw new AgentProtocolException("INVALID_REPLAN", $"{checkpoint.Id} is not a checkpoint."); checkpoint.Status = WorkItemStatus.Superseded; }
            else throw new AgentProtocolException("INVALID_REPLAN", $"Unsupported or unsafe replan operation {kind}.");
        }
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

    private async Task<FactoryCliOutcome?> ResumeSemanticOperationAsync(FactoryState state, PendingContinuation continuation, CancellationToken token)
    {
        switch (continuation.Operation)
        {
            case SemanticOperationKind.PrimarySubtaskImplementation:
                state.WorkItems.Single(x => x.Id == continuation.WorkItemId).Status = WorkItemStatus.Ready;
                return null;
            case SemanticOperationKind.CheckpointReview:
                state.WorkItems.Single(x => x.Id == continuation.WorkItemId).Status = WorkItemStatus.AwaitingReview;
                return null;
            case SemanticOperationKind.FinalReview:
            case SemanticOperationKind.Decomposition:
            case SemanticOperationKind.Replan:
            case SemanticOperationKind.ExecuteWork:
                return null;
            case SemanticOperationKind.SubtaskVerificationFix:
            case SemanticOperationKind.CheckpointVerificationFix:
            case SemanticOperationKind.FinalVerificationFix:
                return await ResumeVerificationFixAsync(state, continuation, token);
            default:
                throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Unsupported semantic continuation operation.");
        }
    }

    private async Task<FactoryCliOutcome?> ResumeVerificationFixAsync(FactoryState state, PendingContinuation continuation, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(continuation.OperationInput))
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Verification-fix continuation requires its operation input.");
        var item = continuation.WorkItemId is null ? null : state.WorkItems.Single(x => x.Id == continuation.WorkItemId);
        state.Blocker = null;
        state.RunStatus = FactoryRunStatus.Running;
        await SaveAsync(state, token);
        var repair = await InvokeAsync(state, "implementer", item, continuation.OperationInput, token,
            continuation.Operation, continuation.OperationInput);
        if (repair.Outcome != "completed")
        {
            if (item is not null) PrepareRepairOutcome(item, repair.Outcome);
            HandleVerificationFixOutcomeContinuation(state, item, continuation.VerificationContext!, repair, continuation.OperationInput);
            await SaveAsync(state, token);
            return await RouteResumedOutcomeAsync(state, continuation.WorkflowStep, repair.Outcome, token);
        }
        HandleVerificationFixOutcomeContinuation(state, item, continuation.VerificationContext!, repair, continuation.OperationInput);
        await SaveAsync(state, token);
        var gateOutcome = continuation.VerificationContext == "final"
            ? await VerifyFinalGateAsync(state, token)
            : await VerifyWorkItemGateAsync(state, item!, continuation.VerificationContext!,
                await File.ReadAllTextAsync(Path.Combine(currentDirectory, item!.ContractPath), token), token);
        if (gateOutcome != "passed") return await StopForOutcomeAsync(state, gateOutcome, token);
        await PrepareAndCompleteResumedVerificationAsync(state, continuation, token);
        return null;
    }

    private async Task<FactoryCliOutcome?> RouteResumedOutcomeAsync(FactoryState state, string workflowStep, string outcome, CancellationToken token)
    {
        var step = steps[workflowStep];
        if (!step.Transitions.TryGetValue(outcome, out var target))
            throw new WorkflowException("UNROUTED_WORKFLOW_OUTCOME", $"Step {step.Id} does not route {outcome}.");
        if (target == "$stop") return await StopForOutcomeAsync(state, outcome, token);
        state.CurrentWorkflowStep = target;
        await SaveAsync(state, token);
        return await ExecuteLoopAsync(state, token);
    }

    private void PersistReplanContinuation(FactoryState state, string sourceWorkflowStep)
    {
        var step = steps[sourceWorkflowStep];
        if (!step.Transitions.TryGetValue("needs-replan", out var target) || target == "$stop" || !steps.TryGetValue(target, out var targetStep) || targetStep.Uses != "factory.replan")
            throw new WorkflowException("UNROUTED_WORKFLOW_OUTCOME", $"Step {step.Id} does not route needs-replan to factory.replan.");
        state.CurrentWorkflowStep = target;
        state.PendingContinuation = new(ContinuationKind.SemanticInvocation, target, null, null, "NEEDS_REPLAN", true,
            SemanticOperationKind.Replan);
    }

    private async Task PrepareAndCompleteResumedVerificationAsync(FactoryState state, PendingContinuation continuation, CancellationToken token)
    {
        if (continuation.VerificationContext == "subtask")
        {
            var item = state.WorkItems.Single(x => x.Id == continuation.WorkItemId);
            item.Status = WorkItemStatus.AwaitingVerification;
            item.CurrentAttemptId = null;
            await SaveAsync(state, token);
            item.Status = WorkItemStatus.Completed;
            PromoteReady(state);
        }
        else if (continuation.VerificationContext == "checkpoint")
            state.WorkItems.Single(x => x.Id == continuation.WorkItemId).Status = WorkItemStatus.AwaitingReview;
        else if (continuation.VerificationContext == "final")
            state.FinalVerificationPassed = true;
        state.PendingContinuation = null;
        state.Blocker = null;
        await SaveAsync(state, token);
    }

    private async Task<FactoryCliOutcome?> ResumeVerificationAsync(FactoryState state, PendingContinuation continuation, CancellationToken token)
    {
        var outcome = continuation.VerificationContext == "final"
            ? await VerifyFinalGateAsync(state, token)
            : await VerifyWorkItemGateAsync(state,
                state.WorkItems.Single(x => x.Id == continuation.WorkItemId),
                continuation.VerificationContext!,
                await File.ReadAllTextAsync(Path.Combine(currentDirectory, state.WorkItems.Single(x => x.Id == continuation.WorkItemId).ContractPath), token), token);
        if (outcome != "passed") return await StopForOutcomeAsync(state, outcome, token);
        await PrepareAndCompleteResumedVerificationAsync(state, continuation, token);
        state.PendingContinuation = null; state.Blocker = null;
        await SaveAsync(state, token);
        return null;
    }

    private async Task<FactoryCliOutcome?> ResumePendingVerificationActionAsync(FactoryState state, PendingContinuation continuation, string outcome, CancellationToken token)
    {
        if (outcome != "passed") return await RouteResumedOutcomeAsync(state, continuation.WorkflowStep, outcome, token);
        await PrepareAndCompleteResumedVerificationAsync(state, continuation, token);
        return null;
    }
    private async Task ReconcileAsync(FactoryState state, CancellationToken token)
    {
        var attemptId = state.CurrentAttemptId;
        if (attemptId is null)
        {
            var activeItemAttempts = state.WorkItems
                .Where(x => x.Status is WorkItemStatus.Dispatching or WorkItemStatus.Running && x.CurrentAttemptId is not null)
                .Select(x => x.CurrentAttemptId!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (activeItemAttempts.Length == 0) return;
            if (activeItemAttempts.Length > 1)
                throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Multiple active work-item attempts cannot be reconciled deterministically.");
            attemptId = activeItemAttempts[0];
        }

        var directory = Path.Combine(currentDirectory, "attempts", attemptId); var invocationPath = Path.Combine(directory, "invocation.json");
        if (!File.Exists(invocationPath)) throw new AgentProtocolException("UNKNOWN_ATTEMPT", $"Persisted attempt {attemptId} has no invocation artifact.");
        var invocation = JsonSerializer.Deserialize<AgentInvocation>(await File.ReadAllTextAsync(invocationPath, token), FactoryJson.Options)
            ?? throw new AgentProtocolException("UNKNOWN_ATTEMPT", $"Persisted attempt {attemptId} is malformed.");
        if (invocation.RunId != state.RunId || invocation.AttemptId != attemptId) throw new AgentProtocolException("UNKNOWN_ATTEMPT", $"Persisted attempt {attemptId} identity is invalid.");
        var item = invocation.WorkItemId is null ? null : state.WorkItems.SingleOrDefault(x => x.Id == invocation.WorkItemId)
            ?? throw new AgentProtocolException("UNKNOWN_ATTEMPT", $"Attempt {attemptId} references unknown work.");
        await RecoverWorkspaceChangesAsync(state, item, invocation, token);
        var resultPath = Path.Combine(directory, "result.json");
        if (File.Exists(resultPath))
        {
            AgentResultEnvelope? persistedResult;
            try { persistedResult = JsonSerializer.Deserialize<AgentResultEnvelope>(await File.ReadAllTextAsync(resultPath, token), FactoryJson.Options); }
            catch (JsonException exception) { throw new AgentProtocolException("MALFORMED_AGENT_RESULT", exception.Message); }
            var result = new AgentResultValidator().Validate(invocation, persistedResult);
            if (item is { Kind: WorkItemKind.ReviewCheckpoint, Status: WorkItemStatus.Running } && item.CurrentAttemptId == attemptId && result.Outcome == "needs-fix")
            {
                item.LastResultRef = $"attempts/{attemptId}/result.json";
                InsertCorrection(state, item, result.Payload);
                state.CurrentAttemptId = null;
                await SaveAsync(state, token);
                await events.WriteAsync(state.RunId, "agent-result-recovered", new { attemptId, invocation.Role, result.Outcome, workItemId = item.Id }, token);
                return;
            }

            state.CurrentAttemptId = attemptId;
            if (item is not null && item.Status is WorkItemStatus.Dispatching or WorkItemStatus.Running) item.Status = WorkItemStatus.Ready;
            await SaveAsync(state, token); return;
        }
        state.CurrentAttemptId = null;
        if (item is not null) { item.CurrentAttemptId = null; if (item.Status is WorkItemStatus.Dispatching or WorkItemStatus.Running) item.Status = WorkItemStatus.Ready; }
        await SaveAsync(state, token); await events.WriteAsync(state.RunId, "agent-attempt-interrupted", new { attemptId }, token);
    }
    private async Task<FactoryCliOutcome> StopForOutcomeAsync(FactoryState state, string outcome, CancellationToken token) => await StopAsync(state, state.Blocker?.Code ?? outcome.ToUpperInvariant().Replace('-', '_'), state.Blocker?.Reason ?? $"Workflow stopped with {outcome}.", state.Blocker?.ResumeWhen ?? "Resolve the reported condition and continue.", token, state.Blocker?.Payload);
    private async Task<FactoryCliOutcome> StopForAgentProtocolExceptionAsync(FactoryState state, AgentProtocolException exception, CancellationToken token)
    {
        var resume = exception.Code.EndsWith("_BUDGET_EXHAUSTED", StringComparison.Ordinal)
            ? "The configured budget is exhausted. Cancel and restart with a workflow that provides sufficient budget; continue cannot add budget to the current run."
            : "Continue to retry within the configured attempt budget.";
        return await StopAsync(state, exception.Code, exception.Message, resume, token);
    }
    private async Task<FactoryCliOutcome> StopAsync(FactoryState state, string code, string reason, string resume, CancellationToken token, JsonElement? payload = null)
    {
        state.RunStatus = FactoryRunStatus.Blocked;
        state.Blocker = new(code, reason, resume, payload);
        if (state.PendingContinuation is null || code.EndsWith("_BUDGET_EXHAUSTED", StringComparison.Ordinal))
            state.PendingContinuation = new(code.EndsWith("_BUDGET_EXHAUSTED", StringComparison.Ordinal) ? ContinuationKind.Terminal : ContinuationKind.SemanticInvocation,
                state.CurrentWorkflowStep, null, null, code, !code.EndsWith("_BUDGET_EXHAUSTED", StringComparison.Ordinal),
                SemanticOperationForStep(state.CurrentWorkflowStep));
        await SaveAsync(state, token); await events.WriteAsync(state.RunId, "run-blocked", new { code, reason, resume, payload }, token); return new(code, state.RunId, reason, resume, Payload: payload);
    }
    private void DetectLegacyState() { if (!Directory.Exists(currentDirectory)) return; if (Directory.EnumerateFiles(currentDirectory, "*.ready.md").Concat(Directory.EnumerateFiles(currentDirectory, "*.active.md")).Concat(Directory.EnumerateFiles(currentDirectory, "*.completed.md")).Concat(Directory.EnumerateFiles(currentDirectory, "*.blocked.md")).Any()) throw new FactoryStateException("LEGACY_FACTORY_STATE", "Finish with the previous Factory version or cancel/restart with the new runtime."); }
    private async Task RecordClarificationAsync(FactoryState state, string sourcePath, CancellationToken token)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Clarification answer file was not found.", sourcePath);
        var directory = Path.Combine(currentDirectory, "clarifications"); Directory.CreateDirectory(directory);
        var relative = $"clarifications/Q{state.ClarificationRefs.Count + 1:00000}.md"; await File.WriteAllTextAsync(Path.Combine(currentDirectory, relative), await File.ReadAllTextAsync(sourcePath, token), token);
        state.ClarificationRefs.Add(relative); state.Blocker = null;
        if (state.PendingContinuation is { Kind: ContinuationKind.Clarification } continuation)
            state.PendingContinuation = continuation with { Kind = ContinuationKind.SemanticInvocation };
        await SaveAsync(state, token);
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
