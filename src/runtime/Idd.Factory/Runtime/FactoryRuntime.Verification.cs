using Idd.Factory.Domain;
using Idd.Factory.Verification;
using System.Text;
using System.Text.Json;

namespace Idd.Factory.Runtime;

public sealed partial class FactoryRuntime
{
    private static readonly JsonSerializerOptions DiagnosticJsonOptions = new(FactoryJson.Options) { WriteIndented = false };
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
                if (fallback.Status is VerificationStatus.Passed or VerificationStatus.NoChecks or VerificationStatus.Failed)
                    RecordLastVerificationCycle(item, EvidenceReferences(fallback.Evidence));
                switch (fallback.Status)
                {
                    case VerificationStatus.Passed:
                    case VerificationStatus.NoChecks:
                        return await CompleteVerificationAsync(state, item, context, [], cancellationToken);
                    case VerificationStatus.Failed:
                        if (state.RepositoryFallbackBaselineAccepted && context == "subtask")
                        {
                            await events.WriteAsync(state.RunId, "repository-fallback-subtask-degraded", new
                            {
                                workItemId = item!.Id,
                                evidenceRefs = EvidenceReferences(fallback.Evidence).ToArray()
                            }, cancellationToken);
                            return await CompleteVerificationAsync(state, item, context, [], cancellationToken);
                        }
                        if (state.RepositoryFallbackBaselineAccepted && context == "final")
                        {
                            return await BlockVerificationAsync(
                                state,
                                item,
                                context,
                                "FINAL_VERIFICATION_FAILED",
                                "Strict final repository fallback still fails. The accepted red baseline suppresses subtask attribution only; final verification must pass before completion.",
                                fallback.Evidence,
                                cancellationToken);
                        }
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
            RecordLastVerificationCycle(item, []);
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

        RecordLastVerificationCycle(item, session.EvidenceRefs);
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
            EvidenceRefs = session.EvidenceRefs.Concat(EvidenceReferences(evidence)).Distinct(StringComparer.Ordinal).ToList(),
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

        if (item is not null)
        {
            if (item.LastResultRef is not null && !item.PriorResultRefs.Contains(item.LastResultRef, StringComparer.Ordinal))
                item.PriorResultRefs.Add(item.LastResultRef);
            state.CurrentPhase = CurrentWorkPhase.Ready;
            state.PendingContinuation = null;
            state.Blocker = null;
            state.RunStatus = FactoryRunStatus.Running;
            await SaveAsync(state, cancellationToken);
            await events.WriteAsync(state.RunId, "verification-decision", new { context, workItemId = item.Id, decision, failedCheckIds }, cancellationToken);
            return null;
        }

        state.FinalVerificationPassed = false;
        state.FinalVerificationPlanRevision = state.PlanRevision;
        state.RunStatus = FactoryRunStatus.Running;
        state.Blocker = null;
        state.PendingContinuation = null;
        await SaveAsync(state, cancellationToken);
        await events.WriteAsync(state.RunId, "verification-decision", new { context, workItemId = item?.Id, decision, failedCheckIds }, cancellationToken);
        return null;
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
        var evidenceList = evidence.ToList();
        state.RunStatus = FactoryRunStatus.Blocked;
        JsonElement? payload = null;
        var resumeWhen = "Resolve the verification condition, then continue.";
        if (code == "VERIFICATION_INFRASTRUCTURE_FAILURE")
        {
            var diagnostic = CreateInfrastructureDiagnostic(code, context, item?.Id, evidenceList);
            payload = SerializeBoundedDiagnostic(diagnostic);
            reason = BuildInfrastructureReason(diagnostic, baseline: false);
            resumeWhen = BuildInfrastructureResumeWhen(diagnostic, baseline: false);
            await WriteInfrastructureFailureEventAsync(state.RunId, diagnostic, cancellationToken);
        }
        state.Blocker = new(code, reason, resumeWhen, payload);
        state.PendingContinuation = new(ContinuationKind.VerificationGate, item?.Id, context, code, true);
        RecordEvidence(state, item, evidenceList);
        await SaveAsync(state, cancellationToken);
        return OutcomeFromBlocker(state, code);
    }

