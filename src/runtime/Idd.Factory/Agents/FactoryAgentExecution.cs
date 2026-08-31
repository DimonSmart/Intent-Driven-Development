using System.Text.Json;
using Idd.Factory.Domain;

namespace Idd.Factory.Agents;

/// <summary>
/// Protocol validator for the dynamic task-graph Factory. Role is transport metadata;
/// runtime scheduling is based on capabilities.
/// </summary>
public sealed class FactoryAgentResultValidator
{
    private static readonly IReadOnlyDictionary<string, HashSet<string>> Outcomes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
    {
        ["task-decomposer"] = ["ready", "intent-required", "needs-clarification", "focused-handoff", "blocked"],
        ["implementer"] = ["completed", "additional-work-required", "global-replan-required", "needs-replan", "blocked", "intent-required"],
        ["researcher"] = ["completed", "additional-work-required", "global-replan-required", "needs-replan", "blocked", "intent-required"],
        ["checkpoint-reviewer"] = ["approved", "needs-fix", "correction-required", "additional-work-required", "global-replan-required", "needs-replan", "blocked", "intent-required"],
        ["final-reviewer"] = ["approved", "needs-fix", "correction-required", "additional-work-required", "global-replan-required", "needs-replan", "blocked", "intent-required"],
        ["factory-replanner"] = ["replan-proposed", "intent-required", "needs-clarification", "blocked"]
    };

    public AgentResultEnvelope Validate(AgentInvocation invocation, AgentResultEnvelope? result)
    {
        if (result is null) throw new AgentProtocolException("MALFORMED_AGENT_RESULT", "Result is null.");
        if (result.ProtocolVersion != AgentInvocation.CurrentProtocolVersion)
            throw new AgentProtocolException("UNSUPPORTED_AGENT_PROTOCOL", $"Unsupported protocol {result.ProtocolVersion}.");
        if (result.RunId != invocation.RunId || result.AttemptId != invocation.AttemptId || result.Role != invocation.Role)
            throw new AgentProtocolException("AGENT_RESULT_IDENTITY_MISMATCH", "Result identity does not match invocation.");
        if (!Outcomes.TryGetValue(result.Role, out var outcomes) || !outcomes.Contains(result.Outcome))
            throw new AgentProtocolException("UNSUPPORTED_AGENT_OUTCOME", $"Outcome {result.Outcome} is invalid for {result.Role}.");
        return result;
    }
}

/// <summary>
/// Uses the existing backend transport, but validates the capability-oriented protocol and
/// additionally protects graph history and Factory policy from worker mutation.
/// </summary>
public sealed class FactoryAgentExecutor(IAgentBackend backend, FactoryAgentResultValidator validator)
{
    public async Task<AgentExecutionResult> ExecuteAsync(AgentInvocation invocation, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(invocation.ResultPath)!);
        var invocationPath = Path.Combine(Path.GetDirectoryName(invocation.ResultPath)!, "invocation.json");
        if (!File.Exists(invocationPath))
            await File.WriteAllTextAsync(invocationPath, JsonSerializer.Serialize(invocation, FactoryJson.Options), cancellationToken);

        var legacyProtected = ProtectedArtifactGuard.Capture(invocation);
        var graphProtected = DynamicProtectedArtifactGuard.Capture(invocation);
        var handle = await backend.StartAsync(invocation, cancellationToken);
        var process = await backend.WaitAsync(handle, cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(Path.GetDirectoryName(invocation.ResultPath)!, "process-telemetry.json"),
            JsonSerializer.Serialize(process, FactoryJson.Options),
            CancellationToken.None);

        // Protected ownership is authoritative even when the semantic process crashes or is cancelled.
        // A failed worker must not bypass the guard and leave runner-owned state damaged before retry/recovery.
        legacyProtected.ValidateUnchanged();
        graphProtected.ValidateUnchanged();

        if (process.TerminationKind == AgentTerminationKind.Cancelled) throw new OperationCanceledException(cancellationToken);
        if (process.TerminationKind == AgentTerminationKind.TransportFailure && !process.CompleteResultObserved)
            throw new AgentProtocolException("AGENT_TRANSPORT_FAILURE", $"Agent exited with {process.ExitCode?.ToString() ?? "unknown"}: {process.Stderr}");

        if (!File.Exists(invocation.ResultPath)) throw new AgentProtocolException("MISSING_AGENT_RESULT", "Agent did not produce result.json.");

        AgentResultEnvelope? result;
        try { result = JsonSerializer.Deserialize<AgentResultEnvelope>(await File.ReadAllTextAsync(invocation.ResultPath, cancellationToken), FactoryJson.Options); }
        catch (JsonException exception) { throw new AgentProtocolException("MALFORMED_AGENT_RESULT", exception.Message); }
        return new(validator.Validate(invocation, result), process);
    }
}

/// <summary>
/// Additional protection for dynamic graph provenance and policy artifacts that were introduced
/// after the base worker ownership guard.
/// </summary>
internal sealed class DynamicProtectedArtifactGuard
{
    private readonly IReadOnlyDictionary<string, string> hashes;
    private readonly IReadOnlyList<string> roots;

    private DynamicProtectedArtifactGuard(IReadOnlyDictionary<string, string> hashes, IReadOnlyList<string> roots)
    {
        this.hashes = hashes;
        this.roots = roots;
    }

    public static DynamicProtectedArtifactGuard Capture(AgentInvocation invocation)
    {
        var attemptDirectory = Path.GetDirectoryName(invocation.ResultPath)!;
        var current = Directory.GetParent(Directory.GetParent(attemptDirectory)!.FullName)!.FullName;
        var roots = new[]
        {
            Path.Combine(current, "graph"),
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
