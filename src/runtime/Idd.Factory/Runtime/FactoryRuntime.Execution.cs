using System.Text.Json;
using Idd.Factory.Agents;
using Idd.Factory.Domain;
using Idd.Factory.Verification;

namespace Idd.Factory.Runtime;

public sealed partial class FactoryRuntime
{
    private async Task<FactoryCliOutcome?> DispatchWorkAsync(FactoryState state, string workItemId, CancellationToken cancellationToken)
    {
        var item = state.WorkItems.Single(x => x.Id == workItemId);
        if (item.DefinitionState != WorkDefinitionState.Executable || !DependenciesCompleted(state, item))
            throw new AgentProtocolException("INVALID_DISPATCH", $"Work item {item.Id} is not executable now.");
        FactoryCapabilityCatalog.ResolveWorkItem(item.Capability!);
        if (!configuration.AllowedCapabilities.Contains(item.Capability!))
            throw new AgentProtocolException("CAPABILITY_NOT_ALLOWED", $"Capability '{item.Capability}' is not allowed by the pinned Factory configuration.");
        if (item.AttemptCount >= configuration.Limits.MaxAgentAttempts)
            throw new AgentProtocolException("RETRY_BUDGET_EXHAUSTED", $"{item.Id} exhausted its semantic attempt budget.");

        if (item.Status is WorkItemStatus.Planned or WorkItemStatus.Waiting or WorkItemStatus.Blocked)
        {
            item.Status = WorkItemStatus.Ready;
            await SaveAsync(state, cancellationToken);
        }
        if (item.Status != WorkItemStatus.Ready)
            throw new AgentProtocolException("INVALID_DISPATCH", $"Work item {item.Id} cannot dispatch from {item.Status}.");

        item.Status = WorkItemStatus.Dispatching;
        await SaveAsync(state, cancellationToken);
        item.Status = WorkItemStatus.Running;
        await SaveAsync(state, cancellationToken);

        var input = await BuildWorkInputAsync(state, item, cancellationToken);
        var result = await InvokeSemanticAsync(state, item.Capability!, item, input, SemanticOperationKind.WorkItemExecution, cancellationToken);
        item = state.WorkItems.Single(x => x.Id == workItemId);
        item.LastResultRef = $"attempts/{result.AttemptId}/result.json";
        item.LastSemanticOutcome = result.Outcome;
        item.CurrentAttemptId = null;

        switch (result.Outcome)
        {
            case "completed":
                state.PendingContinuation = null;
                state.Blocker = null;
                item.Status = RequiresVerification(item) ? WorkItemStatus.AwaitingVerification : WorkItemStatus.Completed;
                await SaveAsync(state, cancellationToken);
                return null;

            case "approved" when item.Capability == "semantic-review":
                item.Status = WorkItemStatus.Completed;
                state.PendingContinuation = null;
                state.Blocker = null;
                if (item.IsFinalReview)
                    state.FinalReview = new("approved", item.LastResultRef, (state.FinalReview?.AttemptCount ?? 0) + 1, item.Id, item.ReviewTargetGraphRevision);
                await SaveAsync(state, cancellationToken);
                return null;

            case "additional-work-required":
                return await MaterializeAdditionalWorkAsync(state, item, result, cancellationToken);

            case "needs-fix" or "correction-required" when item.Capability == "semantic-review":
                return await MaterializeReviewCorrectionAsync(state, item, result, cancellationToken);

            case "global-replan-required" or "needs-replan":
                if (item.Capability == "semantic-review")
                {
                    item.Status = WorkItemStatus.Completed;
                    if (item.IsFinalReview)
                        state.FinalReview = new(result.Outcome, item.LastResultRef, (state.FinalReview?.AttemptCount ?? 0) + 1, item.Id, item.ReviewTargetGraphRevision);
                }
                else
                {
                    item.Status = WorkItemStatus.Ready;
                }
                state.PendingReplanTrigger = new(item.Capability!, item.Id, item.LastResultRef, result.Reason, result.Payload?.Clone(), item.VerificationEvidenceRefs.ToList());
                state.PendingContinuation = null;
                state.Blocker = null;
                await SaveAsync(state, cancellationToken);
                return null;

            case "intent-required" or "needs-clarification" or "blocked" or "focused-handoff":
                return await HandleSemanticStopAsync(state, item, result, SemanticOperationKind.WorkItemExecution, input, cancellationToken);

            default:
                throw new AgentProtocolException("UNSUPPORTED_AGENT_OUTCOME", $"Outcome {result.Outcome} is not valid for capability {item.Capability}.");
        }
    }

