using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Verification;

namespace Idd.Factory.Runtime;

internal sealed partial class RuntimeVerificationService
{
    private static FactoryVerificationStepResult Completed(
        FactoryState state,
        PlannedWorkItem? item,
        string verificationContext,
        IReadOnlyCollection<string> failedCheckIds) =>
        FactoryVerificationStepResult.Completed(
            verificationContext,
            item?.Id,
            ClassifyVerification(item, verificationContext, failedCheckIds),
            failedCheckIds);

    internal static VerificationDecision ClassifyVerification(
        PlannedWorkItem? item,
        string verificationContext,
        IReadOnlyCollection<string> failedCheckIds)
    {
        if (failedCheckIds.Count == 0)
            return VerificationDecision.Ok;
        if (verificationContext == "final" || item is null)
            return VerificationDecision.UnexpectedFailure;
        return failedCheckIds.All(
            id => item.VerificationExpectations.TryGetValue(id, out var expectation)
                  && expectation == VerificationExpectation.MayFail)
            ? VerificationDecision.ExpectedFailure
            : VerificationDecision.UnexpectedFailure;
    }

    private FactoryBlockResult CreateVerificationBlock(
        PlannedWorkItem? item,
        string verificationContext,
        string code,
        string reason,
        IEnumerable<VerificationEvidence> evidence)
    {
        var evidenceList = evidence.ToList();
        JsonElement? payload = null;
        var resumeWhen = "Resolve the verification condition, then continue.";
        if (code == "VERIFICATION_INFRASTRUCTURE_FAILURE")
        {
            var primary = SelectPrimaryInfrastructureFailure(evidenceList);
            var reference = CreateFailureReference(
                verificationContext,
                item?.Id,
                primary.Evidence,
                primary.Failure);
            payload = JsonSerializer.SerializeToElement(reference, FactoryJson.Options);
            reason = BuildInfrastructureReason(
                primary.Evidence,
                primary.Failure,
                baseline: false);
            resumeWhen = BuildInfrastructureResumeWhen(
                primary.Evidence,
                primary.Failure,
                baseline: false);
        }

        return new(
            code,
            reason,
            resumeWhen,
            new(
                ContinuationKind.VerificationGate,
                item?.Id,
                verificationContext,
                code,
                true),
            payload);
    }

    private static PendingVerificationSession AdvanceVerificationSession(
        PendingVerificationSession session,
        string checkId,
        bool failed,
        IEnumerable<VerificationEvidence> evidence)
    {
        var completed = session.CompletedCheckIds.Concat([checkId]).ToList();
        var failures = session.FailedCheckIds.ToList();
        if (failed && !failures.Contains(checkId, StringComparer.Ordinal))
            failures.Add(checkId);
        return session with
        {
            NextCheckIndex = session.NextCheckIndex + 1,
            CompletedCheckIds = completed,
            FailedCheckIds = failures,
            EvidenceRefs = session.EvidenceRefs
                .Concat(EvidenceReferences(evidence))
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            PendingCheckId = null,
            PendingCheckDefinitionHash = null,
            Stage = VerificationContinuationStage.ExecuteCheck
        };
    }

    private static (VerificationEvidence Evidence, VerificationFailure Failure)
        SelectPrimaryInfrastructureFailure(IReadOnlyList<VerificationEvidence> evidence)
    {
        var primary = evidence.FirstOrDefault(x => x.PrimaryFailure is not null)
                      ?? evidence.FirstOrDefault(x => x.Status == "infrastructure-failure")
                      ?? new VerificationEvidence
                      {
                          CheckId = "unknown",
                          EvidenceId = "unknown",
                          Status = "infrastructure-failure",
                          EvidencePersisted = false
                      };
        var failure = primary.PrimaryFailure
                      ?? new VerificationFailure(
                          "unknown",
                          "execute",
                          "Verification infrastructure failed without a primary diagnostic.");
        return (primary, failure);
    }

    private static VerificationFailureReference CreateFailureReference(
        string verificationContext,
        string? workItemId,
        VerificationEvidence evidence,
        VerificationFailure failure) =>
        new(
            verificationContext,
            workItemId,
            evidence.CheckId,
            failure.Kind,
            failure.Stage,
            evidence.EvidencePersisted
                ? PublicVerificationPath($"{evidence.EvidenceId}.json")
                : null);

