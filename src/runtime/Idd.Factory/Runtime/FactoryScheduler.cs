using Idd.Factory.Domain;

namespace Idd.Factory.Runtime;

public enum FactoryCommandKind
{
    Decompose,
    ResumePendingOperation,
    RunVerification,
    RefineWork,
    DispatchWork,
    RunGlobalReplan,
    RunFinalVerification,
    CreateFinalReview,
    Finalize,
    StopBlocked
}

public sealed record FactoryCommand(FactoryCommandKind Kind, string? WorkItemId = null, string? VerificationContext = null);

/// <summary>
/// Pure deterministic scheduler over persisted Factory state. It never asks an LLM what runtime phase comes next.
/// </summary>
public sealed class FactoryScheduler
{
    public FactoryCommand Decide(FactoryState state)
    {
        if (state.PendingContinuation is { IsResumable: false })
            return new(FactoryCommandKind.StopBlocked);

        if (state.PendingContinuation is { } continuation)
        {
            return continuation.Kind switch
            {
                ContinuationKind.VerificationGate => new(FactoryCommandKind.RunVerification, continuation.WorkItemId, continuation.VerificationContext),
                ContinuationKind.SemanticInvocation => new(FactoryCommandKind.ResumePendingOperation, continuation.WorkItemId),
                ContinuationKind.IntentGate or ContinuationKind.Clarification or ContinuationKind.Terminal => new(FactoryCommandKind.StopBlocked),
                _ => new(FactoryCommandKind.StopBlocked)
            };
        }

        if (state.GraphRevision == 0)
            return new(FactoryCommandKind.Decompose);

        if (state.PendingReplanTrigger is not null)
            return new(FactoryCommandKind.RunGlobalReplan);

        var ordered = state.WorkItems
            .OrderBy(item => item.Sequence)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();

        var verification = ordered.FirstOrDefault(item => item.Status == WorkItemStatus.AwaitingVerification);
        if (verification is not null)
            return new(FactoryCommandKind.RunVerification, verification.Id, "subtask");

        // Product work and scoped refinement take precedence over terminal review work.
        var runnable = ordered.FirstOrDefault(item =>
            !item.IsFinalReview &&
            item.DefinitionState == WorkDefinitionState.Executable &&
            item.Status is WorkItemStatus.Ready or WorkItemStatus.Planned or WorkItemStatus.Waiting &&
            DependenciesCompleted(state, item));
        if (runnable is not null)
            return new(FactoryCommandKind.DispatchWork, runnable.Id);

        var refinable = ordered.FirstOrDefault(item =>
            !item.IsFinalReview &&
            item.DefinitionState == WorkDefinitionState.Outline &&
            item.Status is WorkItemStatus.Planned or WorkItemStatus.Ready or WorkItemStatus.Waiting &&
            DependenciesCompleted(state, item));
        if (refinable is not null)
            return new(FactoryCommandKind.RefineWork, refinable.Id);

        if (ordered.Any(item => !item.IsFinalReview && IsRequiredIncomplete(item)))
            return new(FactoryCommandKind.StopBlocked);

        var finalReviewApproved = state.FinalReview is { Verdict: "approved", ReviewedGraphRevision: not null } approved &&
            approved.ReviewedGraphRevision == state.GraphRevision;
        var currentFinalReview = ordered.FirstOrDefault(item =>
            item.IsFinalReview && item.ReviewTargetGraphRevision == state.GraphRevision && IsRequiredIncomplete(item));

        // Strict deterministic verification must pass before runtime materializes final semantic review work.
        // Materializing the read-only review advances GraphRevision but preserves that verified product snapshot
        // at the new revision; any later corrective/product graph mutation invalidates it normally.
        if (!finalReviewApproved && currentFinalReview is null &&
            (!state.FinalVerificationPassed || state.FinalVerificationGraphRevision != state.GraphRevision))
            return new(FactoryCommandKind.RunFinalVerification, VerificationContext: "final");

        if (!finalReviewApproved && currentFinalReview is null)
            return new(FactoryCommandKind.CreateFinalReview);

        if (!state.FinalVerificationPassed || state.FinalVerificationGraphRevision != state.GraphRevision)
            return new(FactoryCommandKind.RunFinalVerification, VerificationContext: "final");

        if (currentFinalReview is not null)
        {
            if (currentFinalReview.DefinitionState == WorkDefinitionState.Executable &&
                currentFinalReview.Status is WorkItemStatus.Ready or WorkItemStatus.Planned or WorkItemStatus.Waiting &&
                DependenciesCompleted(state, currentFinalReview))
                return new(FactoryCommandKind.DispatchWork, currentFinalReview.Id);
            return new(FactoryCommandKind.StopBlocked);
        }

        return finalReviewApproved
            ? new(FactoryCommandKind.Finalize)
            : new(FactoryCommandKind.StopBlocked);
    }

    private static bool DependenciesCompleted(FactoryState state, WorkItemState item) =>
        item.Dependencies.All(id => state.WorkItems.Single(x => x.Id == id).Status == WorkItemStatus.Completed);

    private static bool IsRequiredIncomplete(WorkItemState item) =>
        item.Status is not (WorkItemStatus.Completed or WorkItemStatus.Superseded or WorkItemStatus.Cancelled);
}
