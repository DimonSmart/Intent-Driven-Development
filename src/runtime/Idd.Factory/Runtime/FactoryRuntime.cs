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
        if (await stateStore.LoadAsync(cancellationToken) is not null) return new("RUN_EXISTS", "unknown", "Use continue or cancel for the existing Factory run.");
        Directory.CreateDirectory(currentDirectory);
        Directory.CreateDirectory(Path.Combine(currentDirectory, "work-items"));
        Directory.CreateDirectory(Path.Combine(currentDirectory, "attempts"));
        Directory.CreateDirectory(Path.Combine(currentDirectory, "plan-revisions"));
        await File.WriteAllTextAsync(Path.Combine(currentDirectory, "request.md"), request, cancellationToken);
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
        return await ExecuteLoopAsync(state, cancellationToken);
    }

    public async Task<FactoryCliOutcome> ContinueAsync(CancellationToken cancellationToken, string? answerPath = null, VerificationConfirmation confirmation = VerificationConfirmation.None, bool? verificationPassed = null)
    {
        var state = await stateStore.LoadAsync(cancellationToken) ?? throw new FactoryStateException("MISSING_FACTORY_STATE", "No Factory run exists.");
        if (state.FactoryConfigurationHash != configuration.Hash) return new("FACTORY_CONFIGURATION_CHANGED", state.RunId, "Restore the pinned configuration or cancel and restart.");
        if (state.RunStatus == FactoryRunStatus.Cancelled) return new("CANCELLED", state.RunId);
        await ReconcileAsync(state, cancellationToken);

        if (state.PendingContinuation is { IsResumable: false }) return OutcomeFromBlocker(state, "TERMINAL_STOP");
        if (state.PendingContinuation is { Kind: ContinuationKind.VerificationGate, VerificationStage: VerificationContinuationStage.AwaitingConfirmation or VerificationContinuationStage.AwaitingManualResult } pending)
        {
            if (pending.VerificationStage == VerificationContinuationStage.AwaitingConfirmation && confirmation == VerificationConfirmation.None) return OutcomeFromBlocker(state, "VERIFICATION_CONFIRMATION_REQUIRED");
            if (pending.VerificationStage == VerificationContinuationStage.AwaitingManualResult && verificationPassed is null) return OutcomeFromBlocker(state, "VERIFICATION_RESULT_REQUIRED");
            var resolved = await ResolvePendingVerificationActionAsync(state, pending, confirmation, verificationPassed, cancellationToken);
            if (resolved is not null) return resolved;
        }
        if (state.PendingContinuation is { Kind: ContinuationKind.IntentGate } intent)
        {
            if (state.IntentSnapshotHash == HashIntent()) return OutcomeFromBlocker(state, "INTENT_REQUIRED");
            state.IntentSnapshotHash = null;
            state.PendingContinuation = intent with { Kind = ContinuationKind.SemanticInvocation };
        }
        if (state.PendingContinuation is { Kind: ContinuationKind.Clarification } clarification)
        {
            if (answerPath is null) return OutcomeFromBlocker(state, "NEEDS_CLARIFICATION");
            await RecordClarificationAsync(state, answerPath, cancellationToken);
            state.PendingContinuation = clarification with { Kind = ContinuationKind.SemanticInvocation };
        }
        else if (answerPath is not null) await RecordClarificationAsync(state, answerPath, cancellationToken);
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
                    FactoryCommandKind.RunFinalReview => await RunFinalReviewAsync(state, cancellationToken),
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
            SemanticOperationKind.FinalReview => RunFinalReviewAsync(state, cancellationToken),
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
        if (state.FinalReview is not { Verdict: "approved" } review || review.ReviewedPlanRevision != state.PlanRevision) throw new FactoryStateException("FINAL_REVIEW_REQUIRED", "Final review is stale or missing.");
    }

    private async Task<FactoryCliOutcome?> StopBlockedAsync(FactoryState state, CancellationToken cancellationToken)
    {
        if (state.Blocker is not null) return OutcomeFromBlocker(state, state.Blocker.Code);
        return await StopAsync(state, "FACTORY_BLOCKED", "No deterministic action is applicable.", "Resolve the blocker or cancel/restart.", cancellationToken,
            new(ContinuationKind.Terminal, state.Current?.Id, null, "FACTORY_BLOCKED", false));
    }

    private async Task<FactoryCliOutcome> HandleSemanticStopAsync(FactoryState state, PlannedWorkItem? item, BoundSemanticAgentResult result, SemanticOperationKind operation, string input, CancellationToken cancellationToken)
    {
        if (result.Outcome == "intent-required") IntentRequiredPayload.Validate(result.Payload);
        var code = result.Outcome.ToUpperInvariant().Replace('-', '_');
        var reason = result.Reason ?? $"Semantic operation stopped with {result.Outcome}.";
        var kind = result.Outcome switch { "needs-clarification" => ContinuationKind.Clarification, "intent-required" => ContinuationKind.IntentGate, "focused-handoff" => ContinuationKind.Terminal, _ => ContinuationKind.SemanticInvocation };
        var resumable = result.Outcome != "focused-handoff";
        if (item is not null) state.CurrentPhase = CurrentWorkPhase.Blocked;
        if (result.Outcome == "intent-required") state.IntentSnapshotHash = HashIntent();
        state.RunStatus = FactoryRunStatus.Blocked;
        state.Blocker = new(code, reason, resumable ? "Resolve the reported condition and continue." : "Continue outside Factory.", result.Payload?.Clone());
        state.PendingContinuation = new(kind, item?.Id, null, code, resumable, operation, input);
        await SaveAsync(state, cancellationToken);
        return OutcomeFromBlocker(state, code);
    }

    private async Task<FactoryCliOutcome> StopForAgentProtocolExceptionAsync(FactoryState state, AgentProtocolException exception, CancellationToken cancellationToken)
    {
        var existing = state.PendingContinuation is { IsResumable: true } value ? value : null;
        var hard = exception.Code.EndsWith("_BUDGET_EXHAUSTED", StringComparison.Ordinal) || exception.Code is "UNKNOWN_CAPABILITY" or "CAPABILITY_NOT_ALLOWED" or "INVALID_RUNTIME_STATE";
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
        foreach (var item in state.Remaining.Concat(state.Current is null ? [] : [state.Current])) if (!configuration.AllowedCapabilities.Contains(item.Capability)) throw new AgentProtocolException("CAPABILITY_NOT_ALLOWED", $"Capability '{item.Capability}' is not allowed.");
    }

    private async Task SaveAsync(FactoryState state, CancellationToken cancellationToken) => await stateStore.SaveAsync(state, state.Revision, cancellationToken);
    private static FactoryState CloneState(FactoryState state) => JsonSerializer.Deserialize<FactoryState>(JsonSerializer.Serialize(state, FactoryJson.Options), FactoryJson.Options)!;
    private static void ApplyCandidate(FactoryState state, FactoryState candidate)
    {
        state.PlanRevision = candidate.PlanRevision; state.NextWorkItemNumber = candidate.NextWorkItemNumber; state.RunStatus = candidate.RunStatus;
        state.Completed.Clear(); state.Completed.AddRange(candidate.Completed); state.Current = candidate.Current; state.CurrentPhase = candidate.CurrentPhase;
        state.Remaining.Clear(); state.Remaining.AddRange(candidate.Remaining); state.CurrentAttemptId = candidate.CurrentAttemptId; state.AttemptSequence = candidate.AttemptSequence;
        state.ReplanCount = candidate.ReplanCount; state.CorrectiveCycleCount = candidate.CorrectiveCycleCount; state.InitialPlanningCompleted = candidate.InitialPlanningCompleted;
        state.FinalVerificationPassed = candidate.FinalVerificationPassed; state.FinalVerificationPlanRevision = candidate.FinalVerificationPlanRevision;
        state.Blocker = candidate.Blocker; state.PendingContinuation = candidate.PendingContinuation; state.PendingVerificationSession = candidate.PendingVerificationSession;
        state.PendingReplanTrigger = candidate.PendingReplanTrigger; state.FinalReview = candidate.FinalReview; state.IntentSnapshotHash = candidate.IntentSnapshotHash;
    }

    private async Task RecordClarificationAsync(FactoryState state, string sourcePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Clarification answer file was not found.", sourcePath);
        var relative = $"clarifications/Q{state.ClarificationRefs.Count + 1:00000}.md";
        await WriteRuntimeArtifactAtomicallyAsync(Path.Combine(currentDirectory, relative), await File.ReadAllTextAsync(sourcePath, cancellationToken), cancellationToken);
        state.ClarificationRefs.Add(relative);
    }

    private string HashIntent()
    {
        var root = Path.Combine(workspace, ".idd", "intent");
        if (!Directory.Exists(root)) return "missing";
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        foreach (var path in Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal)) { hash.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(root, path))); hash.AppendData(File.ReadAllBytes(path)); }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string Version() => typeof(FactoryRuntime).Assembly.GetName().Version?.ToString() ?? "0.0.0";
}