    private static bool RequiresVerification(WorkItemState item) =>
        item.Capability is "implementation" or "documentation" || item.VerificationCheckIds.Count > 0 || item.VerificationExpectations.Count > 0;

    private async Task<AgentResultEnvelope> InvokeSemanticAsync(
        FactoryState state,
        string capability,
        WorkItemState? item,
        string input,
        SemanticOperationKind operation,
        CancellationToken cancellationToken)
    {
        var capabilityContract = FactoryCapabilityCatalog.Resolve(capability);
        var agent = capabilityContract.Agent;
        if (state.CurrentAttemptId is { } persistedAttempt)
        {
            var directory = Path.Combine(currentDirectory, "attempts", persistedAttempt);
            var invocationPath = Path.Combine(directory, "invocation.json");
            var resultPath = Path.Combine(directory, "result.json");
            if (File.Exists(invocationPath) && File.Exists(resultPath))
            {
                var invocation = JsonSerializer.Deserialize<AgentInvocation>(await File.ReadAllTextAsync(invocationPath, cancellationToken), FactoryJson.Options)
                    ?? throw new AgentProtocolException("UNKNOWN_ATTEMPT", $"Attempt {persistedAttempt} has no valid invocation.");
                if (invocation.Role != agent.Role || invocation.WorkItemId != item?.Id)
                    throw new AgentProtocolException("UNKNOWN_ATTEMPT", $"Attempt {persistedAttempt} does not belong to the current semantic operation.");
                await RecoverWorkspaceChangesAsync(state, item, invocation, cancellationToken);
                var persistedResult = JsonSerializer.Deserialize<AgentResultEnvelope>(await File.ReadAllTextAsync(resultPath, cancellationToken), FactoryJson.Options);
                var result = new AgentResultValidator().Validate(invocation, persistedResult);
                state.CurrentAttemptId = null;
                if (item is not null) item.CurrentAttemptId = null;
                await SaveAsync(state, cancellationToken);
                await events.WriteAsync(state.RunId, "agent-result-reused", new { attemptId = persistedAttempt, capability, agent.Role, workItemId = item?.Id }, cancellationToken);
                return result;
            }
            throw new AgentProtocolException("UNKNOWN_ATTEMPT", $"Persisted attempt {persistedAttempt} cannot be resumed from its artifacts.");
        }

        var attemptId = $"A{++state.AttemptSequence:000000}";
        state.CurrentAttemptId = attemptId;
        if (item is not null)
        {
            item.CurrentAttemptId = attemptId;
            item.AttemptCount++;
        }
        state.PendingContinuation = new(ContinuationKind.SemanticInvocation, item?.Id, null, operation.ToString().ToUpperInvariant(), true, operation, input);
        await SaveAsync(state, cancellationToken);

        var attemptDirectory = Path.Combine(currentDirectory, "attempts", attemptId);
        Directory.CreateDirectory(attemptDirectory);
        var resultPathNew = Path.Combine(attemptDirectory, "result.json");
        var invocationNew = new AgentInvocation
        {
            RunId = state.RunId,
            AttemptId = attemptId,
            Role = agent.Role,
            WorkItemId = item?.Id,
            Workspace = workspace,
            ResultPath = resultPathNew,
            SkillName = agent.SkillName,
            ExecutionProfile = agent.ExecutionProfile,
            Input = input,
            StartedAt = clock.UtcNow
        };
        await WriteJsonAtomicallyAsync(Path.Combine(attemptDirectory, "invocation.json"), invocationNew, cancellationToken);
        if (agent.ExecutionProfile == AgentExecutionProfile.WorkspaceWrite)
            await PersistWorkspaceSnapshotAsync(state.RunId, attemptDirectory, cancellationToken);
        await events.WriteAsync(state.RunId, "agent-dispatching", new { attemptId, capability, agent.Role, workItemId = item?.Id }, cancellationToken);

        AgentExecutionResult execution;
        try
        {
            execution = await agentExecutor.ExecuteAsync(invocationNew, cancellationToken);
        }
        finally
        {
            if (agent.ExecutionProfile == AgentExecutionProfile.WorkspaceWrite)
                await RecoverWorkspaceChangesAsync(state, item, invocationNew, CancellationToken.None);
        }

        state.CurrentAttemptId = null;
        if (item is not null) item.CurrentAttemptId = null;
        await SaveAsync(state, cancellationToken);
        await events.WriteAsync(state.RunId, "agent-completed", new
        {
            attemptId,
            capability,
            agent.Role,
            execution.Result.Outcome,
            execution.Result.Metrics,
            execution.Process.TerminationKind,
            execution.Process.CompleteResultObserved,
            execution.Process.KillRequired,
            execution.Process.ExitCode
        }, cancellationToken);
        return execution.Result;
    }

