using System.Text.Json;
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
        ValidateIdentity(state, attemptId, invocation);
        await recoverWorkspaceChanges(state, state.Current, invocation, cancellationToken);

        if (File.Exists(Path.Combine(directory, "result.json")))
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
            return;
        }

        ClearInterruptedAttempt(state, decrementWorkItemAttempt: false);
        await save(state, cancellationToken);
    }

    internal static SemanticOperationKind ResolveOperation(AgentInvocation invocation) =>
        FactoryCapabilityCatalog.ResolveSemanticOperation(invocation.Capability);

    internal static void ValidateIdentity(FactoryState state, string attemptId, AgentInvocation invocation)
    {
        var expectedCapability = state.Current?.Capability ?? invocation.Capability;
        var expectedAgent = FactoryCapabilityCatalog.Resolve(expectedCapability).Agent;
        if (invocation.RunId != state.RunId
            || invocation.AttemptId != attemptId
            || invocation.Capability != expectedCapability
            || invocation.Role != expectedAgent.Role
            || invocation.WorkItemId != state.Current?.Id)
            throw new AgentProtocolException("UNKNOWN_ATTEMPT", $"Persisted attempt {attemptId} identity is invalid.");
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
