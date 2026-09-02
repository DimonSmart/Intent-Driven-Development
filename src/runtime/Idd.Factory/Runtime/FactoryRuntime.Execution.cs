using System.Text;
using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Verification;

namespace Idd.Factory.Runtime;

public sealed partial class FactoryRuntime
{
    private async Task<FactoryCliOutcome?> SelectNextWorkAsync(FactoryState state, CancellationToken cancellationToken)
    {
        if (state.Current is not null || state.Remaining.Count == 0) throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Cannot select next work from the current state.");
        state.Current = state.Remaining[0];
        state.Remaining.RemoveAt(0);
        state.CurrentPhase = CurrentWorkPhase.Ready;
        await SaveAsync(state, cancellationToken);
        return null;
    }

    private async Task<FactoryCliOutcome?> DispatchWorkAsync(FactoryState state, string workItemId, CancellationToken cancellationToken)
    {
        var item = state.Current;
        if (item is null || item.Id != workItemId || state.CurrentPhase is not (CurrentWorkPhase.Ready or CurrentWorkPhase.Running))
            throw new AgentProtocolException("INVALID_DISPATCH", $"Work item {workItemId} is not Current executable work.");
        FactoryCapabilityCatalog.ResolveWorkItem(item.Capability);
        if (!configuration.AllowedCapabilities.Contains(item.Capability)) throw new AgentProtocolException("CAPABILITY_NOT_ALLOWED", $"Capability '{item.Capability}' is not allowed.");
        var reusable = state.CurrentAttemptId is { } attempt && File.Exists(Path.Combine(currentDirectory, "attempts", attempt, "result.json"));
        if (!reusable && item.AttemptCount >= configuration.Limits.MaxAgentAttempts)
            throw new AgentProtocolException("RETRY_BUDGET_EXHAUSTED", await BuildRetryBudgetExhaustedMessageAsync(item, cancellationToken));

        state.CurrentPhase = CurrentWorkPhase.Running;
        await SaveAsync(state, cancellationToken);
        var input = await BuildWorkInputAsync(state, item, cancellationToken);
        var result = await InvokeSemanticAsync(state, item.Capability, item, input, SemanticOperationKind.WorkItemExecution, cancellationToken);
        item = state.Current ?? throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Current work disappeared during dispatch.");
        item.LastResultRef = $"attempts/{result.AttemptId}/result.json";
        item.LastSemanticOutcome = result.Outcome;
        item.CurrentAttemptId = null;

        switch (result.Outcome)
        {
            case "completed":
                state.PendingContinuation = null;
                state.Blocker = null;
                if (RequiresVerification(item))
                {
                    state.CurrentPhase = CurrentWorkPhase.AwaitingVerification;
                    await SaveAsync(state, cancellationToken);
                }
                else await CommitCurrentAsync(state, cancellationToken);
                return null;
            case "approved" when item.Capability == "semantic-review":
                state.PendingContinuation = null;
                state.Blocker = null;
                await CommitCurrentAsync(state, cancellationToken);
                return null;
            case "additional-work-required":
                return await PrependAdditionalWorkAsync(state, item, result, cancellationToken);
            case "correction-required" when item.Capability == "semantic-review":
                return await PrependReviewCorrectionAsync(state, item, result, cancellationToken);
            case "global-replan-required":
                state.PendingReplanTrigger = new(item.Capability, item.Id, item.LastResultRef, result.Reason, result.Payload?.Clone(), item.VerificationEvidenceRefs.ToList());
                state.PendingContinuation = null;
                state.Blocker = null;
                state.CurrentPhase = CurrentWorkPhase.Ready;
                await SaveAsync(state, cancellationToken);
                return null;
            case "intent-required" or "needs-clarification" or "blocked" or "focused-handoff":
                return await HandleSemanticStopAsync(state, item, result, SemanticOperationKind.WorkItemExecution, input, cancellationToken);
            default:
                throw new AgentProtocolException("UNSUPPORTED_AGENT_OUTCOME", $"Outcome {result.Outcome} is not valid for capability {item.Capability}.");
        }
    }

