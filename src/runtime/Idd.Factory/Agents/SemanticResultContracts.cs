using System.Text.Json;
using Idd.Factory.Domain;

namespace Idd.Factory.Agents;

internal sealed record SemanticOutcomeContract(
    IReadOnlySet<string> AllowedFields,
    IReadOnlySet<string> RequiredFields);

internal sealed record SemanticResultContract(
    string Schema,
    string Capability,
    IReadOnlyDictionary<string, SemanticOutcomeContract> Outcomes)
{
    public SemanticOutcomeContract ResolveOutcome(string outcome) =>
        Outcomes.TryGetValue(outcome, out var value)
            ? value
            : throw new AgentProtocolException(
                "UNSUPPORTED_AGENT_OUTCOME",
                $"Outcome '{outcome}' is invalid for capability '{Capability}' and semantic result schema '{Schema}'.");
}

internal static class SemanticResultContracts
{
    private static readonly HashSet<string> RuntimeOwnedFields = new(StringComparer.Ordinal)
    {
        "runId", "attemptId", "workItemId", "role", "capability", "skill", "skillName",
        "executionProfile", "schemaVersion", "protocolVersion", "resultPath", "rawResultPath",
        "semanticResultSchema", "workspace", "input", "startedAt"
    };

    private static readonly IReadOnlyDictionary<string, SemanticResultContract> BySchema = new[]
    {
        Contract("planning-v1", "planning", new Dictionary<string, SemanticOutcomeContract>(StringComparer.Ordinal)
        {
            ["ready"] = Outcome(["tasks", "metrics"], ["tasks"]),
            ["intent-required"] = Outcome(["reason", "payload", "metrics"], ["payload"]),
            ["needs-clarification"] = Outcome(["reason", "payload", "metrics"]),
            ["focused-handoff"] = Outcome(["reason", "payload", "metrics"]),
            ["blocked"] = Outcome(["reason", "payload", "metrics"])
        }),
        Contract("implementation-v1", "implementation", new Dictionary<string, SemanticOutcomeContract>(StringComparer.Ordinal)
        {
            ["completed"] = Outcome(["summary", "declaredChanges", "concerns", "verificationClaims", "metrics"], ["summary"]),
            ["additional-work-required"] = Outcome(["payload", "metrics"], ["payload"]),
            ["global-replan-required"] = Outcome(["reason", "payload", "metrics"]),
            ["intent-required"] = Outcome(["reason", "payload", "metrics"], ["payload"]),
            ["blocked"] = Outcome(["reason", "payload", "metrics"])
        }),
        Contract("research-v1", "research", new Dictionary<string, SemanticOutcomeContract>(StringComparer.Ordinal)
        {
            ["completed"] = Outcome(["summary", "concerns", "payload", "metrics"]),
            ["additional-work-required"] = Outcome(["payload", "metrics"], ["payload"]),
            ["global-replan-required"] = Outcome(["reason", "payload", "metrics"]),
            ["intent-required"] = Outcome(["reason", "payload", "metrics"], ["payload"]),
            ["blocked"] = Outcome(["reason", "payload", "metrics"])
        }),
        Contract("semantic-review-v1", "semantic-review", new Dictionary<string, SemanticOutcomeContract>(StringComparer.Ordinal)
        {
            ["approved"] = Outcome(["reason", "payload", "metrics"]),
            ["correction-required"] = Outcome(["payload", "metrics"], ["payload"]),
            ["additional-work-required"] = Outcome(["payload", "metrics"], ["payload"]),
            ["global-replan-required"] = Outcome(["reason", "payload", "metrics"]),
            ["intent-required"] = Outcome(["reason", "payload", "metrics"], ["payload"]),
            ["blocked"] = Outcome(["reason", "payload", "metrics"])
        }),
        Contract("final-review-v1", "final-review", new Dictionary<string, SemanticOutcomeContract>(StringComparer.Ordinal)
        {
            ["approved"] = Outcome(["reason", "payload", "metrics"]),
            ["correction-required"] = Outcome(["payload", "metrics"], ["payload"]),
            ["additional-work-required"] = Outcome(["payload", "metrics"], ["payload"]),
            ["global-replan-required"] = Outcome(["reason", "payload", "metrics"]),
            ["intent-required"] = Outcome(["reason", "payload", "metrics"], ["payload"]),
            ["blocked"] = Outcome(["reason", "payload", "metrics"])
        })
    }.ToDictionary(x => x.Schema, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, SemanticResultContract> ByCapability =
        BySchema.Values.ToDictionary(x => x.Capability, StringComparer.Ordinal);

    public static string SchemaForCapability(string capability) =>
        ByCapability.TryGetValue(capability, out var contract)
            ? contract.Schema
            : throw new AgentProtocolException("UNKNOWN_CAPABILITY", $"Unknown Factory capability '{capability}'.");

    public static SemanticResultContract Resolve(AgentInvocation invocation)
    {
        if (!BySchema.TryGetValue(invocation.SemanticResultSchema, out var contract))
            throw new AgentProtocolException(
                "UNSUPPORTED_SEMANTIC_RESULT_SCHEMA",
                $"Semantic result schema '{invocation.SemanticResultSchema}' is not supported for capability '{invocation.Capability}'.");
        if (contract.Capability != invocation.Capability)
            throw new AgentProtocolException(
                "UNSUPPORTED_SEMANTIC_RESULT_SCHEMA",
                $"Semantic result schema '{invocation.SemanticResultSchema}' belongs to capability '{contract.Capability}', not '{invocation.Capability}'.");
        return contract;
    }

    public static void ValidateJsonFields(AgentInvocation invocation, JsonElement root, SemanticResultContract contract, SemanticOutcomeContract outcome)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name == "outcome" || outcome.AllowedFields.Contains(property.Name)) continue;
            var reason = RuntimeOwnedFields.Contains(property.Name)
                ? $"field '{property.Name}' is runtime-owned and must not be returned by workers"
                : $"field '{property.Name}' is not allowed for outcome '{root.GetProperty("outcome").GetString()}'";
            throw Malformed(invocation, contract, reason);
        }

        foreach (var required in outcome.RequiredFields)
            if (!root.TryGetProperty(required, out _))
                throw Malformed(invocation, contract, $"required field '{required}' is missing");
    }

    public static void ValidateTypedFields(AgentInvocation invocation, SemanticAgentResult result, SemanticResultContract contract, SemanticOutcomeContract outcome)
    {
        var fields = PresentFields(result);
        foreach (var field in fields)
            if (field != "outcome" && !outcome.AllowedFields.Contains(field))
                throw Malformed(invocation, contract, $"field '{field}' is not allowed for outcome '{result.Outcome}'");

        foreach (var required in outcome.RequiredFields)
            if (!fields.Contains(required))
                throw Malformed(invocation, contract, $"required field '{required}' is missing");
    }

    private static HashSet<string> PresentFields(SemanticAgentResult result)
    {
        var fields = new HashSet<string>(StringComparer.Ordinal) { "outcome" };
        if (result.Summary is not null) fields.Add("summary");
        if (result.DeclaredChanges is not null) fields.Add("declaredChanges");
        if (result.Concerns is not null) fields.Add("concerns");
        if (result.VerificationClaims is not null) fields.Add("verificationClaims");
        if (result.Tasks is not null) fields.Add("tasks");
        if (result.Reason is not null) fields.Add("reason");
        if (result.Payload is not null) fields.Add("payload");
        if (result.Metrics is not null) fields.Add("metrics");
        return fields;
    }

    private static SemanticResultContract Contract(string schema, string capability, IReadOnlyDictionary<string, SemanticOutcomeContract> outcomes) =>
        new(schema, capability, outcomes);

    private static SemanticOutcomeContract Outcome(IEnumerable<string> allowed, IEnumerable<string>? required = null) =>
        new(new HashSet<string>(allowed, StringComparer.Ordinal), new HashSet<string>(required ?? [], StringComparer.Ordinal));

    private static AgentProtocolException Malformed(AgentInvocation invocation, SemanticResultContract contract, string condition) =>
        new(
            "MALFORMED_AGENT_RESULT",
            $"Capability '{invocation.Capability}', semantic result schema '{contract.Schema}': {condition}.");
}
