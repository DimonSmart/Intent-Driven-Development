using System.Text.Json;
using System.Text.RegularExpressions;
using Idd.Factory.LiveTests.Models;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed record CodexCodeModeTelemetry(int ModelTurns, IReadOnlyList<AgentFileRead> FileReads);

public static class CodexCodeModeTelemetryReader
{
    private static readonly Regex ReadCommandPattern = new("(?ix)(?:Get-Content\\s+(?:-[a-z]+\\s+)*(?:'(?<sq>[^']+)'|\\\"(?<dq>[^\\\"]+)\\\"|(?<plain>[^\\s;|,)]+))|(?:cat|type)\\s+(?:'(?<sq2>[^']+)'|\\\"(?<dq2>[^\\\"]+)\\\"|(?<plain2>[^\\s;|,)]+)))", RegexOptions.Compiled);

    public static CodexCodeModeTelemetry Read(CodexRollout rollout, ICollection<AgentTraceDiagnostic> diagnostics)
    {
        var turns = 0;
        var reads = new List<AgentFileRead>();
        UsageSignature? previousUsage = null;
        var sequence = 0;

        try
        {
            foreach (var line in File.ReadLines(rollout.Path))
            {
                sequence++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    var payload = Object(root, "payload") ?? root;
                    var eventType = string.Equals(String(root, "type"), "event_msg", StringComparison.Ordinal)
                        ? String(payload, "type")
                        : String(root, "type");

                    if (eventType == "token_count" && ReadUsage(root, payload) is { } usage && HasActivity(usage))
                    {
                        if (previousUsage is null || previousUsage != usage)
                        {
                            turns++;
                            previousUsage = usage;
                        }
                    }

                    var item = Object(payload, "item") ?? Object(payload, "response_item") ?? Object(root, "item") ?? payload;
                    if (!string.Equals(String(item, "type"), "custom_tool_call", StringComparison.Ordinal) ||
                        !string.Equals(String(item, "name") ?? String(item, "tool"), "exec", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var source = DetailString(item, "input") ?? DetailString(item, "arguments") ?? DetailString(item, "command");
                    foreach (Match match in ReadCommandPattern.Matches(source ?? string.Empty))
                    {
                        var raw = new[] { "sq", "dq", "plain", "sq2", "dq2", "plain2" }
                            .Select(name => match.Groups[name].Value)
                            .FirstOrDefault(value => value.Length > 0);
                        if (raw is not null)
                            reads.Add(new(CodexRolloutReader.NormalizePath(raw), sequence, 0, 0));
                    }
                }
                catch (JsonException)
                {
                    // The main rollout reader already reports malformed JSONL.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new("CODE_MODE_TELEMETRY_READ_FAILED", "warning", "Code Mode telemetry could not be read: " + exception.Message, rollout.ThreadId, rollout.File));
        }

        return new(turns, reads);
    }

    private static UsageSignature? ReadUsage(JsonElement root, JsonElement payload)
    {
        var info = Object(payload, "info");
        var usage = Object(root, "usage") ?? Object(payload, "usage") ?? Object(payload, "total_token_usage") ?? (info is null ? null : Object(info.Value, "total_token_usage"));
        if (usage is null) return null;
        return new(
            Integer(usage.Value, "input_tokens") ?? Integer(usage.Value, "inputTokens"),
            Integer(usage.Value, "cached_input_tokens") ?? Integer(usage.Value, "cachedInputTokens"),
            Integer(usage.Value, "output_tokens") ?? Integer(usage.Value, "outputTokens"),
            Integer(usage.Value, "reasoning_output_tokens") ?? Integer(usage.Value, "reasoningOutputTokens"),
            Integer(usage.Value, "total_tokens") ?? Integer(usage.Value, "totalTokens"));
    }

    private static bool HasActivity(UsageSignature usage) =>
        (usage.InputTokens ?? 0) > 0 ||
        (usage.OutputTokens ?? 0) > 0 ||
        (usage.ReasoningOutputTokens ?? 0) > 0 ||
        (usage.TotalTokens ?? 0) > 0;

    private static JsonElement? Object(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result) && result.ValueKind == JsonValueKind.Object ? result : null;

    private static string? String(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result) && result.ValueKind == JsonValueKind.String ? result.GetString() : null;

    private static string? DetailString(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var result)) return null;
        if (result.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        if (result.ValueKind == JsonValueKind.String) return result.GetString();
        return result.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? result.GetRawText() : result.ToString();
    }

    private static long? Integer(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result) && result.ValueKind == JsonValueKind.Number && result.TryGetInt64(out var number) ? number : null;

    private sealed record UsageSignature(long? InputTokens, long? CachedInputTokens, long? OutputTokens, long? ReasoningOutputTokens, long? TotalTokens);
}

public sealed record CodexProcessToolFailureTelemetry(int FailedToolCalls, int RejectedToolCalls);

public static class CodexProcessToolFailureReader
{
    private const string RouterErrorMarker = "ERROR codex_core::tools::router: error=";

    public static CodexProcessToolFailureTelemetry? Read(string? path, string? rootThreadId, ICollection<AgentTraceDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        var failed = 0;
        var rejected = 0;
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (!line.Contains(RouterErrorMarker, StringComparison.Ordinal)) continue;
                failed++;
                if (line.Contains("rejected", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("blocked by policy", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("denied", StringComparison.OrdinalIgnoreCase))
                    rejected++;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new("PROCESS_TOOL_FAILURE_READ_FAILED", "warning", "Codex process tool failures could not be read: " + exception.Message, rootThreadId, path));
            return null;
        }

        return new(failed, rejected);
    }
}