    private static bool RequiresVerification(PlannedWorkItem item) =>
        item.Capability == "implementation" || item.VerificationCheckIds.Count > 0 || item.VerificationExpectations.Count > 0;

    private async Task CommitCurrentAsync(FactoryState state, CancellationToken cancellationToken)
    {
        var item = state.Current ?? throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Completion requires Current work.");
        state.Completed.Add(new CompletedWorkItem
        {
            Id = item.Id,
            Capability = item.Capability,
            ContractPath = item.ContractPath,
            ResultRef = item.LastResultRef,
            ChangedPaths = item.ChangedPaths.ToList(),
            VerificationEvidenceRefs = item.VerificationEvidenceRefs.ToList(),
            VerificationDecision = item.LastVerificationDecision
        });
        state.Current = null;
        state.CurrentPhase = null;
        state.PlanRevision++;
        InvalidateFinalEvidence(state);
        await SaveAsync(state, cancellationToken);
    }

    private async Task<BoundSemanticAgentResult> InvokeSemanticAsync(FactoryState state, string capability, PlannedWorkItem? item, string input, SemanticOperationKind operation, CancellationToken cancellationToken)
    {
        var agent = FactoryCapabilityCatalog.Resolve(capability).Agent;
        if (state.CurrentAttemptId is { } persistedAttempt)
        {
            var directory = Path.Combine(currentDirectory, "attempts", persistedAttempt);
            var invocationPath = Path.Combine(directory, "invocation.json");
            var persistedResultPath = Path.Combine(directory, "result.json");
            if (!File.Exists(invocationPath) || !File.Exists(persistedResultPath)) throw new AgentProtocolException("UNKNOWN_ATTEMPT", $"Persisted attempt {persistedAttempt} cannot be resumed from its artifacts.");
            var invocation = JsonSerializer.Deserialize<AgentInvocation>(await File.ReadAllTextAsync(invocationPath, cancellationToken), FactoryJson.Options)
                ?? throw new AgentProtocolException("UNKNOWN_ATTEMPT", $"Attempt {persistedAttempt} has no valid invocation.");
            if (invocation.RunId != state.RunId || invocation.AttemptId != persistedAttempt || invocation.Capability != capability || invocation.Role != agent.Role || invocation.WorkItemId != item?.Id)
                throw new AgentProtocolException("UNKNOWN_ATTEMPT", $"Attempt {persistedAttempt} does not belong to the current operation.");
            await RecoverWorkspaceChangesAsync(state, item, invocation, cancellationToken);
            var persisted = JsonSerializer.Deserialize<PersistedAttemptResult>(await File.ReadAllTextAsync(persistedResultPath, cancellationToken), FactoryJson.Options);
            var validated = ValidatePersistedResult(invocation, persisted);
            state.CurrentAttemptId = null;
            if (item is not null) item.CurrentAttemptId = null;
            state.PendingContinuation = null;
            await SaveAsync(state, cancellationToken);
            return validated;
        }

        var attemptId = $"A{++state.AttemptSequence:000000}";
        state.CurrentAttemptId = attemptId;
        if (item is not null) { item.CurrentAttemptId = attemptId; item.AttemptCount++; }
        state.PendingContinuation = new(ContinuationKind.SemanticInvocation, item?.Id, null, operation.ToString().ToUpperInvariant(), true, operation, input);
        await SaveAsync(state, cancellationToken);

        var attemptDirectory = Path.Combine(currentDirectory, "attempts", attemptId);
        Directory.CreateDirectory(attemptDirectory);
        var rawResultPath = Path.Combine(attemptDirectory, "raw-result.json");
        var invocationNew = new AgentInvocation
        {
            RunId = state.RunId, AttemptId = attemptId, Capability = capability, Role = agent.Role, WorkItemId = item?.Id,
            Workspace = workspace, RawResultPath = rawResultPath, SkillName = agent.SkillName,
            ExecutionProfile = agent.ExecutionProfile, SemanticResultSchema = SemanticResultContracts.SchemaForCapability(capability), Input = input, StartedAt = clock.UtcNow
        };
        await WriteJsonAtomicallyAsync(Path.Combine(attemptDirectory, "invocation.json"), invocationNew, cancellationToken);
        if (agent.ExecutionProfile == AgentExecutionProfile.WorkspaceWrite) await PersistWorkspaceSnapshotAsync(state.RunId, attemptDirectory, cancellationToken);
        await events.WriteAsync(state.RunId, "agent-dispatching", new { attemptId, capability, agent.Role, workItemId = item?.Id }, cancellationToken);
        AgentExecutionResult execution;
        try { execution = await agentExecutor.ExecuteAsync(invocationNew, cancellationToken); }
        finally { if (agent.ExecutionProfile == AgentExecutionProfile.WorkspaceWrite) await RecoverWorkspaceChangesAsync(state, item, invocationNew, CancellationToken.None); }
        state.CurrentAttemptId = null;
        if (item is not null) item.CurrentAttemptId = null;
        state.PendingContinuation = null;
        await SaveAsync(state, cancellationToken);
        await events.WriteAsync(state.RunId, "agent-completed", new { attemptId, capability, agent.Role, execution.Result.Outcome, execution.Process.TerminationKind }, cancellationToken);
        return execution.Result;
    }

