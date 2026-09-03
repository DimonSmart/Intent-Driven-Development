using System.Text;
using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Verification;

namespace Idd.Factory.Runtime;

public sealed partial class FactoryRuntime
{
    private readonly WorkspaceChangeCalculator workspaceChangeCalculator = new();

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
        var reusable = state.CurrentAttemptId is { } attempt && File.Exists(Path.Combine(currentDirectory, "attempts", attempt, "result.json"));
        if (!reusable && item.AttemptCount >= configuration.Limits.MaxAttemptsPerTask)
            throw new AgentProtocolException("RETRY_BUDGET_EXHAUSTED", await BuildRetryBudgetExhaustedMessageAsync(item, cancellationToken));
        var verificationDrivenRetry = item.LastVerificationDecision == VerificationDecision.UnexpectedFailure;

        state.CurrentPhase = CurrentWorkPhase.Running;
        await SaveAsync(state, cancellationToken);
        var input = await BuildWorkInputAsync(state, item, cancellationToken);
        var result = await InvokeSemanticAsync(state, "implementation", item, input, SemanticOperationKind.WorkItemExecution, cancellationToken);
        item = state.Current ?? throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Current work disappeared during dispatch.");
        item.LastResultRef = result.SemanticResultPath;
        item.CurrentAttemptId = null;

        if (verificationDrivenRetry && !await AttemptChangedWorkspaceAsync(result.AttemptId, cancellationToken))
        {
            state.CurrentPhase = CurrentWorkPhase.Blocked;
            return await StopAsync(
                state,
                "VERIFICATION_RETRY_NO_PROGRESS",
                $"Work item {item.Id} was retried because authoritative verification failed, but retry attempt {result.AttemptId} produced no workspace changes.",
                "Inspect the verification evidence and executor result, resolve the condition, then cancel/restart the Factory run.",
                cancellationToken,
                new(ContinuationKind.Terminal, item.Id, "subtask", "VERIFICATION_RETRY_NO_PROGRESS", false));
        }

