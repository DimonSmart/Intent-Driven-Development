using Idd.Factory.Domain;
using Idd.Factory.Verification;

namespace Idd.Factory.Runtime;

public sealed partial class FactoryRuntime
{
    private async Task<FactoryCliOutcome?> RunVerificationAsync(
        FactoryState state,
        string? workItemId,
        string context,
        CancellationToken cancellationToken)
    {
        if (context is not ("subtask" or "final"))
            throw new VerificationException("INVALID_VERIFICATION_CONTEXT", $"Unsupported verification context {context}.");
        var item = workItemId is null ? null : state.Current is { } current && current.Id == workItemId
            ? current
            : throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Verification must target Current work.");
        if (context == "subtask" && item is null)
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Subtask verification requires a work item.");

        var session = state.PendingVerificationSession;
        if (session is null || session.Context != context || session.WorkItemId != item?.Id)
        {
            var changedPaths = context == "final" ? state.FactoryRunChangedPaths : item!.ChangedPaths;
            var selection = await verification.ResolveContextAsync(context, changedPaths, cancellationToken);
            var selected = context == "subtask" && item!.VerificationCheckIds.Count > 0
                ? item.VerificationCheckIds.ToList()
                : selection.CheckIds.ToList();
            if (item is not null)
                foreach (var checkId in item.VerificationExpectations.Keys)
                    if (!selected.Contains(checkId, StringComparer.Ordinal)) selected.Add(checkId);
            selected = selected.Distinct(StringComparer.Ordinal).ToList();
            verification.ValidateCheckIds(selected);
            session = new PendingVerificationSession(
                context,
                item?.Id,
                selected,
                changedPaths.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList(),
                0,
                [],
                [],
                [],
                null,
                null,
                selection.PolicyHash,
                VerificationContinuationStage.ExecuteCheck);
            state.PendingVerificationSession = session;
            state.PendingContinuation = new(ContinuationKind.VerificationGate, item?.Id, context, "VERIFICATION_GATE", true);
            await SaveAsync(state, cancellationToken);
        }

        if (session.CheckIds.Count == 0)
        {
            if (session.PolicyHash == "not-configured")
            {
                var fallback = await verification.RunContextAsync(context, session.ChangedPaths, cancellationToken);
                RecordEvidence(state, item, fallback.Evidence);
                switch (fallback.Status)
                {
                    case VerificationStatus.Passed:
                    case VerificationStatus.NoChecks:
                        return await CompleteVerificationAsync(state, item, context, [], cancellationToken);
                    case VerificationStatus.Failed:
                        return await CompleteVerificationAsync(state, item, context,
                            fallback.Evidence.Where(x => x.Status == "failed").Select(x => x.CheckId).ToArray(), cancellationToken);
                    case VerificationStatus.InfrastructureFailure:
                        return await BlockVerificationAsync(state, item, context, "VERIFICATION_INFRASTRUCTURE_FAILURE",
                            "Authoritative verification could not execute because of an infrastructure failure.", fallback.Evidence, cancellationToken);
                    default:
                        return await BlockVerificationAsync(state, item, context, "VERIFICATION_ACTION_REQUIRED",
                            $"Authoritative verification requires user action: {fallback.Status}.", fallback.Evidence, cancellationToken);
                }
            }
            return await CompleteVerificationAsync(state, item, context, [], cancellationToken);
        }

        while (session.NextCheckIndex < session.CheckIds.Count)
        {
            var checkId = session.CheckIds[session.NextCheckIndex];
            var definitionHash = await verification.GetCheckDefinitionHashAsync(checkId, cancellationToken);
            var result = await verification.RunCheckAsync(checkId, false, null, definitionHash, session.PolicyHash, cancellationToken);
            RecordEvidence(state, item, result.Evidence);

            if (result.Status is VerificationStatus.ConfirmationRequired or VerificationStatus.ResultRequired)
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
                var code = result.Status == VerificationStatus.ConfirmationRequired ? "VERIFICATION_CONFIRMATION_REQUIRED" : "VERIFICATION_RESULT_REQUIRED";
                var reason = result.Status == VerificationStatus.ConfirmationRequired
                    ? $"Check {checkId} requires explicit confirmation before running: {result.PendingCommand}"
                    : $"Manual check {checkId} requires a passed or failed result: {result.PendingInstructions}";
                state.RunStatus = FactoryRunStatus.Blocked;
                state.Blocker = new(code, reason,
                    result.Status == VerificationStatus.ConfirmationRequired
                        ? "Continue with --confirmation approve or decline for this exact check."
                        : "Continue with --verification-result passed or failed for this exact check.");
                state.PendingContinuation = new(ContinuationKind.VerificationGate, item?.Id, context, code, true,
                    VerificationCheckId: checkId, VerificationStage: session.Stage);
                await SaveAsync(state, cancellationToken);
                return OutcomeFromBlocker(state, code);
            }

            if (result.Status == VerificationStatus.InfrastructureFailure)
                return await BlockVerificationAsync(state, item, context, "VERIFICATION_INFRASTRUCTURE_FAILURE",
                    $"Check {checkId} could not execute because of an infrastructure failure.", result.Evidence, cancellationToken);
            if (result.Status is not (VerificationStatus.Passed or VerificationStatus.Failed))
                return await BlockVerificationAsync(state, item, context, "VERIFICATION_ACTION_REQUIRED",
                    $"Check {checkId} ended as {result.Status}.", result.Evidence, cancellationToken);

            session = AdvanceVerificationSession(session, checkId, result.Status == VerificationStatus.Failed, result.Evidence);
            state.PendingVerificationSession = session;
            await SaveAsync(state, cancellationToken);
        }

