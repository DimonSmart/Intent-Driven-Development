using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Finalization;
using Idd.Factory.Verification;

namespace Idd.Factory.Runtime;

internal enum FactoryRuntimeState
{
    Planning,
    SelectingWork,
    Executing,
    Verifying,
    FinalVerifying,
    Finalizing,
    Blocked
}

public enum FactoryCommandKind
{
    Plan,
    ResumePendingOperation,
    RunVerification,
    SelectNextWork,
    DispatchWork,
    RunFinalVerification,
    Finalize,
    StopBlocked
}

internal sealed record FactoryTransition(
    FactoryRuntimeState From,
    FactoryRuntimeState To,
    string Reason,
    FactoryCliOutcome? Outcome = null);

internal sealed record FactoryContinuationInput(
    string? UserAnswer,
    VerificationConfirmation Confirmation,
    bool? VerificationPassed);

internal sealed class FactoryStateMachine(
    FactoryRuntimeContext context,
    PlanningService planning,
    PlanMutationService planMutation,
    ExecutionService execution,
    SemanticExecutionService semanticExecution,
    RuntimeVerificationService verification,
    FactoryStopService stop)
{
    public FactoryRuntimeState ResolveState(FactoryState state)
    {
        if (state.PendingContinuation is { IsResumable: false })
            return FactoryRuntimeState.Blocked;

        if (state.PendingContinuation is { } continuation)
        {
            return continuation.Kind switch
            {
                ContinuationKind.VerificationGate => continuation.VerificationContext switch
                {
                    "final" => FactoryRuntimeState.FinalVerifying,
                    "subtask" => FactoryRuntimeState.Verifying,
                    _ => FactoryRuntimeState.Blocked
                },
                ContinuationKind.SemanticInvocation => continuation.Operation switch
                {
                    SemanticOperationKind.Planning => FactoryRuntimeState.Planning,
                    SemanticOperationKind.WorkItemExecution => FactoryRuntimeState.Executing,
                    _ => FactoryRuntimeState.Blocked
                },
                _ => FactoryRuntimeState.Blocked
            };
        }

        if (state.PlanningCycleCount == 0)
            return FactoryRuntimeState.Planning;

        if (state.Current is { })
        {
            return state.CurrentPhase switch
            {
                CurrentWorkPhase.AwaitingVerification => FactoryRuntimeState.Verifying,
                CurrentWorkPhase.Ready or CurrentWorkPhase.Running => FactoryRuntimeState.Executing,
                _ => FactoryRuntimeState.Blocked
            };
        }

        if (state.Remaining.Count > 0)
            return FactoryRuntimeState.SelectingWork;
        if (state.Completed.Count > state.PlannedThroughCompletedCount)
            return FactoryRuntimeState.Planning;

        var finalVerificationIsCurrent = state.FinalVerificationPlanRevision == state.PlanRevision;
        if (finalVerificationIsCurrent && !state.FinalVerificationPassed)
            return FactoryRuntimeState.Planning;
        if (!finalVerificationIsCurrent)
            return FactoryRuntimeState.FinalVerifying;
        return FactoryRuntimeState.Finalizing;
    }

    public async Task<FactoryCliOutcome> RunAsync(
        FactoryState state,
        CancellationToken cancellationToken)
    {
        try
        {
            await ReconcileSemanticAttemptAsync(state, cancellationToken);
            var baselineOutcome = await EnsureBaselineAsync(state, cancellationToken);
            if (baselineOutcome is not null)
                return baselineOutcome;
            return await RunCoreAsync(state, cancellationToken);
        }
        catch (AgentProtocolException exception)
        {
            return await BlockForAgentProtocolExceptionAsync(state, exception, cancellationToken);
        }
        catch (VerificationException exception)
        {
            return await BlockForVerificationExceptionAsync(state, exception, cancellationToken);
        }
    }

    public async Task<FactoryCliOutcome> ContinueAsync(
        FactoryState state,
        FactoryContinuationInput input,
        CancellationToken cancellationToken)
    {
        if (state.RunStatus == FactoryRunStatus.Cancelled)
            return new("CANCELLED", state.RunId);

        try
        {
            await ReconcileSemanticAttemptAsync(state, cancellationToken);

            if (state.PendingContinuation is { Kind: ContinuationKind.UserQuestion })
            {
                if (string.IsNullOrWhiteSpace(input.UserAnswer))
                    return stop.OutcomeFromBlocker(state, "USER_DECISION_REQUIRED");

                var question = state.Blocker?.Reason;
                if (string.IsNullOrWhiteSpace(question))
                {
                    throw new FactoryStateException(
                        "CORRUPT_FACTORY_STATE",
                        "User-question continuation has no persisted question.");
                }

                await planning.PersistAnswerAsync(
                    question,
                    input.UserAnswer,
                    cancellationToken);
                state.Blocker = null;
                state.RunStatus = FactoryRunStatus.Running;
                await context.Events.WriteAsync(
                    state.RunId,
                    "user-answer-recorded",
                    new { },
                    cancellationToken);

                var preparation = await planning.PrepareAsync(state, cancellationToken);
                if (preparation.ImmediateResult is { } immediate)
                {
                    return await BlockAsync(
                        state,
                        new(
                            "PLANNING_BUDGET_EXHAUSTED",
                            immediate.Detail ?? "Factory planning-cycle budget exhausted.",
                            "Resolve the condition, then use factory_restart to replace the run, or factory_cancel if no replacement run is wanted.",
                            new(
                                ContinuationKind.Terminal,
                                null,
                                null,
                                "PLANNING_BUDGET_EXHAUSTED",
                                false)),
                        cancellationToken);
                }

                await StartSemanticAttemptAsync(
                    state,
                    null,
                    SemanticOperationKind.Planning,
                    preparation.Input!,
                    cancellationToken);
            }
            else if (input.UserAnswer is not null)
            {
                return new(
                    "UNEXPECTED_USER_ANSWER",
                    state.RunId,
                    "The current Factory continuation is not waiting for a planner question.",
                    "Continue without a user answer, or cancel the run.");
            }

            if (state.PendingContinuation is { IsResumable: false })
                return stop.OutcomeFromBlocker(state, "TERMINAL_STOP");

            if (state.PendingContinuation is
                {
                    Kind: ContinuationKind.VerificationGate,
                    VerificationContext: "baseline",
                    VerificationStage: VerificationContinuationStage.AwaitingConfirmation
                })
            {
                if (input.Confirmation == VerificationConfirmation.None)
                {
                    return stop.OutcomeFromBlocker(
                        state,
                        "VERIFICATION_CONFIRMATION_REQUIRED");
                }

                if (input.Confirmation == VerificationConfirmation.Decline)
                {
                    return await BlockAsync(
                        state,
                        new(
                            "VERIFICATION_DECLINED",
                            "User declined running Factory with an already-failing repository fallback baseline.",
                            "Fix the repository baseline, then use factory_restart to replace the run, or factory_cancel if no replacement run is wanted.",
                            new(
                                ContinuationKind.Terminal,
                                null,
                                "baseline",
                                "VERIFICATION_DECLINED",
                                false)),
                        cancellationToken);
                }

                state.RepositoryFallbackBaselineAccepted = true;
                state.PendingContinuation = null;
                state.Blocker = null;
                state.RunStatus = FactoryRunStatus.Running;
                await context.Events.WriteAsync(
                    state.RunId,
                    "repository-fallback-baseline-accepted",
                    new { },
                    cancellationToken);
                await context.SaveAsync(state, cancellationToken);
            }
            else if (state.PendingContinuation is
                {
                    Kind: ContinuationKind.VerificationGate,
                    VerificationStage: VerificationContinuationStage.AwaitingConfirmation
                        or VerificationContinuationStage.AwaitingManualResult
                } pending)
            {
                if (pending.VerificationStage == VerificationContinuationStage.AwaitingConfirmation
                    && input.Confirmation == VerificationConfirmation.None)
                {
                    return stop.OutcomeFromBlocker(
                        state,
                        "VERIFICATION_CONFIRMATION_REQUIRED");
                }

                if (pending.VerificationStage == VerificationContinuationStage.AwaitingManualResult
                    && input.VerificationPassed is null)
                {
                    return stop.OutcomeFromBlocker(
                        state,
                        "VERIFICATION_RESULT_REQUIRED");
                }

                var result = await verification.ResolvePendingActionAsync(
                    state,
                    input.Confirmation,
                    input.VerificationPassed,
                    cancellationToken);
                ApplyVerificationSnapshot(state, result);
                if (result.Block is not null)
                {
                    if (result.Block.Code == "VERIFICATION_DECLINED" && state.Current is not null)
                        state.CurrentPhase = CurrentWorkPhase.Blocked;
                    return await BlockAsync(state, result.Block, cancellationToken);
                }

                await PersistVerificationCursorAsync(state, result, cancellationToken);
            }
            else if (state.PendingContinuation is { IsResumable: true })
            {
                state.Blocker = null;
                state.RunStatus = FactoryRunStatus.Running;
                if (state.Current is not null && state.CurrentPhase == CurrentWorkPhase.Blocked)
                    state.CurrentPhase = CurrentWorkPhase.Ready;
                await context.SaveAsync(state, cancellationToken);
            }

            var baselineOutcome = await EnsureBaselineAsync(state, cancellationToken);
            if (baselineOutcome is not null)
                return baselineOutcome;
            return await RunCoreAsync(state, cancellationToken);
        }
        catch (AgentProtocolException exception)
        {
            return await BlockForAgentProtocolExceptionAsync(state, exception, cancellationToken);
        }
        catch (VerificationException exception)
        {
            return await BlockForVerificationExceptionAsync(state, exception, cancellationToken);
        }
    }

    public async Task<FactoryCliOutcome> RetryAsync(
        FactoryState state,
        int additionalAttempts,
        CancellationToken cancellationToken)
    {
        if (additionalAttempts < 1)
        {
            return new(
                "INVALID_RETRY_ATTEMPTS",
                state.RunId,
                "additionalAttempts must be at least 1.");
        }

        if (state.RunStatus != FactoryRunStatus.Blocked
            || state.Blocker?.Code != "RETRY_BUDGET_EXHAUSTED"
            || state.PendingContinuation is not { Kind: ContinuationKind.Terminal }
            || state.Current is null)
        {
            return new(
                "RETRY_NOT_AVAILABLE",
                state.RunId,
                "The current run is not blocked by an exhausted work-item retry budget.",
                "Use factory_continue for a resumable continuation, factory_restart to replace the run, or factory_cancel if no replacement run is wanted.");
        }

        var effectiveBudget = execution.EffectiveAttemptBudget(state.Current);
        var availableAttempts = execution.AvailableAdditionalAttempts(state.Current);
        if (additionalAttempts > availableAttempts)
        {
            return new(
                "INVALID_RETRY_ATTEMPTS",
                state.RunId,
                $"Only {availableAttempts} additional attempts are available; Factory permits at most 10 attempts per work item.");
        }

        if (state.Current.AttemptCount < effectiveBudget)
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                "Retry-budget exhaustion was recorded before the current work item consumed its effective attempt budget.");
        }

        state.Current.AdditionalAttemptBudget += additionalAttempts;
        state.CurrentPhase = CurrentWorkPhase.Ready;
        state.CurrentAttemptId = null;
        state.Current.CurrentAttemptId = null;
        state.PendingContinuation = null;
        state.Blocker = null;
        state.RunStatus = FactoryRunStatus.Running;
        await context.Events.WriteAsync(
            state.RunId,
            "retry-budget-extended",
            new
            {
                workItemId = state.Current.Id,
                additionalAttempts,
                effectiveAttemptBudget = effectiveBudget + additionalAttempts
            },
            cancellationToken);
        await context.SaveAsync(state, cancellationToken);
        return await RunAsync(state, cancellationToken);
    }

    public async Task<FactoryCliOutcome> CancelAsync(
        FactoryState state,
        CancellationToken cancellationToken)
    {
        if (state.RunStatus != FactoryRunStatus.Cancelled)
        {
            state.RunStatus = FactoryRunStatus.Cancelled;
            state.Blocker = new(
                "CANCELLED",
                "The user cancelled the run.",
                "Start a new Factory run.");
            state.PendingContinuation = new(
                ContinuationKind.Terminal,
                state.Current?.Id,
                null,
                "CANCELLED",
                false);
            await context.Events.WriteAsync(
                state.RunId,
                "run-cancelled",
                new { },
                cancellationToken);
            await context.SaveAsync(state, cancellationToken);
        }

        return new("CANCELLED", state.RunId);
    }

    private async Task<FactoryCliOutcome> RunCoreAsync(
        FactoryState state,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transition = await ExecuteAsync(state, cancellationToken);
            if (transition.Outcome is not null)
                return transition.Outcome;
        }
    }

    internal async Task<FactoryTransition> ExecuteAsync(
        FactoryState state,
        CancellationToken cancellationToken)
    {
        FactoryRuntimeState currentState;
        try
        {
            context.ValidateRuntimeState(state);
            currentState = ResolveState(state);
        }
        catch (AgentProtocolException exception)
        {
            var outcome = await BlockForAgentProtocolExceptionAsync(
                state,
                exception,
                cancellationToken);
            return new(
                FactoryRuntimeState.Blocked,
                FactoryRuntimeState.Blocked,
                exception.Code,
                outcome);
        }
        catch (VerificationException exception)
        {
            var outcome = await BlockForVerificationExceptionAsync(
                state,
                exception,
                cancellationToken);
            return new(
                FactoryRuntimeState.Blocked,
                FactoryRuntimeState.Blocked,
                exception.Code,
                outcome);
        }

        await WriteDecisionEventAsync(state, currentState, cancellationToken);

        try
        {
            return currentState switch
            {
                FactoryRuntimeState.Planning => await ExecutePlanningAsync(state, cancellationToken),
                FactoryRuntimeState.SelectingWork => await SelectNextWorkAsync(state, cancellationToken),
                FactoryRuntimeState.Executing => await ExecuteWorkAsync(state, cancellationToken),
                FactoryRuntimeState.Verifying => await ExecuteVerificationAsync(state, final: false, cancellationToken),
                FactoryRuntimeState.FinalVerifying => await ExecuteVerificationAsync(state, final: true, cancellationToken),
                FactoryRuntimeState.Finalizing => await ExecuteFinalizationAsync(state, cancellationToken),
                FactoryRuntimeState.Blocked => await ExecuteBlockedAsync(state, cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(currentState))
            };
        }
        catch (AgentProtocolException exception)
        {
            var outcome = await BlockForAgentProtocolExceptionAsync(
                state,
                exception,
                cancellationToken);
            return new(currentState, FactoryRuntimeState.Blocked, exception.Code, outcome);
        }
        catch (VerificationException exception)
        {
            var outcome = await BlockForVerificationExceptionAsync(
                state,
                exception,
                cancellationToken);
            return new(currentState, FactoryRuntimeState.Blocked, exception.Code, outcome);
        }
    }

    private async Task<FactoryTransition> ExecutePlanningAsync(
        FactoryState state,
        CancellationToken cancellationToken)
    {
        string input;
        string attemptId;
        if (state.CurrentAttemptId is { } currentAttempt
            && state.PendingContinuation is
            {
                Kind: ContinuationKind.SemanticInvocation,
                Operation: SemanticOperationKind.Planning
            } pending)
        {
            attemptId = currentAttempt;
            input = pending.OperationInput
                    ?? (await planning.PrepareAsync(state, cancellationToken)).Input
                    ?? throw new FactoryStateException(
                        "CORRUPT_FACTORY_STATE",
                        "Recovered planning attempt has no semantic input.");
        }
        else
        {
            var preparation = await planning.PrepareAsync(state, cancellationToken);
            if (preparation.ImmediateResult is { } immediate)
            {
                var outcome = await BlockAsync(
                    state,
                    new(
                        "PLANNING_BUDGET_EXHAUSTED",
                        immediate.Detail ?? "Factory planning-cycle budget exhausted.",
                        "Resolve the condition, then use factory_restart to replace the run, or factory_cancel if no replacement run is wanted.",
                        new(
                            ContinuationKind.Terminal,
                            null,
                            null,
                            "PLANNING_BUDGET_EXHAUSTED",
                            false)),
                    cancellationToken);
                return new(
                    FactoryRuntimeState.Planning,
                    FactoryRuntimeState.Blocked,
                    immediate.Reason,
                    outcome);
            }

            input = preparation.Input!;
            attemptId = await StartSemanticAttemptAsync(
                state,
                null,
                SemanticOperationKind.Planning,
                input,
                cancellationToken);
        }

        var semantic = await semanticExecution.InvokeAsync(
            state,
            "planning",
            null,
            input,
            attemptId,
            cancellationToken);
        CompleteSemanticAttempt(state, null, semantic);

        var result = planning.ParseResult(state, semantic.Result);
        if (result.Kind == PlanningResultKind.Question)
        {
            state.PlanningCycleCount++;
            state.PlannedThroughCompletedCount = state.Completed.Count;
            FactoryRuntimeContext.InvalidateFinalEvidence(state);
            var payload = JsonSerializer.SerializeToElement(
                new { question = result.Question },
                FactoryJson.Options);
            var outcome = await BlockAsync(
                state,
                new(
                    "USER_DECISION_REQUIRED",
                    result.Question!,
                    "Answer the planner question to continue this run, or cancel the Factory run.",
                    new(
                        ContinuationKind.UserQuestion,
                        null,
                        null,
                        "USER_DECISION_REQUIRED",
                        true),
                    payload),
                cancellationToken);
            return new(
                FactoryRuntimeState.Planning,
                FactoryRuntimeState.Blocked,
                "planner-requested-user-decision",
                outcome);
        }

        await ApplyPlanAsync(
            state,
            result.Tasks,
            result.Reason,
            result.AttemptId!,
            cancellationToken);
        return TransitionFromCurrent(
            FactoryRuntimeState.Planning,
            state,
            "planning-completed");
    }

    private async Task ApplyPlanAsync(
        FactoryState state,
        IReadOnlyList<PlannerTaskDefinition> tasks,
        string reason,
        string sourceAttemptId,
        CancellationToken cancellationToken)
    {
        var previous = context.CloneState(state);
        var prepared = await planMutation.PrepareAsync(state, tasks, cancellationToken);

        state.Current = null;
        state.CurrentPhase = null;
        state.Remaining.Clear();
        state.Remaining.AddRange(prepared.WorkItems);
        state.NextWorkItemNumber = prepared.NextWorkItemNumber;
        state.PlanningCycleCount++;
        state.PlannedThroughCompletedCount = state.Completed.Count;
        state.PendingContinuation = null;
        state.Blocker = null;
        state.RunStatus = FactoryRunStatus.Running;
        state.PlanRevision++;
        FactoryRuntimeContext.InvalidateFinalEvidence(state);
        context.ValidateRuntimeState(state);

        await planMutation.WriteRevisionAsync(
            previous,
            state,
            reason,
            sourceAttemptId,
            cancellationToken);
        await context.SaveAsync(state, cancellationToken);
    }

    private async Task<FactoryTransition> SelectNextWorkAsync(
        FactoryState state,
        CancellationToken cancellationToken)
    {
        if (state.Current is not null || state.Remaining.Count == 0)
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                "Cannot select next work from the current state.");
        }

        state.Current = state.Remaining[0];
        state.Remaining.RemoveAt(0);
        state.CurrentPhase = CurrentWorkPhase.Ready;
        await context.SaveAsync(state, cancellationToken);
        return TransitionFromCurrent(
            FactoryRuntimeState.SelectingWork,
            state,
            "work-selected");
    }

    private async Task<FactoryTransition> ExecuteWorkAsync(
        FactoryState state,
        CancellationToken cancellationToken)
    {
        var item = state.Current
            ?? throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                "Execution requires Current work.");

        string input;
        string attemptId;
        bool verificationDrivenRetry;
        if (state.CurrentAttemptId is { } currentAttempt
            && state.PendingContinuation is
            {
                Kind: ContinuationKind.SemanticInvocation,
                Operation: SemanticOperationKind.WorkItemExecution
            } pending)
        {
            attemptId = currentAttempt;
            var preparation = await execution.PrepareAsync(state, item.Id, cancellationToken);
            if (preparation.Kind == FactoryExecutionResultKind.RetryBudgetExhausted)
            {
                return await RetryBudgetExhaustedAsync(state, item, preparation.Detail, cancellationToken);
            }
            input = pending.OperationInput ?? preparation.Input!;
            verificationDrivenRetry = preparation.VerificationDrivenRetry;
        }
        else
        {
            var preparation = await execution.PrepareAsync(state, item.Id, cancellationToken);
            if (preparation.Kind == FactoryExecutionResultKind.RetryBudgetExhausted)
            {
                return await RetryBudgetExhaustedAsync(state, item, preparation.Detail, cancellationToken);
            }

            input = preparation.Input!;
            verificationDrivenRetry = preparation.VerificationDrivenRetry;
            attemptId = await StartSemanticAttemptAsync(
                state,
                item,
                SemanticOperationKind.WorkItemExecution,
                input,
                cancellationToken);
        }

        SemanticExecutionResult semantic;
        try
        {
            semantic = await semanticExecution.InvokeAsync(
                state,
                "implementation",
                item,
                input,
                attemptId,
                cancellationToken);
        }
        catch (AgentProtocolException exception) when (
            TechnicalFailureClassifier.Classify(
                SemanticOperationKind.WorkItemExecution,
                exception) == TechnicalFailureClassification.RestartableTechnicalFailure)
        {
            var failedChangedPaths = await semanticExecution.RecoverFailedAttemptWorkspaceChangesAsync(
                state,
                item,
                attemptId,
                CancellationToken.None);
            ApplyChangedPaths(state, item, failedChangedPaths);

            var diagnosticReference = await execution.PersistCommandFailureDiagnosticAsync(
                attemptId,
                exception,
                CancellationToken.None);
            var boundedMessage = exception.Message.Length <= 4096
                ? exception.Message
                : exception.Message[..4096] + " [truncated]";
            var failure = new TechnicalFailureDiagnostic(
                attemptId,
                exception.Code,
                diagnosticReference,
                boundedMessage,
                item.SemanticAttemptCount,
                failedChangedPaths.ToList());
            if (!item.PriorTechnicalFailures.Any(x => x.FailedAttemptId == attemptId))
                item.PriorTechnicalFailures.Add(failure);
            if (!item.PriorAttemptDiagnosticRefs.Contains(
                    diagnosticReference,
                    StringComparer.Ordinal))
            {
                item.PriorAttemptDiagnosticRefs.Add(diagnosticReference);
            }

            item.NextInvocationKind = WorkItemInvocationKind.TechnicalRestart;
            state.CurrentAttemptId = null;
            item.CurrentAttemptId = null;
            state.PendingContinuation = null;
            state.Blocker = null;
            state.RunStatus = FactoryRunStatus.Running;
            state.CurrentPhase = CurrentWorkPhase.Ready;

            var technicalRestartBudget = context.Configuration.Limits.MaxTechnicalRestartsPerTask;
            if (item.TechnicalRestartCount >= technicalRestartBudget)
            {
                await context.Events.WriteAsync(
                    state.RunId,
                    "technical-restart-budget-exhausted",
                    new
                    {
                        workItemId = item.Id,
                        failedAttemptId = attemptId,
                        failureCode = exception.Code,
                        diagnosticReference,
                        technicalRestartCount = item.TechnicalRestartCount,
                        technicalRestartBudget
                    },
                    CancellationToken.None);
                var budgetException = new AgentProtocolException(
                    "TECHNICAL_RESTART_BUDGET_EXHAUSTED",
                    $"Work item {item.Id} exhausted its technical restart budget ({item.TechnicalRestartCount}/{technicalRestartBudget}) after {exception.Code} in {attemptId}. Diagnostic: {diagnosticReference}.");
                var outcome = await BlockAsync(
                    state,
                    stop.FromAgentProtocolException(state, budgetException),
                    CancellationToken.None);
                return new(
                    FactoryRuntimeState.Executing,
                    FactoryRuntimeState.Blocked,
                    "technical-restart-budget-exhausted",
                    outcome);
            }

            await context.Events.WriteAsync(
                state.RunId,
                "technical-restart-scheduled",
                new
                {
                    workItemId = item.Id,
                    failedAttemptId = attemptId,
                    failureCode = exception.Code,
                    semanticAttemptNumber = item.SemanticAttemptCount,
                    technicalRestartNumber = item.TechnicalRestartCount + 1,
                    technicalRestartBudget,
                    diagnosticReference
                },
                CancellationToken.None);
            await context.SaveAsync(state, CancellationToken.None);
            return TransitionFromCurrent(
                FactoryRuntimeState.Executing,
                state,
                "technical-restart-scheduled");
        }

        var verificationRetryMadeProgress = semantic.ChangedPaths.Count != 0
            || item.PriorTechnicalFailures.Any(x =>
                x.SemanticAttemptNumber == item.SemanticAttemptCount
                && x.ChangedPaths.Count != 0);

        CompleteSemanticAttempt(state, item, semantic);
        item.LastResultRef = semantic.Result.SemanticResultPath;

        if (verificationDrivenRetry && !verificationRetryMadeProgress)
        {
            state.CurrentPhase = CurrentWorkPhase.Blocked;
            var outcome = await BlockAsync(
                state,
                new(
                    "VERIFICATION_RETRY_NO_PROGRESS",
                    $"Work item {item.Id} was retried because authoritative verification failed, but retry attempt {attemptId} produced no workspace changes.",
                    "Inspect the verification evidence and executor result, resolve the condition, then use factory_restart to replace the run, or factory_cancel if no replacement run is wanted.",
                    new(
                        ContinuationKind.Terminal,
                        item.Id,
                        "subtask",
                        "VERIFICATION_RETRY_NO_PROGRESS",
                        false)),
                cancellationToken);
            return new(
                FactoryRuntimeState.Executing,
                FactoryRuntimeState.Blocked,
                "verification-retry-no-progress",
                outcome);
        }

        state.PendingContinuation = null;
        state.Blocker = null;
        state.CurrentPhase = CurrentWorkPhase.AwaitingVerification;
        await context.SaveAsync(state, cancellationToken);
        return TransitionFromCurrent(
            FactoryRuntimeState.Executing,
            state,
            "execution-completed");
    }

    private async Task<FactoryTransition> RetryBudgetExhaustedAsync(
        FactoryState state,
        PlannedWorkItem item,
        string? detail,
        CancellationToken cancellationToken)
    {
        var outcome = await BlockAsync(
            state,
            new(
                "RETRY_BUDGET_EXHAUSTED",
                detail ?? $"{item.Id} exhausted its semantic attempt budget.",
                "Resolve the condition, then call factory_retry with additional attempts (maximum 10 total), use factory_restart to replace the run, or factory_cancel if no replacement run is wanted.",
                new(
                    ContinuationKind.Terminal,
                    item.Id,
                    null,
                    "RETRY_BUDGET_EXHAUSTED",
                    false)),
            cancellationToken);
        return new(
            FactoryRuntimeState.Executing,
            FactoryRuntimeState.Blocked,
            "retry-budget-exhausted",
            outcome);
    }

    private async Task<FactoryTransition> ExecuteVerificationAsync(
        FactoryState state,
        bool final,
        CancellationToken cancellationToken)
    {
        var verificationContext = final ? "final" : "subtask";
        var workItemId = final ? null : state.Current?.Id;
        var result = await verification.RunAsync(
            state,
            workItemId,
            verificationContext,
            cancellationToken);
        ApplyVerificationSnapshot(state, result);

        if (result.Block is not null)
        {
            var outcome = await BlockAsync(state, result.Block, cancellationToken);
            return new(
                final ? FactoryRuntimeState.FinalVerifying : FactoryRuntimeState.Verifying,
                FactoryRuntimeState.Blocked,
                result.Block.Code,
                outcome);
        }

        if (result.Decision == VerificationDecision.None)
        {
            await PersistVerificationCursorAsync(state, result, cancellationToken);
            return TransitionFromCurrent(
                final ? FactoryRuntimeState.FinalVerifying : FactoryRuntimeState.Verifying,
                state,
                "verification-checkpoint");
        }

        await ApplyVerificationResultAsync(state, result, cancellationToken);
        return TransitionFromCurrent(
            final ? FactoryRuntimeState.FinalVerifying : FactoryRuntimeState.Verifying,
            state,
            result.Decision.ToString());
    }

    private void ApplyVerificationSnapshot(
        FactoryState state,
        FactoryVerificationStepResult result)
    {
        state.PendingVerificationSession = result.Session;
        if (result.EvidenceRefs is not null)
        {
            state.VerificationEvidenceRefs.Clear();
            state.VerificationEvidenceRefs.AddRange(result.EvidenceRefs);
        }

        if (result.WorkItemId is not null
            && state.Current is { } item
            && item.Id == result.WorkItemId)
        {
            if (result.WorkItemEvidenceRefs is not null)
            {
                item.VerificationEvidenceRefs.Clear();
                item.VerificationEvidenceRefs.AddRange(result.WorkItemEvidenceRefs);
            }
            if (result.LastWorkItemEvidenceRefs is not null)
            {
                item.LastVerificationEvidenceRefs.Clear();
                item.LastVerificationEvidenceRefs.AddRange(result.LastWorkItemEvidenceRefs);
            }
        }
    }

    private async Task PersistVerificationCursorAsync(
        FactoryState state,
        FactoryVerificationStepResult result,
        CancellationToken cancellationToken)
    {
        if (result.Session is null)
        {
            state.PendingContinuation = null;
        }
        else
        {
            state.PendingContinuation = new(
                ContinuationKind.VerificationGate,
                result.WorkItemId,
                result.Context,
                "VERIFICATION_GATE",
                true,
                VerificationCheckId: result.Session.PendingCheckId,
                VerificationStage: result.Session.Stage);
        }
        state.Blocker = null;
        state.RunStatus = FactoryRunStatus.Running;
        await context.SaveAsync(state, cancellationToken);
    }

    private async Task ApplyVerificationResultAsync(
        FactoryState state,
        FactoryVerificationStepResult result,
        CancellationToken cancellationToken)
    {
        var item = result.WorkItemId is null
            ? null
            : state.Current is { } current && current.Id == result.WorkItemId
                ? current
                : throw new FactoryStateException(
                    "CORRUPT_FACTORY_STATE",
                    "Verification result no longer targets Current work.");
        if (item is not null)
            item.LastVerificationDecision = result.Decision;
        state.PendingVerificationSession = null;

        if (result.Decision is VerificationDecision.Ok or VerificationDecision.ExpectedFailure)
        {
            state.PendingContinuation = null;
            state.Blocker = null;
            state.RunStatus = FactoryRunStatus.Running;
            if (item is not null)
                await CommitCurrentAsync(state, cancellationToken);
            else
            {
                state.FinalVerificationPassed = true;
                state.FinalVerificationPlanRevision = state.PlanRevision;
                await context.SaveAsync(state, cancellationToken);
            }
        }
        else if (item is not null)
        {
            if (item.LastResultRef is not null
                && !item.PriorResultRefs.Contains(item.LastResultRef, StringComparer.Ordinal))
            {
                item.PriorResultRefs.Add(item.LastResultRef);
            }
            state.CurrentPhase = CurrentWorkPhase.Ready;
            state.PendingContinuation = null;
            state.Blocker = null;
            state.RunStatus = FactoryRunStatus.Running;
            await context.SaveAsync(state, cancellationToken);
        }
        else
        {
            state.FinalVerificationPassed = false;
            state.FinalVerificationPlanRevision = state.PlanRevision;
            state.RunStatus = FactoryRunStatus.Running;
            state.Blocker = null;
            state.PendingContinuation = null;
            await context.SaveAsync(state, cancellationToken);
        }

        await context.Events.WriteAsync(
            state.RunId,
            "verification-decision",
            new
            {
                context = result.Context,
                workItemId = item?.Id,
                decision = result.Decision,
                failedCheckIds = result.FailedCheckIds
            },
            cancellationToken);
    }

    private async Task CommitCurrentAsync(
        FactoryState state,
        CancellationToken cancellationToken)
    {
        var item = state.Current
            ?? throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                "Completion requires Current work.");
        state.Completed.Add(new CompletedWorkItem
        {
            Id = item.Id,
            ContractPath = item.ContractPath,
            TaskRelatedIntentIds = item.TaskRelatedIntentIds.ToList(),
            RelevantCompletedWorkIds = item.RelevantCompletedWorkIds.ToList(),
            ResultRef = item.LastResultRef,
            ChangedPaths = item.ChangedPaths.ToList(),
            VerificationEvidenceRefs = item.VerificationEvidenceRefs.ToList(),
            VerificationDecision = item.LastVerificationDecision
        });
        state.Current = null;
        state.CurrentPhase = null;
        state.PlanRevision++;
        FactoryRuntimeContext.InvalidateFinalEvidence(state);
        await context.SaveAsync(state, cancellationToken);
    }

    private async Task<FactoryTransition> ExecuteFinalizationAsync(
        FactoryState state,
        CancellationToken cancellationToken)
    {
        EnsureFinalizationPreconditions(state);
        var resultDirectory = await new FinalizeHandler(context.Workspace)
            .FinalizeAsync(state, cancellationToken);
        return new(
            FactoryRuntimeState.Finalizing,
            FactoryRuntimeState.Finalizing,
            "finalized",
            new(
                "COMPLETED",
                state.RunId,
                ResultDirectory: resultDirectory));
    }

    private static void EnsureFinalizationPreconditions(FactoryState state)
    {
        if (state.Current is not null || state.Remaining.Count != 0)
        {
            throw new FactoryStateException(
                "FINAL_VERIFICATION_FAILED",
                "Future work remains incomplete.");
        }
        if (state.CurrentAttemptId is not null
            || state.PendingVerificationSession is not null
            || state.PendingContinuation is not null)
        {
            throw new FactoryStateException(
                "FINAL_VERIFICATION_FAILED",
                "An operation is still active.");
        }
        if (!state.FinalVerificationPassed
            || state.FinalVerificationPlanRevision != state.PlanRevision)
        {
            throw new FactoryStateException(
                "FINAL_VERIFICATION_FAILED",
                "Strict final verification is stale or missing.");
        }
    }

    private async Task<FactoryTransition> ExecuteBlockedAsync(
        FactoryState state,
        CancellationToken cancellationToken)
    {
        if (state.Blocker is not null)
        {
            return new(
                FactoryRuntimeState.Blocked,
                FactoryRuntimeState.Blocked,
                state.Blocker.Code,
                stop.OutcomeFromBlocker(state, state.Blocker.Code));
        }

        var outcome = await BlockAsync(
            state,
            new(
                "FACTORY_BLOCKED",
                "No deterministic action is applicable.",
                "Resolve the blocker, then use factory_restart to replace the run, or factory_cancel if no replacement run is wanted.",
                new(
                    ContinuationKind.Terminal,
                    state.Current?.Id,
                    null,
                    "FACTORY_BLOCKED",
                    false)),
            cancellationToken);
        return new(
            FactoryRuntimeState.Blocked,
            FactoryRuntimeState.Blocked,
            "FACTORY_BLOCKED",
            outcome);
    }

    private async Task<string> StartSemanticAttemptAsync(
        FactoryState state,
        PlannedWorkItem? item,
        SemanticOperationKind operation,
        string input,
        CancellationToken cancellationToken)
    {
        var attemptId = $"A{++state.AttemptSequence:000000}";
        state.CurrentAttemptId = attemptId;
        if (item is not null)
        {
            item.CurrentAttemptId = attemptId;
            item.AttemptCount++;
            state.CurrentPhase = CurrentWorkPhase.Running;
        }
        state.PendingContinuation = new(
            ContinuationKind.SemanticInvocation,
            item?.Id,
            null,
            operation.ToString().ToUpperInvariant(),
            true,
            operation,
            input);
        await context.SaveAsync(state, cancellationToken);
        return attemptId;
    }

    private static void CompleteSemanticAttempt(
        FactoryState state,
        PlannedWorkItem? item,
        SemanticExecutionResult result)
    {
        ApplyChangedPaths(state, item, result.ChangedPaths);
        state.CurrentAttemptId = null;
        if (item is not null)
            item.CurrentAttemptId = null;
        state.PendingContinuation = null;
    }

    private async Task ReconcileSemanticAttemptAsync(
        FactoryState state,
        CancellationToken cancellationToken)
    {
        var recovery = await semanticExecution.InspectRecoveryAsync(state, cancellationToken);
        if (recovery.Kind == SemanticAttemptRecoveryKind.NoPendingAttempt)
            return;

        var attemptId = recovery.AttemptId
            ?? throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                "Semantic recovery result has no attempt identity.");
        state.CurrentAttemptId = attemptId;
        if (state.Current is not null)
            state.Current.CurrentAttemptId = attemptId;
        ApplyChangedPaths(state, state.Current, recovery.ChangedPaths ?? []);

        switch (recovery.Kind)
        {
            case SemanticAttemptRecoveryKind.InvocationNeverStarted:
                if (state.Current is { } neverStarted)
                {
                    neverStarted.AttemptCount = Math.Max(0, neverStarted.AttemptCount - 1);
                    neverStarted.CurrentAttemptId = null;
                    state.CurrentPhase = CurrentWorkPhase.Ready;
                }
                state.CurrentAttemptId = null;
                state.PendingContinuation = null;
                state.Blocker = null;
                state.RunStatus = FactoryRunStatus.Running;
                await context.SaveAsync(state, cancellationToken);
                break;

            case SemanticAttemptRecoveryKind.InterruptedWithoutResult:
                if (state.Current is { } interrupted)
                {
                    interrupted.CurrentAttemptId = null;
                    state.CurrentPhase = CurrentWorkPhase.Ready;
                }
                state.CurrentAttemptId = null;
                state.PendingContinuation = null;
                state.Blocker = null;
                state.RunStatus = FactoryRunStatus.Running;
                await context.SaveAsync(state, cancellationToken);
                break;

            case SemanticAttemptRecoveryKind.CompletedResultAvailable:
            case SemanticAttemptRecoveryKind.RecoverableSemanticOutput:
                var existingInput = state.PendingContinuation?.OperationInput;
                state.PendingContinuation = new(
                    ContinuationKind.SemanticInvocation,
                    state.Current?.Id,
                    null,
                    recovery.Operation.ToString(),
                    true,
                    recovery.Operation,
                    existingInput);
                await context.SaveAsync(state, cancellationToken);
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static void ApplyChangedPaths(
        FactoryState state,
        PlannedWorkItem? item,
        IEnumerable<string> changedPaths)
    {
        foreach (var path in changedPaths)
        {
            if (item is not null && !item.ChangedPaths.Contains(path, StringComparer.Ordinal))
                item.ChangedPaths.Add(path);
            if (!state.FactoryRunChangedPaths.Contains(path, StringComparer.Ordinal))
                state.FactoryRunChangedPaths.Add(path);
        }
    }

    private async Task<FactoryCliOutcome?> EnsureBaselineAsync(
        FactoryState state,
        CancellationToken cancellationToken)
    {
        if (state.PlanningCycleCount != 0 || state.Current is not null)
            return null;
        if (state.PendingContinuation is
            {
                Kind: ContinuationKind.VerificationGate,
                VerificationContext: "baseline"
            })
        {
            return stop.OutcomeFromBlocker(state, "VERIFICATION_CONFIRMATION_REQUIRED");
        }

        var result = await verification.RunBaselineAsync(state, cancellationToken);
        var changed = false;
        foreach (var reference in result.EvidenceRefs)
        {
            if (!state.VerificationEvidenceRefs.Contains(reference, StringComparer.Ordinal))
            {
                state.VerificationEvidenceRefs.Add(reference);
                changed = true;
            }
        }

        if (result.Block is not null)
            return await BlockAsync(state, result.Block, cancellationToken);
        if (changed)
            await context.SaveAsync(state, cancellationToken);
        return null;
    }

    private async Task<FactoryCliOutcome> BlockForAgentProtocolExceptionAsync(
        FactoryState state,
        AgentProtocolException exception,
        CancellationToken cancellationToken)
    {
        if (exception.Code == "AGENT_TRANSPORT_FAILURE"
            && state.CurrentAttemptId is { } attemptId
            && state.Current is { AttemptCount: > 0 } current
            && current.CurrentAttemptId == attemptId)
        {
            current.AttemptCount--;
        }
        return await BlockAsync(
            state,
            stop.FromAgentProtocolException(state, exception),
            cancellationToken);
    }

    private Task<FactoryCliOutcome> BlockForVerificationExceptionAsync(
        FactoryState state,
        VerificationException exception,
        CancellationToken cancellationToken) =>
        BlockAsync(
            state,
            new(
                exception.Code,
                exception.Message,
                "Resolve verification configuration, then continue.",
                state.PendingContinuation
                ?? new(
                    ContinuationKind.Terminal,
                    state.Current?.Id,
                    null,
                    exception.Code,
                    false)),
            cancellationToken);

    private async Task<FactoryCliOutcome> BlockAsync(
        FactoryState state,
        FactoryBlockResult block,
        CancellationToken cancellationToken)
    {
        state.RunStatus = FactoryRunStatus.Blocked;
        state.Blocker = new(
            block.Code,
            block.Reason,
            block.ResumeWhen,
            block.Payload);
        state.PendingContinuation = block.Continuation;
        await context.SaveAsync(state, cancellationToken);
        return stop.Outcome(state, block);
    }

    private FactoryTransition TransitionFromCurrent(
        FactoryRuntimeState from,
        FactoryState state,
        string reason) =>
        new(from, ResolveState(state), reason);

    private Task WriteDecisionEventAsync(
        FactoryState state,
        FactoryRuntimeState runtimeState,
        CancellationToken cancellationToken)
    {
        var kind = ToDiagnosticCommand(runtimeState, state);
        var verificationContext = runtimeState == FactoryRuntimeState.FinalVerifying
            ? "final"
            : runtimeState == FactoryRuntimeState.Verifying
                ? "subtask"
                : state.PendingContinuation?.VerificationContext;
        return context.Events.WriteAsync(
            state.RunId,
            "scheduler-decision",
            new
            {
                Kind = kind,
                WorkItemId = state.Current?.Id,
                VerificationContext = verificationContext,
                state.PlanRevision
            },
            cancellationToken);
    }

    private static FactoryCommandKind ToDiagnosticCommand(
        FactoryRuntimeState runtimeState,
        FactoryState state) =>
        runtimeState switch
        {
            FactoryRuntimeState.Planning => state.PendingContinuation is
                { Kind: ContinuationKind.SemanticInvocation }
                ? FactoryCommandKind.ResumePendingOperation
                : FactoryCommandKind.Plan,
            FactoryRuntimeState.SelectingWork => FactoryCommandKind.SelectNextWork,
            FactoryRuntimeState.Executing => state.PendingContinuation is
                { Kind: ContinuationKind.SemanticInvocation }
                ? FactoryCommandKind.ResumePendingOperation
                : FactoryCommandKind.DispatchWork,
            FactoryRuntimeState.Verifying => FactoryCommandKind.RunVerification,
            FactoryRuntimeState.FinalVerifying => FactoryCommandKind.RunFinalVerification,
            FactoryRuntimeState.Finalizing => FactoryCommandKind.Finalize,
            FactoryRuntimeState.Blocked => FactoryCommandKind.StopBlocked,
            _ => throw new ArgumentOutOfRangeException(nameof(runtimeState))
        };
}