    private async Task<string> BuildWorkInputAsync(FactoryState state, WorkItemState item, CancellationToken cancellationToken)
    {
        var contract = await File.ReadAllTextAsync(Path.Combine(currentDirectory, item.ContractPath), cancellationToken);
        var dependencies = await BuildDependencyContextAsync(state, item, cancellationToken);
        var prior = await BuildPriorResultContextAsync(item, cancellationToken);
        var observations = await BuildCompactVerificationSummaryAsync(state, item, cancellationToken);
        var original = item.IsFinalReview
            ? $"\n\nOriginal Factory request:\n{await File.ReadAllTextAsync(Path.Combine(currentDirectory, state.RequestPath), cancellationToken)}"
            : "";
        return $"Work item contract:\n{contract}{original}\n\nCompleted dependency results:\n{dependencies}\n\nPrevious semantic result context:\n{prior}\n\n" +
               $"Authoritative Runtime verification observations:\n{observations}\n\nUse a fresh semantic context. Do not rely on conversation history.";
    }

    private async Task<string> BuildDependencyContextAsync(FactoryState state, WorkItemState item, CancellationToken cancellationToken)
    {
        if (item.Dependencies.Count == 0) return "none";
        var lines = new List<string>();
        foreach (var dependencyId in item.Dependencies)
        {
            var dependency = state.WorkItems.Single(x => x.Id == dependencyId);
            lines.Add($"- {dependency.Id}: status={dependency.Status}, result={dependency.LastResultRef ?? "none"}");
            if (dependency.LastResultRef is { } resultRef)
                lines.Add("  " + await ReadResultSummaryAsync(resultRef, cancellationToken));
        }
        return string.Join("\n", lines);
    }

    private async Task<string> BuildPriorResultContextAsync(WorkItemState item, CancellationToken cancellationToken)
    {
        if (item.PriorResultRefs.Count == 0) return "none";
        var lines = new List<string>();
        foreach (var resultRef in item.PriorResultRefs.TakeLast(3))
            lines.Add($"- {resultRef}: {await ReadResultSummaryAsync(resultRef, cancellationToken)}");
        return string.Join("\n", lines);
    }

    private async Task<string> ReadResultSummaryAsync(string relative, CancellationToken cancellationToken)
    {
        var path = Path.Combine(currentDirectory, relative);
        if (!File.Exists(path)) return "missing result artifact";
        try
        {
            var result = JsonSerializer.Deserialize<AgentResultEnvelope>(await File.ReadAllTextAsync(path, cancellationToken), FactoryJson.Options);
            if (result is null) return "invalid result artifact";
            var summary = JsonSerializer.Serialize(new { result.Outcome, result.Reason, result.Payload }, FactoryJson.Options).Replace("\r\n", "\n").Trim();
            return summary.Length <= 4000 ? summary : summary[..4000] + " [truncated; use result ref for focused inspection]";
        }
        catch (JsonException) { return "invalid result artifact"; }
    }

