using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Runtime;

namespace Idd.Factory.Agents;

/// <summary>
/// Protocol validator for the linear Factory. Role is transport metadata;
/// runtime scheduling is based on capabilities.
/// </summary>
public sealed class FactoryAgentResultValidator
{
    public SemanticAgentResult ParseAndValidate(AgentInvocation invocation, string json)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(json); }
        catch (JsonException exception) { throw Malformed(invocation, exception.Message); }
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw Malformed(invocation, "Semantic result must be one JSON object.");

            var contract = SemanticResultContracts.Resolve(invocation);
            if (!document.RootElement.TryGetProperty("outcome", out var outcomeElement)
                || outcomeElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(outcomeElement.GetString()))
                throw Malformed(invocation, "Result outcome is required.");
            var outcome = contract.ResolveOutcome(outcomeElement.GetString()!);
            SemanticResultContracts.ValidateJsonFields(invocation, document.RootElement, contract, outcome);

            SemanticAgentResult? result;
            try { result = document.RootElement.Deserialize<SemanticAgentResult>(FactoryJson.Options); }
            catch (JsonException exception) { throw Malformed(invocation, exception.Message); }
            return Validate(invocation, result);
        }
    }

    public SemanticAgentResult Validate(AgentInvocation invocation, SemanticAgentResult? result)
    {
        if (result is null || string.IsNullOrWhiteSpace(result.Outcome)) throw Malformed(invocation, "Result outcome is required.");
        var capability = FactoryCapabilityCatalog.Resolve(invocation.Capability);
        if (capability.Agent.Role != invocation.Role || capability.Agent.SkillName != invocation.SkillName || capability.Agent.ExecutionProfile != invocation.ExecutionProfile)
            throw new AgentProtocolException("INVALID_AGENT_INVOCATION", "Invocation capability does not match its runtime-assigned agent contract.");

        var contract = SemanticResultContracts.Resolve(invocation);
        var outcome = contract.ResolveOutcome(result.Outcome);
        SemanticResultContracts.ValidateTypedFields(invocation, result, contract, outcome);

        if (result.Outcome == "completed"
            && invocation.Capability is "implementation" or "research"
            && string.IsNullOrWhiteSpace(result.Summary))
            throw Malformed(invocation, $"{invocation.Capability} completed result requires non-empty summary.");
        if (result.Outcome == "ready") ValidatePlanningTasks(result.Tasks);
        if (result.Outcome is "additional-work-required" or "correction-required") ValidateFutureTask(result.Payload, result.Outcome);
        if (result.Outcome == "intent-required") IntentRequiredPayload.Validate(result.Payload);
        return result;
    }

    private static void ValidatePlanningTasks(JsonElement? tasks)
    {
        if (tasks is not { ValueKind: JsonValueKind.Array } array) throw Malformed("Planning ready result requires top-level tasks.");
        foreach (var task in array.EnumerateArray())
        {
            if (task.ValueKind != JsonValueKind.Object) throw Malformed("Each planned task must be an object.");
            if (task.EnumerateObject().Any(property => property.Name is not ("capability" or "task")))
                throw Malformed("Planning tasks may contain only capability and task.");
            var capability = RequiredString(task, "capability", "Planned task capability is required.");
            FactoryCapabilityCatalog.ResolveWorkItem(capability);
            RequiredString(task, "task", "Planned task text is required.");
        }
    }

    private static void ValidateFutureTask(JsonElement? payload, string outcome)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } task) throw Malformed($"{outcome} requires an object payload.");
        var capability = RequiredString(task, "capability", $"{outcome} payload.capability is required.");
        FactoryCapabilityCatalog.ResolveWorkItem(capability);
        RequiredString(task, "task", $"{outcome} payload.task is required.");
        RequiredString(task, "reason", $"{outcome} payload.reason is required.");
    }

    private static string RequiredString(JsonElement value, string property, string message) =>
        value.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(element.GetString())
            ? element.GetString()!
            : throw Malformed(message);

    private static AgentProtocolException Malformed(AgentInvocation invocation, string message) =>
        new("MALFORMED_AGENT_RESULT", $"Capability '{invocation.Capability}', semantic result schema '{invocation.SemanticResultSchema}': {message}");

    private static AgentProtocolException Malformed(string message) => new("MALFORMED_AGENT_RESULT", message);
}

/// <summary>
/// Uses the existing backend transport, validates the capability-oriented protocol, and enforces
/// runner/product ownership by restoring protected artifacts before reporting worker violations.
/// </summary>
public sealed class FactoryAgentExecutor(IAgentBackend backend, FactoryAgentResultValidator validator)
{
    public async Task<AgentExecutionResult> ExecuteAsync(AgentInvocation invocation, CancellationToken cancellationToken)
    {
        var attemptDirectory = Path.GetDirectoryName(invocation.RawResultPath)!;
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

        // Protected ownership is authoritative even when the semantic process crashes or is cancelled.
        // Restore protected roots before any retry/recovery path can observe worker mutations.
        protectedArtifacts.ValidateAndRestore();

        if (process.TerminationKind == AgentTerminationKind.Cancelled) throw new OperationCanceledException(cancellationToken);
        if (process.TerminationKind == AgentTerminationKind.TransportFailure && !process.CompleteResultObserved)
            throw new AgentProtocolException("AGENT_TRANSPORT_FAILURE", BuildTransportFailureMessage(process));

        if (!File.Exists(invocation.RawResultPath)) throw new AgentProtocolException("MISSING_AGENT_RESULT", "Agent did not produce raw-result.json.");

        var semanticResult = validator.ParseAndValidate(invocation, await File.ReadAllTextAsync(invocation.RawResultPath, cancellationToken));
        var persisted = new PersistedAttemptResult
        {
            Invocation = AttemptIdentity.From(invocation),
            SemanticResult = semanticResult,
            ReceivedAt = DateTimeOffset.UtcNow,
            TerminationKind = process.TerminationKind
        };
        await WriteJsonAtomicallyAsync(Path.Combine(attemptDirectory, "result.json"), persisted, cancellationToken);
        return new(new BoundSemanticAgentResult(invocation.AttemptId, semanticResult), process);
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