        state.PendingContinuation = null;
        state.Blocker = null;
        state.CurrentPhase = CurrentWorkPhase.AwaitingVerification;
        await SaveAsync(state, cancellationToken);
        return null;
    }

    private async Task CommitCurrentAsync(FactoryState state, CancellationToken cancellationToken)
    {
        var item = state.Current ?? throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Completion requires Current work.");
        state.Completed.Add(new CompletedWorkItem
        {
            Id = item.Id,
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

    private async Task<BoundSemanticResult> InvokeSemanticAsync(FactoryState state, string capability, PlannedWorkItem? item, string input, SemanticOperationKind operation, CancellationToken cancellationToken)
    {
        var agent = FactoryCapabilityCatalog.Resolve(capability);
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
        var semanticOutputPath = Path.Combine(attemptDirectory, capability == "planning" ? "planning-output.md" : "semantic-result.md");
        var invocationNew = new AgentInvocation
        {
            RunId = state.RunId, AttemptId = attemptId, Capability = capability, Role = agent.Role, WorkItemId = item?.Id,
            Workspace = workspace, SemanticOutputPath = semanticOutputPath, SkillName = agent.SkillName,
            ExecutionProfile = agent.ExecutionProfile, Input = input, StartedAt = clock.UtcNow
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
        await events.WriteAsync(state.RunId, "agent-completed", new { attemptId, capability, agent.Role, execution.Process.TerminationKind }, cancellationToken);
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
            lines.Add($"## {completed.Id}");
            lines.Add("Task contract:");
            lines.Add(ReadContract(completed.ContractPath));
            lines.Add("Semantic result:");
            lines.Add(completed.ResultRef is null ? "none" : await ReadSemanticResultAsync(completed.ResultRef, cancellationToken));
            lines.Add("Actual changed paths: " + (completed.ChangedPaths.Count == 0 ? "none" : string.Join(", ", completed.ChangedPaths)));
            lines.Add("Verification evidence: " + (completed.VerificationEvidenceRefs.Count == 0 ? "none" : string.Join(", ", completed.VerificationEvidenceRefs)));
        }
        return string.Join("\n", lines);
    }

    private async Task<string> BuildPriorResultContextAsync(PlannedWorkItem item, CancellationToken cancellationToken)
    {
        if (item.PriorResultRefs.Count == 0) return "none";
        var lines = new List<string>();
        foreach (var reference in item.PriorResultRefs.TakeLast(3)) lines.Add($"- {reference}:\n{await ReadSemanticResultAsync(reference, cancellationToken)}");
        return string.Join("\n", lines);
    }

    private async Task<string> ReadSemanticResultAsync(string relative, CancellationToken cancellationToken)
    {
        var path = Path.Combine(currentDirectory, relative);
        if (!File.Exists(path)) return "missing result artifact";
        var result = (await File.ReadAllTextAsync(path, cancellationToken)).Replace("\r\n", "\n").Trim();
        return result.Length <= 8000 ? result : result[..8000] + "\n[result truncated; see semantic artifact]";
    }

    internal async Task<string> BuildVerificationObservationsAsync(PlannedWorkItem item, CancellationToken cancellationToken)
    {
        var failures = await ReadFailedVerificationEvidenceAsync(item, cancellationToken);
        if (failures.Count == 0) return "none";

        var currentReferences = item.LastVerificationEvidenceRefs.Count == 0
            ? failures.Select(x => x.Reference).ToHashSet(StringComparer.Ordinal)
            : item.LastVerificationEvidenceRefs.ToHashSet(StringComparer.Ordinal);
        var currentFailures = failures.Where(x => currentReferences.Contains(x.Reference)).ToList();
        var historicalFailures = failures.Where(x => !currentReferences.Contains(x.Reference)).ToList();
        var observations = new List<string> { "Current authoritative verification failures:" };

        if (currentFailures.Count == 0)
        {
            observations.Add("none");
        }
        else
        {
            foreach (var failure in currentFailures)
            {
                AppendVerificationMetadata(observations, failure.Reference, failure.Evidence);
                observations.Add("");
                observations.Add("  Relevant output:");
                foreach (var line in BoundedVerificationOutput(failure.Evidence.Output).Replace("\r\n", "\n").Split('\n'))
                    observations.Add($"  {line}");
            }
        }

        observations.Add("");
        observations.Add("Historical verification failures:");
        if (historicalFailures.Count == 0)
        {
            observations.Add("none");
        }
        else
        {
            foreach (var failure in historicalFailures)
            {
                AppendVerificationMetadata(observations, failure.Reference, failure.Evidence);
                observations.Add("");
            }
        }

        return string.Join("\n", observations).TrimEnd();
    }

    private static void AppendVerificationMetadata(List<string> observations, string reference, VerificationEvidence evidence)
    {
        observations.Add($"- Check: {evidence.CheckId}");
        observations.Add($"  Status: {evidence.Status}");
        observations.Add($"  Exit code: {evidence.ExitCode}");
        observations.Add($"  Evidence: {reference}");
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

    private async Task<bool> AttemptChangedWorkspaceAsync(string attemptId, CancellationToken cancellationToken)
    {
        var changesPath = Path.Combine(currentDirectory, "attempts", attemptId, "workspace-changes.json");
        if (!File.Exists(changesPath))
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", $"Attempt {attemptId} has no workspace changes artifact.");
        var changes = JsonSerializer.Deserialize<WorkspaceChangesArtifact>(await File.ReadAllTextAsync(changesPath, cancellationToken), FactoryJson.Options)
            ?? throw new FactoryStateException("CORRUPT_FACTORY_STATE", $"Attempt {attemptId} has an invalid workspace changes artifact.");
        if (changes.SchemaVersion != 1)
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", $"Attempt {attemptId} has an unsupported workspace changes schema.");
        return changes.ChangedPaths.Count > 0;
    }

    private async Task RecoverWorkspaceChangesAsync(FactoryState state, PlannedWorkItem? item, AgentInvocation invocation, CancellationToken cancellationToken)
    {
        if (invocation.ExecutionProfile != AgentExecutionProfile.WorkspaceWrite) return;
        var directory = Path.GetDirectoryName(invocation.SemanticOutputPath)!;
        var changesPath = Path.Combine(directory, "workspace-changes.json");
        WorkspaceChangesArtifact changes;
        if (File.Exists(changesPath)) changes = JsonSerializer.Deserialize<WorkspaceChangesArtifact>(await File.ReadAllTextAsync(changesPath, cancellationToken), FactoryJson.Options)!;
        else
        {
            var beforePath = Path.Combine(directory, "workspace-before.json");
            if (!File.Exists(beforePath)) return;
            var before = JsonSerializer.Deserialize<WorkspaceSnapshotArtifact>(await File.ReadAllTextAsync(beforePath, cancellationToken), FactoryJson.Options)!;
            var after = await SnapshotWorkspaceAsync(state.RunId, cancellationToken);
            changes = new(1, workspaceChangeCalculator.Calculate(before.Files, after).ToList());
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

    private BoundSemanticResult ValidatePersistedResult(AgentInvocation invocation, PersistedAttemptResult? persisted)
    {
        if (persisted is null || persisted.SchemaVersion != PersistedAttemptResult.CurrentSchemaVersion)
            throw new AgentProtocolException("UNSUPPORTED_ATTEMPT_RESULT_SCHEMA", "Persisted attempt result has an unsupported schema version.");
        var expected = AttemptIdentity.From(invocation);
        if (persisted.Invocation != expected)
            throw new AgentProtocolException("ATTEMPT_RESULT_IDENTITY_MISMATCH", "Persisted attempt result does not belong to its invocation.");
        var semanticPath = Path.Combine(currentDirectory, persisted.SemanticResultPath);
        if (!File.Exists(semanticPath))
            throw new AgentProtocolException("MISSING_AGENT_RESULT", "Persisted semantic result artifact is missing.");
        var semantic = File.ReadAllText(semanticPath);
        if (invocation.Capability == "implementation" && string.IsNullOrWhiteSpace(semantic))
            throw new AgentProtocolException("MALFORMED_AGENT_RESULT", "Executor semantic result must contain human-readable text.");
        return new BoundSemanticResult(invocation.AttemptId, semantic, persisted.SemanticResultPath);
    }

    private sealed record WorkspaceSnapshotArtifact(int SchemaVersion, SortedDictionary<string, string> Files);
    private sealed record WorkspaceChangesArtifact(int SchemaVersion, List<string> ChangedPaths);
}