    private static string BuildInfrastructureReason(
        VerificationEvidence evidence,
        VerificationFailure failure,
        bool baseline)
    {
        var prefix = baseline
            ? $"Repository fallback baseline verification check {evidence.CheckId}"
            : $"Verification check {evidence.CheckId}";
        var description = failure.Kind == "timeout"
                          && evidence.TimeoutMilliseconds is not null
            ? $"The check exceeded its configured {FormatDuration(evidence.TimeoutMilliseconds.Value)} timeout."
            : Shorten(failure.Message, 256);
        var exit = evidence.ExitCode is null ? "" : $" Exit code: {evidence.ExitCode}.";
        var evidencePath = evidence.EvidencePersisted
            ? PublicVerificationPath($"{evidence.EvidenceId}.json")
            : null;
        var persisted = evidencePath is null
            ? " No verification evidence JSON was persisted."
            : $" Evidence: {evidencePath}.";
        return $"{prefix} could not execute: {failure.Kind} at {failure.Stage}. {description}{exit}{persisted}";
    }

    private static string BuildInfrastructureResumeWhen(
        VerificationEvidence evidence,
        VerificationFailure failure,
        bool baseline)
    {
        var condition = failure.Kind switch
        {
            "process-start-failure" => "Make the verification executable and working directory available",
            "output-capture-failure" => "Restore writable verification log storage",
            "evidence-persistence-failure" => "Restore writable verification evidence storage",
            "timeout" => evidence.TimeoutMilliseconds is null
                ? $"Resolve the timeout for verification check {evidence.CheckId}"
                : $"Allow verification check {evidence.CheckId} to finish within its configured {FormatDuration(evidence.TimeoutMilliseconds.Value)} timeout",
            "termination-failure" => "Ensure the timed-out verification process can be terminated",
            _ => $"Resolve the {failure.Kind} failure at {failure.Stage}"
        };
        return baseline
            ? $"{condition}, then cancel/restart the Factory run."
            : $"{condition}, then call factory_continue.";
    }

    private static string Shorten(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;
        var length = maxLength;
        if (length > 0 && char.IsHighSurrogate(value[length - 1]))
            length--;
        return value[..length];
    }

    private static string FormatDuration(long milliseconds)
    {
        if (milliseconds >= 60_000 && milliseconds % 60_000 == 0)
            return $"{milliseconds / 60_000} {(milliseconds == 60_000 ? "minute" : "minutes")}";
        if (milliseconds >= 1_000 && milliseconds % 1_000 == 0)
            return $"{milliseconds / 1_000} {(milliseconds == 1_000 ? "second" : "seconds")}";
        return milliseconds < 1_000
            ? $"{milliseconds} ms"
            : $"{milliseconds / 1000d:0.###} seconds";
    }

    private static string? PublicVerificationPath(string? path)
    {
        if (path is null)
            return null;
        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith(
                ".idd/factory/current/",
                StringComparison.Ordinal))
        {
            return normalized;
        }

        if (normalized.StartsWith("verification/", StringComparison.Ordinal))
            normalized = normalized["verification/".Length..];
        return $".idd/factory/current/verification/{normalized}";
    }

    internal static void RecordLastVerificationCycle(
        PlannedWorkItem? item,
        IEnumerable<string> evidenceRefs)
    {
        if (item is null)
            return;
        item.LastVerificationEvidenceRefs.Clear();
        item.LastVerificationEvidenceRefs.AddRange(
            evidenceRefs.Distinct(StringComparer.Ordinal));
    }

    internal static void RecordEvidence(
        FactoryState state,
        PlannedWorkItem? item,
        IEnumerable<VerificationEvidence> evidence)
    {
        foreach (var record in evidence.Where(x => x.EvidencePersisted))
        {
            var relative = $"verification/{record.EvidenceId}.json";
            if (item is not null
                && !item.VerificationEvidenceRefs.Contains(relative, StringComparer.Ordinal))
            {
                item.VerificationEvidenceRefs.Add(relative);
            }

            if (!state.VerificationEvidenceRefs.Contains(relative, StringComparer.Ordinal))
                state.VerificationEvidenceRefs.Add(relative);
        }
    }

    internal static IEnumerable<string> EvidenceReferences(
        IEnumerable<VerificationEvidence> evidence) =>
        evidence
            .Where(x => x.EvidencePersisted)
            .Select(x => $"verification/{x.EvidenceId}.json");
}
