using System.Text.Json;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed class CodexRolloutReader
{
    public IReadOnlyList<CodexRollout> Index(string sessionsDirectory)
    {
        var rollouts = new List<CodexRollout>();
        foreach (var path in Directory.EnumerateFiles(sessionsDirectory, "*.jsonl", SearchOption.AllDirectories))
        {
            var rollout = ReadMetadata(path, sessionsDirectory);
            if (rollout is not null) rollouts.Add(rollout);
        }
        return rollouts;
    }

    public CodexRolloutAnalysis Analyze(CodexRollout rollout, ICollection<Models.AgentTraceDiagnostic> diagnostics)
    {
        var analysis = new CodexRolloutAnalysis(rollout);
        try
        {
            foreach (var line in File.ReadLines(rollout.Path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var document = JsonDocument.Parse(line);
                    ReadEvent(document.RootElement, analysis);
                }
                catch (JsonException)
                {
                    diagnostics.Add(new("ROLLOUT_MALFORMED_LINE", "warning", "Malformed rollout JSONL line was ignored.", rollout.ThreadId, rollout.File));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new("ROLLOUT_READ_FAILED", "warning", "Rollout could not be read: " + exception.Message, rollout.ThreadId, rollout.File));
        }
        return analysis;
    }

    private static CodexRollout? ReadMetadata(string path, string sessionsDirectory)
    {
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (!string.Equals(String(root, "type"), "session_meta", StringComparison.Ordinal)) continue;
                    var payload = Object(root, "payload") ?? root;
                    var threadId = String(payload, "thread_id") ?? String(payload, "threadId") ?? String(payload, "id");
                    if (string.IsNullOrWhiteSpace(threadId)) return null;
                    return new(path, Path.GetRelativePath(sessionsDirectory, path), threadId,
                        String(payload, "parent_thread_id") ?? String(payload, "parentThreadId"),
                        String(payload, "agent_role") ?? String(payload, "agentRole") ?? String(payload, "role"),
                        ParseDate(String(payload, "timestamp") ?? String(root, "timestamp")));
                }
                catch (JsonException) { }
            }
        }
        catch (Exception) { }
        return null;
    }

    private static void ReadEvent(JsonElement root, CodexRolloutAnalysis analysis)
    {
        var type = String(root, "type");
        var payload = Object(root, "payload") ?? root;
        var item = Object(payload, "item") ?? Object(payload, "response_item") ?? payload;
        var timestamp = ParseDate(String(root, "timestamp") ?? String(payload, "timestamp")) ?? ParseUnixDate(Number(payload, "completed_at"));
        var itemType = String(item, "type");
        if (itemType is "function_call" or "custom_tool_call" or "local_shell_call" or "collab_tool_call")
        {
            var id = String(item, "id") ?? String(item, "call_id");
            analysis.RegisterToolCall(id);
            if (string.Equals(String(item, "tool") ?? String(item, "name"), "spawn_agent", StringComparison.OrdinalIgnoreCase))
            {
                var prompt = String(item, "prompt") ?? String(item, "message");
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(prompt))
                    analysis.SpawnPromptsByCallId[id] = prompt;
                var effectivePrompt = prompt ?? (string.IsNullOrWhiteSpace(id) ? null : analysis.SpawnPromptsByCallId.GetValueOrDefault(id));
                foreach (var child in Strings(item, "receiver_thread_ids"))
                {
                    analysis.SpawnedThreadIds.Add(child);
                    if (!string.IsNullOrWhiteSpace(effectivePrompt)) analysis.SpawnPrompts[child] = effectivePrompt;
                }
            }
        }
        if (itemType is "message" or "input_message" or "user_message")
        {
            var text = Text(item);
            if (text is not null && (analysis.DispatchMessage is null || text.Contains("Role:", StringComparison.OrdinalIgnoreCase)))
                analysis.DispatchMessage = text;
        }

        var eventType = type == "event_msg" ? String(payload, "type") : type;
        if (eventType == "turn.completed")
        {
            analysis.TurnCount++;
            if (!analysis.HasTokenCountTelemetry) ReadTokenUsage(FindUsage(root, payload), analysis);
            analysis.CompletedAt = timestamp ?? analysis.CompletedAt;
            analysis.Status = "completed";
        }
        else if (eventType == "token_count")
        {
            analysis.HasTokenCountTelemetry |= ReadTokenUsage(FindUsage(root, payload), analysis);
        }
        else if (eventType is "session.completed" or "thread.completed" or "task_complete")
        {
            analysis.CompletedAt = timestamp ?? analysis.CompletedAt;
            analysis.Status = "completed";
        }
        else if (eventType is "turn.failed" or "session.failed" or "thread.failed" or "turn.error" or "session.error")
        {
            analysis.CompletedAt = timestamp ?? analysis.CompletedAt;
            analysis.Status = "failed";
        }
        else if (eventType is "turn.cancelled" or "session.cancelled" or "thread.cancelled")
        {
            analysis.CompletedAt = timestamp ?? analysis.CompletedAt;
            analysis.Status = "cancelled";
        }
    }

    private static JsonElement? FindUsage(JsonElement root, JsonElement payload)
    {
        var info = Object(payload, "info");
        return Object(root, "usage") ?? Object(payload, "usage") ??
            Object(payload, "total_token_usage") ?? (info is null ? null : Object(info.Value, "total_token_usage"));
    }

    private static bool ReadTokenUsage(JsonElement? usage, CodexRolloutAnalysis analysis)
    {
        if (usage is null) return false;
        var input = Integer(usage.Value, "input_tokens") ?? Integer(usage.Value, "inputTokens");
        var cached = Integer(usage.Value, "cached_input_tokens") ?? Integer(usage.Value, "cachedInputTokens");
        var output = Integer(usage.Value, "output_tokens") ?? Integer(usage.Value, "outputTokens");
        var reasoning = Integer(usage.Value, "reasoning_output_tokens") ?? Integer(usage.Value, "reasoningOutputTokens");
        var total = Integer(usage.Value, "total_tokens") ?? Integer(usage.Value, "totalTokens");
        if (input is null && cached is null && output is null && reasoning is null && total is null) return false;
        analysis.TokenUsage = new(input, cached, output, reasoning, total ?? (input is not null && output is not null ? input + output : null));
        return true;
    }

    private static JsonElement? Object(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result) && result.ValueKind == JsonValueKind.Object ? result : null;
    private static string? String(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result) && result.ValueKind == JsonValueKind.String ? result.GetString() : null;
    private static double? Number(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result) && result.ValueKind == JsonValueKind.Number && result.TryGetDouble(out var number) ? number : null;
    private static long? Integer(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result) && result.ValueKind == JsonValueKind.Number && result.TryGetInt64(out var number) ? number : null;
    private static IEnumerable<string> Strings(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result) && result.ValueKind == JsonValueKind.Array ? result.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToArray() : [];
    private static string? Text(JsonElement item) => String(item, "text") ?? String(item, "content") ?? (item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array ? content.EnumerateArray().Select(v => String(v, "text")).FirstOrDefault(v => v is not null) : null);
    private static DateTimeOffset? ParseDate(string? text) => DateTimeOffset.TryParse(text, out var value) ? value : null;
    private static DateTimeOffset? ParseUnixDate(double? seconds) => seconds is null ? null : DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds.Value * 1000));
}

public sealed record CodexRollout(string Path, string File, string ThreadId, string? ParentThreadId, string? MetadataRole, DateTimeOffset? StartedAt);
public sealed class CodexRolloutAnalysis(CodexRollout rollout)
{
    public CodexRollout Rollout { get; } = rollout;
    public HashSet<string> SpawnedThreadIds { get; } = new(StringComparer.Ordinal);
    public HashSet<string> ToolCallIds { get; } = new(StringComparer.Ordinal);
    public int AnonymousToolCallCount { get; private set; }
    public int ToolCallCount => ToolCallIds.Count + AnonymousToolCallCount;
    public Dictionary<string, string> SpawnPrompts { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> SpawnPromptsByCallId { get; } = new(StringComparer.Ordinal);
    public string? DispatchMessage { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int TurnCount { get; set; }
    public bool HasTokenCountTelemetry { get; set; }
    public CodexTokenUsage? TokenUsage { get; set; }
    public void RegisterToolCall(string? id) { if (string.IsNullOrWhiteSpace(id)) AnonymousToolCallCount++; else ToolCallIds.Add(id); }
}

public sealed record CodexTokenUsage(long? InputTokens, long? CachedInputTokens, long? OutputTokens, long? ReasoningOutputTokens, long? TotalTokens);
