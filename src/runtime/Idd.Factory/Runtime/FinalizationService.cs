using Idd.Factory.Domain;
using Idd.Factory.Finalization;

namespace Idd.Factory.Runtime;

internal sealed class FinalizationService(FactoryRuntimeContext context)
{
    public async Task<FactoryCliOutcome> FinalizeAsync(
        FactoryState state,
        CancellationToken cancellationToken)
    {
        EnsurePreconditions(state);
        var resultDirectory = await new FinalizeHandler(context.Workspace)
            .FinalizeAsync(state, cancellationToken);
        return new(
            "COMPLETED",
            state.RunId,
            ResultDirectory: resultDirectory);
    }

    private static void EnsurePreconditions(FactoryState state)
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
}
