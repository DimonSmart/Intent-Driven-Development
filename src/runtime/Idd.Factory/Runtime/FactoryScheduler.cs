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
    RunFinalReview,
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

        if (!state.InitialPlanningCompleted || state.PendingReplanTrigger is not null) return new(FactoryCommandKind.Plan);
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

        var finalVerified = state.FinalVerificationPassed && state.FinalVerificationPlanRevision == state.PlanRevision;
        var finalReviewed = state.FinalReview is { Verdict: "approved", ReviewedPlanRevision: not null } review && review.ReviewedPlanRevision == state.PlanRevision;
        if (!finalVerified) return new(FactoryCommandKind.RunFinalVerification, VerificationContext: "final");
        if (!finalReviewed) return new(FactoryCommandKind.RunFinalReview);
        return new(FactoryCommandKind.Finalize);
    }
}
