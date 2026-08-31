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
    private static readonly IReadOnlyDictionary<string, HashSet<string>> Outcomes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
    {
        ["planning"] = ["ready", "intent-required", "needs-clarification", "focused-handoff", "blocked"],
        ["implementation"] = ["completed", "additional-work-required", "global-replan-required", "blocked", "intent-required"],
        ["research"] = ["completed", "additional-work-required", "global-replan-required", "blocked", "intent-required"],
        ["semantic-review"] = ["approved", "correction-required", "additional-work-required", "global-replan-required", "blocked", "intent-required"],
        ["final-review"] = ["approved", "correction-required", "additional-work-required", "global-replan-required", "blocked", "intent-required"]
    };

    private static readonly HashSet<string> CommonFields = ["outcome", "reason", "payload", "metrics"];

    public SemanticAgentResult ParseAndValidate(AgentInvocation invocation, string json)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(json); }
        catch (JsonException exception) { throw new AgentProtocolException("MALFORMED_AGENT_RESULT", exception.Message); }
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw Malformed("Semantic result must be one JSON object.");
            var allowed = new HashSet<string>(CommonFields, StringComparer.Ordinal);
            if (invocation.Capability == "planning") allowed.Add("tasks");
            var unexpected = document.RootElement.EnumerateObject().FirstOrDefault(property => !allowed.Contains(property.Name));
            if (unexpected.Name is not null)
                throw Malformed($"Semantic result field '{unexpected.Name}' is not allowed; runtime identity and bookkeeping must not be returned by workers.");
            SemanticAgentResult? result;
            try { result = document.RootElement.Deserialize<SemanticAgentResult>(FactoryJson.Options); }
            catch (JsonException exception) { throw Malformed(exception.Message); }
            return Validate(invocation, result);
        }
    }

    public SemanticAgentResult Validate(AgentInvocation invocation, SemanticAgentResult? result)
    {
        if (result is null || string.IsNullOrWhiteSpace(result.Outcome)) throw Malformed("Result outcome is required.");
        var contract = FactoryCapabilityCatalog.Resolve(invocation.Capability);
        if (contract.Agent.Role != invocation.Role || contract.Agent.SkillName != invocation.SkillName || contract.Agent.ExecutionProfile != invocation.ExecutionProfile)
            throw new AgentProtocolException("INVALID_AGENT_INVOCATION", "Invocation capability does not match its runtime-assigned agent contract.");
        if (!Outcomes.TryGetValue(invocation.Capability, out var outcomes) || !outcomes.Contains(result.Outcome))
            throw new AgentProtocolException("UNSUPPORTED_AGENT_OUTCOME", $"Outcome {result.Outcome} is invalid for capability {invocation.Capability} and role {invocation.Role}.");
        if (invocation.Capability != "planning" && result.Tasks is not null)
            throw Malformed("Top-level tasks are allowed only for planning.");
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

    private static AgentProtocolException Malformed(string message) => new("MALFORMED_AGENT_RESULT", message);
}

/// <summary>
/// Uses the existing backend transport, but validates the capability-oriented protocol and
/// additionally protects plan history and Factory policy from worker mutation.
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

        var legacyProtected = ProtectedArtifactGuard.Capture(invocation);
        var planProtected = PlanProtectedArtifactGuard.Capture(invocation);
        var invocationHash = Hash(invocationPath);
        var handle = await backend.StartAsync(invocation, cancellationToken);
        var process = await backend.WaitAsync(handle, cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(attemptDirectory, "process-telemetry.json"),
            JsonSerializer.Serialize(process, FactoryJson.Options),
            CancellationToken.None);

        // Protected ownership is authoritative even when the semantic process crashes or is cancelled.
        // A failed worker must not bypass the guard and leave runner-owned state damaged before retry/recovery.
        legacyProtected.ValidateUnchanged();
        planProtected.ValidateUnchanged();
        if (!File.Exists(invocationPath) || Hash(invocationPath) != invocationHash)
            throw new AgentProtocolException("WORKER_CHANGED_RUNNER_STATE", $"Worker changed protected artifact {invocationPath}.");

        if (process.TerminationKind == AgentTerminationKind.Cancelled) throw new OperationCanceledException(cancellationToken);
        if (process.TerminationKind == AgentTerminationKind.TransportFailure && !process.CompleteResultObserved)
            throw new AgentProtocolException("AGENT_TRANSPORT_FAILURE", $"Agent exited with {process.ExitCode?.ToString() ?? "unknown"}: {process.Stderr}");

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

    private static string Hash(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

    private static async Task WriteJsonAtomicallyAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, FactoryJson.Options), cancellationToken);
        File.Move(temporary, path, true);
    }
}

/// <summary>
/// Additional protection for plan provenance and policy artifacts.
/// </summary>
internal sealed class PlanProtectedArtifactGuard
{
    private readonly IReadOnlyDictionary<string, string> hashes;
    private readonly IReadOnlyList<string> roots;

    private PlanProtectedArtifactGuard(IReadOnlyDictionary<string, string> hashes, IReadOnlyList<string> roots)
    {
        this.hashes = hashes;
        this.roots = roots;
    }

    public static PlanProtectedArtifactGuard Capture(AgentInvocation invocation)
    {
        var attemptDirectory = Path.GetDirectoryName(invocation.RawResultPath)!;
        var current = Directory.GetParent(Directory.GetParent(attemptDirectory)!.FullName)!.FullName;
        var roots = new[]
        {
            Path.Combine(current, "plan-revisions"),
            Path.Combine(invocation.Workspace, ".idd", "factory.yaml")
        };
        return new(Enumerate(roots).ToDictionary(path => path, Hash, StringComparer.OrdinalIgnoreCase), roots);
    }

    public void ValidateUnchanged()
    {
        var current = Enumerate(roots).ToDictionary(path => path, Hash, StringComparer.OrdinalIgnoreCase);
        foreach (var path in hashes.Keys.Union(current.Keys, StringComparer.OrdinalIgnoreCase))
        {
            if (hashes.TryGetValue(path, out var before) && current.TryGetValue(path, out var after) && before == after) continue;
            throw new AgentProtocolException(
                path.EndsWith("factory.yaml", StringComparison.OrdinalIgnoreCase) ? "WORKER_CHANGED_FACTORY_POLICY" : "WORKER_CHANGED_RUNNER_STATE",
                $"Worker changed protected artifact {path}.");
        }
    }

    private static IEnumerable<string> Enumerate(IEnumerable<string> roots) =>
        roots.SelectMany(root => File.Exists(root) ? [root] : Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories) : []);

    private static string Hash(string path) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
}