    private async Task<string> BuildCompactVerificationSummaryAsync(FactoryState state, WorkItemState item, CancellationToken cancellationToken)
    {
        var refs = item.VerificationEvidenceRefs
            .Concat(item.Dependencies.SelectMany(id => state.WorkItems.Single(x => x.Id == id).VerificationEvidenceRefs))
            .Distinct(StringComparer.Ordinal)
            .TakeLast(20)
            .ToArray();
        if (refs.Length == 0) return "none";
        var lines = new List<string>();
        foreach (var relative in refs)
        {
            try
            {
                var evidence = JsonSerializer.Deserialize<VerificationEvidence>(await File.ReadAllTextAsync(Path.Combine(currentDirectory, relative), cancellationToken), FactoryJson.Options);
                if (evidence is not null) lines.Add($"- {evidence.CheckId}: {evidence.Status.ToUpperInvariant()} (evidence: {relative})");
            }
            catch (Exception exception) when (exception is JsonException or IOException)
            {
                lines.Add($"- evidence unavailable: {relative}");
            }
        }
        return string.Join("\n", lines);
    }

    private async Task ReconcileAsync(FactoryState state, CancellationToken cancellationToken)
    {
        var attemptId = state.CurrentAttemptId;
        if (attemptId is null)
        {
            var active = state.WorkItems.Where(x => x.CurrentAttemptId is not null).Select(x => x.CurrentAttemptId!).Distinct(StringComparer.Ordinal).ToArray();
            if (active.Length == 0) return;
            if (active.Length > 1) throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Multiple active semantic attempts cannot be reconciled deterministically.");
            attemptId = active[0];
            state.CurrentAttemptId = attemptId;
        }

        var directory = Path.Combine(currentDirectory, "attempts", attemptId);
        var invocationPath = Path.Combine(directory, "invocation.json");
        if (!File.Exists(invocationPath)) throw new AgentProtocolException("UNKNOWN_ATTEMPT", $"Persisted attempt {attemptId} has no invocation artifact.");
        var invocation = JsonSerializer.Deserialize<AgentInvocation>(await File.ReadAllTextAsync(invocationPath, cancellationToken), FactoryJson.Options)
            ?? throw new AgentProtocolException("UNKNOWN_ATTEMPT", $"Persisted attempt {attemptId} is malformed.");
        if (invocation.RunId != state.RunId || invocation.AttemptId != attemptId)
            throw new AgentProtocolException("UNKNOWN_ATTEMPT", $"Persisted attempt {attemptId} identity is invalid.");
        var item = invocation.WorkItemId is null ? null : state.WorkItems.SingleOrDefault(x => x.Id == invocation.WorkItemId)
            ?? throw new AgentProtocolException("UNKNOWN_ATTEMPT", $"Attempt {attemptId} references unknown work.");
        await RecoverWorkspaceChangesAsync(state, item, invocation, cancellationToken);

        var resultPath = Path.Combine(directory, "result.json");
        if (File.Exists(resultPath))
        {
            AgentResultEnvelope? persisted;
            try { persisted = JsonSerializer.Deserialize<AgentResultEnvelope>(await File.ReadAllTextAsync(resultPath, cancellationToken), FactoryJson.Options); }
            catch (JsonException exception) { throw new AgentProtocolException("MALFORMED_AGENT_RESULT", exception.Message); }
            _ = new AgentResultValidator().Validate(invocation, persisted);
            await SaveAsync(state, cancellationToken);
            await events.WriteAsync(state.RunId, "agent-result-recoverable", new { attemptId, invocation.Role, invocation.WorkItemId }, cancellationToken);
            return;
        }

        state.CurrentAttemptId = null;
        if (item is not null)
        {
            item.CurrentAttemptId = null;
            if (item.Status is WorkItemStatus.Dispatching or WorkItemStatus.Running) item.Status = WorkItemStatus.Ready;
        }
        await SaveAsync(state, cancellationToken);
        await events.WriteAsync(state.RunId, "agent-attempt-interrupted", new { attemptId, invocation.Role, invocation.WorkItemId }, cancellationToken);
    }

    private async Task PersistWorkspaceSnapshotAsync(string runId, string attemptDirectory, CancellationToken cancellationToken)
    {
        var snapshot = await SnapshotWorkspaceAsync(runId, cancellationToken);
        await WriteJsonAtomicallyAsync(Path.Combine(attemptDirectory, "workspace-before.json"), new WorkspaceSnapshotArtifact(1, snapshot), cancellationToken);
    }

