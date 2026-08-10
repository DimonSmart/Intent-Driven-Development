using System.Text.Json;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed record CodexRootRuntimeTelemetryReadResult(string? Model, string? Error)
{
    public bool IsSuccess => Model is not null && Error is null;
}

public static class CodexRootRuntimeTelemetryReader
{
    public static CodexRootRuntimeTelemetryReadResult TryRead(string? sessionsDirectory, string? rootThreadId)
    {
        if (string.IsNullOrWhiteSpace(rootThreadId))
            return new(null, "Root thread ID is unavailable.");
        if (string.IsNullOrWhiteSpace(sessionsDirectory) || !Directory.Exists(sessionsDirectory))
            return new(null, "Codex sessions directory is unavailable.");

        IReadOnlyList<CodexRollout> matching;
        try
        {
            matching = new CodexRolloutReader().Index(sessionsDirectory)
                .Where(rollout => rollout.ThreadId.Equals(rootThreadId, StringComparison.Ordinal))
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(null, "Codex sessions could not be indexed: " + exception.Message);
        }

        if (matching.Count == 0)
            return new(null, $"Root rollout '{rootThreadId}' was not found in Codex session storage.");
        if (matching.Count > 1)
            return new(null, $"Multiple Codex rollouts were found for root thread '{rootThreadId}'.");

        return ReadModel(matching[0]);
    }

    private static CodexRootRuntimeTelemetryReadResult ReadModel(CodexRollout rollout)
    {
        var models = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var line in File.ReadLines(rollout.Path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                JsonDocument document;
                try { document = JsonDocument.Parse(line); }
                catch (JsonException exception)
                {
                    return new(null, $"Root rollout '{rollout.File}' contains malformed JSON: {exception.Message}");
                }

                using (document)
                {
                    var root = document.RootElement;
                    var payload = Object(root, "payload") ?? root;
                    var type = String(root, "type");
                    var eventType = type == "event_msg" ? String(payload, "type") : type;
                    if (eventType != "turn_context") continue;

                    var model = String(payload, "model");
                    if (string.IsNullOrWhiteSpace(model))
                        return new(null, $"Root rollout '{rollout.File}' contains turn_context without model.");
                    models.Add(model);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(null, $"Root rollout '{rollout.File}' could not be read: {exception.Message}");
        }

        return models.Count switch
        {
            0 => new(null, $"Root rollout '{rollout.File}' contains no turn_context model telemetry."),
            1 => new(models.Single(), null),
            _ => new(null, $"Root rollout '{rollout.File}' used multiple models: {string.Join(", ", models.OrderBy(model => model, StringComparer.Ordinal))}.")
        };
    }

    private static JsonElement? Object(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(name, out var result) &&
        result.ValueKind == JsonValueKind.Object
            ? result
            : null;

    private static string? String(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(name, out var result) &&
        result.ValueKind == JsonValueKind.String
            ? result.GetString()
            : null;
}
