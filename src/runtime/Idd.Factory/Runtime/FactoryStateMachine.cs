using System.Text.Json;
using Idd.Factory.Domain;
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

// Kept as the diagnostic event vocabulary consumed by the MCP progress monitor.
// Control-flow decisions are made by FactoryStateMachine, not by a scheduler.
internal enum FactoryCommandKind
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

internal sealed class FactoryStateMachine(
    FactoryRuntimeContext context,
    PlanningService planning,
    PlanMutationService planMutation,
    ExecutionService execution,
    RuntimeVerificationService verification,
    FinalizationService finalization,
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
                ContinuationKind.VerificationGate => continuation.VerificationContext == "final"
                    ? FactoryRuntimeState.FinalVerifying
                    : FactoryRuntimeState.Verifying,
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
            var outcome = await stop.StopForAgentProtocolExceptionAsync(state, exception, cancellationToken);
            return new(FactoryRuntimeState.Blocked, FactoryRuntimeState.Blocked, exception.Code, outcome);
        }
        catch (VerificationException exception)
        {
            var outcome = await stop.StopAsync(
                state,
                exception.Code,
                exception.Message,
                "Resolve verification configuration, then continue.",
                cancellationToken);
            return new(FactoryRuntimeState.Blocked, FactoryRuntimeState.Blocked, exception.Code, outcome);
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
            var outcome = await stop.StopForAgentProtocolExceptionAsync(state, exception, cancellationToken);
            return new(currentState, FactoryRuntimeState.Blocked, exception.Code, outcome);
        }
        catch (VerificationException exception)
        {
            var outcome = await stop.StopAsync(
                state,
                exception.Code,
                exception.Message,
                "Resolve verification configuration, then continue.",
                cancellationToken,
                state.PendingContinuation);
            return new(currentState, FactoryRuntimeState.Blocked, exception.Code, outcome);
        }
    }

    private async Task<FactoryTransition> ExecutePlanningAsync(
        FactoryState state,
        CancellationToken cancellationToken)
    {
        var result = await planning.PlanAsync(state, cancellationToken);
        if (result.Kind == PlanningResultKind.Question)
        {
            state.PlanningCycleCount++;
            state.PlannedThroughCompletedCount = state.Completed.Count;
            FactoryRuntimeContext.InvalidateFinalEvidence(state);
            var payload = JsonSerializer.SerializeToElement(
                new { question = result.Question },
                FactoryJson.Options);
            var outcome = await stop.ApplyAsync(
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

        await planMutation.ApplyAsync(
            state,
            result.Tasks,
            result.Reason,
            result.AttemptId,
            cancellationToken);
        return TransitionFromCurrent(
            FactoryRuntimeState.Planning,
            state,
            "planning-completed");
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
        var result = await execution.ExecuteAsync(state, item.Id, cancellationToken);

        if (result.Kind == FactoryExecutionResultKind.VerificationRetryNoProgress)
        {
            state.CurrentPhase = CurrentWorkPhase.Blocked;
            var outcome = await stop.ApplyAsync(
                state,
                new(
                    "VERIFICATION_RETRY_NO_PROGRESS",
                    $"Work item {item.Id} was retried because authoritative verification failed, but retry attempt {result.AttemptId} produced no workspace changes.",
                    "Inspect the verification evidence and executor result, resolve the condition, then cancel/restart the Factory run.",
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

        if (result.Kind == FactoryExecutionResultKind.Completed)
        {
            state.PendingContinuation = null;
            state.Blocker = null;
            state.CurrentPhase = CurrentWorkPhase.AwaitingVerification;
            await context.SaveAsync(state, cancellationToken);
        }

        return TransitionFromCurrent(
            FactoryRuntimeState.Executing,
            state,
            result.Kind == FactoryExecutionResultKind.Retry
                ? "execution-retry"
                : "execution-completed");
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

        if (result.Block is not null)
        {
            var outcome = await stop.ApplyAsync(state, result.Block, cancellationToken);
            return new(
                final ? FactoryRuntimeState.FinalVerifying : FactoryRuntimeState.Verifying,
                FactoryRuntimeState.Blocked,
                result.Block.Code,
                outcome);
        }

        await ApplyVerificationResultAsync(state, result, cancellationToken);
        return TransitionFromCurrent(
            final ? FactoryRuntimeState.FinalVerifying : FactoryRuntimeState.Verifying,
            state,
            result.Decision.ToString());
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
        var outcome = await finalization.FinalizeAsync(state, cancellationToken);
        return new(
            FactoryRuntimeState.Finalizing,
            FactoryRuntimeState.Finalizing,
            "finalized",
            outcome);
    }

    private async Task<FactoryTransition> ExecuteBlockedAsync(
        FactoryState state,
        CancellationToken cancellationToken)
    {
        var outcome = await stop.StopBlockedAsync(state, cancellationToken);
        return new(
            FactoryRuntimeState.Blocked,
            FactoryRuntimeState.Blocked,
            state.Blocker?.Code ?? "FACTORY_BLOCKED",
            outcome);
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
            FactoryRuntimeState.Planning => state.PendingContinuation is { Kind: ContinuationKind.SemanticInvocation }
                ? FactoryCommandKind.ResumePendingOperation
                : FactoryCommandKind.Plan,
            FactoryRuntimeState.SelectingWork => FactoryCommandKind.SelectNextWork,
            FactoryRuntimeState.Executing => state.PendingContinuation is { Kind: ContinuationKind.SemanticInvocation }
                ? FactoryCommandKind.ResumePendingOperation
                : FactoryCommandKind.DispatchWork,
            FactoryRuntimeState.Verifying => FactoryCommandKind.RunVerification,
            FactoryRuntimeState.FinalVerifying => FactoryCommandKind.RunFinalVerification,
            FactoryRuntimeState.Finalizing => FactoryCommandKind.Finalize,
            FactoryRuntimeState.Blocked => FactoryCommandKind.StopBlocked,
            _ => throw new ArgumentOutOfRangeException(nameof(runtimeState))
        };
}