    private async Task RecoverWorkspaceChangesAsync(FactoryState state, WorkItemState? item, AgentInvocation invocation, CancellationToken cancellationToken)
    {
        if (invocation.ExecutionProfile != AgentExecutionProfile.WorkspaceWrite) return;
        var attemptDirectory = Path.GetDirectoryName(invocation.ResultPath)!;
        var changesPath = Path.Combine(attemptDirectory, "workspace-changes.json");
        WorkspaceChangesArtifact? changes;
        if (File.Exists(changesPath))
        {
            changes = JsonSerializer.Deserialize<WorkspaceChangesArtifact>(await File.ReadAllTextAsync(changesPath, cancellationToken), FactoryJson.Options)
                ?? throw new AgentProtocolException("MALFORMED_WORKSPACE_CHANGES", $"Attempt {invocation.AttemptId} has malformed workspace changes.");
        }
        else
        {
            var beforePath = Path.Combine(attemptDirectory, "workspace-before.json");
            if (!File.Exists(beforePath)) return;
            var before = JsonSerializer.Deserialize<WorkspaceSnapshotArtifact>(await File.ReadAllTextAsync(beforePath, cancellationToken), FactoryJson.Options)
                ?? throw new AgentProtocolException("MALFORMED_WORKSPACE_SNAPSHOT", $"Attempt {invocation.AttemptId} has malformed workspace baseline.");
            var after = await SnapshotWorkspaceAsync(state.RunId, cancellationToken);
            var changedPaths = after.Where(pair => !before.Files.TryGetValue(pair.Key, out var prior) || prior != pair.Value).Select(pair => pair.Key)
                .Concat(before.Files.Keys.Where(path => !after.ContainsKey(path)))
                .Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToList();
            changes = new WorkspaceChangesArtifact(1, changedPaths);
            await WriteJsonAtomicallyAsync(changesPath, changes, cancellationToken);
        }
        if (item is not null)
            foreach (var path in changes.ChangedPaths)
                if (!item.ChangedPaths.Contains(path, StringComparer.Ordinal)) item.ChangedPaths.Add(path);
        foreach (var path in changes.ChangedPaths)
            if (!state.FactoryRunChangedPaths.Contains(path, StringComparer.Ordinal)) state.FactoryRunChangedPaths.Add(path);
    }

    private async Task<SortedDictionary<string, string>> SnapshotWorkspaceAsync(string runId, CancellationToken cancellationToken)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(workspace);
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] directories;
            string[] files;
            try { directories = Directory.GetDirectories(directory); files = Directory.GetFiles(directory); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                await events.WriteAsync(runId, "workspace-snapshot-directory-skipped", new { path = RelativePath(directory), exception = exception.GetType().Name }, CancellationToken.None);
                continue;
            }
            foreach (var child in directories.OrderByDescending(path => path, StringComparer.Ordinal))
            {
                var relative = RelativePath(child);
                if (IsOperationalArtifact(relative)) continue;
                try { if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { await events.WriteAsync(runId, "workspace-snapshot-directory-skipped", new { path = relative, exception = exception.GetType().Name }, CancellationToken.None); }
            }
            foreach (var path in files.OrderBy(path => path, StringComparer.Ordinal))
            {
                var relative = RelativePath(path);
                if (IsOperationalArtifact(relative)) continue;
                try { result.Add(relative, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path, cancellationToken)))); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { await events.WriteAsync(runId, "workspace-snapshot-file-skipped", new { path = relative, exception = exception.GetType().Name }, CancellationToken.None); }
            }
        }
        return result;
    }

    private string RelativePath(string path) => Path.GetRelativePath(workspace, path).Replace('\\', '/');

    private static bool IsOperationalArtifact(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2 && segments[0].Equals(".idd", StringComparison.OrdinalIgnoreCase) && segments[1].Equals("factory", StringComparison.OrdinalIgnoreCase)) return true;
        return segments.Any(segment => segment.Equals(".git", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".vs", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".idea", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".vscode", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("TestResults", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task WriteJsonAtomicallyAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, FactoryJson.Options), cancellationToken);
        File.Move(temporary, path, true);
    }

    private sealed record WorkspaceSnapshotArtifact(int SchemaVersion, SortedDictionary<string, string> Files);
    private sealed record WorkspaceChangesArtifact(int SchemaVersion, List<string> ChangedPaths);
}
