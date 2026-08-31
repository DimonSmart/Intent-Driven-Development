using System.Text.Json;
using Idd.Factory.Agents;
using Idd.Factory.Configuration;
using Idd.Factory.Domain;
using Idd.Factory.Finalization;
using Idd.Factory.Persistence;
using Idd.Factory.State;
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
    private readonly GraphMutationWriter graphMutations = new(Path.Combine(workspace, ".idd", "factory", "current"), clock);

    public async Task<FactoryCliOutcome> RunAsync(string requestPath, string methodologyVersion, CancellationToken cancellationToken) =>
        await RunRequestAsync(await File.ReadAllTextAsync(requestPath, cancellationToken), methodologyVersion, cancellationToken);

    public async Task<FactoryCliOutcome> RunRequestAsync(string request, string methodologyVersion, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request)) throw new ArgumentException("Factory request cannot be empty.", nameof(request));
        DetectLegacyState();
        if (await stateStore.LoadAsync(cancellationToken) is not null)
            return new("RUN_EXISTS", "unknown", "Use continue or cancel for the existing Factory run.");

        Directory.CreateDirectory(currentDirectory);
        Directory.CreateDirectory(Path.Combine(currentDirectory, "work-items"));
        Directory.CreateDirectory(Path.Combine(currentDirectory, "attempts"));
        Directory.CreateDirectory(Path.Combine(currentDirectory, "graph", "mutations"));
        await File.WriteAllTextAsync(Path.Combine(currentDirectory, "request.md"), request, cancellationToken);

        var state = new FactoryState
        {
            MethodologyVersion = methodologyVersion,
            RuntimeVersion = Version(),
            RunId = Guid.NewGuid().ToString("N"),
            Revision = 0,
            GraphRevision = 0,
            FactoryConfigurationHash = configuration.Hash,
            RequestPath = "request.md"
        };
        await stateStore.CreateAsync(state, cancellationToken);
        await events.WriteAsync(state.RunId, "run-created", new { configurationHash = configuration.Hash }, cancellationToken);
        return await ExecuteLoopAsync(state, cancellationToken);
    }

    public async Task<FactoryCliOutcome> ContinueAsync(
        CancellationToken cancellationToken,
        string? answerPath = null,
        VerificationConfirmation confirmation = VerificationConfirmation.None,
        bool? verificationPassed = null)
    {
        DetectLegacyState();
        var state = await stateStore.LoadAsync(cancellationToken)
            ?? throw new FactoryStateException("MISSING_FACTORY_STATE", "No Factory run exists.");
        if (state.FactoryConfigurationHash != configuration.Hash)
            return new("FACTORY_CONFIGURATION_CHANGED", state.RunId, "Restore the Factory configuration used to start this run or cancel and restart.");
        if (state.RunStatus == FactoryRunStatus.Cancelled) return new("CANCELLED", state.RunId);

        await ReconcileAsync(state, cancellationToken);

        if (state.PendingContinuation is { IsResumable: false })
            return OutcomeFromBlocker(state, "TERMINAL_STOP");

        if (state.PendingContinuation is { Kind: ContinuationKind.VerificationGate, VerificationStage: VerificationContinuationStage.AwaitingConfirmation or VerificationContinuationStage.AwaitingManualResult } pendingVerification)
        {
            if (pendingVerification.VerificationStage == VerificationContinuationStage.AwaitingConfirmation && confirmation == VerificationConfirmation.None)
                return OutcomeFromBlocker(state, "VERIFICATION_CONFIRMATION_REQUIRED");
            if (pendingVerification.VerificationStage == VerificationContinuationStage.AwaitingManualResult && verificationPassed is null)
                return OutcomeFromBlocker(state, "VERIFICATION_RESULT_REQUIRED");
            var resolved = await ResolvePendingVerificationActionAsync(state, pendingVerification, confirmation, verificationPassed, cancellationToken);
            if (resolved is not null) return resolved;
        }

        if (state.PendingContinuation is { Kind: ContinuationKind.IntentGate } intentContinuation)
        {
            if (state.IntentSnapshotHash == HashIntent())
                return OutcomeFromBlocker(state, "INTENT_REQUIRED");
            state.IntentSnapshotHash = null;
            state.PendingContinuation = intentContinuation with { Kind = ContinuationKind.SemanticInvocation };
            state.Blocker = null;
            state.RunStatus = FactoryRunStatus.Running;
            await SaveAsync(state, cancellationToken);
        }

        if (state.PendingContinuation is { Kind: ContinuationKind.Clarification } clarificationContinuation)
        {
            if (answerPath is null) return OutcomeFromBlocker(state, "NEEDS_CLARIFICATION");
            await RecordClarificationAsync(state, answerPath, cancellationToken);
            state.PendingContinuation = clarificationContinuation with { Kind = ContinuationKind.SemanticInvocation };
            state.Blocker = null;
            state.RunStatus = FactoryRunStatus.Running;
            await SaveAsync(state, cancellationToken);
        }
        else if (answerPath is not null)
        {
            await RecordClarificationAsync(state, answerPath, cancellationToken);
        }

        if (state.PendingContinuation is { Kind: ContinuationKind.SemanticInvocation } semantic)
        {
            if (semantic.WorkItemId is { } workItemId)
            {
                var item = state.WorkItems.Single(x => x.Id == workItemId);
                if (item.Status == WorkItemStatus.Blocked && item.DefinitionState == WorkDefinitionState.Executable)
                    item.Status = WorkItemStatus.Ready;
            }
            state.Blocker = null;
            state.RunStatus = FactoryRunStatus.Running;
            await SaveAsync(state, cancellationToken);
        }
        else if (state.PendingContinuation is { Kind: ContinuationKind.VerificationGate })
        {
            state.Blocker = null;
            state.RunStatus = FactoryRunStatus.Running;
            await SaveAsync(state, cancellationToken);
        }

        return await ExecuteLoopAsync(state, cancellationToken);
    }

    public async Task<FactoryCliOutcome> CancelAsync(CancellationToken cancellationToken)
    {
        var state = await stateStore.LoadAsync(cancellationToken)
            ?? throw new FactoryStateException("MISSING_FACTORY_STATE", "No Factory run exists.");
        state.RunStatus = FactoryRunStatus.Cancelled;
        state.Blocker = new("CANCELLED", "The user cancelled the run.", "Start a new Factory run.");
        state.PendingContinuation = new(ContinuationKind.Terminal, null, null, "CANCELLED", false);
        foreach (var item in state.WorkItems.Where(x => x.Status is not WorkItemStatus.Completed and not WorkItemStatus.Superseded and not WorkItemStatus.Cancelled))
            item.Status = WorkItemStatus.Cancelled;
        await SaveAsync(state, cancellationToken);
        await events.WriteAsync(state.RunId, "run-cancelled", new { state.CurrentAttemptId }, cancellationToken);
        return new("CANCELLED", state.RunId, "Product changes and Factory diagnostics were preserved.");
    }

    private async Task<FactoryCliOutcome> ExecuteLoopAsync(FactoryState state, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (NormalizeLifecycle(state)) await SaveAsync(state, cancellationToken);

            FactoryCommand command;
            try
            {
                ValidateRuntimeGraph(state);
                command = scheduler.Decide(state);
            }
            catch (AgentProtocolException exception)
            {
                return await StopForAgentProtocolExceptionAsync(state, exception, cancellationToken);
            }
            catch (VerificationException exception)
            {
                return await StopAsync(state, exception.Code, exception.Message, "Resolve the verification configuration, then continue.", cancellationToken,
                    state.PendingContinuation ?? new PendingContinuation(ContinuationKind.Terminal, null, null, exception.Code, false));
            }

            await events.WriteAsync(state.RunId, "scheduler-decision", new { command.Kind, command.WorkItemId, command.VerificationContext, state.GraphRevision }, cancellationToken);

            try
            {
                FactoryCliOutcome? stop = command.Kind switch
                {
                    FactoryCommandKind.Decompose => await DecomposeAsync(state, cancellationToken),
                    FactoryCommandKind.ResumePendingOperation => await ResumeSemanticOperationAsync(state, cancellationToken),
                    FactoryCommandKind.RunVerification => await RunVerificationAsync(state, command.WorkItemId, command.VerificationContext!, cancellationToken),
                    FactoryCommandKind.RefineWork => await RefineWorkAsync(state, command.WorkItemId!, cancellationToken),
                    FactoryCommandKind.DispatchWork => await DispatchWorkAsync(state, command.WorkItemId!, cancellationToken),
                    FactoryCommandKind.RunGlobalReplan => await ReplanAsync(state, cancellationToken),
                    FactoryCommandKind.RunFinalVerification => await RunVerificationAsync(state, null, "final", cancellationToken),
                    FactoryCommandKind.CreateFinalReview => await CreateFinalReviewAsync(state, cancellationToken),
                    FactoryCommandKind.Finalize => await FinalizeAsync(state, cancellationToken),
                    FactoryCommandKind.StopBlocked => await StopBlockedGraphAsync(state, cancellationToken),
                    _ => throw new ArgumentOutOfRangeException()
                };
                if (stop is not null) return stop;
                if (command.Kind == FactoryCommandKind.Finalize)
                    throw new InvalidOperationException("Finalization returned without a terminal outcome.");
            }
            catch (AgentProtocolException exception)
            {
                return await StopForAgentProtocolExceptionAsync(state, exception, cancellationToken);
            }
            catch (VerificationException exception)
            {
                var continuation = state.PendingContinuation ?? new PendingContinuation(
                    ContinuationKind.VerificationGate,
                    command.WorkItemId,
                    command.VerificationContext,
                    exception.Code,
                    true);
                return await StopAsync(state, exception.Code, exception.Message, "Resolve the verification configuration, then continue.", cancellationToken, continuation);
            }
        }
    }

    private async Task<FactoryCliOutcome?> ResumeSemanticOperationAsync(FactoryState state, CancellationToken cancellationToken)
    {
        var continuation = state.PendingContinuation
            ?? throw new FactoryStateException("CORRUPT_FACTORY_STATE", "A resume command requires a pending continuation.");
        return continuation.Operation switch
        {
            SemanticOperationKind.Decomposition => await DecomposeAsync(state, cancellationToken),
            SemanticOperationKind.ScopedRefinement => await RefineWorkAsync(state, continuation.WorkItemId!, cancellationToken),
            SemanticOperationKind.WorkItemExecution => await DispatchWorkAsync(state, continuation.WorkItemId!, cancellationToken),
            SemanticOperationKind.GlobalReplan => await ReplanAsync(state, cancellationToken),
            _ => throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Unsupported semantic continuation operation.")
        };
    }

    private async Task<FactoryCliOutcome?> FinalizeAsync(FactoryState state, CancellationToken cancellationToken)
    {
        EnsureFinalizationPreconditions(state);
        var directory = await new FinalizeHandler(workspace).FinalizeAsync(state, cancellationToken);
        return new("COMPLETED", state.RunId, ResultDirectory: directory);
    }

    private void EnsureFinalizationPreconditions(FactoryState state)
    {
        if (state.WorkItems.Any(x => x.Status is not WorkItemStatus.Completed and not WorkItemStatus.Superseded and not WorkItemStatus.Cancelled))
            throw new FactoryStateException("FINAL_VERIFICATION_FAILED", "Required task-graph work remains incomplete.");
        if (state.CurrentAttemptId is not null || state.PendingVerificationSession is not null || state.PendingContinuation is not null)
            throw new FactoryStateException("FINAL_VERIFICATION_FAILED", "A semantic or verification operation is still active.");
        if (!state.FinalVerificationPassed || state.FinalVerificationGraphRevision != state.GraphRevision)
            throw new FactoryStateException("FINAL_VERIFICATION_FAILED", "Strict final verification has not passed for the current graph.");
        if (state.FinalReview is not { Verdict: "approved" } review || review.ReviewedGraphRevision != state.GraphRevision)
            throw new FactoryStateException("FINAL_REVIEW_REQUIRED", "The mandatory final integrated semantic review is not approved for the current graph.");
    }

    private async Task<FactoryCliOutcome?> StopBlockedGraphAsync(FactoryState state, CancellationToken cancellationToken)
    {
        if (state.Blocker is not null)
        {
            if (state.RunStatus != FactoryRunStatus.Blocked)
            {
                state.RunStatus = FactoryRunStatus.Blocked;
                await SaveAsync(state, cancellationToken);
            }
            return OutcomeFromBlocker(state, state.Blocker.Code);
        }

        var waiting = state.WorkItems
            .Where(x => x.Status is WorkItemStatus.Waiting or WorkItemStatus.Blocked or WorkItemStatus.Failed)
            .Select(x => x.Id)
            .ToArray();
        return await StopAsync(
            state,
            "TASK_GRAPH_BLOCKED",
            waiting.Length == 0 ? "The task graph has required work but no deterministic action is applicable." : $"Required work is not runnable: {string.Join(", ", waiting)}.",
            "Resolve the reported blocker or cancel/restart the run.",
            cancellationToken,
            new PendingContinuation(ContinuationKind.Terminal, null, null, "TASK_GRAPH_BLOCKED", false));
    }

    private async Task<FactoryCliOutcome> HandleSemanticStopAsync(
        FactoryState state,
        WorkItemState? item,
        AgentResultEnvelope result,
        SemanticOperationKind operation,
        string input,
        CancellationToken cancellationToken)
    {
        if (result.Outcome == "intent-required") IntentRequiredPayload.Validate(result.Payload);
        var code = result.Outcome.ToUpperInvariant().Replace('-', '_');
        var reason = string.IsNullOrWhiteSpace(result.Reason)
            ? result.Outcome == "intent-required" ? "Durable product intent is missing decisions required for semantic work." : $"Semantic operation stopped with {result.Outcome}."
            : result.Reason;
        var resume = result.Outcome switch
        {
            "needs-clarification" => "Provide the requested clarification and continue.",
            "intent-required" => "Update the listed durable intent decisions in .idd/intent, then run continue.",
            "focused-handoff" => "Continue outside Factory with the focused handoff result.",
            _ => "Resolve the reported condition and continue."
        };
        var continuationKind = result.Outcome switch
        {
            "needs-clarification" => ContinuationKind.Clarification,
            "intent-required" => ContinuationKind.IntentGate,
            "focused-handoff" => ContinuationKind.Terminal,
            _ => ContinuationKind.SemanticInvocation
        };
        var resumable = result.Outcome != "focused-handoff";
        if (item is not null && item.DefinitionState == WorkDefinitionState.Executable)
            item.Status = WorkItemStatus.Blocked;
        if (result.Outcome == "intent-required") state.IntentSnapshotHash = HashIntent();
        state.RunStatus = FactoryRunStatus.Blocked;
        state.Blocker = new(code, reason, resume, result.Payload?.Clone());
        state.PendingContinuation = new(continuationKind, item?.Id, null, code, resumable, operation, input);
        await SaveAsync(state, cancellationToken);
        return new(code, state.RunId, reason, resume, Payload: result.Payload?.Clone());
    }

    private async Task<FactoryCliOutcome> StopForAgentProtocolExceptionAsync(FactoryState state, AgentProtocolException exception, CancellationToken cancellationToken)
    {
        var hardTerminal = exception.Code.EndsWith("_BUDGET_EXHAUSTED", StringComparison.Ordinal) ||
            exception.Code is "UNKNOWN_CAPABILITY" or "CAPABILITY_NOT_ALLOWED" or "INVALID_RUNTIME_GRAPH";
        var existingExactContinuation = state.PendingContinuation is { IsResumable: true } existing &&
            (existing.Kind != ContinuationKind.SemanticInvocation || existing.Operation != SemanticOperationKind.None)
            ? existing
            : null;
        var resumable = !hardTerminal && existingExactContinuation is not null;
        var continuation = resumable
            ? existingExactContinuation!
            : new PendingContinuation(ContinuationKind.Terminal, null, null, exception.Code, false);
        var resume = resumable
            ? "Resolve the reported protocol condition, then continue the exact persisted operation."
            : "The run cannot continue deterministically from this state. Cancel/restart after resolving the condition.";
        return await StopAsync(state, exception.Code, exception.Message, resume, cancellationToken, continuation);
    }

    private async Task<FactoryCliOutcome> StopAsync(
        FactoryState state,
        string code,
        string reason,
        string resume,
        CancellationToken cancellationToken,
        PendingContinuation? continuation = null,
        JsonElement? payload = null)
    {
        state.RunStatus = FactoryRunStatus.Blocked;
        state.Blocker = new(code, reason, resume, payload);
        if (continuation is not null) state.PendingContinuation = continuation;
        await SaveAsync(state, cancellationToken);
        await events.WriteAsync(state.RunId, "run-blocked", new { code, reason, resume, payload }, cancellationToken);
        return new(code, state.RunId, reason, resume, Payload: payload);
    }

    private FactoryCliOutcome OutcomeFromBlocker(FactoryState state, string fallbackCode)
    {
        var blocker = state.Blocker;
        return new(blocker?.Code ?? fallbackCode, state.RunId, blocker?.Reason, blocker?.ResumeWhen, Payload: blocker?.Payload);
    }

    private bool NormalizeLifecycle(FactoryState state)
    {
        var changed = false;
        foreach (var item in state.WorkItems.OrderBy(x => x.Sequence).ThenBy(x => x.Id, StringComparer.Ordinal))
        {
            if (item.DefinitionState != WorkDefinitionState.Executable || item.Status is not (WorkItemStatus.Planned or WorkItemStatus.Waiting)) continue;
            if (!DependenciesCompleted(state, item)) continue;
            item.Status = WorkItemStatus.Ready;
            changed = true;
        }
        return changed;
    }

    private static bool DependenciesCompleted(FactoryState state, WorkItemState item) =>
        item.Dependencies.All(id => state.WorkItems.Single(x => x.Id == id).Status == WorkItemStatus.Completed);

    private void ValidateRuntimeGraph(FactoryState state)
    {
        stateValidator.Validate(state);
        if (state.WorkItems.Count > configuration.Limits.MaxWorkItems)
            throw new AgentProtocolException("WORK_EXPANSION_BUDGET_EXHAUSTED", $"Task graph exceeds the configured maximum of {configuration.Limits.MaxWorkItems} work items.");
        foreach (var item in state.WorkItems.Where(x => x.DefinitionState == WorkDefinitionState.Executable))
        {
            if (!configuration.AllowedCapabilities.Contains(item.Capability!))
                throw new AgentProtocolException("CAPABILITY_NOT_ALLOWED", $"Capability '{item.Capability}' is not allowed by the pinned Factory configuration.");
        }
        verification.ValidateCheckIds(state.WorkItems.SelectMany(x => x.VerificationCheckIds).Concat(state.WorkItems.SelectMany(x => x.VerificationExpectations.Keys)));

        var active = state.WorkItems.Where(x => x.CurrentAttemptId is not null).ToArray();
        if (active.Length > 1 ||
            (state.CurrentAttemptId is null && active.Length != 0) ||
            (state.CurrentAttemptId is not null && (active.Length != 1 || active[0].CurrentAttemptId != state.CurrentAttemptId)))
            throw new AgentProtocolException("INVALID_RUNTIME_GRAPH", "Active work-item attempt identity is inconsistent.");
    }

    private async Task SaveAsync(FactoryState state, CancellationToken cancellationToken)
    {
        var revision = state.Revision;
        await stateStore.SaveAsync(state, revision, cancellationToken);
    }

    private static FactoryState CloneState(FactoryState state) =>
        JsonSerializer.Deserialize<FactoryState>(JsonSerializer.Serialize(state, FactoryJson.Options), FactoryJson.Options)
        ?? throw new InvalidOperationException("Cannot clone Factory state.");

    private static void ApplyCandidate(FactoryState state, FactoryState candidate)
    {
        state.GraphRevision = candidate.GraphRevision;
        state.RunStatus = candidate.RunStatus;
        state.WorkItems.Clear();
        state.WorkItems.AddRange(candidate.WorkItems);
        state.CurrentAttemptId = candidate.CurrentAttemptId;
        state.AttemptSequence = candidate.AttemptSequence;
        state.ReplanCount = candidate.ReplanCount;
        state.CorrectiveCycleCount = candidate.CorrectiveCycleCount;
        state.FinalVerificationPassed = candidate.FinalVerificationPassed;
        state.FinalVerificationGraphRevision = candidate.FinalVerificationGraphRevision;
        state.Blocker = candidate.Blocker;
        state.PendingContinuation = candidate.PendingContinuation;
        state.PendingVerificationSession = candidate.PendingVerificationSession;
        state.PendingReplanTrigger = candidate.PendingReplanTrigger;
        state.FinalReview = candidate.FinalReview;
        state.IntentSnapshotHash = candidate.IntentSnapshotHash;
        state.VerificationEvidenceRefs.Clear();
        state.VerificationEvidenceRefs.AddRange(candidate.VerificationEvidenceRefs);
        state.ClarificationRefs.Clear();
        state.ClarificationRefs.AddRange(candidate.ClarificationRefs);
        state.FactoryRunChangedPaths.Clear();
        state.FactoryRunChangedPaths.AddRange(candidate.FactoryRunChangedPaths);
    }

    private async Task RecordClarificationAsync(FactoryState state, string sourcePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Clarification answer file was not found.", sourcePath);
        var directory = Path.Combine(currentDirectory, "clarifications");
        Directory.CreateDirectory(directory);
        var relative = $"clarifications/Q{state.ClarificationRefs.Count + 1:00000}.md";
        await File.WriteAllTextAsync(Path.Combine(currentDirectory, relative), await File.ReadAllTextAsync(sourcePath, cancellationToken), cancellationToken);
        state.ClarificationRefs.Add(relative);
        await SaveAsync(state, cancellationToken);
    }

    private async Task<string> ReadClarificationsAsync(FactoryState state, CancellationToken cancellationToken) =>
        string.Join("\n\n", await Task.WhenAll(state.ClarificationRefs.Select(path => File.ReadAllTextAsync(Path.Combine(currentDirectory, path), cancellationToken))));

    private string HashIntent()
    {
        var root = Path.Combine(workspace, ".idd", "intent");
        if (!Directory.Exists(root)) return "missing";
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        foreach (var path in Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(root, path).Replace('\\', '/') + "\0"));
            hash.AppendData(File.ReadAllBytes(path));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private void DetectLegacyState()
    {
        if (!Directory.Exists(currentDirectory)) return;
        if (Directory.EnumerateFiles(currentDirectory, "*.ready.md")
            .Concat(Directory.EnumerateFiles(currentDirectory, "*.active.md"))
            .Concat(Directory.EnumerateFiles(currentDirectory, "*.completed.md"))
            .Concat(Directory.EnumerateFiles(currentDirectory, "*.blocked.md")).Any())
            throw new FactoryStateException("LEGACY_FACTORY_STATE", "Finish with the previous Factory version or cancel/restart with the new runtime.");
    }

    private static string Version() => typeof(FactoryRuntime).Assembly.GetName().Version?.ToString() ?? "0.0.0";
}
