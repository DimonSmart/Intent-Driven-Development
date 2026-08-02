using System.Text.Json;
using Idd.Factory.LiveTests.Models;

namespace Idd.Factory.LiveTests.Infrastructure;

public static class CodexJsonlAnalyzer
{
    public static FactoryEvalMetrics Analyze(string eventsPath, TimeSpan wallTime)
    {
        var metrics = new FactoryEvalMetrics { WallTimeMs = (long)wallTime.TotalMilliseconds };
        foreach (var line in File.ReadLines(eventsPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var type = FindString(root, "type");
                if (type is null) { metrics.UnknownEventCount++; continue; }
                if (type.Contains("turn", StringComparison.OrdinalIgnoreCase) || type.Contains("message", StringComparison.OrdinalIgnoreCase)) metrics.ModelTurnCount++;
                if (type.Contains("response_item", StringComparison.OrdinalIgnoreCase) || type.Contains("tool", StringComparison.OrdinalIgnoreCase)) CountTool(root, metrics);
                metrics.ModelEffective ??= FindString(root, "model");
                metrics.ReasoningEffortEffective ??= FindString(root, "reasoning_effort") ?? FindString(root, "reasoningEffort");
                metrics.SessionId ??= FindString(root, "session_id") ?? FindString(root, "sessionId");
                AddTokenUsage(root, metrics);
            }
            catch (JsonException) { metrics.MalformedLineCount++; }
        }
        return metrics;
    }

    private static void CountTool(JsonElement root, FactoryEvalMetrics metrics)
    {
        foreach (var value in Traverse(root))
        {
            if (value.ValueKind != JsonValueKind.Object) continue;
            var name = FindString(value, "name");
            var kind = FindString(value, "type");
            if (name is null || !(kind?.Contains("function", StringComparison.OrdinalIgnoreCase) ?? false)) continue;
            metrics.ToolCallCount++;
            if (name.Contains("spawn_agent", StringComparison.OrdinalIgnoreCase)) metrics.SpawnAgentCallCount++;
            if (name.Contains("wait_agent", StringComparison.OrdinalIgnoreCase)) metrics.WaitAgentCallCount++;
        }
    }

    private static void AddTokenUsage(JsonElement root, FactoryEvalMetrics metrics)
    {
        foreach (var value in Traverse(root))
        {
            if (value.ValueKind != JsonValueKind.Object) continue;
            if (TryLong(value, "input_tokens", out var input)) metrics.InputTokens = (metrics.InputTokens ?? 0) + input;
            if (TryLong(value, "cached_input_tokens", out var cached)) metrics.CachedInputTokens = (metrics.CachedInputTokens ?? 0) + cached;
            if (TryLong(value, "output_tokens", out var output)) metrics.OutputTokens = (metrics.OutputTokens ?? 0) + output;
            if (TryLong(value, "total_tokens", out var total)) metrics.TotalTokens = (metrics.TotalTokens ?? 0) + total;
        }
    }

    private static IEnumerable<JsonElement> Traverse(JsonElement value)
    {
        yield return value;
        if (value.ValueKind == JsonValueKind.Object) foreach (var property in value.EnumerateObject()) foreach (var nested in Traverse(property.Value)) yield return nested;
        if (value.ValueKind == JsonValueKind.Array) foreach (var nested in value.EnumerateArray()) foreach (var item in Traverse(nested)) yield return item;
    }
    private static string? FindString(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    private static bool TryLong(JsonElement value, string name, out long result)
    {
        result = 0;
        return value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) && property.TryGetInt64(out result);
    }
}
