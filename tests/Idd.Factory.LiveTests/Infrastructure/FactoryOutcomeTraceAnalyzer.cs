using System.Text.Json;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed record FactoryOutcomeTraceAnalysis(
    IReadOnlyList<FactoryResponse> PublicFactoryOutcomes,
    IReadOnlyList<string> ActivityAfterOutcome);

public static class FactoryOutcomeTraceAnalyzer
{
    public static FactoryOutcomeTraceAnalysis Analyze(string eventsPath)
    {
        var outcomes = new List<FactoryResponse>();
        var activityAfterOutcome = new List<string>();

        foreach (var line in File.ReadLines(eventsPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonDocument document;
            try { document = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }

            using (document)
            {
                var root = document.RootElement;
                var response = TryReadPublicFactoryOutcome(root);
                if (response is not null)
                {
                    if (outcomes.Count > 0) activityAfterOutcome.Add("agent_message");
                    outcomes.Add(response);
                    continue;
                }

                if (outcomes.Count == 0) continue;
                var activity = DescribeActivity(root);
                if (activity is not null) activityAfterOutcome.Add(activity);
            }
        }

        return new(outcomes, activityAfterOutcome.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static FactoryResponse? TryReadPublicFactoryOutcome(JsonElement root)
    {
        if (ReadString(root, "type") != "item.completed" ||
            !root.TryGetProperty("item", out var item) ||
            item.ValueKind != JsonValueKind.Object ||
            ReadString(item, "type") != "agent_message" ||
            ReadString(item, "text") is not { } text)
            return null;

        return FactoryResponseParser.TryParse(text).Response;
    }

    private static string? DescribeActivity(JsonElement root)
    {
        var eventType = ReadString(root, "type");
        if (eventType == "turn.completed") return null;

        if (eventType is "item.started" or "item.completed" &&
            root.TryGetProperty("item", out var item) &&
            item.ValueKind == JsonValueKind.Object)
        {
            var itemType = ReadString(item, "type") ?? "item";
            if (itemType == "agent_message") return "agent_message";

            var operation = ReadString(item, "tool") ?? ReadString(item, "name") ?? itemType;
            return operation.Equals("wait", StringComparison.OrdinalIgnoreCase) ? "wait_agent" : operation;
        }

        return eventType ?? "unknown_event";
    }

    private static string? ReadString(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
