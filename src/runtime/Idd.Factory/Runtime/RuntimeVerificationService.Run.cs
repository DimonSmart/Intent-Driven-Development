using Idd.Factory.Domain;
using Idd.Factory.Verification;

namespace Idd.Factory.Runtime;

internal sealed partial class RuntimeVerificationService
{
    public async Task<FactoryVerificationStepResult> RunAsync(
        FactoryState state,
        string? workItemId,
        string verificationContext,
        CancellationToken cancellationToken)
    {
        if (verificationContext is not ("subtask" or "final"))
        {
            throw new VerificationException(
                "INVALID_VERIFICATION_CONTEXT",
                $"Unsupported verification context {verificationContext}.");
        }

        var item = workItemId is null
            ? null
            : state.Current is { } current && current.Id == workItemId
                ? current
                : throw new FactoryStateException(
                    "CORRUPT_FACTORY_STATE",
                    "Verification must target Current work.");
        if (verificationContext == "subtask" && item is null)
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                "Subtask verification requires a work item.");
        }

        var session = state.PendingVerificationSession;
        if (session is null
            || session.Context != verificationContext
            || session.WorkItemId != item?.Id)
        {
            var changedPaths = verificationContext == "final"
                ? state.FactoryRunChangedPaths
                : item!.ChangedPaths;
            var selection = await verification.ResolveContextAsync(
                verificationContext,
                changedPaths,
                cancellationToken);
            var selected = verificationContext == "subtask"
                           && item!.VerificationCheckIds.Count > 0
                ? item.VerificationCheckIds.ToList()
                : selection.CheckIds.ToList();
            if (item is not null)
            {
                foreach (var checkId in item.VerificationExpectations.Keys)
                {
                    if (!selected.Contains(checkId, StringComparer.Ordinal))
                        selected.Add(checkId);
                }
            }

            selected = selected.Distinct(StringComparer.Ordinal).ToList();
            verification.ValidateCheckIds(selected);
            session = new PendingVerificationSession(
                verificationContext,
                item?.Id,
                selected,
                changedPaths
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToList(),
                0,
                [],
                [],
                [],
                null,
                null,
                selection.PolicyHash,
                VerificationContinuationStage.ExecuteCheck);
            state.PendingVerificationSession = session;
            state.PendingContinuation = new(
                ContinuationKind.VerificationGate,
                item?.Id,
                verificationContext,
                "VERIFICATION_GATE",
                true);
            await context.SaveAsync(state, cancellationToken);
        }

        if (session.CheckIds.Count == 0)
        {
            if (session.PolicyHash == "not-configured")
            {
                var fallback = await verification.RunContextAsync(
                    verificationContext,
                    session.ChangedPaths,
                    cancellationToken);
                RecordEvidence(state, item, fallback.Evidence);
                if (fallback.Status is VerificationStatus.Passed
                    or VerificationStatus.NoChecks
                    or VerificationStatus.Failed)
                {
                    RecordLastVerificationCycle(
                        item,
                        EvidenceReferences(fallback.Evidence));
                }

                switch (fallback.Status)
                {
                    case VerificationStatus.Passed:
                    case VerificationStatus.NoChecks:
                        return Completed(state, item, verificationContext, []);

                    case VerificationStatus.Failed:
                        if (state.RepositoryFallbackBaselineAccepted
                            && verificationContext == "subtask")
                        {
                            await context.Events.WriteAsync(
                                state.RunId,
                                "repository-fallback-subtask-degraded",
                                new
                                {
                                    workItemId = item!.Id,
                                    evidenceRefs = EvidenceReferences(fallback.Evidence).ToArray()
                                },
                                cancellationToken);
                            return Completed(state, item, verificationContext, []);
                        }

                        if (state.RepositoryFallbackBaselineAccepted
                            && verificationContext == "final")
                        {
                            return FactoryVerificationStepResult.Blocked(
                                verificationContext,
                                item?.Id,
                                CreateVerificationBlock(
                                    item,
                                    verificationContext,
                                    "FINAL_VERIFICATION_FAILED",
                                    "Strict final repository fallback still fails. The accepted red baseline suppresses subtask attribution only; final verification must pass before completion.",
                                    fallback.Evidence));
                        }

                        return Completed(
                            state,
                            item,
                            verificationContext,
                            fallback.Evidence
                                .Where(x => x.Status == "failed")
                                .Select(x => x.CheckId)
                                .ToArray());

                    case VerificationStatus.InfrastructureFailure:
                        return FactoryVerificationStepResult.Blocked(
                            verificationContext,
                            item?.Id,
                            CreateVerificationBlock(
                                item,
                                verificationContext,
                                "VERIFICATION_INFRASTRUCTURE_FAILURE",
                                "Authoritative verification could not execute because of an infrastructure failure.",
                                fallback.Evidence));

                    default:
                        return FactoryVerificationStepResult.Blocked(
                            verificationContext,
                            item?.Id,
                            CreateVerificationBlock(
                                item,
                                verificationContext,
                                "VERIFICATION_ACTION_REQUIRED",
                                $"Authoritative verification requires user action: {fallback.Status}.",
                                fallback.Evidence));
                }
            }

            RecordLastVerificationCycle(item, []);
            return Completed(state, item, verificationContext, []);
        }

        while (session.NextCheckIndex < session.CheckIds.Count)
        {
            var checkId = session.CheckIds[session.NextCheckIndex];
            var definitionHash = await verification.GetCheckDefinitionHashAsync(
                checkId,
                cancellationToken);
            var result = await verification.RunCheckAsync(
                checkId,
                false,
                null,
                definitionHash,
                session.PolicyHash,
                cancellationToken);
            RecordEvidence(state, item, result.Evidence);

            if (result.Status is VerificationStatus.ConfirmationRequired
                or VerificationStatus.ResultRequired)
            {
                session = session with
                {
                    PendingCheckId = checkId,
                    PendingCheckDefinitionHash = definitionHash,
                    Stage = result.Status == VerificationStatus.ConfirmationRequired
                        ? VerificationContinuationStage.AwaitingConfirmation
                        : VerificationContinuationStage.AwaitingManualResult
                };
                state.PendingVerificationSession = session;
                var code = result.Status == VerificationStatus.ConfirmationRequired
                    ? "VERIFICATION_CONFIRMATION_REQUIRED"
                    : "VERIFICATION_RESULT_REQUIRED";
                var reason = result.Status == VerificationStatus.ConfirmationRequired
                    ? $"Check {checkId} requires explicit confirmation before running: {result.PendingCommand}"
                    : $"Manual check {checkId} requires a passed or failed result: {result.PendingInstructions}";
                var resumeWhen = result.Status == VerificationStatus.ConfirmationRequired
                    ? "Continue with --confirmation approve or decline for this exact check."
                    : "Continue with --verification-result passed or failed for this exact check.";
                return FactoryVerificationStepResult.Blocked(
                    verificationContext,
                    item?.Id,
                    new(
                        code,
                        reason,
                        resumeWhen,
                        new(
                            ContinuationKind.VerificationGate,
                            item?.Id,
                            verificationContext,
                            code,
                            true,
                            VerificationCheckId: checkId,
                            VerificationStage: session.Stage)));
            }

            if (result.Status == VerificationStatus.InfrastructureFailure)
            {
                return FactoryVerificationStepResult.Blocked(
                    verificationContext,
                    item?.Id,
                    CreateVerificationBlock(
                        item,
                        verificationContext,
                        "VERIFICATION_INFRASTRUCTURE_FAILURE",
                        $"Check {checkId} could not execute because of an infrastructure failure.",
                        result.Evidence));
            }

            if (result.Status is not (VerificationStatus.Passed or VerificationStatus.Failed))
            {
                return FactoryVerificationStepResult.Blocked(
                    verificationContext,
                    item?.Id,
                    CreateVerificationBlock(
                        item,
                        verificationContext,
                        "VERIFICATION_ACTION_REQUIRED",
                        $"Check {checkId} ended as {result.Status}.",
                        result.Evidence));
            }

            session = AdvanceVerificationSession(
                session,
                checkId,
                result.Status == VerificationStatus.Failed,
                result.Evidence);
            state.PendingVerificationSession = session;
            await context.SaveAsync(state, cancellationToken);
        }

        RecordLastVerificationCycle(item, session.EvidenceRefs);
        return Completed(
            state,
            item,
            verificationContext,
            session.FailedCheckIds);
    }
}
