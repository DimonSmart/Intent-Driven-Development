using Idd.Factory.Domain;

namespace Idd.Factory.Runtime;

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

public sealed record FactoryCommand(FactoryCommandKind Kind, string? WorkItemId = null, string? VerificationContext = null);

public sealed class FactoryScheduler
{
    public FactoryCommand Decide(FactoryState state)
    {
        if (state.PendingContinuation is { IsResumable: false }) return new(FactoryCommandKind.StopBlocked);
        if (state.PendingContinuation is { } continuation)
        {
            return continuation.Kind switch
            {
                ContinuationKind.VerificationGate => new(FactoryCommandKind.RunVerification, continuation.WorkItemId, continuation.VerificationContext),
                ContinuationKind.SemanticInvocation => new(FactoryCommandKind.ResumePendingOperation, continuation.WorkItemId),
                _ => new(FactoryCommandKind.StopBlocked)
            };
        }

        if (state.PlanningCycleCount == 0) return new(FactoryCommandKind.Plan);
        if (state.Current is { } current)
        {
            return state.CurrentPhase switch
            {
                CurrentWorkPhase.AwaitingVerification => new(FactoryCommandKind.RunVerification, current.Id, "subtask"),
                CurrentWorkPhase.Ready or CurrentWorkPhase.Running => new(FactoryCommandKind.DispatchWork, current.Id),
                _ => new(FactoryCommandKind.StopBlocked)
            };
        }
        if (state.Remaining.Count > 0) return new(FactoryCommandKind.SelectNextWork);
        if (state.Completed.Count > state.PlannedThroughCompletedCount) return new(FactoryCommandKind.Plan);

        var finalVerificationIsCurrent = state.FinalVerificationPlanRevision == state.PlanRevision;
        if (finalVerificationIsCurrent && !state.FinalVerificationPassed) return new(FactoryCommandKind.Plan);
        if (!finalVerificationIsCurrent) return new(FactoryCommandKind.RunFinalVerification, VerificationContext: "final");
        return new(FactoryCommandKind.Finalize);
    }
}
