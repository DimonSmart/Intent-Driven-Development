using System.Text;
using System.Text.Json;
using Idd.Factory.Configuration;
using Idd.Factory.Domain;
using Idd.Factory.Finalization;
using Idd.Factory.Persistence;
using Idd.Factory.Telemetry;
using Idd.Factory.Verification;

namespace Idd.Factory.Runtime;

public sealed partial class FactoryRuntime(
    string workspace,
    FactoryConfiguration configuration,
    IFactoryStateStore stateStore,
    FactoryAgentExecutor agentExecutor,
    VerificationEngine verification,
    FactoryEventWriter events,
    IClock clock)
{
    private static readonly UTF8Encoding HumanReadableUtf8 = new(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly string workspace = workspace;
    private readonly FactoryConfiguration configuration = configuration;
    private readonly IFactoryStateStore stateStore = stateStore;
    private readonly FactoryAgentExecutor agentExecutor = agentExecutor;
    private readonly VerificationEngine verification = verification;
    private readonly FactoryEventWriter events = events;
    private readonly IClock clock = clock;
    private readonly string currentDirectory = Path.Combine(workspace, ".idd", "factory", "current");
    private readonly FactoryScheduler scheduler = new();
    private readonly FactoryStateValidator stateValidator = new();
    private readonly PlanRevisionWriter planRevisions = new(Path.Combine(workspace, ".idd", "factory", "current"), clock);

    public async Task<FactoryCliOutcome> RunAsync(string requestPath, string methodologyVersion, CancellationToken cancellationToken) =>
        await RunRequestAsync(await File.ReadAllTextAsync(requestPath, cancellationToken), methodologyVersion, cancellationToken);

    public async Task<FactoryCliOutcome> RunRequestAsync(string request, string methodologyVersion, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request)) throw new ArgumentException("Factory request cannot be empty.", nameof(request));
        ValidateUtf8Text(request, "INVALID_REQUEST_ENCODING", "Factory request");
        ValidateMaterializedRequest(request);
        if (await stateStore.LoadAsync(cancellationToken) is not null) return new("RUN_EXISTS", "unknown", "Use continue or cancel for the existing Factory run.");
        Directory.CreateDirectory(currentDirectory);
        Directory.CreateDirectory(Path.Combine(currentDirectory, "work-items"));
        Directory.CreateDirectory(Path.Combine(currentDirectory, "attempts"));
        Directory.CreateDirectory(Path.Combine(currentDirectory, "plan-revisions"));
        await File.WriteAllTextAsync(Path.Combine(currentDirectory, "request.md"), request, HumanReadableUtf8, cancellationToken);
        var state = new FactoryState
        {
            MethodologyVersion = methodologyVersion,
            RuntimeVersion = Version(),
            RunId = Guid.NewGuid().ToString("N"),
            FactoryConfigurationHash = configuration.Hash,
            RequestPath = "request.md"
        };
        await stateStore.CreateAsync(state, cancellationToken);
        await events.WriteAsync(state.RunId, "run-created", new { configurationHash = configuration.Hash }, cancellationToken);
        var baselineStop = await RunRepositoryFallbackBaselineAsync(state, cancellationToken);
        if (baselineStop is not null) return baselineStop;
        return await ExecuteLoopAsync(state, cancellationToken);
    }

    public async Task<FactoryCliOutcome> ContinueAsync(
        CancellationToken cancellationToken,
        VerificationConfirmation confirmation = VerificationConfirmation.None,
        bool? verificationPassed = null,
        string? userAnswer = null)
    {
        var state = await stateStore.LoadAsync(cancellationToken) ?? throw new FactoryStateException("MISSING_FACTORY_STATE", "No Factory run exists.");
        if (state.FactoryConfigurationHash != configuration.Hash) return new("FACTORY_CONFIGURATION_CHANGED", state.RunId, "Restore the pinned configuration or cancel and restart.");
        if (state.RunStatus == FactoryRunStatus.Cancelled) return new("CANCELLED", state.RunId);
        try { await ReconcileAsync(state, cancellationToken); }
        catch (AgentProtocolException exception) { return await StopForAgentProtocolExceptionAsync(state, exception, cancellationToken); }

        if (state.PendingContinuation is { Kind: ContinuationKind.UserQuestion })
        {
            if (string.IsNullOrWhiteSpace(userAnswer)) return OutcomeFromBlocker(state, "USER_DECISION_REQUIRED");
            ValidateUtf8Text(userAnswer, "INVALID_USER_ANSWER_ENCODING", "Factory user answer");
            var question = state.Blocker?.Reason;
            if (string.IsNullOrWhiteSpace(question))
                throw new FactoryStateException("CORRUPT_FACTORY_STATE", "User-question continuation has no persisted question.");
            await PersistPlanningAnswerAsync(question, userAnswer, cancellationToken);
            state.PendingContinuation = new(
                ContinuationKind.SemanticInvocation,
                null,
                null,
                "PLANNING_AFTER_USER_ANSWER",
                true,
                SemanticOperationKind.Planning);
            state.Blocker = null;
            state.RunStatus = FactoryRunStatus.Running;
            await events.WriteAsync(state.RunId, "user-answer-recorded", new { }, cancellationToken);
            await SaveAsync(state, cancellationToken);
        }
        else if (userAnswer is not null)
        {
            return new("UNEXPECTED_USER_ANSWER", state.RunId, "The current Factory continuation is not waiting for a planner question.", "Continue without a user answer, or cancel the run.");
        }

        if (state.PendingContinuation is { IsResumable: false }) return OutcomeFromBlocker(state, "TERMINAL_STOP");
        if (state.PendingContinuation is
            {
                Kind: ContinuationKind.VerificationGate,
                VerificationContext: "baseline",
                VerificationStage: VerificationContinuationStage.AwaitingConfirmation
            })
        {
            if (confirmation == VerificationConfirmation.None) return OutcomeFromBlocker(state, "VERIFICATION_CONFIRMATION_REQUIRED");
            if (confirmation == VerificationConfirmation.Decline)
            {
                return await StopAsync(
                    state,
                    "VERIFICATION_DECLINED",
                    "User declined running Factory with an already-failing repository fallback baseline.",
                    "Fix the repository baseline, then cancel/restart the Factory run.",
                    cancellationToken,
                    new(ContinuationKind.Terminal, null, "baseline", "VERIFICATION_DECLINED", false));
            }

            state.RepositoryFallbackBaselineAccepted = true;
            state.PendingContinuation = null;
            state.Blocker = null;
            state.RunStatus = FactoryRunStatus.Running;
            await events.WriteAsync(state.RunId, "repository-fallback-baseline-accepted", new { }, cancellationToken);
            await SaveAsync(state, cancellationToken);
        }
        if (state.PendingContinuation is { Kind: ContinuationKind.VerificationGate, VerificationStage: VerificationContinuationStage.AwaitingConfirmation or VerificationContinuationStage.AwaitingManualResult } pending)
        {
            if (pending.VerificationStage == VerificationContinuationStage.AwaitingConfirmation && confirmation == VerificationConfirmation.None) return OutcomeFromBlocker(state, "VERIFICATION_CONFIRMATION_REQUIRED");
            if (pending.VerificationStage == VerificationContinuationStage.AwaitingManualResult && verificationPassed is null) return OutcomeFromBlocker(state, "VERIFICATION_RESULT_REQUIRED");
            var resolved = await ResolvePendingVerificationActionAsync(state, pending, confirmation, verificationPassed, cancellationToken);
            if (resolved is not null) return resolved;
        }
        if (state.PlanningCycleCount == 0 && state.Current is null && state.PendingContinuation is null)
        {
            var baselineStop = await RunRepositoryFallbackBaselineAsync(state, cancellationToken);
            if (baselineStop is not null) return baselineStop;
        }
        state.Blocker = null;
        state.RunStatus = FactoryRunStatus.Running;
        if (state.Current is not null && state.CurrentPhase == CurrentWorkPhase.Blocked) state.CurrentPhase = CurrentWorkPhase.Ready;
        await SaveAsync(state, cancellationToken);
        return await ExecuteLoopAsync(state, cancellationToken);
    }

    public async Task<FactoryCliOutcome> CancelAsync(CancellationToken cancellationToken)
    {
        var state = await stateStore.LoadAsync(cancellationToken) ?? throw new FactoryStateException("MISSING_FACTORY_STATE", "No Factory run exists.");
        state.RunStatus = FactoryRunStatus.Cancelled;
        state.Blocker = new("CANCELLED", "The user cancelled the run.", "Start a new Factory run.");
        state.PendingContinuation = new(ContinuationKind.Terminal, state.Current?.Id, null, "CANCELLED", false);
        await SaveAsync(state, cancellationToken);
        return new("CANCELLED", state.RunId, "Product changes and Factory diagnostics were preserved.");
    }

    private async Task<FactoryCliOutcome?> RunRepositoryFallbackBaselineAsync(FactoryState state, CancellationToken cancellationToken)
    {
        if (state.RepositoryFallbackBaselineAccepted || File.Exists(Path.Combine(workspace, ".idd", "verification.yaml"))) return null;

        var baseline = await verification.RunContextAsync("final", cancellationToken);
        RecordEvidence(state, null, baseline.Evidence);
        var evidenceRefs = baseline.Evidence.Select(x => $"verification/{x.EvidenceId}.json").ToArray();
        await events.WriteAsync(state.RunId, "repository-fallback-baseline", new { baseline.Status, evidenceRefs }, cancellationToken);

        if (baseline.Status is VerificationStatus.Passed or VerificationStatus.NoChecks)
        {
            if (baseline.Evidence.Count > 0) await SaveAsync(state, cancellationToken);
            return null;
        }

        var checkIds = baseline.Evidence.Select(x => x.CheckId).Distinct(StringComparer.Ordinal).ToArray();
        var checkSummary = checkIds.Length == 0 ? "repository fallback" : string.Join(", ", checkIds);
        var evidenceSummary = evidenceRefs.Length == 0 ? "none" : string.Join(", ", evidenceRefs);

        if (baseline.Status == VerificationStatus.Failed)
        {
            return await StopAsync(
                state,
                "VERIFICATION_CONFIRMATION_REQUIRED",
                $"Repository fallback baseline already fails before Factory planning. Failed checks: {checkSummary}. Evidence: {evidenceSummary}. Repository-wide subtask verification cannot reliably attribute that failure to the current work item.",
                "Fix the repository baseline and cancel/restart, or continue with --confirmation approve to accept the existing red baseline.",
                cancellationToken,
                new(
                    ContinuationKind.VerificationGate,
                    null,
                    "baseline",
                    "VERIFICATION_CONFIRMATION_REQUIRED",
                    true,
                    VerificationStage: VerificationContinuationStage.AwaitingConfirmation));
        }

        var terminal = new PendingContinuation(ContinuationKind.Terminal, null, null, "BASELINE_VERIFICATION", false);
        return baseline.Status switch
        {
            VerificationStatus.InfrastructureFailure => await StopAsync(
                state,
                "BASELINE_VERIFICATION_INFRASTRUCTURE_FAILURE",
                $"Repository fallback baseline could not execute before Factory planning. Checks: {checkSummary}. Evidence: {evidenceSummary}.",
                "Fix the verification infrastructure, then cancel/restart the Factory run.",
                cancellationToken,
                terminal),
            _ => await StopAsync(
                state,
                "BASELINE_VERIFICATION_ACTION_REQUIRED",
                $"Repository fallback baseline ended as {baseline.Status} before Factory planning.",
                "Resolve the baseline verification condition, then cancel/restart the Factory run.",
                cancellationToken,
                terminal)
        };
    }

    private async Task<FactoryCliOutcome> ExecuteLoopAsync(FactoryState state, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FactoryCommand command;
            try { ValidateRuntimeState(state); command = scheduler.Decide(state); }
            catch (AgentProtocolException exception) { return await StopForAgentProtocolExceptionAsync(state, exception, cancellationToken); }
            catch (VerificationException exception) { return await StopAsync(state, exception.Code, exception.Message, "Resolve verification configuration, then continue.", cancellationToken); }
            await events.WriteAsync(state.RunId, "scheduler-decision", new { command.Kind, command.WorkItemId, command.VerificationContext, state.PlanRevision }, cancellationToken);
            try
            {
                FactoryCliOutcome? stop = command.Kind switch
                {
                    FactoryCommandKind.Plan => await PlanAsync(state, cancellationToken),
                    FactoryCommandKind.ResumePendingOperation => await ResumeSemanticOperationAsync(state, cancellationToken),
                    FactoryCommandKind.RunVerification => await RunVerificationAsync(state, command.WorkItemId, command.VerificationContext!, cancellationToken),
                    FactoryCommandKind.SelectNextWork => await SelectNextWorkAsync(state, cancellationToken),
                    FactoryCommandKind.DispatchWork => await DispatchWorkAsync(state, command.WorkItemId!, cancellationToken),
                    FactoryCommandKind.RunFinalVerification => await RunVerificationAsync(state, null, "final", cancellationToken),
                    FactoryCommandKind.Finalize => await FinalizeAsync(state, cancellationToken),
                    FactoryCommandKind.StopBlocked => await StopBlockedAsync(state, cancellationToken),
                    _ => throw new ArgumentOutOfRangeException()
                };
                if (stop is not null) return stop;
            }
            catch (AgentProtocolException exception) { return await StopForAgentProtocolExceptionAsync(state, exception, cancellationToken); }
            catch (VerificationException exception) { return await StopAsync(state, exception.Code, exception.Message, "Resolve verification configuration, then continue.", cancellationToken, state.PendingContinuation); }
        }
    }

    private Task<FactoryCliOutcome?> ResumeSemanticOperationAsync(FactoryState state, CancellationToken cancellationToken)
    {
        var continuation = state.PendingContinuation ?? throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Resume requires a pending continuation.");
        return continuation.Operation switch
        {
            SemanticOperationKind.Planning => PlanAsync(state, cancellationToken),
            SemanticOperationKind.WorkItemExecution => DispatchWorkAsync(state, continuation.WorkItemId!, cancellationToken),
            _ => throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Unsupported semantic continuation.")
        };
    }

    private async Task<FactoryCliOutcome?> FinalizeAsync(FactoryState state, CancellationToken cancellationToken)
    {
        EnsureFinalizationPreconditions(state);
        return new("COMPLETED", state.RunId, ResultDirectory: await new FinalizeHandler(workspace).FinalizeAsync(state, cancellationToken));
    }

    private void EnsureFinalizationPreconditions(FactoryState state)
    {
        if (state.Current is not null || state.Remaining.Count != 0) throw new FactoryStateException("FINAL_VERIFICATION_FAILED", "Future work remains incomplete.");
        if (state.CurrentAttemptId is not null || state.PendingVerificationSession is not null || state.PendingContinuation is not null) throw new FactoryStateException("FINAL_VERIFICATION_FAILED", "An operation is still active.");
        if (!state.FinalVerificationPassed || state.FinalVerificationPlanRevision != state.PlanRevision) throw new FactoryStateException("FINAL_VERIFICATION_FAILED", "Strict final verification is stale or missing.");
    }

    private async Task<FactoryCliOutcome?> StopBlockedAsync(FactoryState state, CancellationToken cancellationToken)
    {
        if (state.Blocker is not null) return OutcomeFromBlocker(state, state.Blocker.Code);
        return await StopAsync(state, "FACTORY_BLOCKED", "No deterministic action is applicable.", "Resolve the blocker or cancel/restart.", cancellationToken,
            new(ContinuationKind.Terminal, state.Current?.Id, null, "FACTORY_BLOCKED", false));
    }

    private async Task<FactoryCliOutcome> StopForAgentProtocolExceptionAsync(FactoryState state, AgentProtocolException exception, CancellationToken cancellationToken)
    {
        if (exception.Code == "AGENT_TRANSPORT_FAILURE"
            && state.CurrentAttemptId is { } attemptId
            && state.Current is { AttemptCount: > 0 } current
            && current.CurrentAttemptId == attemptId)
            current.AttemptCount--;

        var existing = state.PendingContinuation is { IsResumable: true } value ? value : null;
        var hard = exception.Code.EndsWith("_BUDGET_EXHAUSTED", StringComparison.Ordinal) || exception.Code is "UNKNOWN_CAPABILITY" or "INVALID_RUNTIME_STATE";
        return await StopAsync(state, exception.Code, exception.Message, hard || existing is null ? "Cancel/restart after resolving the condition." : "Resolve the condition, then continue the exact operation.", cancellationToken,
            hard || existing is null ? new(ContinuationKind.Terminal, state.Current?.Id, null, exception.Code, false) : existing);
    }

    private async Task<FactoryCliOutcome> StopAsync(FactoryState state, string code, string reason, string resume, CancellationToken cancellationToken, PendingContinuation? continuation = null, JsonElement? payload = null)
    {
        state.RunStatus = FactoryRunStatus.Blocked;
        state.Blocker = new(code, reason, resume, payload);
        if (continuation is not null) state.PendingContinuation = continuation;
        await SaveAsync(state, cancellationToken);
        return new(code, state.RunId, reason, resume, Payload: payload);
    }

    private FactoryCliOutcome OutcomeFromBlocker(FactoryState state, string fallback) => new(state.Blocker?.Code ?? fallback, state.RunId, state.Blocker?.Reason, state.Blocker?.ResumeWhen, Payload: state.Blocker?.Payload);

    private void ValidateRuntimeState(FactoryState state)
    {
        stateValidator.Validate(state);
        if (state.Completed.Count + state.Remaining.Count + (state.Current is null ? 0 : 1) > configuration.Limits.MaxWorkItems) throw new AgentProtocolException("WORK_EXPANSION_BUDGET_EXHAUSTED", "Factory work exceeds the configured maximum.");
    }

    private async Task SaveAsync(FactoryState state, CancellationToken cancellationToken) => await stateStore.SaveAsync(state, state.Revision, cancellationToken);
    private static FactoryState CloneState(FactoryState state) => JsonSerializer.Deserialize<FactoryState>(JsonSerializer.Serialize(state, FactoryJson.Options), FactoryJson.Options)!;
    private static void ApplyCandidate(FactoryState state, FactoryState candidate)
    {
        state.PlanRevision = candidate.PlanRevision; state.NextWorkItemNumber = candidate.NextWorkItemNumber; state.RunStatus = candidate.RunStatus;
        state.Completed.Clear(); state.Completed.AddRange(candidate.Completed); state.Current = candidate.Current; state.CurrentPhase = candidate.CurrentPhase;
        state.Remaining.Clear(); state.Remaining.AddRange(candidate.Remaining); state.CurrentAttemptId = candidate.CurrentAttemptId; state.AttemptSequence = candidate.AttemptSequence;
        state.PlanningCycleCount = candidate.PlanningCycleCount;
        state.PlannedThroughCompletedCount = candidate.PlannedThroughCompletedCount;
        state.RepositoryFallbackBaselineAccepted = candidate.RepositoryFallbackBaselineAccepted;
        state.FinalVerificationPassed = candidate.FinalVerificationPassed; state.FinalVerificationPlanRevision = candidate.FinalVerificationPlanRevision;
        state.Blocker = candidate.Blocker; state.PendingContinuation = candidate.PendingContinuation; state.PendingVerificationSession = candidate.PendingVerificationSession;
        state.FactoryRunChangedPaths.Clear(); state.FactoryRunChangedPaths.AddRange(candidate.FactoryRunChangedPaths);
    }

    private static void ValidateUtf8Text(string text, string code, string label)
    {
        if (text.Contains('\uFFFD'))
            throw new FactoryStateException(code, $"{label} contains Unicode replacement character U+FFFD and may have been corrupted before Factory received it.");
        try { _ = StrictUtf8.GetByteCount(text); }
        catch (EncoderFallbackException)
        {
            throw new FactoryStateException(code, $"{label} contains invalid Unicode data that cannot be represented as UTF-8 without replacement.");
        }
    }

    private static void ValidateMaterializedRequest(string request)
    {
        var hasSuppliedFileEnvelope =
            request.Contains("# Files pasted by the user:", StringComparison.OrdinalIgnoreCase) ||
            request.Contains("# Files mentioned by the user:", StringComparison.OrdinalIgnoreCase);
        if (!hasSuppliedFileEnvelope) return;

        var normalized = request.Replace('\\', '/');
        if (!normalized.Contains("/.codex/attachments/", StringComparison.OrdinalIgnoreCase) ||
            !normalized.Contains("pasted-text.txt", StringComparison.OrdinalIgnoreCase)) return;

        throw new FactoryStateException(
            "UNMATERIALIZED_REQUEST_INPUT",
            "Factory request contains a host-local pasted-text reference instead of the supplied text. Materialize the exact user-supplied content into a self-contained request before starting Factory.");
    }

    private static string Version() => typeof(FactoryRuntime).Assembly.GetName().Version?.ToString() ?? "0.0.0";
}