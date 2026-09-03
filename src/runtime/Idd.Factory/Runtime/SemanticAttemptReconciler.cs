using System.Text.Json;
using Idd.Factory.Agents;
using Idd.Factory.Domain;

namespace Idd.Factory.Runtime;

internal sealed class SemanticAttemptReconciler(
    string currentDirectory,
    Func<FactoryState, PlannedWorkItem?, AgentInvocation, CancellationToken, Task> recoverWorkspaceChanges,
    Func<FactoryState, CancellationToken, Task> save)
{
    public async Task ReconcileAsync(FactoryState state, CancellationToken cancellationToken)
    {
        var attemptId = state.CurrentAttemptId ?? state.Current?.CurrentAttemptId;
        if (attemptId is null) return;

        state.CurrentAttemptId = attemptId;
        var directory = Path.Combine(currentDirectory, "attempts", attemptId);
        var invocationPath = Path.Combine(directory, "invocation.json");
        if (!File.Exists(invocationPath))
        {
            ClearInterruptedAttempt(state, decrementWorkItemAttempt: true);
            await save(state, cancellationToken);
            return;
        }

        var invocation = JsonSerializer.Deserialize<AgentInvocation>(await File.ReadAllTextAsync(invocationPath, cancellationToken), FactoryJson.Options)
            ?? throw new AgentProtocolException("UNKNOWN_ATTEMPT", $"Persisted attempt {attemptId} is malformed.");
        ValidateIdentity(state, attemptId, invocation, directory);
        await recoverWorkspaceChanges(state, state.Current, invocation, cancellationToken);

        var resultPath = Path.Combine(directory, "result.json");
        if (File.Exists(resultPath))
        {
            await PrepareContinuationAsync(state, invocation, cancellationToken);
            return;
        }

        if (File.Exists(invocation.SemanticOutputPath))
        {
            await RecoverSemanticResultAsync(state, attemptId, directory, invocation, resultPath, cancellationToken);
            return;
        }

        ClearInterruptedAttempt(state, decrementWorkItemAttempt: false);
        await save(state, cancellationToken);
    }

    private async Task RecoverSemanticResultAsync(
        FactoryState state,
        string attemptId,
        string directory,
        AgentInvocation invocation,
        string resultPath,
        CancellationToken cancellationToken)
    {
        var telemetryPath = Path.Combine(directory, "process-telemetry.json");
        if (!File.Exists(telemetryPath))
            throw new AgentProtocolException("ATTEMPT_RECOVERY_UNSAFE", $"Attempt '{attemptId}' has semantic output but no process telemetry.");

        AgentProcessResult? process;
        try
        {
            process = JsonSerializer.Deserialize<AgentProcessResult>(await File.ReadAllTextAsync(telemetryPath, cancellationToken), FactoryJson.Options);
        }
        catch (JsonException exception)
        {
            throw new AgentProtocolException("ATTEMPT_RECOVERY_UNSAFE", $"Attempt '{attemptId}' has invalid process telemetry: {exception.Message}");
        }
        if (process is null || !process.CompleteResultObserved || process.TerminationKind == AgentTerminationKind.Cancelled)
            throw new AgentProtocolException("ATTEMPT_RECOVERY_UNSAFE", $"Attempt '{attemptId}' does not have telemetry proving that complete semantic output was observed.");

        var semantic = await File.ReadAllTextAsync(invocation.SemanticOutputPath, cancellationToken);
        if (invocation.Capability == "implementation" && string.IsNullOrWhiteSpace(semantic))
            throw new AgentProtocolException("MALFORMED_AGENT_RESULT", $"Executor result for attempt '{attemptId}' is empty.");

        var relative = Path.GetRelativePath(currentDirectory, invocation.SemanticOutputPath).Replace('\\', '/');
        var persisted = new PersistedAttemptResult
        {
            Invocation = AttemptIdentity.From(invocation),
            SemanticResultPath = relative,
            ReceivedAt = DateTimeOffset.UtcNow,
            TerminationKind = process.TerminationKind
        };
        await WriteJsonAtomicallyAsync(resultPath, persisted, cancellationToken);
        await PrepareContinuationAsync(state, invocation, cancellationToken);
    }

    private async Task PrepareContinuationAsync(FactoryState state, AgentInvocation invocation, CancellationToken cancellationToken)
    {
        var operation = ResolveOperation(invocation);
        state.PendingContinuation = new(
            ContinuationKind.SemanticInvocation,
            state.Current?.Id,
            null,
            operation.ToString(),
            true,
            operation);
        await save(state, cancellationToken);
    }

    internal static SemanticOperationKind ResolveOperation(AgentInvocation invocation) => invocation.Capability switch
    {
        "planning" => SemanticOperationKind.Planning,
        "implementation" => SemanticOperationKind.WorkItemExecution,
        _ => throw new AgentProtocolException("UNKNOWN_CAPABILITY", $"Unknown Factory capability '{invocation.Capability}'.")
    };

    internal static void ValidateIdentity(FactoryState state, string attemptId, AgentInvocation invocation, string? attemptDirectory = null)
    {
        var expectedCapability = state.Current is null ? "planning" : "implementation";
        var expectedAgent = FactoryCapabilityCatalog.Resolve(expectedCapability);
        if (invocation.SchemaVersion != AgentInvocation.CurrentSchemaVersion
            || invocation.RunId != state.RunId
            || invocation.AttemptId != attemptId
            || invocation.Capability != expectedCapability
            || invocation.Role != expectedAgent.Role
            || invocation.SkillName != expectedAgent.SkillName
            || invocation.ExecutionProfile != expectedAgent.ExecutionProfile
            || invocation.WorkItemId != state.Current?.Id)
            throw new AgentProtocolException("UNKNOWN_ATTEMPT", $"Persisted attempt {attemptId} identity is invalid.");

        if (state.PendingContinuation is { Kind: ContinuationKind.SemanticInvocation, Operation: not SemanticOperationKind.None } pending
            && (pending.Operation != ResolveOperation(invocation) || pending.WorkItemId != state.Current?.Id))
            throw new AgentProtocolException("UNKNOWN_ATTEMPT", $"Persisted attempt {attemptId} does not belong to the pending semantic operation.");

        if (attemptDirectory is not null)
        {
            var expectedName = expectedCapability == "planning" ? "planning-output.md" : "semantic-result.md";
            var expectedOutput = Path.Combine(attemptDirectory, expectedName);
            if (!SamePath(expectedOutput, invocation.SemanticOutputPath))
                throw new AgentProtocolException("UNKNOWN_ATTEMPT", $"Persisted attempt {attemptId} points to semantic output outside its exact attempt directory.");
        }
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static async Task WriteJsonAtomicallyAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, FactoryJson.Options), cancellationToken);
        File.Move(temporary, path, true);
    }

    private static void ClearInterruptedAttempt(FactoryState state, bool decrementWorkItemAttempt)
    {
        state.CurrentAttemptId = null;
        if (state.Current is not null)
        {
            state.Current.CurrentAttemptId = null;
            if (decrementWorkItemAttempt) state.Current.AttemptCount = Math.Max(0, state.Current.AttemptCount - 1);
            state.CurrentPhase = CurrentWorkPhase.Ready;
        }
        state.PendingContinuation = null;
    }
}
