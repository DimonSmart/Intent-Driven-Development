using System.Text.Json;
using Idd.Factory.Domain;

namespace Idd.Factory.Runtime;

internal sealed class SemanticExecutionService(
    FactoryRuntimeContext context,
    FactoryAgentExecutor agentExecutor)
{
    private readonly WorkspaceChangeCalculator workspaceChangeCalculator = new();

    public Task ReconcileAsync(FactoryState state, CancellationToken cancellationToken) =>
        new SemanticAttemptReconciler(
                context.CurrentDirectory,
                RecoverWorkspaceChangesAsync,
                context.SaveAsync)
            .ReconcileAsync(state, cancellationToken);

    public async Task<BoundSemanticResult> InvokeAsync(
        FactoryState state,
        string capability,
        PlannedWorkItem? item,
        string input,
        SemanticOperationKind operation,
        CancellationToken cancellationToken)
    {
        var agent = FactoryCapabilityCatalog.Resolve(capability);
        if (state.CurrentAttemptId is { } persistedAttempt)
        {
            var directory = Path.Combine(context.CurrentDirectory, "attempts", persistedAttempt);
            var invocationPath = Path.Combine(directory, "invocation.json");
            var persistedResultPath = Path.Combine(directory, "result.json");
            if (!File.Exists(invocationPath) || !File.Exists(persistedResultPath))
            {
                throw new AgentProtocolException(
                    "UNKNOWN_ATTEMPT",
                    $"Persisted attempt {persistedAttempt} cannot be resumed from its artifacts.");
            }

            var invocation = JsonSerializer.Deserialize<AgentInvocation>(
                                 await File.ReadAllTextAsync(invocationPath, cancellationToken),
                                 FactoryJson.Options)
                             ?? throw new AgentProtocolException(
                                 "UNKNOWN_ATTEMPT",
                                 $"Attempt {persistedAttempt} has no valid invocation.");
            if (invocation.RunId != state.RunId
                || invocation.AttemptId != persistedAttempt
                || invocation.Capability != capability
                || invocation.Role != agent.Role
                || invocation.WorkItemId != item?.Id)
            {
                throw new AgentProtocolException(
                    "UNKNOWN_ATTEMPT",
                    $"Attempt {persistedAttempt} does not belong to the current operation.");
            }

            await RecoverWorkspaceChangesAsync(state, item, invocation, cancellationToken);
            var persisted = JsonSerializer.Deserialize<PersistedAttemptResult>(
                await File.ReadAllTextAsync(persistedResultPath, cancellationToken),
                FactoryJson.Options);
            var validated = ValidatePersistedResult(invocation, persisted);
            state.CurrentAttemptId = null;
            if (item is not null)
                item.CurrentAttemptId = null;
            state.PendingContinuation = null;
            await context.SaveAsync(state, cancellationToken);
            return validated;
        }

        var attemptId = $"A{++state.AttemptSequence:000000}";
        state.CurrentAttemptId = attemptId;
        if (item is not null)
        {
            item.CurrentAttemptId = attemptId;
            item.AttemptCount++;
        }

        state.PendingContinuation = new(
            ContinuationKind.SemanticInvocation,
            item?.Id,
            null,
            operation.ToString().ToUpperInvariant(),
            true,
            operation,
            input);
        await context.SaveAsync(state, cancellationToken);

        var attemptDirectory = Path.Combine(context.CurrentDirectory, "attempts", attemptId);
        Directory.CreateDirectory(attemptDirectory);
        var semanticOutputPath = Path.Combine(
            attemptDirectory,
            capability == "planning" ? "planning-output.md" : "semantic-result.md");
        var invocationNew = new AgentInvocation
        {
            RunId = state.RunId,
            AttemptId = attemptId,
            Capability = capability,
            Role = agent.Role,
            WorkItemId = item?.Id,
            Workspace = context.Workspace,
            SemanticOutputPath = semanticOutputPath,
            SkillName = agent.SkillName,
            ExecutionProfile = agent.ExecutionProfile,
            Input = input,
            StartedAt = context.Clock.UtcNow
        };
        await WriteJsonAtomicallyAsync(
            Path.Combine(attemptDirectory, "invocation.json"),
            invocationNew,
            cancellationToken);
        if (agent.ExecutionProfile == AgentExecutionProfile.WorkspaceWrite)
            await PersistWorkspaceSnapshotAsync(state.RunId, attemptDirectory, cancellationToken);

        await context.Events.WriteAsync(
            state.RunId,
            "agent-dispatching",
            new { attemptId, capability, agent.Role, workItemId = item?.Id },
            cancellationToken);

        AgentExecutionResult execution;
        try
        {
            execution = await agentExecutor.ExecuteAsync(invocationNew, cancellationToken);
        }
        finally
        {
            if (agent.ExecutionProfile == AgentExecutionProfile.WorkspaceWrite)
            {
                await RecoverWorkspaceChangesAsync(
                    state,
                    item,
                    invocationNew,
                    CancellationToken.None);
            }
        }

        state.CurrentAttemptId = null;
        if (item is not null)
            item.CurrentAttemptId = null;
        state.PendingContinuation = null;
        await context.SaveAsync(state, cancellationToken);
        await context.Events.WriteAsync(
            state.RunId,
            "agent-completed",
            new { attemptId, capability, agent.Role, execution.Process.TerminationKind },
            cancellationToken);
        return execution.Result;
    }

    public async Task<bool> AttemptChangedWorkspaceAsync(
        string attemptId,
        CancellationToken cancellationToken)
    {
        var changesPath = Path.Combine(
            context.CurrentDirectory,
            "attempts",
            attemptId,
            "workspace-changes.json");
        if (!File.Exists(changesPath))
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                $"Attempt {attemptId} has no workspace changes artifact.");
        }

        var changes = JsonSerializer.Deserialize<WorkspaceChangesArtifact>(
                          await File.ReadAllTextAsync(changesPath, cancellationToken),
                          FactoryJson.Options)
                      ?? throw new FactoryStateException(
                          "CORRUPT_FACTORY_STATE",
                          $"Attempt {attemptId} has an invalid workspace changes artifact.");
        if (changes.SchemaVersion != 1)
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                $"Attempt {attemptId} has an unsupported workspace changes schema.");
        }

        return changes.ChangedPaths.Count > 0;
    }

    private async Task PersistWorkspaceSnapshotAsync(
        string runId,
        string attemptDirectory,
        CancellationToken cancellationToken) =>
        await WriteJsonAtomicallyAsync(
            Path.Combine(attemptDirectory, "workspace-before.json"),
            new WorkspaceSnapshotArtifact(
                1,
                await SnapshotWorkspaceAsync(runId, cancellationToken)),
            cancellationToken);

    private async Task RecoverWorkspaceChangesAsync(
        FactoryState state,
        PlannedWorkItem? item,
        AgentInvocation invocation,
        CancellationToken cancellationToken)
    {
        if (invocation.ExecutionProfile != AgentExecutionProfile.WorkspaceWrite)
            return;

        var directory = Path.GetDirectoryName(invocation.SemanticOutputPath)!;
        var changesPath = Path.Combine(directory, "workspace-changes.json");
        WorkspaceChangesArtifact changes;
        if (File.Exists(changesPath))
        {
            changes = JsonSerializer.Deserialize<WorkspaceChangesArtifact>(
                          await File.ReadAllTextAsync(changesPath, cancellationToken),
                          FactoryJson.Options)
                      ?? throw new FactoryStateException(
                          "CORRUPT_FACTORY_STATE",
                          $"Attempt {invocation.AttemptId} has an invalid workspace changes artifact.");
        }
        else
        {
            var beforePath = Path.Combine(directory, "workspace-before.json");
            if (!File.Exists(beforePath))
                return;

            var before = JsonSerializer.Deserialize<WorkspaceSnapshotArtifact>(
                             await File.ReadAllTextAsync(beforePath, cancellationToken),
                             FactoryJson.Options)
                         ?? throw new FactoryStateException(
                             "CORRUPT_FACTORY_STATE",
                             $"Attempt {invocation.AttemptId} has an invalid workspace snapshot artifact.");
            var after = await SnapshotWorkspaceAsync(state.RunId, cancellationToken);
            changes = new(
                1,
                workspaceChangeCalculator.Calculate(before.Files, after).ToList());
            await WriteJsonAtomicallyAsync(changesPath, changes, cancellationToken);
        }

        foreach (var path in changes.ChangedPaths)
        {
            if (item is not null && !item.ChangedPaths.Contains(path, StringComparer.Ordinal))
                item.ChangedPaths.Add(path);
            if (!state.FactoryRunChangedPaths.Contains(path, StringComparer.Ordinal))
                state.FactoryRunChangedPaths.Add(path);
        }
    }

    private async Task<SortedDictionary<string, string>> SnapshotWorkspaceAsync(
        string runId,
        CancellationToken cancellationToken)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in await WorkspaceSnapshotFileEnumerator.EnumerateAsync(
                     context.Workspace,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = RelativePath(path);
            if (IsOperationalArtifact(relative))
                continue;

            try
            {
                result[relative] = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        await File.ReadAllBytesAsync(path, cancellationToken)));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                await context.Events.WriteAsync(
                    runId,
                    "workspace-snapshot-file-skipped",
                    new { path = relative, exception = exception.GetType().Name },
                    CancellationToken.None);
            }
        }

        return result;
    }

    private string RelativePath(string path) =>
        Path.GetRelativePath(context.Workspace, path).Replace('\\', '/');

    private static bool IsOperationalArtifact(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2
            && segments[0].Equals(".idd", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals("factory", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return segments.Any(x =>
            x.Equals(".git", StringComparison.OrdinalIgnoreCase)
            || x.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || x.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || x.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
            || x.Equals("TestResults", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task WriteJsonAtomicallyAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(
            temporary,
            JsonSerializer.Serialize(value, FactoryJson.Options),
            cancellationToken);
        File.Move(temporary, path, true);
    }

    private BoundSemanticResult ValidatePersistedResult(
        AgentInvocation invocation,
        PersistedAttemptResult? persisted)
    {
        if (persisted is null
            || persisted.SchemaVersion != PersistedAttemptResult.CurrentSchemaVersion)
        {
            throw new AgentProtocolException(
                "UNSUPPORTED_ATTEMPT_RESULT_SCHEMA",
                "Persisted attempt result has an unsupported schema version.");
        }

        var expected = AttemptIdentity.From(invocation);
        if (persisted.Invocation != expected)
        {
            throw new AgentProtocolException(
                "ATTEMPT_RESULT_IDENTITY_MISMATCH",
                "Persisted attempt result does not belong to its invocation.");
        }

        var semanticPath = Path.Combine(
            context.CurrentDirectory,
            persisted.SemanticResultPath);
        if (!File.Exists(semanticPath))
        {
            throw new AgentProtocolException(
                "MISSING_AGENT_RESULT",
                "Persisted semantic result artifact is missing.");
        }

        var semantic = File.ReadAllText(semanticPath);
        if (invocation.Capability == "implementation"
            && string.IsNullOrWhiteSpace(semantic))
        {
            throw new AgentProtocolException(
                "MALFORMED_AGENT_RESULT",
                "Executor semantic result must contain human-readable text.");
        }

        return new(
            invocation.AttemptId,
            semantic,
            persisted.SemanticResultPath);
    }

    private sealed record WorkspaceSnapshotArtifact(
        int SchemaVersion,
        SortedDictionary<string, string> Files);

    private sealed record WorkspaceChangesArtifact(
        int SchemaVersion,
        List<string> ChangedPaths);
}