    internal static VerificationInfrastructureDiagnosticPayload CreateInfrastructureDiagnostic(
        string code, string context, string? workItemId, IReadOnlyList<VerificationEvidence> evidence)
    {
        var relevant = evidence.Where(x => x.Status == "infrastructure-failure" || x.PrimaryFailure is not null).ToList();
        var primary = relevant.FirstOrDefault(x => x.PrimaryFailure is not null) ?? relevant.FirstOrDefault()
            ?? new VerificationEvidence { CheckId = "unknown", EvidenceId = "unknown", Status = "infrastructure-failure" };
        if (relevant.Count > 0)
            relevant = relevant.OrderByDescending(x => ReferenceEquals(x, primary)).ToList();
        return BoundDiagnostic(new VerificationInfrastructureDiagnosticPayload
        {
            Code = code,
            Context = context,
            WorkItemId = workItemId,
            PrimaryCheckId = primary.CheckId,
            Checks = relevant.Count == 0 ? [CreateCheckDiagnostic(primary)] : relevant.Select(CreateCheckDiagnostic).ToList()
        });
    }

    private static VerificationInfrastructureCheckDiagnostic CreateCheckDiagnostic(VerificationEvidence evidence)
    {
        var failure = evidence.PrimaryFailure ?? new VerificationFailure("unknown", "execute", "Verification infrastructure failed without a primary diagnostic.");
        return new()
        {
            CheckId = evidence.CheckId, EvidenceId = evidence.EvidenceId,
            EvidencePath = evidence.EvidencePersisted ? PublicVerificationPath($"{evidence.EvidenceId}.json") : null,
            FailureKind = failure.Kind, FailureStage = failure.Stage,
            Summary = failure.Kind == "timeout" && evidence.TimeoutMilliseconds is not null
                ? $"Verification check {BoundDiagnosticText(evidence.CheckId, 96)} timed out after {FormatDuration(evidence.TimeoutMilliseconds.Value)}."
                : failure.Message,
            StartedAt = evidence.StartedAt, FinishedAt = evidence.FinishedAt,
            DurationMilliseconds = evidence.DurationMilliseconds, TimeoutMilliseconds = evidence.TimeoutMilliseconds,
            TimedOut = evidence.TimedOut, ExitCode = evidence.ExitCode,
            Stdout = new(PublicVerificationPath(evidence.Stdout.Path), evidence.Stdout.Tail, evidence.Stdout.Truncated),
            Stderr = new(PublicVerificationPath(evidence.Stderr.Path), evidence.Stderr.Tail, evidence.Stderr.Truncated),
            Termination = evidence.Termination is null ? null : new(evidence.Termination.Requested, evidence.Termination.EntireProcessTree,
                evidence.Termination.Succeeded, evidence.Termination.Error is null ? null : new(evidence.Termination.Error.Type, evidence.Termination.Error.Message)),
            SecondaryIssueCount = evidence.DiagnosticIssues.Count
        };
    }

    internal static JsonElement SerializeBoundedDiagnostic(VerificationInfrastructureDiagnosticPayload diagnostic)
    {
        const int maximumBytes = 16 * 1024;
        var candidate = BoundDiagnostic(diagnostic);
        while (SerializedSize(candidate) > maximumBytes
               && candidate.Checks.Any(x => x.Stdout.Tail.Length > 0 || x.Stderr.Tail.Length > 0))
        {
            candidate = candidate with
            {
                Checks = candidate.Checks.Select(x => x with
                {
                    Stdout = ReduceStreamTail(x.Stdout),
                    Stderr = ReduceStreamTail(x.Stderr)
                }).ToList()
            };
        }
        if (SerializedSize(candidate) > maximumBytes)
            candidate = candidate with { MetadataTruncated = true, Checks = candidate.Checks.Select(x => x with { Summary = BoundDiagnosticText(x.Summary, 64)!, Termination = x.Termination is null ? null : x.Termination with { Error = null } }).ToList() };
        while (SerializedSize(candidate) > maximumBytes && candidate.Checks.Any(x => x.Summary.Length > 0))
            candidate = candidate with { Checks = candidate.Checks.Select(x => x with { Summary = BoundDiagnosticText(x.Summary, Math.Max(0, x.Summary.Length / 2))! }).ToList() };
        while (SerializedSize(candidate) > maximumBytes && candidate.Checks.Count > 1)
            candidate = candidate with { MetadataTruncated = true, OmittedCheckCount = candidate.OmittedCheckCount + 1, Checks = candidate.Checks.Take(candidate.Checks.Count - 1).ToList() };

        // The fixed caps below make a primary-only payload comfortably smaller than the limit.
        // Keep this final reduction defensive so serialization never turns oversized diagnostics
        // into a new infrastructure exception.
        if (SerializedSize(candidate) > maximumBytes)
        {
            var primary = candidate.Checks[0];
            candidate = candidate with
            {
                MetadataTruncated = true,
                Code = BoundDiagnosticText(candidate.Code, 32)!,
                Context = BoundDiagnosticText(candidate.Context, 32)!,
                WorkItemId = BoundDiagnosticText(candidate.WorkItemId, 32),
                PrimaryCheckId = BoundDiagnosticText(primary.CheckId, 64)!,
                Checks = [primary with
                {
                    CheckId = BoundDiagnosticText(primary.CheckId, 64)!, EvidenceId = BoundDiagnosticText(primary.EvidenceId, 64)!, EvidencePath = null,
                    FailureKind = BoundDiagnosticText(primary.FailureKind, 32)!, FailureStage = BoundDiagnosticText(primary.FailureStage, 32)!,
                    Summary = BoundDiagnosticText(primary.Summary, 64)!, Stdout = new(null, "", true), Stderr = new(null, "", true),
                    Termination = primary.Termination is null ? null : primary.Termination with { Error = null }
                }]
            };
        }
        return JsonSerializer.SerializeToElement(candidate, DiagnosticJsonOptions);
    }