        return await CompleteVerificationAsync(state, item, context, session.FailedCheckIds, cancellationToken);
    }

    private async Task<FactoryCliOutcome?> ResolvePendingVerificationActionAsync(
        FactoryState state,
        PendingContinuation continuation,
        VerificationConfirmation confirmation,
        bool? verificationPassed,
        CancellationToken cancellationToken)
    {
        var session = state.PendingVerificationSession
            ?? throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Verification action requires a persisted verification session.");
        var checkId = session.PendingCheckId
            ?? throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Verification action requires a pending check ID.");
        var definitionHash = session.PendingCheckDefinitionHash
            ?? throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Verification action requires a pending check definition hash.");
        var item = session.WorkItemId is null ? null : state.Current is { } current && current.Id == session.WorkItemId
            ? current
            : throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Verification session does not target Current work.");

        if (session.Stage == VerificationContinuationStage.AwaitingConfirmation && confirmation == VerificationConfirmation.Decline)
        {
            var declined = await verification.DeclineCheckAsync(checkId, definitionHash, session.PolicyHash, cancellationToken);
            RecordEvidence(state, item, declined.Evidence);
            state.PendingVerificationSession = null;
            state.RunStatus = FactoryRunStatus.Blocked;
            state.Blocker = new("VERIFICATION_DECLINED", $"User declined authoritative check {checkId}.", "Cancel/restart the run when verification can be performed.");
            state.PendingContinuation = new(ContinuationKind.Terminal, item?.Id, session.Context, "VERIFICATION_DECLINED", false);
            if (item is not null) state.CurrentPhase = CurrentWorkPhase.Blocked;
            await SaveAsync(state, cancellationToken);
            return OutcomeFromBlocker(state, "VERIFICATION_DECLINED");
        }

        VerificationResult result;
        if (session.Stage == VerificationContinuationStage.AwaitingConfirmation)
        {
            if (confirmation != VerificationConfirmation.Approve)
                return OutcomeFromBlocker(state, "VERIFICATION_CONFIRMATION_REQUIRED");
            result = await verification.RunCheckAsync(checkId, true, null, definitionHash, session.PolicyHash, cancellationToken);
        }
        else if (session.Stage == VerificationContinuationStage.AwaitingManualResult)
        {
            if (verificationPassed is null) return OutcomeFromBlocker(state, "VERIFICATION_RESULT_REQUIRED");
            result = await verification.RunCheckAsync(checkId, false, verificationPassed, definitionHash, session.PolicyHash, cancellationToken);
        }
        else
        {
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "No user verification action is pending.");
        }

        RecordEvidence(state, item, result.Evidence);
        if (result.Status == VerificationStatus.InfrastructureFailure)
            return await BlockVerificationAsync(state, item, session.Context, "VERIFICATION_INFRASTRUCTURE_FAILURE",
                $"Check {checkId} could not execute because of an infrastructure failure.", result.Evidence, cancellationToken);
        if (result.Status is not (VerificationStatus.Passed or VerificationStatus.Failed))
            return await BlockVerificationAsync(state, item, session.Context, "VERIFICATION_ACTION_REQUIRED",
                $"Check {checkId} ended as {result.Status}.", result.Evidence, cancellationToken);

        session = AdvanceVerificationSession(session, checkId, result.Status == VerificationStatus.Failed, result.Evidence) with
        {
            PendingCheckId = null,
            PendingCheckDefinitionHash = null,
            Stage = VerificationContinuationStage.ExecuteCheck
        };
        state.PendingVerificationSession = session;
        state.PendingContinuation = new(ContinuationKind.VerificationGate, item?.Id, session.Context, "VERIFICATION_GATE", true);
        state.Blocker = null;
        state.RunStatus = FactoryRunStatus.Running;
        await SaveAsync(state, cancellationToken);
        return null;
    }

    private static PendingVerificationSession AdvanceVerificationSession(
        PendingVerificationSession session,
        string checkId,
        bool failed,
        IEnumerable<VerificationEvidence> evidence)
    {
        var completed = session.CompletedCheckIds.Concat([checkId]).ToList();
        var failures = session.FailedCheckIds.ToList();
        if (failed && !failures.Contains(checkId, StringComparer.Ordinal)) failures.Add(checkId);
        return session with
        {
            NextCheckIndex = session.NextCheckIndex + 1,
            CompletedCheckIds = completed,
            FailedCheckIds = failures,
            EvidenceRefs = session.EvidenceRefs.Concat(evidence.Select(x => $"verification/{x.EvidenceId}.json")).Distinct(StringComparer.Ordinal).ToList(),
            PendingCheckId = null,
            PendingCheckDefinitionHash = null,
            Stage = VerificationContinuationStage.ExecuteCheck
        };
    }

    private async Task<FactoryCliOutcome?> CompleteVerificationAsync(
        FactoryState state,
        PlannedWorkItem? item,
        string context,
        IReadOnlyCollection<string> failedCheckIds,
        CancellationToken cancellationToken)
    {
        var decision = ClassifyVerification(item, context, failedCheckIds);
        if (item is not null) item.LastVerificationDecision = decision;
        state.PendingVerificationSession = null;

        if (decision is VerificationDecision.Ok or VerificationDecision.ExpectedFailure)
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
                await SaveAsync(state, cancellationToken);
            }
            await events.WriteAsync(state.RunId, "verification-decision", new { context, workItemId = item?.Id, decision, failedCheckIds }, cancellationToken);
            return null;
        }

        if (item is not null) state.CurrentPhase = CurrentWorkPhase.Blocked;
        else
        {
            state.FinalVerificationPassed = false;
            state.FinalVerificationPlanRevision = null;
        }
        state.RunStatus = FactoryRunStatus.Blocked;
        var failed = failedCheckIds.Count == 0 ? "unknown check" : string.Join(", ", failedCheckIds);
        state.Blocker = new(
            "UNEXPECTED_VERIFICATION_FAILURE",
            context == "final"
                ? $"Strict final verification failed: {failed}."
                : $"Verification failed outside the work item's deterministic may-fail expectations: {failed}.",
            "Correct the regression, then continue to rerun the authoritative checks.");
        state.PendingContinuation = new(ContinuationKind.VerificationGate, item?.Id, context, "UNEXPECTED_VERIFICATION_FAILURE", true);
        await SaveAsync(state, cancellationToken);
        await events.WriteAsync(state.RunId, "verification-decision", new { context, workItemId = item?.Id, decision, failedCheckIds }, cancellationToken);
        return OutcomeFromBlocker(state, "UNEXPECTED_VERIFICATION_FAILURE");
    }

    internal static VerificationDecision ClassifyVerification(PlannedWorkItem? item, string context, IReadOnlyCollection<string> failedCheckIds)
    {
        if (failedCheckIds.Count == 0) return VerificationDecision.Ok;
        if (context == "final" || item is null) return VerificationDecision.UnexpectedFailure;
        return failedCheckIds.All(id => item.VerificationExpectations.TryGetValue(id, out var expectation) && expectation == VerificationExpectation.MayFail)
            ? VerificationDecision.ExpectedFailure
            : VerificationDecision.UnexpectedFailure;
    }

    private async Task<FactoryCliOutcome> BlockVerificationAsync(
        FactoryState state,
        PlannedWorkItem? item,
        string context,
        string code,
        string reason,
        IEnumerable<VerificationEvidence> evidence,
        CancellationToken cancellationToken)
    {
        state.RunStatus = FactoryRunStatus.Blocked;
        state.Blocker = new(code, reason, "Resolve the verification condition, then continue.");
        state.PendingContinuation = new(ContinuationKind.VerificationGate, item?.Id, context, code, true);
        RecordEvidence(state, item, evidence);
        await SaveAsync(state, cancellationToken);
        return OutcomeFromBlocker(state, code);
    }

    private static void RecordEvidence(FactoryState state, PlannedWorkItem? item, IEnumerable<VerificationEvidence> evidence)
    {
        foreach (var record in evidence)
        {
            var relative = $"verification/{record.EvidenceId}.json";
            if (item is not null && !item.VerificationEvidenceRefs.Contains(relative, StringComparer.Ordinal)) item.VerificationEvidenceRefs.Add(relative);
            if (!state.VerificationEvidenceRefs.Contains(relative, StringComparer.Ordinal)) state.VerificationEvidenceRefs.Add(relative);
        }
    }
}