    private async Task<string> BuildWorkInputAsync(FactoryState state, PlannedWorkItem item, CancellationToken cancellationToken)
    {
        var contract = await File.ReadAllTextAsync(Path.Combine(currentDirectory, item.ContractPath), cancellationToken);
        var completed = await BuildCompletedContextAsync(state, cancellationToken);
        var prior = await BuildPriorResultContextAsync(item, cancellationToken);
        var verificationObservations = await BuildVerificationObservationsAsync(item, cancellationToken);
        return $"Work item contract:\n{contract}\n\nRelevant completed work and results:\n{completed}\n\nPrevious attempts for this task:\n{prior}\n\nAuthoritative verification observations:\n{verificationObservations}\n\nUse a fresh semantic context. Do not rely on conversation history or internal planning state.";
    }

    private async Task<string> BuildCompletedContextAsync(FactoryState state, CancellationToken cancellationToken)
    {
        if (state.Completed.Count == 0) return "none";
        var lines = new List<string>();
        foreach (var completed in state.Completed)
        {
            lines.Add($"- {completed.Id} ({completed.Capability}), contract={completed.ContractPath}, result={completed.ResultRef ?? "none"}");
            if (completed.ResultRef is not null) lines.Add("  " + await ReadResultSummaryAsync(completed.ResultRef, cancellationToken));
        }
        return string.Join("\n", lines);
    }

    private async Task<string> BuildPriorResultContextAsync(PlannedWorkItem item, CancellationToken cancellationToken)
    {
        if (item.PriorResultRefs.Count == 0) return "none";
        var lines = new List<string>();
        foreach (var reference in item.PriorResultRefs.TakeLast(3)) lines.Add($"- {reference}: {await ReadResultSummaryAsync(reference, cancellationToken)}");
        return string.Join("\n", lines);
    }

    private async Task<string> ReadResultSummaryAsync(string relative, CancellationToken cancellationToken)
    {
        var path = Path.Combine(currentDirectory, relative);
        if (!File.Exists(path)) return "missing result artifact";
        try
        {
            var result = JsonSerializer.Deserialize<PersistedAttemptResult>(await File.ReadAllTextAsync(path, cancellationToken), FactoryJson.Options);
            var semantic = result?.SemanticResult;
            var summary = JsonSerializer.Serialize(new
            {
                semantic?.Outcome,
                semantic?.Summary,
                Concerns = semantic?.Concerns?.Take(8).ToArray(),
                DeclaredChanges = semantic?.DeclaredChanges?.Take(8).ToArray(),
                semantic?.Reason,
                semantic?.Payload
            }, FactoryJson.Options).Replace("\r\n", "\n").Trim();
            return summary.Length <= 4000 ? summary : summary[..4000] + " [truncated]";
        }
        catch (JsonException) { return "invalid result artifact"; }
    }

