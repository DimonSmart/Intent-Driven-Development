using System.Text.Json;
using Idd.Factory.Domain;

namespace Idd.Factory.Agents;

public sealed class FactoryAgentExecutor(IAgentBackend backend)
{
    public async Task<AgentExecutionResult> ExecuteAsync(AgentInvocation invocation, CancellationToken cancellationToken)
    {
        ValidateInvocation(invocation);
        var attemptDirectory = Path.GetDirectoryName(invocation.SemanticOutputPath)!;
        Directory.CreateDirectory(attemptDirectory);
        var invocationPath = Path.Combine(attemptDirectory, "invocation.json");
        if (!File.Exists(invocationPath))
        {
            await File.WriteAllTextAsync(
                invocationPath,
                JsonSerializer.Serialize(invocation, FactoryJson.Options),
                cancellationToken);
        }

        var protectedArtifacts = ProtectedArtifactEnforcer.Capture(invocation);
        var handle = await backend.StartAsync(invocation, cancellationToken);
        var process = await backend.WaitAsync(handle, cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(attemptDirectory, "process-telemetry.json"),
            JsonSerializer.Serialize(process, FactoryJson.Options),
            CancellationToken.None);

        protectedArtifacts.ValidateAndRestore();

        if (process.CompleteResultObserved && File.Exists(invocation.SemanticOutputPath))
            return await PersistSuccessfulResultAsync(invocation, process, attemptDirectory, cancellationToken);

        if (process.TerminationKind == AgentTerminationKind.Cancelled)
            throw new OperationCanceledException(cancellationToken);

        if (process.TerminationKind is AgentTerminationKind.CommandTimeout
            or AgentTerminationKind.IncompleteCommand
            or AgentTerminationKind.TransportFailure)
        {
            var diagnostic = await AgentFailureDiagnosticStore.PersistAsync(
                invocation.AttemptId,
                attemptDirectory,
                process,
                CancellationToken.None);
            throw new AgentProtocolException(
                diagnostic.FailureCode,
                diagnostic.HumanReadableMessage,
                AgentFailureDiagnosticStore.ReferenceFor(invocation.AttemptId),
                diagnostic);
        }

        if (!File.Exists(invocation.SemanticOutputPath))
        {
            throw new AgentProtocolException(
                "MISSING_AGENT_RESULT",
                "Agent did not produce its semantic output artifact.");
        }

        return await PersistSuccessfulResultAsync(invocation, process, attemptDirectory, cancellationToken);
    }

    private static async Task<AgentExecutionResult> PersistSuccessfulResultAsync(
        AgentInvocation invocation,
        AgentProcessResult process,
        string attemptDirectory,
        CancellationToken cancellationToken)
    {
        var semanticResult = await File.ReadAllTextAsync(invocation.SemanticOutputPath, cancellationToken);
        if (invocation.Capability == "implementation" && string.IsNullOrWhiteSpace(semanticResult))
        {
            throw new AgentProtocolException(
                "MALFORMED_AGENT_RESULT",
                "Executor semantic result must contain human-readable text.");
        }

        var relativeResultPath = Path.GetRelativePath(
            Path.Combine(invocation.Workspace, ".idd", "factory", "current"),
            invocation.SemanticOutputPath).Replace('\\', '/');
        var persisted = new PersistedAttemptResult
        {
            Invocation = AttemptIdentity.From(invocation),
            SemanticResultPath = relativeResultPath,
            ReceivedAt = DateTimeOffset.UtcNow,
            TerminationKind = process.TerminationKind
        };
        await WriteJsonAtomicallyAsync(
            Path.Combine(attemptDirectory, "result.json"),
            persisted,
            cancellationToken);
        return new(
            new BoundSemanticResult(invocation.AttemptId, semanticResult, relativeResultPath),
            process);
    }

    private static void ValidateInvocation(AgentInvocation invocation)
    {
        var agent = FactoryCapabilityCatalog.Resolve(invocation.Capability);
        if (agent.Role != invocation.Role
            || agent.SkillName != invocation.SkillName
            || agent.ExecutionProfile != invocation.ExecutionProfile)
        {
            throw new AgentProtocolException(
                "INVALID_AGENT_INVOCATION",
                "Invocation capability does not match its runtime-assigned agent contract.");
        }
    }

    private static async Task WriteJsonAtomicallyAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(
            temporary,
            JsonSerializer.Serialize(value, FactoryJson.Options),
            cancellationToken);
        File.Move(temporary, path, true);
    }
}
