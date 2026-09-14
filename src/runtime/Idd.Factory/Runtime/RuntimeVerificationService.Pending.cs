using Idd.Factory.Domain;
using Idd.Factory.Verification;

namespace Idd.Factory.Runtime;

internal sealed partial class RuntimeVerificationService
{
    public async Task<FactoryVerificationStepResult> ResolvePendingActionAsync(
        FactoryState state,
        VerificationConfirmation confirmation,
        bool? verificationPassed,
        CancellationToken cancellationToken)
    {
        var candidate = context.CloneState(state);
        var result = await ResolvePendingActionCoreAsync(
            candidate,
            confirmation,
            verificationPassed,
            cancellationToken);
        return CaptureState(candidate, result);
    }

    private async Task<FactoryVerificationStepResult> ResolvePendingActionCoreAsync(
        FactoryState state,
        VerificationConfirmation confirmation,
        bool? verificationPassed,
        CancellationToken cancellationToken)
    {
        var session = state.PendingVerificationSession
            ?? throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                "Verification action requires a persisted verification session.");
        var checkId = session.PendingCheckId
            ?? throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                "Verification action requires a pending check ID.");
        var definitionHash = session.PendingCheckDefinitionHash
            ?? throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                "Verification action requires a pending check definition hash.");
        var item = session.WorkItemId is null
            ? null
            : state.Current is { } current && current.Id == session.WorkItemId
                ? current
                : throw new FactoryStateException(
                    "CORRUPT_FACTORY_STATE",
                    "Verification session does not target Current work.");

        if (session.Stage == VerificationContinuationStage.AwaitingConfirmation
            && confirmation == VerificationConfirmation.Decline)
        {
            var declined = await verification.DeclineCheckAsync(
                checkId,
                definitionHash,
                session.PolicyHash,
                cancellationToken);
            RecordEvidence(state, item, declined.Evidence);
            state.PendingVerificationSession = null;
            return FactoryVerificationStepResult.Blocked(
                session.Context,
                item?.Id,
                new(
                    "VERIFICATION_DECLINED",
                    $"User declined authoritative check {checkId}.",
                    "Cancel/restart the run when verification can be performed.",
                    new(
                        ContinuationKind.Terminal,
                        item?.Id,
                        session.Context,
                        "VERIFICATION_DECLINED",
                        false)));
        }

        VerificationResult result;
        if (session.Stage == VerificationContinuationStage.AwaitingConfirmation)
        {
            if (confirmation != VerificationConfirmation.Approve)
            {
                return FactoryVerificationStepResult.Pending(
                    session.Context,
                    item?.Id);
            }
            result = await verification.RunCheckAsync(
                checkId,
                true,
                null,
                definitionHash,
                session.PolicyHash,
                cancellationToken);
        }
        else if (session.Stage == VerificationContinuationStage.AwaitingManualResult)
        {
            if (verificationPassed is null)
            {
                return FactoryVerificationStepResult.Pending(
                    session.Context,
                    item?.Id);
            }
            result = await verification.RunCheckAsync(
                checkId,
                false,
                verificationPassed,
                definitionHash,
                session.PolicyHash,
                cancellationToken);
        }
        else
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                "No user verification action is pending.");
        }

        RecordEvidence(state, item, result.Evidence);
        if (result.Status == VerificationStatus.InfrastructureFailure)
        {
            return FactoryVerificationStepResult.Blocked(
                session.Context,
                item?.Id,
                CreateVerificationBlock(
                    item,
                    session.Context,
                    "VERIFICATION_INFRASTRUCTURE_FAILURE",
                    $"Check {checkId} could not execute because of an infrastructure failure.",
                    result.Evidence));
        }

        if (result.Status is not (VerificationStatus.Passed or VerificationStatus.Failed))
        {
            return FactoryVerificationStepResult.Blocked(
                session.Context,
                item?.Id,
                CreateVerificationBlock(
                    item,
                    session.Context,
                    "VERIFICATION_ACTION_REQUIRED",
                    $"Check {checkId} ended as {result.Status}.",
                    result.Evidence));
        }

        session = AdvanceVerificationSession(
            session,
            checkId,
            result.Status == VerificationStatus.Failed,
            result.Evidence) with
        {
            PendingCheckId = null,
            PendingCheckDefinitionHash = null,
            Stage = VerificationContinuationStage.ExecuteCheck
        };
        state.PendingVerificationSession = session;
        return FactoryVerificationStepResult.Pending(
            session.Context,
            item?.Id);
    }
}
