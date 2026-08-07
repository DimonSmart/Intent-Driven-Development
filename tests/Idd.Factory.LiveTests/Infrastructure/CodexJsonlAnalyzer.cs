using System.Text.Json;
using Idd.Factory.LiveTests.Models;

namespace Idd.Factory.LiveTests.Infrastructure;

public static class CodexJsonlAnalyzer
{
    public static string? TryReadRootThreadId(string eventsPath)
    {
        if (!File.Exists(eventsPath)) return null;
        foreach (var line in File.ReadLines(eventsPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (FindString(root, "type") == "thread.started" && FindString(root, "thread_id") is { Length: > 0 } id) return id;
            }
            catch (JsonException) { }
        }
        return null;
    }

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

                if (type == "thread.started") metrics.RootThreadId ??= FindString(root, "thread_id");

                if (type == "turn.completed") metrics.ModelTurnCount++;
                metrics.ModelEffective ??= FindString(root, "model");
                metrics.ReasoningEffortEffective ??= FindString(root, "reasoning_effort") ?? FindString(root, "reasoningEffort");
                metrics.SessionId ??= FindString(root, "session_id") ?? FindString(root, "sessionId") ?? FindString(root, "thread_id");

                if (type == "turn.completed") SetCumulativeTokenUsage(root, metrics);
                if (type is "item.started" or "item.completed") ReadToolEvent(root, type, calls);
            }
            catch (JsonException) { metrics.MalformedLineCount++; }
        }

        foreach (var call in calls.Values)
        {
            metrics.ToolCallCount++;
            if (call.IsWait) metrics.WaitAgentCallCount++;
            if (!call.IsSpawn) continue;

            metrics.SpawnAgentCallCount++;
            if (!call.Completed) throw new CodexJsonlAnalysisException($"spawn_agent call '{call.Id}' has no item.completed event.");
            if (call.Failed) { metrics.FailedSpawnAgentCallCount++; continue; }
            if (call.CreatedAgentIds.Count == 0) throw new CodexJsonlAnalysisException($"Successful spawn_agent call '{call.Id}' did not confirm a created child agent or thread.");
            metrics.RootLevelSpawnedAgentCount += call.CreatedAgentIds.Count;
        }

        var spawnedChildIds = calls.Values.Where(call => call.IsSpawn && call.Completed && !call.Failed).SelectMany(call => call.CreatedAgentIds).ToHashSet(StringComparer.Ordinal);
        var completedChildIds = calls.Values.Where(call => call.IsWait && call.Completed && !call.Failed).SelectMany(call => call.CompletedAgentIds).ToHashSet(StringComparer.Ordinal);
        metrics.CompletedChildAgentCount = spawnedChildIds.Intersect(completedChildIds, StringComparer.Ordinal).LongCount();
        return metrics;
    }

    private static void ReadToolEvent(JsonElement root, string eventType, Dictionary<string, ToolCall> calls)
    {
        if (!root.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object) return;
        var itemType = FindString(item, "type");
        if (itemType == "function_call")
        {
            ReadFunctionCall(item, eventType, calls);
            return;
        }
        if (itemType == "collab_tool_call")
        {
            ReadCollaborationCall(item, eventType, calls);
            return;
        }
        if (itemType is "custom_tool_call" or "local_shell_call")
        {
            var call = GetOrCreateCall(item, FindString(item, "name") ?? FindString(item, "tool") ?? itemType, calls);
            if (eventType == "item.completed")
            {
                call.Completed = true;
                call.Failed = IsFailed(item);
            }
            return;
        }
        if (IsStructuredSpawnAgentCall(item))
        {
            throw new CodexJsonlAnalysisException($"Unsupported spawn_agent item type '{itemType ?? "missing"}'.");
        }
    }

    private static void ReadFunctionCall(JsonElement item, string eventType, Dictionary<string, ToolCall> calls)
    {
        var name = FindString(item, "name");
        if (string.IsNullOrWhiteSpace(name)) throw new CodexJsonlAnalysisException("Function call is missing its tool name.");
        var call = GetOrCreateCall(item, name, calls);
        if (eventType != "item.completed") return;
        call.Completed = true;
        call.Failed = IsFailed(item);
        call.CreatedAgentIds.UnionWith(FindCreatedAgentIds(FindProperty(item, "output") ?? FindProperty(item, "result") ?? FindProperty(item, "content")));
    }

    private static void ReadCollaborationCall(JsonElement item, string eventType, Dictionary<string, ToolCall> calls)
    {
        var tool = FindString(item, "tool");
        if (tool is not ("spawn_agent" or "wait" or "wait_agent" or "close_agent")) throw new CodexJsonlAnalysisException($"Unsupported collaboration tool '{tool ?? "missing"}'. Cannot determine whether subagents were used.");
        var call = GetOrCreateCall(item, tool, calls);
        if (eventType != "item.completed") return;
        call.Completed = true;
        call.Failed = IsFailed(item);
        if (call.IsSpawn) call.CreatedAgentIds.UnionWith(ReadStringArray(item, "receiver_thread_ids"));
        if (call.IsWait) call.CompletedAgentIds.UnionWith(FindCompletedAgentIds(item));
    }

    private static ToolCall GetOrCreateCall(JsonElement item, string name, Dictionary<string, ToolCall> calls)
    {
        var id = FindString(item, "id") ?? FindString(item, "call_id");
        if (string.IsNullOrWhiteSpace(id)) throw new CodexJsonlAnalysisException($"Tool call '{name}' is missing item.id or call_id.");
        if (!calls.TryGetValue(id, out var call)) calls.Add(id, call = new ToolCall(id, name));
        else if (!call.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) throw new CodexJsonlAnalysisException($"Tool call '{id}' has conflicting names '{call.Name}' and '{name}'.");
        return call;
    }

    private static void SetCumulativeTokenUsage(JsonElement root, FactoryEvalMetrics metrics)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return;
        metrics.InputTokens = GetNullableLong(usage, "input_tokens");
        metrics.CachedInputTokens = GetNullableLong(usage, "cached_input_tokens");
        metrics.OutputTokens = GetNullableLong(usage, "output_tokens");
        metrics.ReasoningOutputTokens = GetNullableLong(usage, "reasoning_output_tokens");
        metrics.TotalTokens = GetNullableLong(usage, "total_tokens") ??
            (metrics.InputTokens is not null && metrics.OutputTokens is not null ? metrics.InputTokens + metrics.OutputTokens : null);
    }

    private static bool IsFailed(JsonElement item) =>
        (FindString(item, "status")?.Equals("failed", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (FindString(item, "status")?.Equals("error", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (FindString(item, "status")?.Equals("declined", StringComparison.OrdinalIgnoreCase) ?? false) ||
        item.TryGetProperty("error", out var error) && error.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;

    private static IEnumerable<string> FindCreatedAgentIds(JsonElement? output)
    {
        if (output is null) return [];
        if (output.Value.ValueKind == JsonValueKind.String)
        {
            try { using var document = JsonDocument.Parse(output.Value.GetString()!); return FindCreatedAgentIds(document.RootElement).ToArray(); }
            catch (JsonException) { return []; }
        }
        if (output.Value.ValueKind != JsonValueKind.Object) return [];
        return new[] { "agent_id", "agentId", "child_context_id", "childContextId", "child_thread_id", "childThreadId" }
            .Select(name => FindString(output.Value, name)).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!).ToArray();
    }

    private static IEnumerable<string> FindCompletedAgentIds(JsonElement item)
    {
        if (!item.TryGetProperty("agents_states", out var states) || states.ValueKind != JsonValueKind.Object) return [];
        return states.EnumerateObject().Where(state => FindString(state.Value, "status")?.Equals("completed", StringComparison.OrdinalIgnoreCase) == true).Select(state => state.Name).ToArray();
    }

    private static IEnumerable<string> ReadStringArray(JsonElement item, string name) =>
        item.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).ToArray()
            : [];

    private static bool IsStructuredSpawnAgentCall(JsonElement item) =>
        FindString(item, "name")?.Equals("spawn_agent", StringComparison.OrdinalIgnoreCase) == true ||
        FindString(item, "tool")?.Equals("spawn_agent", StringComparison.OrdinalIgnoreCase) == true;

    private static JsonElement? FindProperty(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) ? property.Clone() : null;
    private static string? FindString(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    private static long? GetNullableLong(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var result) ? result : null;

    private sealed class ToolCall(string id, string name)
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public bool IsSpawn => Name.Equals("spawn_agent", StringComparison.OrdinalIgnoreCase);
        public bool IsWait => Name.Equals("wait", StringComparison.OrdinalIgnoreCase) || Name.Equals("wait_agent", StringComparison.OrdinalIgnoreCase);
        public bool Completed { get; set; }
        public bool Failed { get; set; }
        public HashSet<string> CreatedAgentIds { get; } = new(StringComparer.Ordinal);
        public HashSet<string> CompletedAgentIds { get; } = new(StringComparer.Ordinal);
    }
}

public sealed class CodexJsonlAnalysisException(string message) : Exception(message);
