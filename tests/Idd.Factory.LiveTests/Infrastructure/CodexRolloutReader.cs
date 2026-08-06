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
        if (itemType is "function_call" or "collab_tool_call")
        {
            var id = String(item, "id") ?? String(item, "call_id");
            if (!string.IsNullOrWhiteSpace(id)) analysis.ToolCallIds.Add(id);
            if (string.Equals(String(item, "tool") ?? String(item, "name"), "spawn_agent", StringComparison.OrdinalIgnoreCase))
                foreach (var child in Strings(item, "receiver_thread_ids")) analysis.SpawnedThreadIds.Add(child);
        }
        if (itemType is "message" or "input_message" or "user_message")
        {
            var text = Text(item);
            if (text is not null && (analysis.DispatchMessage is null || text.Contains("Role:", StringComparison.OrdinalIgnoreCase)))
                analysis.DispatchMessage = text;
        }

        var eventType = type == "event_msg" ? String(payload, "type") : type;
        if (eventType is "turn.completed" or "session.completed" or "thread.completed" or "task_complete")
        {
            analysis.CompletedAt ??= timestamp;
            analysis.Status = "completed";
        }
        else if (eventType is "turn.failed" or "session.failed" or "thread.failed" or "turn.error" or "session.error")
        {
            analysis.CompletedAt ??= timestamp;
            analysis.Status = "failed";
        }
    }

    private static JsonElement? Object(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result) && result.ValueKind == JsonValueKind.Object ? result : null;
    private static string? String(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result) && result.ValueKind == JsonValueKind.String ? result.GetString() : null;
    private static double? Number(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result) && result.ValueKind == JsonValueKind.Number && result.TryGetDouble(out var number) ? number : null;
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
    public string? DispatchMessage { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
