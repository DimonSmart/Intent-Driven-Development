using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Verification;

namespace Idd.Factory.Runtime;

internal sealed record FactoryVerificationStepResult(
    string Context,
    string? WorkItemId,
    VerificationDecision Decision,
    IReadOnlyCollection<string> FailedCheckIds,
    FactoryBlockResult? Block = null)
{
    public static FactoryVerificationStepResult Completed(
        string context,
        string? workItemId,
        VerificationDecision decision,
        IReadOnlyCollection<string> failedCheckIds) =>
        new(context, workItemId, decision, failedCheckIds);

    public static FactoryVerificationStepResult Blocked(
        string context,
        string? workItemId,
        FactoryBlockResult block) =>
        new(context, workItemId, VerificationDecision.None, [], block);
}

internal sealed partial class RuntimeVerificationService
{
    private readonly FactoryRuntimeContext context;
    private readonly VerificationEngine verification;

    public RuntimeVerificationService(
        FactoryRuntimeContext context,
        VerificationEngine verification)
    {
        this.context = context;
        this.verification = verification;
    }

    public async Task<FactoryBlockResult?> RunBaselineAsync(
        FactoryState state,
        CancellationToken cancellationToken)
    {
        if (state.RepositoryFallbackBaselineAccepted
            || File.Exists(Path.Combine(context.Workspace, ".idd", "verification.yaml")))
        {
            return null;
        }

        var baseline = await verification.RunContextAsync("final", cancellationToken);
        RecordEvidence(state, null, baseline.Evidence);
        var evidenceRefs = EvidenceReferences(baseline.Evidence).ToArray();
        await context.Events.WriteAsync(
            state.RunId,
            "repository-fallback-baseline",
            new { baseline.Status, evidenceRefs },
            cancellationToken);

        if (baseline.Status is VerificationStatus.Passed or VerificationStatus.NoChecks)
        {
            if (baseline.Evidence.Count > 0)
                await context.SaveAsync(state, cancellationToken);
            return null;
        }

        var checkIds = baseline.Evidence
            .Select(x => x.CheckId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var checkSummary = checkIds.Length == 0
            ? "repository fallback"
            : string.Join(", ", checkIds);
        var evidenceSummary = evidenceRefs.Length == 0
            ? "none"
            : string.Join(", ", evidenceRefs);

        if (baseline.Status == VerificationStatus.Failed)
        {
            return new(
                "VERIFICATION_CONFIRMATION_REQUIRED",
                $"Repository fallback baseline already fails before Factory planning. Failed checks: {checkSummary}. Evidence: {evidenceSummary}. Repository-wide subtask verification cannot reliably attribute that failure to the current work item.",
                "Fix the repository baseline and cancel/restart, or continue with --confirmation approve to accept the existing red baseline.",
                new(
                    ContinuationKind.VerificationGate,
                    null,
                    "baseline",
                    "VERIFICATION_CONFIRMATION_REQUIRED",
                    true,
                    VerificationStage: VerificationContinuationStage.AwaitingConfirmation));
        }

        var terminal = new PendingContinuation(
            ContinuationKind.Terminal,
            null,
            null,
            "BASELINE_VERIFICATION",
            false);
        if (baseline.Status == VerificationStatus.InfrastructureFailure)
        {
            var primary = SelectPrimaryInfrastructureFailure(baseline.Evidence);
            var reference = CreateFailureReference(
                "baseline",
                null,
                primary.Evidence,
                primary.Failure);
            var payload = JsonSerializer.SerializeToElement(reference, FactoryJson.Options);
            return new(
                "BASELINE_VERIFICATION_INFRASTRUCTURE_FAILURE",
                BuildInfrastructureReason(primary.Evidence, primary.Failure, baseline: true),
                BuildInfrastructureResumeWhen(primary.Evidence, primary.Failure, baseline: true),
                terminal,
                payload);
        }

        return new(
            "BASELINE_VERIFICATION_ACTION_REQUIRED",
            $"Repository fallback baseline ended as {baseline.Status} before Factory planning.",
            "Resolve the baseline verification condition, then cancel/restart the Factory run.",
            terminal);
    }

}