    private static int SerializedSize(VerificationInfrastructureDiagnosticPayload diagnostic) =>
        Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(diagnostic, DiagnosticJsonOptions));

    private static VerificationDiagnosticStream ReduceStreamTail(VerificationDiagnosticStream stream) =>
        stream.Tail.Length == 0 ? stream : stream with { Tail = RemoveTailPrefix(stream.Tail, Math.Max(1, stream.Tail.Length / 4)), Truncated = true };

    private static VerificationInfrastructureDiagnosticPayload BoundDiagnostic(VerificationInfrastructureDiagnosticPayload diagnostic)
    {
        var boundedChecks = diagnostic.Checks.Select(x => x with
        {
            CheckId = BoundDiagnosticText(x.CheckId, 256)!, EvidenceId = BoundDiagnosticText(x.EvidenceId, 256)!,
            EvidencePath = BoundDiagnosticText(x.EvidencePath, 512), FailureKind = BoundDiagnosticText(x.FailureKind, 64)!,
            FailureStage = BoundDiagnosticText(x.FailureStage, 64)!, Summary = BoundDiagnosticText(x.Summary)!,
            Stdout = x.Stdout with { Path = BoundDiagnosticText(x.Stdout.Path, 512) },
            Stderr = x.Stderr with { Path = BoundDiagnosticText(x.Stderr.Path, 512) },
            Termination = x.Termination is null ? null : x.Termination with
            {
                Error = x.Termination.Error is null ? null : x.Termination.Error with
                { Type = BoundDiagnosticText(x.Termination.Error.Type, 128), Message = BoundDiagnosticText(x.Termination.Error.Message) }
            }
        }).ToList();
        var primaryIndex = diagnostic.Checks.ToList().FindIndex(x => x.CheckId == diagnostic.PrimaryCheckId);
        if (primaryIndex > 0) (boundedChecks[0], boundedChecks[primaryIndex]) = (boundedChecks[primaryIndex], boundedChecks[0]);
        var metadataTruncated = diagnostic.MetadataTruncated || diagnostic.Code.Length > 128 || diagnostic.Context.Length > 128
            || diagnostic.WorkItemId?.Length > 128 || diagnostic.Checks.Zip(boundedChecks).Any(pair => pair.First != pair.Second);
        return diagnostic with
        {
            Code = BoundDiagnosticText(diagnostic.Code, 128)!, Context = BoundDiagnosticText(diagnostic.Context, 128)!,
            WorkItemId = BoundDiagnosticText(diagnostic.WorkItemId, 128), PrimaryCheckId = boundedChecks[0].CheckId,
            Checks = boundedChecks, MetadataTruncated = metadataTruncated
        };
    }

    private static string? BoundDiagnosticText(string? value, int maximumCharacters = 256)
    {
        if (value is null || value.Length <= maximumCharacters) return value;
        if (maximumCharacters == 0) return "";
        var length = maximumCharacters;
        if (char.IsHighSurrogate(value[length - 1])) length--;
        return value[..length];
    }

    private static string RemoveTailPrefix(string value, int count)
    {
        var index = Math.Min(count, value.Length);
        if (index < value.Length && char.IsLowSurrogate(value[index]) && index > 0) index++;
        return value[index..];
    }

    private static string BuildInfrastructureReason(VerificationInfrastructureDiagnosticPayload diagnostic, bool baseline)
    {
        var primary = diagnostic.Checks[0];
        var prefix = baseline ? "Repository fallback baseline verification" : $"Verification check {primary.CheckId}";
        var exit = primary.ExitCode is null ? "" : $" Exit code: {primary.ExitCode}.";
        var evidence = primary.EvidencePath is null ? " No evidence JSON was persisted." : $" Evidence: {primary.EvidencePath}.";
        return $"{prefix} could not execute: {primary.FailureKind} at {primary.FailureStage}. {primary.Summary}{exit}{evidence}";
    }

    private static string BuildInfrastructureResumeWhen(VerificationInfrastructureDiagnosticPayload diagnostic, bool baseline)
    {
        var primary = diagnostic.Checks[0];
        var condition = primary.FailureKind switch
        {
            "process-start-failure" => "Make the verification executable and working directory available",
            "output-capture-failure" => "Restore writable verification log storage",
            "evidence-persistence-failure" => "Restore writable verification evidence storage",
            "timeout" => primary.TimeoutMilliseconds is null
                ? $"Resolve the timeout for verification check {primary.CheckId}"
                : $"Allow verification check {primary.CheckId} to finish within its configured {FormatDuration(primary.TimeoutMilliseconds.Value)} timeout",
            "termination-failure" => "Ensure the timed-out verification process can be terminated",
            _ => $"Resolve the {primary.FailureKind} failure at {primary.FailureStage}"
        };
        return baseline ? $"{condition}, then cancel/restart the Factory run." : $"{condition}, then call factory_continue.";
    }

    private static string FormatDuration(long milliseconds)
    {
        if (milliseconds >= 60_000 && milliseconds % 60_000 == 0)
            return $"{milliseconds / 60_000} {(milliseconds == 60_000 ? "minute" : "minutes")}";
        if (milliseconds >= 1_000 && milliseconds % 1_000 == 0)
            return $"{milliseconds / 1_000} {(milliseconds == 1_000 ? "second" : "seconds")}";
        return milliseconds < 1_000 ? $"{milliseconds} ms" : $"{milliseconds / 1000d:0.###} seconds";
    }

    private async Task WriteInfrastructureFailureEventAsync(string runId, VerificationInfrastructureDiagnosticPayload diagnostic, CancellationToken cancellationToken) =>
        await events.WriteAsync(runId, "verification-infrastructure-failure", new
        {
            diagnostic.Code,
            diagnostic.Context,
            diagnostic.WorkItemId,
            diagnostic.PrimaryCheckId,
            checks = diagnostic.Checks.Select(x => new { x.CheckId, x.EvidenceId, x.EvidencePath, x.FailureKind, x.FailureStage, x.StartedAt, x.FinishedAt, x.DurationMilliseconds, x.TimeoutMilliseconds, x.TimedOut, stdoutPath = x.Stdout.Path, stdoutTruncated = x.Stdout.Truncated, stderrPath = x.Stderr.Path, stderrTruncated = x.Stderr.Truncated, x.ExitCode, x.Termination, x.SecondaryIssueCount }).ToArray()
        }, cancellationToken);

    private static string? PublicVerificationPath(string? path)
    {
        if (path is null) return null;
        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith(".idd/factory/current/", StringComparison.Ordinal)) return normalized;
        if (normalized.StartsWith("verification/", StringComparison.Ordinal)) normalized = normalized["verification/".Length..];
        return $".idd/factory/current/verification/{normalized}";
    }

    private static void RecordLastVerificationCycle(PlannedWorkItem? item, IEnumerable<string> evidenceRefs)
    {
        if (item is null) return;
        item.LastVerificationEvidenceRefs.Clear();
        item.LastVerificationEvidenceRefs.AddRange(evidenceRefs.Distinct(StringComparer.Ordinal));
    }

    private static void RecordEvidence(FactoryState state, PlannedWorkItem? item, IEnumerable<VerificationEvidence> evidence)
    {
        foreach (var record in evidence.Where(x => x.EvidencePersisted))
        {
            var relative = $"verification/{record.EvidenceId}.json";
            if (item is not null && !item.VerificationEvidenceRefs.Contains(relative, StringComparer.Ordinal)) item.VerificationEvidenceRefs.Add(relative);
            if (!state.VerificationEvidenceRefs.Contains(relative, StringComparer.Ordinal)) state.VerificationEvidenceRefs.Add(relative);
        }
    }

    private static IEnumerable<string> EvidenceReferences(IEnumerable<VerificationEvidence> evidence) =>
        evidence.Where(x => x.EvidencePersisted).Select(x => $"verification/{x.EvidenceId}.json");
}
