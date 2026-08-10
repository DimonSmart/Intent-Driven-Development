using System.Text.Json;
using System.Text.RegularExpressions;
using Idd.Factory.LiveTests.Models;

namespace Idd.Factory.LiveTests.Infrastructure;

public static class CodexWorkerResultReader
{
    private static readonly Regex VerdictPattern = new("(?im)^\\s*Verdict:\\s*(?<value>approved|needs-fix|needs-replan|blocked|intent-required)\\b", RegexOptions.Compiled);
    private static readonly Regex ImplementerPattern = new("(?im)^\\s*(?<value>DONE|NEEDS_REPLAN|BLOCKED|INTENT_REQUIRED)\\b", RegexOptions.Compiled);
    private static readonly Regex DecomposerPattern = new("(?im)^\\s*(?<value>READY|NEEDS_CLARIFICATION|INTENT_REQUIRED|FOCUSED_HANDOFF|BLOCKED)\\b", RegexOptions.Compiled);
    private static readonly Regex FieldPattern = new("^[A-Za-z][A-Za-z -]{0,40}:\\s*", RegexOptions.Compiled);

    public static AgentTerminalResult? TryRead(CodexRollout rollout, string role, ICollection<AgentTraceDiagnostic> diagnostics)
    {
        if (role is not ("implementer" or "checkpoint-reviewer" or "final-reviewer" or "task-decomposer")) return null;

        AgentTerminalResult? result = null;
        try
        {
            foreach (var line in File.ReadLines(rollout.Path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var text = ReadAssistantText(document.RootElement);
                    if (text is null) continue;
                    if (TryParse(text, role) is { } parsed) result = parsed;
                }
                catch (JsonException)
                {
                    // CodexRolloutReader already records malformed rollout lines.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new("WORKER_RESULT_READ_FAILED", "warning", "Worker terminal result could not be read: " + exception.Message, rollout.ThreadId, rollout.File));
        }
        return result;
    }

    public static AgentTerminalResult? TryParse(string text, string role)
    {
        Match? match = role switch
        {
            "implementer" => ImplementerPattern.Match(text),
            "checkpoint-reviewer" or "final-reviewer" => VerdictPattern.Match(text),
            "task-decomposer" => DecomposerPattern.Match(text),
            _ => null
        };
        if (match is null || !match.Success) return null;

        var kind = NormalizeKind(match.Groups["value"].Value);
        var dependency = ReadField(text, "Dependency");
        var reason = ReadField(text, "Reason");
        var resumeWhen = ReadField(text, "Resume when");
        return new(kind, dependency ?? reason ?? resumeWhen, dependency, reason, resumeWhen);
    }

    private static string? ReadAssistantText(JsonElement root)
    {
        var payload = Object(root, "payload") ?? root;
        var item = Object(payload, "item") ?? Object(payload, "response_item") ?? Object(root, "item");
        if (item is { } message && String(message, "type") == "message")
        {
            var messageRole = String(message, "role");
            if (messageRole is not ("user" or "system" or "developer")) return Text(message);
        }

        var eventType = String(root, "type") == "event_msg" ? String(payload, "type") : String(root, "type");
        if (eventType is "agent_message" or "assistant_message")
            return String(payload, "message") ?? String(payload, "text") ?? Text(payload);
        return null;
    }

    private static string? ReadField(string text, string field)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (!line.StartsWith(field + ":", StringComparison.OrdinalIgnoreCase)) continue;
            var sameLine = line[(field.Length + 1)..].Trim();
            if (sameLine.Length > 0) return Compact(sameLine);

            var values = new List<string>();
            for (var next = index + 1; next < lines.Length; next++)
            {
                var value = lines[next].Trim();
                if (value.Length == 0)
                {
                    if (values.Count > 0) break;
                    continue;
                }
                if (FieldPattern.IsMatch(value)) break;
                values.Add(value);
            }
            return values.Count == 0 ? null : Compact(string.Join(" ", values));
        }
        return null;
    }

    private static string NormalizeKind(string value) => value.Trim().Replace('-', '_').ToUpperInvariant();
    private static string Compact(string value) => value.Length <= 1000 ? value : value[..1000];
    private static JsonElement? Object(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result) && result.ValueKind == JsonValueKind.Object ? result : null;
    private static string? String(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result) && result.ValueKind == JsonValueKind.String ? result.GetString() : null;
    private static string? Text(JsonElement item)
    {
        var direct = String(item, "text");
        if (direct is not null) return direct;
        if (!item.TryGetProperty("content", out var content)) return null;
        if (content.ValueKind == JsonValueKind.String) return content.GetString();
        if (content.ValueKind != JsonValueKind.Array) return null;
        var parts = content.EnumerateArray().Select(part => part.ValueKind == JsonValueKind.String ? part.GetString() : String(part, "text")).Where(part => !string.IsNullOrWhiteSpace(part)).ToArray();
        return parts.Length == 0 ? null : string.Join("\n", parts);
    }
}
