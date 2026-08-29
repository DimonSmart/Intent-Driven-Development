using System.Text.Json;
using Idd.Factory.Agents;

namespace Idd.Factory.Runtime;

internal static class IntentRequiredPayload
{
    public static void Validate(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty("missingIntentDecisions", out var decisions) ||
            decisions.ValueKind != JsonValueKind.Array ||
            decisions.GetArrayLength() == 0)
        {
            throw Malformed("intent-required requires a non-empty payload.missingIntentDecisions array.");
        }

        foreach (var decision in decisions.EnumerateArray())
        {
            if (decision.ValueKind != JsonValueKind.Object)
                throw Malformed("Each missingIntentDecisions item must be an object.");

            RequireString(decision, "area");
            RequireString(decision, "whyBlocking");
            RequireStringArray(decision, "requiredDecisions", requireNonEmpty: true);
            RequireStringArray(decision, "intentReferences", requireNonEmpty: false);

            if (decision.TryGetProperty("recommendedNextWorkflow", out var workflow) &&
                workflow.ValueKind is not JsonValueKind.Null &&
                (workflow.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(workflow.GetString())))
            {
                throw Malformed("recommendedNextWorkflow must be a non-empty string when present.");
            }
        }
    }

    private static void RequireString(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw Malformed($"missingIntentDecisions.{property} must be a non-empty string.");
        }
    }

    private static void RequireStringArray(JsonElement item, string property, bool requireNonEmpty)
    {
        if (!item.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
            throw Malformed($"missingIntentDecisions.{property} must be an array.");
        if (requireNonEmpty && value.GetArrayLength() == 0)
            throw Malformed($"missingIntentDecisions.{property} must not be empty.");
        if (value.EnumerateArray().Any(entry => entry.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(entry.GetString())))
            throw Malformed($"missingIntentDecisions.{property} must contain only non-empty strings.");
    }

    private static AgentProtocolException Malformed(string message) => new("MALFORMED_AGENT_RESULT", message);
}
