using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Verification;

namespace Idd.Factory.Runtime;

internal sealed record FactoryVerificationStepResult(
    string Context,
    string? WorkItemId,
    VerificationDecision Decision,
    IReadOnlyCollection<string> FailedCheckIds,
    FactoryBlockResult? Block = null,
    PendingVerificationSession? Session = null,
    IReadOnlyList<string>? EvidenceRefs = null,
    IReadOnlyList<string>? WorkItemEvidenceRefs = null,
    IReadOnlyList<string>? LastWorkItemEvidenceRefs = null)
{
    public static FactoryVerificationStepResult Completed(
        string context,
        string? workItemId,
        VerificationDecision decision,
        IReadOnlyCollection<string> failedCheckIds) =>
        new(context, workItemId, decision, failedCheckIds);

    public static FactoryVerificationStepResult Pending(
        string context,
        string? workItemId) =>
        new(context, workItemId, VerificationDecision.None, []);

    public static FactoryVerificationStepResult Blocked(
        string context,
        string? workItemId,
        FactoryBlockResult block) =>
        new(context, workItemId, VerificationDecision.None, [], block);
}

internal sealed record FactoryBaselineVerificationResult(
    bool Required,
    VerificationStatus Status,
    IReadOnlyList<string> EvidenceRefs,
    FactoryBlockResult? Block = null);

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

    public async Task<FactoryBaselineVerificationResult> RunBaselineAsync(
        FactoryState state,
        CancellationToken cancellationToken)
    {
        if (state.RepositoryFallbackBaselineAccepted
            || File.Exists(Path.Combine(context.Workspace, ".idd", "verification.yaml")))
        {
            return new(false, VerificationStatus.NoChecks, []);
        }

        var baseline = await verification.RunContextAsync("final", cancellationToken);
        var evidenceRefs = EvidenceReferences(baseline.Evidence).ToArray();
        await context.Events.WriteAsync(
            state.RunId,
            "repository-fallback-baseline",
            new { baseline.Status, evidenceRefs },
            cancellationToken);

        if (baseline.Status is VerificationStatus.Passed or VerificationStatus.NoChecks)
            return new(true, baseline.Status, evidenceRefs);

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
                true,
                baseline.Status,
                evidenceRefs,
                new(
                    "VERIFICATION_CONFIRMATION_REQUIRED",
                    $"Repository fallback baseline already fails before Factory planning. Failed checks: {checkSummary}. Evidence: {evidenceSummary}. Repository-wide subtask verification cannot reliably attribute that failure to the current work item.",
                    "Fix the repository baseline and use factory_restart to replace the run, or continue with --confirmation approve to accept the existing red baseline. Use factory_cancel only if no replacement run is wanted.",
                    new(
                        ContinuationKind.VerificationGate,
                        null,
                        "baseline",
                        "VERIFICATION_CONFIRMATION_REQUIRED",
                        true,
                        VerificationStage: VerificationContinuationStage.AwaitingConfirmation)));
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
                true,
                baseline.Status,
                evidenceRefs,
                new(
                    "BASELINE_VERIFICATION_INFRASTRUCTURE_FAILURE",
                    BuildInfrastructureReason(primary.Evidence, primary.Failure, baseline: true),
                    BuildInfrastructureResumeWhen(primary.Evidence, primary.Failure, baseline: true),
                    terminal,
                    payload));
        }

        return new(
            true,
            baseline.Status,
            evidenceRefs,
            new(
                "BASELINE_VERIFICATION_ACTION_REQUIRED",
                $"Repository fallback baseline ended as {baseline.Status} before Factory planning.",
                "Resolve the baseline verification condition, then use factory_restart to replace the run, or factory_cancel if no replacement run is wanted.",
                terminal));
    }

    private static FactoryVerificationStepResult WithOperationState(
        FactoryVerificationStepResult result,
        PendingVerificationSession? session,
        IReadOnlyList<string> evidenceRefs,
        IReadOnlyList<string> workItemEvidenceRefs,
        IReadOnlyList<string> lastWorkItemEvidenceRefs) =>
        result with
        {
            Session = session,
            EvidenceRefs = evidenceRefs,
            WorkItemEvidenceRefs = workItemEvidenceRefs,
            LastWorkItemEvidenceRefs = lastWorkItemEvidenceRefs
        };
}