    private async Task<string> BuildVerificationObservationsAsync(PlannedWorkItem item, CancellationToken cancellationToken)
    {
        var failures = await ReadFailedVerificationEvidenceAsync(item, cancellationToken);
        if (failures.Count == 0) return "none";

        var observations = new List<string>();
        foreach (var (reference, evidence) in failures)
        {
            observations.Add($"- Check: {evidence.CheckId}");
            observations.Add($"  Status: {evidence.Status}");
            observations.Add($"  Exit code: {evidence.ExitCode}");
            observations.Add($"  Evidence: {reference}");
            observations.Add("");
            observations.Add("  Relevant output:");
            foreach (var line in BoundedVerificationOutput(evidence.Output).Replace("\r\n", "\n").Split('\n'))
                observations.Add($"  {line}");
        }
        return string.Join("\n", observations).TrimEnd();
    }

    private async Task<string> BuildRetryBudgetExhaustedMessageAsync(PlannedWorkItem item, CancellationToken cancellationToken)
    {
        var failures = await ReadFailedVerificationEvidenceAsync(item, cancellationToken);
        if (failures.Count == 0) return $"{item.Id} exhausted its semantic attempt budget.";

        var (reference, evidence) = failures[^1];
        return $"Work item {item.Id} could not pass authoritative verification after {item.AttemptCount} semantic attempts.\n\nFailed check:\n{evidence.CheckId}\n\nLatest verification output:\n{BoundedVerificationOutput(evidence.Output)}\n\nEvidence:\n{reference}";
    }

    private async Task<List<(string Reference, VerificationEvidence Evidence)>> ReadFailedVerificationEvidenceAsync(
        PlannedWorkItem item,
        CancellationToken cancellationToken)
    {
        var failures = new List<(string Reference, VerificationEvidence Evidence)>();
        foreach (var reference in item.VerificationEvidenceRefs)
        {
            var path = Path.Combine(currentDirectory, reference);
            if (!File.Exists(path)) continue;
            try
            {
                var evidence = JsonSerializer.Deserialize<VerificationEvidence>(await File.ReadAllTextAsync(path, cancellationToken), FactoryJson.Options);
                if (evidence?.Status == "failed") failures.Add((reference, evidence));
            }
            catch (JsonException) { }
        }
        return failures;
    }

    private static string BoundedVerificationOutput(string output)
    {
        const int maximumBytes = 12 * 1024;
        if (Encoding.UTF8.GetByteCount(output) <= maximumBytes) return output;

        var minimum = 0;
        var maximum = Math.Min(output.Length, maximumBytes);
        while (minimum < maximum)
        {
            var candidate = minimum + (maximum - minimum + 1) / 2;
            if (Encoding.UTF8.GetByteCount(output.AsSpan(0, candidate)) <= maximumBytes) minimum = candidate;
            else maximum = candidate - 1;
        }
        var length = minimum;
        return output[..length] + "\n[verification output truncated; see evidence artifact]";
    }

    private Task ReconcileAsync(FactoryState state, CancellationToken cancellationToken) =>
        new SemanticAttemptReconciler(currentDirectory, RecoverWorkspaceChangesAsync, SaveAsync).ReconcileAsync(state, cancellationToken);

    private async Task PersistWorkspaceSnapshotAsync(string runId, string attemptDirectory, CancellationToken cancellationToken) =>
        await WriteJsonAtomicallyAsync(Path.Combine(attemptDirectory, "workspace-before.json"), new WorkspaceSnapshotArtifact(1, await SnapshotWorkspaceAsync(runId, cancellationToken)), cancellationToken);

