using System.Text.Json;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed record ExecutionResponse(string FactoryOutcome, string? FactoryResultPath, string? Reason);

public sealed record ExecutionResponseReadResult(ExecutionResponse? Response, string? Error)
{
    public bool IsSuccess => Response is not null;
}

public static class ExecutionResponseReader
{
    private static readonly HashSet<string> Outcomes = ["COMPLETED", "FOCUSED_HANDOFF", "NEEDS_CLARIFICATION", "INTENT_REQUIRED", "BLOCKED", "CORRUPT_FACTORY_STATE"];

    public static ExecutionResponseReadResult TryRead(string path, string workspace)
    {
        if (!File.Exists(path)) return new(null, "last-message.json is missing.");
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object) return new(null, "last-message.json root must be a JSON object.");
            var root = document.RootElement;
            if (!root.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1) return new(null, "schemaVersion must be 1.");
            if (!root.TryGetProperty("factoryOutcome", out var outcomeValue) || outcomeValue.ValueKind != JsonValueKind.String || !Outcomes.Contains(outcomeValue.GetString()!)) return new(null, "factoryOutcome is missing or unsupported.");
            var outcome = outcomeValue.GetString()!;
            var resultPath = ReadNullableString(root, "factoryResultPath");
            var reason = ReadNullableString(root, "reason");
            if (resultPath.Error is not null || reason.Error is not null) return new(null, resultPath.Error ?? reason.Error);
            if (outcome == "COMPLETED")
            {
                if (string.IsNullOrWhiteSpace(resultPath.Value)) return new(null, "COMPLETED requires a non-empty factoryResultPath.");
                if (!File.Exists(Path.Combine(workspace, resultPath.Value.Replace('/', Path.DirectorySeparatorChar)))) return new(null, "COMPLETED factoryResultPath does not exist.");
                if (!string.IsNullOrWhiteSpace(reason.Value)) return new(null, "COMPLETED requires an empty reason.");
            }
            else if (resultPath.Value is not null || string.IsNullOrWhiteSpace(reason.Value)) return new(null, $"{outcome} requires factoryResultPath null and a non-empty reason.");
            return new(new(outcome, resultPath.Value, reason.Value), null);
        }
        catch (JsonException exception)
        {
            return new(null, $"last-message.json is invalid JSON: {exception.Message}");
        }
    }

    private static (string? Value, string? Error) ReadNullableString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return (null, $"{name} is missing.");
        return value.ValueKind switch { JsonValueKind.Null => (null, null), JsonValueKind.String => (value.GetString(), null), _ => (null, $"{name} must be a string or null.") };
    }
}
