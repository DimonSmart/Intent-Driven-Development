using System.Text.Json;
using Idd.Factory.LiveTests.Models;

namespace Idd.Factory.LiveTests.Infrastructure;

public static class CodexJsonlAnalyzer
{
    public static FactoryEvalMetrics Analyze(string eventsPath, TimeSpan wallTime)
    {
        var metrics = new FactoryEvalMetrics { WallTimeMs = (long)wallTime.TotalMilliseconds };
        var calls = new Dictionary<string, ToolCall>(StringComparer.Ordinal);

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
                metrics.ModelEffective ??= FindString(root, "model");
                metrics.ReasoningEffortEffective ??= FindString(root, "reasoning_effort") ?? FindString(root, "reasoningEffort");
                metrics.SessionId ??= FindString(root, "session_id") ?? FindString(root, "sessionId");

                if (type == "turn.completed") SetCumulativeTokenUsage(root, metrics);
                if (type is "item.started" or "item.completed") ReadToolEvent(root, type, calls);
                if (ContainsSpawnAgent(root) && type is not "item.started" and not "item.completed") throw new CodexJsonlAnalysisException($"Unsupported spawn_agent event type '{type}'.");
            }
            catch (JsonException) { metrics.MalformedLineCount++; }
        }

        foreach (var call in calls.Values)
        {
            metrics.ToolCallCount++;
            if (call.Name.Equals("wait_agent", StringComparison.OrdinalIgnoreCase)) metrics.WaitAgentCallCount++;
            if (!call.Name.Equals("spawn_agent", StringComparison.OrdinalIgnoreCase)) continue;

            metrics.SpawnAgentCallCount++;
            if (!call.Completed) throw new CodexJsonlAnalysisException($"spawn_agent call '{call.Id}' has no item.completed event.");
            if (call.Failed) { metrics.FailedSpawnAgentCallCount++; continue; }
            if (!HasCreatedAgentId(call.Output)) throw new CodexJsonlAnalysisException($"Successful spawn_agent call '{call.Id}' did not return a child agent or context identifier.");
            metrics.SpawnedAgentCount++;
        }

        return metrics;
    }

    private static void ReadToolEvent(JsonElement root, string eventType, Dictionary<string, ToolCall> calls)
    {
        if (!root.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object) return;
        var itemType = FindString(item, "type");
        var name = FindString(item, "name");
        if (itemType != "function_call")
        {
            if (ContainsSpawnAgent(item)) throw new CodexJsonlAnalysisException($"Unsupported spawn_agent item type '{itemType ?? "missing"}'.");
            return;
        }
        var id = FindString(item, "id") ?? FindString(item, "call_id");
        if (string.IsNullOrWhiteSpace(id)) throw new CodexJsonlAnalysisException($"Function call '{name ?? "missing"}' is missing item.id or call_id.");

        if (!calls.TryGetValue(id, out var call))
        {
            if (string.IsNullOrWhiteSpace(name)) throw new CodexJsonlAnalysisException($"Function call '{id}' is missing its tool name.");
            calls.Add(id, call = new ToolCall(id, name));
        }
        else if (!string.IsNullOrWhiteSpace(name) && !call.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) throw new CodexJsonlAnalysisException($"Tool call '{id}' has conflicting names '{call.Name}' and '{name}'.");

        if (eventType != "item.completed") return;
        call.Completed = true;
        call.Failed = IsFailed(item);
        call.Output = FindProperty(item, "output") ?? FindProperty(item, "result") ?? FindProperty(item, "content");
    }

    private static void SetCumulativeTokenUsage(JsonElement root, FactoryEvalMetrics metrics)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return;
        metrics.InputTokens = GetNullableLong(usage, "input_tokens");
        metrics.CachedInputTokens = GetNullableLong(usage, "cached_input_tokens");
        metrics.OutputTokens = GetNullableLong(usage, "output_tokens");
        metrics.TotalTokens = GetNullableLong(usage, "total_tokens");
    }

    private static bool IsFailed(JsonElement item) =>
        (FindString(item, "status")?.Equals("failed", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (FindString(item, "status")?.Equals("error", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (FindString(item, "status")?.Equals("declined", StringComparison.OrdinalIgnoreCase) ?? false) ||
        item.TryGetProperty("error", out var error) && error.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;

    private static bool HasCreatedAgentId(JsonElement? output)
    {
        if (output is null) return false;
        if (output.Value.ValueKind == JsonValueKind.String)
        {
            var text = output.Value.GetString();
            if (string.IsNullOrWhiteSpace(text)) return false;
            try { using var document = JsonDocument.Parse(text); return HasCreatedAgentId(document.RootElement); }
            catch (JsonException) { return false; }
        }
        if (output.Value.ValueKind != JsonValueKind.Object) return false;
        return new[] { "agent_id", "agentId", "child_context_id", "childContextId", "child_thread_id", "childThreadId" }
            .Any(name => !string.IsNullOrWhiteSpace(FindString(output.Value, name)));
    }

    private static bool ContainsSpawnAgent(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()?.Contains("spawn_agent", StringComparison.OrdinalIgnoreCase) == true,
        JsonValueKind.Object => value.EnumerateObject().Any(property => property.Name.Contains("spawn_agent", StringComparison.OrdinalIgnoreCase) || ContainsSpawnAgent(property.Value)),
        JsonValueKind.Array => value.EnumerateArray().Any(ContainsSpawnAgent),
        _ => false
    };

    private static JsonElement? FindProperty(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) ? property.Clone() : null;
    private static string? FindString(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    private static long? GetNullableLong(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) && property.TryGetInt64(out var result) ? result : null;

    private sealed class ToolCall(string id, string name)
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public bool Completed { get; set; }
        public bool Failed { get; set; }
        public JsonElement? Output { get; set; }
    }
}

public sealed class CodexJsonlAnalysisException(string message) : Exception(message);