    private async Task RecoverWorkspaceChangesAsync(FactoryState state, PlannedWorkItem? item, AgentInvocation invocation, CancellationToken cancellationToken)
    {
        if (invocation.ExecutionProfile != AgentExecutionProfile.WorkspaceWrite) return;
        var directory = Path.GetDirectoryName(invocation.RawResultPath)!;
        var changesPath = Path.Combine(directory, "workspace-changes.json");
        WorkspaceChangesArtifact changes;
        if (File.Exists(changesPath)) changes = JsonSerializer.Deserialize<WorkspaceChangesArtifact>(await File.ReadAllTextAsync(changesPath, cancellationToken), FactoryJson.Options)!;
        else
        {
            var beforePath = Path.Combine(directory, "workspace-before.json");
            if (!File.Exists(beforePath)) return;
            var before = JsonSerializer.Deserialize<WorkspaceSnapshotArtifact>(await File.ReadAllTextAsync(beforePath, cancellationToken), FactoryJson.Options)!;
            var after = await SnapshotWorkspaceAsync(state.RunId, cancellationToken);
            changes = new(1, after.Where(x => !before.Files.TryGetValue(x.Key, out var prior) || prior != x.Value).Select(x => x.Key)
                .Concat(before.Files.Keys.Where(path => !after.ContainsKey(path))).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList());
            await WriteJsonAtomicallyAsync(changesPath, changes, cancellationToken);
        }
        foreach (var path in changes.ChangedPaths)
        {
            if (item is not null && !item.ChangedPaths.Contains(path, StringComparer.Ordinal)) item.ChangedPaths.Add(path);
            if (!state.FactoryRunChangedPaths.Contains(path, StringComparer.Ordinal)) state.FactoryRunChangedPaths.Add(path);
        }
    }

    private async Task<SortedDictionary<string, string>> SnapshotWorkspaceAsync(string runId, CancellationToken cancellationToken)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in await WorkspaceSnapshotFileEnumerator.EnumerateAsync(workspace, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = RelativePath(path);
            if (IsOperationalArtifact(relative)) continue;
            try { result[relative] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path, cancellationToken))); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { await events.WriteAsync(runId, "workspace-snapshot-file-skipped", new { path = relative, exception = exception.GetType().Name }, CancellationToken.None); }
        }
        return result;
    }

    private string RelativePath(string path) => Path.GetRelativePath(workspace, path).Replace('\\', '/');
    private static bool IsOperationalArtifact(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2 && segments[0].Equals(".idd", StringComparison.OrdinalIgnoreCase) && segments[1].Equals("factory", StringComparison.OrdinalIgnoreCase)) return true;
        return segments.Any(x => x.Equals(".git", StringComparison.OrdinalIgnoreCase) || x.Equals("bin", StringComparison.OrdinalIgnoreCase) || x.Equals("obj", StringComparison.OrdinalIgnoreCase) || x.Equals("node_modules", StringComparison.OrdinalIgnoreCase) || x.Equals("TestResults", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task WriteJsonAtomicallyAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, FactoryJson.Options), cancellationToken);
        File.Move(temporary, path, true);
    }

    private static BoundSemanticAgentResult ValidatePersistedResult(AgentInvocation invocation, PersistedAttemptResult? persisted)
    {
        if (persisted is null || persisted.SchemaVersion != PersistedAttemptResult.CurrentSchemaVersion)
            throw new AgentProtocolException("UNSUPPORTED_ATTEMPT_RESULT_SCHEMA", "Persisted attempt result has an unsupported schema version.");
        var expected = AttemptIdentity.From(invocation);
        if (persisted.Invocation != expected)
            throw new AgentProtocolException("ATTEMPT_RESULT_IDENTITY_MISMATCH", "Persisted attempt result does not belong to its invocation.");
        var semantic = new FactoryAgentResultValidator().Validate(invocation, persisted.SemanticResult);
        return new BoundSemanticAgentResult(invocation.AttemptId, semantic);
    }

    private sealed record WorkspaceSnapshotArtifact(int SchemaVersion, SortedDictionary<string, string> Files);
    private sealed record WorkspaceChangesArtifact(int SchemaVersion, List<string> ChangedPaths);
}
