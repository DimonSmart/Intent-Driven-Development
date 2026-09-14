using System.Text.Json;
using Idd.Factory.Domain;

namespace Idd.Factory.Runtime;

internal sealed record SemanticExecutionResult(
    BoundSemanticResult Result,
    IReadOnlyList<string> ChangedPaths);

internal sealed class SemanticExecutionService(
    FactoryRuntimeContext context,
    FactoryAgentExecutor agentExecutor)
{
    private readonly WorkspaceChangeCalculator workspaceChangeCalculator = new();

    public Task<SemanticAttemptRecoveryResult> InspectRecoveryAsync(
        FactoryState state,
        CancellationToken cancellationToken) =>
        new SemanticAttemptReconciler(
                context.CurrentDirectory,
                RecoverWorkspaceChangesAsync)
            .AnalyzeAsync(state, cancellationToken);

    public async Task<SemanticExecutionResult> InvokeAsync(
        FactoryState state,
        string capability,
        PlannedWorkItem? item,
        string input,
        string attemptId,
        CancellationToken cancellationToken)
    {
        var agent = FactoryCapabilityCatalog.Resolve(capability);
        var directory = Path.Combine(context.CurrentDirectory, "attempts", attemptId);
        var invocationPath = Path.Combine(directory, "invocation.json");
        var persistedResultPath = Path.Combine(directory, "result.json");

        if (File.Exists(invocationPath) || File.Exists(persistedResultPath))
        {
            if (!File.Exists(invocationPath) || !File.Exists(persistedResultPath))
            {
                throw new AgentProtocolException(
                    "UNKNOWN_ATTEMPT",
                    $"Persisted attempt {attemptId} cannot be resumed from its artifacts.");
            }

            var invocation = JsonSerializer.Deserialize<AgentInvocation>(
                                 await File.ReadAllTextAsync(invocationPath, cancellationToken),
                                 FactoryJson.Options)
                             ?? throw new AgentProtocolException(
                                 "UNKNOWN_ATTEMPT",
                                 $"Attempt {attemptId} has no valid invocation.");
            ValidateInvocation(
                state,
                attemptId,
                capability,
                agent,
                item,
                invocation,
                directory);
            var changedPaths = await RecoverWorkspaceChangesAsync(invocation, cancellationToken);
            var persisted = JsonSerializer.Deserialize<PersistedAttemptResult>(
                await File.ReadAllTextAsync(persistedResultPath, cancellationToken),
                FactoryJson.Options);
            return new(
                ValidatePersistedResult(invocation, persisted),
                changedPaths);
        }

        Directory.CreateDirectory(directory);
        var semanticOutputPath = Path.Combine(
            directory,
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
        await WriteJsonAtomicallyAsync(invocationPath, invocationNew, cancellationToken);
        if (agent.ExecutionProfile == AgentExecutionProfile.WorkspaceWrite)
            await PersistWorkspaceSnapshotAsync(state.RunId, directory, cancellationToken);

        await context.Events.WriteAsync(
            state.RunId,
            "agent-dispatching",
            new { attemptId, capability, agent.Role, workItemId = item?.Id },
            cancellationToken);

        AgentExecutionResult execution;
        IReadOnlyList<string> changedPathsAfterExecution;
        try
        {
            execution = await agentExecutor.ExecuteAsync(invocationNew, cancellationToken);
        }
        finally
        {
            changedPathsAfterExecution = agent.ExecutionProfile == AgentExecutionProfile.WorkspaceWrite
                ? await RecoverWorkspaceChangesAsync(invocationNew, CancellationToken.None)
                : [];
        }

        await context.Events.WriteAsync(
            state.RunId,
            "agent-completed",
            new { attemptId, capability, agent.Role, execution.Process.TerminationKind },
            cancellationToken);
        return new(execution.Result, changedPathsAfterExecution);
    }

    private static void ValidateInvocation(
        FactoryState state,
        string attemptId,
        string capability,
        FactoryAgentContract agent,
        PlannedWorkItem? item,
        AgentInvocation invocation,
        string directory)
    {
        if (invocation.RunId != state.RunId
            || invocation.AttemptId != attemptId
            || invocation.Capability != capability
            || invocation.Role != agent.Role
            || invocation.WorkItemId != item?.Id)
        {
            throw new AgentProtocolException(
                "UNKNOWN_ATTEMPT",
                $"Attempt {attemptId} does not belong to the current operation.");
        }

        SemanticAttemptReconciler.ValidateIdentity(state, attemptId, invocation, directory);
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

    private async Task<IReadOnlyList<string>> RecoverWorkspaceChangesAsync(
        AgentInvocation invocation,
        CancellationToken cancellationToken)
    {
        if (invocation.ExecutionProfile != AgentExecutionProfile.WorkspaceWrite)
            return [];

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
                return [];

            var before = JsonSerializer.Deserialize<WorkspaceSnapshotArtifact>(
                             await File.ReadAllTextAsync(beforePath, cancellationToken),
                             FactoryJson.Options)
                         ?? throw new FactoryStateException(
                             "CORRUPT_FACTORY_STATE",
                             $"Attempt {invocation.AttemptId} has an invalid workspace snapshot artifact.");
            var after = await SnapshotWorkspaceAsync(invocation.RunId, cancellationToken);
            changes = new(
                1,
                workspaceChangeCalculator.Calculate(before.Files, after).ToList());
            await WriteJsonAtomicallyAsync(changesPath, changes, cancellationToken);
        }

        if (changes.SchemaVersion != 1)
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                $"Attempt {invocation.AttemptId} has an unsupported workspace changes schema.");
        }

        return changes.ChangedPaths;
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
