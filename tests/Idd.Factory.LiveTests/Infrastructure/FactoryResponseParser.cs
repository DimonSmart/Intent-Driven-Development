using System.Text.Json;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed record FactoryResponse(int SchemaVersion, string FactoryOutcome, string? FactoryResultPath, string? Reason);

public sealed record FactoryResponseParseResult(FactoryResponse? Response, string? Error)
{
    public bool IsSuccess => Response is not null;
}

public static class FactoryResponseParser
{
    private static readonly HashSet<string> Properties =
        ["schemaVersion", "factoryOutcome", "factoryResultPath", "reason"];

    private static readonly HashSet<string> Outcomes =
    [
        "COMPLETED",
        "FOCUSED_HANDOFF",
        "NEEDS_CLARIFICATION",
        "INTENT_REQUIRED",
        "BLOCKED",
        "CORRUPT_FACTORY_STATE"
    ];

    public static FactoryResponseParseResult TryParse(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return new(null, "Factory response root must be a JSON object.");

            var root = document.RootElement;
            var unexpectedProperty = root.EnumerateObject()
                .Select(property => property.Name)
                .FirstOrDefault(name => !Properties.Contains(name));
            if (unexpectedProperty is not null)
                return new(null, $"Factory response contains unsupported property '{unexpectedProperty}'.");

            if (!root.TryGetProperty("schemaVersion", out var version) ||
                version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var schemaVersion) ||
                schemaVersion != 1)
                return new(null, "schemaVersion must be 1.");

            if (!root.TryGetProperty("factoryOutcome", out var outcomeValue) ||
                outcomeValue.ValueKind != JsonValueKind.String ||
                !Outcomes.Contains(outcomeValue.GetString()!))
                return new(null, "factoryOutcome is missing or unsupported.");

            var resultPath = ReadNullableString(root, "factoryResultPath");
            var reason = ReadNullableString(root, "reason");
            if (resultPath.Error is not null || reason.Error is not null)
                return new(null, resultPath.Error ?? reason.Error);

            var outcome = outcomeValue.GetString()!;
            if (outcome == "COMPLETED")
            {
                if (string.IsNullOrWhiteSpace(resultPath.Value))
                    return new(null, "COMPLETED requires a non-empty factoryResultPath.");
                if (!string.IsNullOrWhiteSpace(reason.Value))
                    return new(null, "COMPLETED requires an empty reason.");
            }
            else if (resultPath.Value is not null || string.IsNullOrWhiteSpace(reason.Value))
            {
                return new(null, $"{outcome} requires factoryResultPath null and a non-empty reason.");
            }

            return new(new(schemaVersion, outcome, resultPath.Value, reason.Value), null);
        }
        catch (JsonException exception)
        {
            return new(null, $"Factory response is invalid JSON: {exception.Message}");
        }
    }

    private static (string? Value, string? Error) ReadNullableString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return (null, $"{name} is missing.");
        return value.ValueKind switch
        {
            JsonValueKind.Null => (null, null),
            JsonValueKind.String => (value.GetString(), null),
            _ => (null, $"{name} must be a string or null.")
        };
    }
}
