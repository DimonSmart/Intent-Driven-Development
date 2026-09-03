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
            await File.WriteAllTextAsync(invocationPath, JsonSerializer.Serialize(invocation, FactoryJson.Options), cancellationToken);

        var protectedArtifacts = ProtectedArtifactEnforcer.Capture(invocation);
        var handle = await backend.StartAsync(invocation, cancellationToken);
        var process = await backend.WaitAsync(handle, cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(attemptDirectory, "process-telemetry.json"),
            JsonSerializer.Serialize(process, FactoryJson.Options),
            CancellationToken.None);

        protectedArtifacts.ValidateAndRestore();

        if (process.TerminationKind == AgentTerminationKind.Cancelled)
            throw new OperationCanceledException(cancellationToken);
        if (process.TerminationKind == AgentTerminationKind.TransportFailure && !process.CompleteResultObserved)
            throw new AgentProtocolException("AGENT_TRANSPORT_FAILURE", BuildTransportFailureMessage(process));
        if (!File.Exists(invocation.SemanticOutputPath))
            throw new AgentProtocolException("MISSING_AGENT_RESULT", "Agent did not produce its semantic output artifact.");

        var semanticResult = await File.ReadAllTextAsync(invocation.SemanticOutputPath, cancellationToken);
        if (invocation.Capability == "implementation" && string.IsNullOrWhiteSpace(semanticResult))
            throw new AgentProtocolException("MALFORMED_AGENT_RESULT", "Executor semantic result must contain human-readable text.");

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
        await WriteJsonAtomicallyAsync(Path.Combine(attemptDirectory, "result.json"), persisted, cancellationToken);
        return new(new BoundSemanticResult(invocation.AttemptId, semanticResult, relativeResultPath), process);
    }

    private static void ValidateInvocation(AgentInvocation invocation)
    {
        var agent = FactoryCapabilityCatalog.Resolve(invocation.Capability);
        if (agent.Role != invocation.Role || agent.SkillName != invocation.SkillName || agent.ExecutionProfile != invocation.ExecutionProfile)
            throw new AgentProtocolException("INVALID_AGENT_INVOCATION", "Invocation capability does not match its runtime-assigned agent contract.");
    }

    private static string BuildTransportFailureMessage(AgentProcessResult process)
    {
        const int maximumDiagnosticLength = 4096;
        var diagnostic = TryExtractStructuredFailure(process.Stdout);
        if (string.IsNullOrWhiteSpace(diagnostic))
            diagnostic = !string.IsNullOrWhiteSpace(process.Stderr)
                ? BoundedTail(process.Stderr, maximumDiagnosticLength)
                : BoundedTail(process.Stdout, maximumDiagnosticLength);
        else diagnostic = BoundedHead(diagnostic, maximumDiagnosticLength);

        if (string.IsNullOrWhiteSpace(diagnostic)) diagnostic = "no diagnostic output";
        return $"Agent exited with {process.ExitCode?.ToString() ?? "unknown"}: {diagnostic.Trim()}";
    }

    private static string? TryExtractStructuredFailure(string stdout)
    {
        string? errorMessage = null;
        string? turnFailedMessage = null;
        using var reader = new StringReader(stdout);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("type", out var type)
                    || type.ValueKind != JsonValueKind.String)
                    continue;

                switch (type.GetString())
                {
                    case "turn.failed" when root.TryGetProperty("error", out var error)
                                            && error.ValueKind == JsonValueKind.Object
                                            && error.TryGetProperty("message", out var failedMessage)
                                            && failedMessage.ValueKind == JsonValueKind.String
                                            && !string.IsNullOrWhiteSpace(failedMessage.GetString()):
                        turnFailedMessage = failedMessage.GetString();
                        break;
                    case "error" when root.TryGetProperty("message", out var message)
                                      && message.ValueKind == JsonValueKind.String
                                      && !string.IsNullOrWhiteSpace(message.GetString()):
                        errorMessage = message.GetString();
                        break;
                }
            }
            catch (JsonException) { }
        }
        return turnFailedMessage ?? errorMessage;
    }

    private static string BoundedHead(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength] + " [truncated]";

    private static string BoundedTail(string value, int maximumLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maximumLength ? trimmed : "[truncated] " + trimmed[^maximumLength..];
    }

    private static async Task WriteJsonAtomicallyAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, FactoryJson.Options), cancellationToken);
        File.Move(temporary, path, true);
    }
}
