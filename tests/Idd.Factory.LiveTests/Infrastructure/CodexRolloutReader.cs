using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Idd.Factory.LiveTests.Models;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed class CodexRolloutReader
{
    private static readonly Regex ReadCommandPattern = new("(?ix)(?:Get-Content\\s+(?:-[a-z]+\\s+)*(?:'(?<sq>[^']+)'|\\\"(?<dq>[^\\\"]+)\\\"|(?<plain>[^\\s;|]+))|(?:cat|type)\\s+(?:'(?<sq2>[^']+)'|\\\"(?<dq2>[^\\\"]+)\\\"|(?<plain2>[^\\s;|]+)))", RegexOptions.Compiled);
    private static readonly Regex ReferencePattern = new("(?i)(?<path>(?:[a-z]:)?[^\\s`'\\\"]*(?:SKILL\\.md|references[/\\\\][^\\s`'\\\"]+\\.md|project-verification\\.md|platform-dispatch\\.md|\\.idd[/\\\\]factory[/\\\\]current[/\\\\][^\\s`'\\\"]+\\.md|request\\.md))", RegexOptions.Compiled);
    private static readonly Regex DispatchRolePattern = new("(?im)^\\s*Role:[ \\t]*(?:\\r?\\n[ \\t]*)?(?<role>[^\\r\\n]+)", RegexOptions.Compiled);

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

    public CodexRolloutAnalysis Analyze(CodexRollout rollout, ICollection<AgentTraceDiagnostic> diagnostics)
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
                    ReadEvent(document.RootElement, analysis, diagnostics);
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
        analysis.ClassifyRetries();
        analysis.DispatchReferences.AddRange(ReadDispatchReferences(analysis.DispatchMessage, rollout.WorkingDirectory));
        return analysis;
    }

    public CodexRolloutAnalysis AnalyzeJsonl(string path, string threadId, string role, DateTimeOffset? startedAt, string workingDirectory, ICollection<AgentTraceDiagnostic> diagnostics)
        => Analyze(new(path, Path.GetFileName(path), threadId, null, role, startedAt, workingDirectory), diagnostics);

    public static IReadOnlyList<DispatchReferenceSize> ReadDispatchReferences(string? dispatch, string? workingDirectory)
    {
        var result = new List<DispatchReferenceSize>();
        foreach (Match match in ReferencePattern.Matches(dispatch ?? string.Empty))
        {
            var path = NormalizePath(match.Groups["path"].Value.TrimEnd('.', ',', ')', ']'));
            var kind = path.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase) ? "skill" : path.Contains("/roles/", StringComparison.OrdinalIgnoreCase) ? "role" : path.EndsWith("project-verification.md", StringComparison.OrdinalIgnoreCase) ? "project-verification" : path.EndsWith("platform-dispatch.md", StringComparison.OrdinalIgnoreCase) ? "platform-dispatch" : path.Contains(".idd/factory/current/", StringComparison.OrdinalIgnoreCase) ? "active-work-item" : "reference";
            int? characters = null, bytes = null;
            try
            {
                var local = path.Replace('/', Path.DirectorySeparatorChar);
                var resolved = Path.IsPathRooted(local) ? local : workingDirectory is null ? null : Path.Combine(workingDirectory, local);
                if (resolved is not null && File.Exists(resolved)) { var content = File.ReadAllText(resolved); characters = content.Length; bytes = Encoding.UTF8.GetByteCount(content); }
            }
            catch (Exception) { }
            if (!result.Any(reference => reference.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) result.Add(new(path, characters, bytes, kind));
        }
        return result;
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
                        ParseDate(String(payload, "timestamp") ?? String(root, "timestamp")),
                        String(payload, "cwd") ?? String(payload, "working_directory"));
                }
                catch (JsonException) { }
            }
        }
        catch (Exception) { }
        return null;
    }

    private static void ReadEvent(JsonElement root, CodexRolloutAnalysis analysis, ICollection<AgentTraceDiagnostic> diagnostics)
    {
        var sequence = analysis.NextSequence();
        var type = String(root, "type");
        var payload = Object(root, "payload") ?? root;
        var item = Object(payload, "item") ?? Object(payload, "response_item") ?? Object(root, "item") ?? payload;
        var timestamp = ParseDate(String(root, "timestamp") ?? String(payload, "timestamp") ?? String(item, "timestamp")) ?? ParseUnixDate(Number(payload, "completed_at"));
        var itemType = String(item, "type");

        if (IsToolCall(itemType)) ReadToolCall(type, item, sequence, timestamp, analysis);
        else if (IsToolResult(itemType)) ReadToolResult(item, sequence, timestamp, analysis);

        if (itemType is "message" or "input_message" or "user_message")
        {
            var text = Text(item);
            if (text is not null && (analysis.DispatchMessage is null || text.Contains("Role:", StringComparison.OrdinalIgnoreCase))) analysis.DispatchMessage = text;
        }

        var eventType = type == "event_msg" ? String(payload, "type") : type;
        if (eventType == "turn.completed")
        {
            analysis.TurnCount++;
            var usage = FindUsage(root, payload);
            if (usage is not null) analysis.AddTokenSnapshot(sequence, timestamp, eventType, ReadTokenUsage(usage), diagnostics, updateLatest: !analysis.HasTokenCountTelemetry);
            analysis.CompletedAt = timestamp ?? analysis.CompletedAt;
            analysis.Status = "completed";
        }
        else if (eventType == "token_count")
        {
            var usage = ReadTokenUsage(FindUsage(root, payload));
            if (usage is not null)
            {
                analysis.HasTokenCountTelemetry = true;
                analysis.AddTokenSnapshot(sequence, timestamp, eventType, usage, diagnostics, updateLatest: true);
            }
        }
        else if (eventType is "session.completed" or "thread.completed" or "task_complete") { analysis.CompletedAt = timestamp ?? analysis.CompletedAt; analysis.Status = "completed"; }
        else if (eventType is "turn.failed" or "session.failed" or "thread.failed" or "turn.error" or "session.error") { analysis.CompletedAt = timestamp ?? analysis.CompletedAt; analysis.Status = "failed"; }
        else if (eventType is "turn.cancelled" or "session.cancelled" or "thread.cancelled") { analysis.CompletedAt = timestamp ?? analysis.CompletedAt; analysis.Status = "cancelled"; }
    }

    private static void ReadToolCall(string? eventType, JsonElement item, int sequence, DateTimeOffset? timestamp, CodexRolloutAnalysis analysis)
    {
        var id = String(item, "id") ?? String(item, "call_id");
        var itemKind = String(item, "type");
        var tool = String(item, "tool") ?? String(item, "name") ?? itemKind switch
        {
            "local_shell_call" or "command_execution" => "shell",
            "file_change" => "apply_patch",
            _ => "unknown"
        };
        var status = String(item, "status") ?? (eventType?.Contains("completed", StringComparison.OrdinalIgnoreCase) == true ? "completed" : "started");
        var command = DetailString(item, "command") ?? DetailString(item, "cmd");
        var output = DetailString(item, "aggregated_output") ?? DetailString(item, "output") ?? DetailString(item, "result");
        var exitCode = Int32(item, "exit_code") ?? Int32(item, "exitCode");
        var prompt = DetailString(item, "prompt") ?? DetailString(item, "message");
        var children = Strings(item, "receiver_thread_ids").Concat(Strings(item, "child_thread_ids")).Distinct(StringComparer.Ordinal).ToArray();
        var error = DetailString(item, "error") ?? (Object(item, "error") is { } errorObject ? DetailString(errorObject, "message") : null);
        var terminalWait = ReadTerminalWait(item, tool);
        var call = analysis.RegisterToolCall(id, sequence, tool);
        call.Update(status, timestamp, eventType, command, exitCode, output, children, terminalWait, error);

        if (tool.Equals("spawn_agent", StringComparison.OrdinalIgnoreCase))
        {
            call.SetDispatch(prompt, prompt is null ? null : DispatchRolePattern.Match(prompt).Groups["role"].Value.Trim());
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(prompt)) analysis.SpawnPromptsByCallId[id] = prompt;
            var effectivePrompt = prompt ?? (string.IsNullOrWhiteSpace(id) ? null : analysis.SpawnPromptsByCallId.GetValueOrDefault(id));
            foreach (var child in children)
            {
                analysis.SpawnedThreadIds.Add(child);
                if (!string.IsNullOrWhiteSpace(effectivePrompt)) analysis.SpawnPrompts[child] = effectivePrompt;
            }
        }
        var terminal = status is "completed" or "failed" or "error" or "rejected" or "cancelled" || eventType?.Contains("completed", StringComparison.OrdinalIgnoreCase) == true;
        foreach (Match match in terminal ? ReadCommandPattern.Matches(command ?? string.Empty).Cast<Match>() : [])
        {
            var raw = new[] { "sq", "dq", "plain", "sq2", "dq2", "plain2" }.Select(name => match.Groups[name].Value).FirstOrDefault(value => value.Length > 0);
            var path = raw is null ? null : NormalizePath(raw);
            if (!string.IsNullOrWhiteSpace(path) && !path.StartsWith('$')) analysis.FileReads.Add(new(path, sequence, output is null ? 0 : Encoding.UTF8.GetByteCount(output), output?.Length ?? 0));
        }
    }

    private static void ReadToolResult(JsonElement item, int sequence, DateTimeOffset? timestamp, CodexRolloutAnalysis analysis)
    {
        var id = String(item, "call_id") ?? String(item, "id");
        if (string.IsNullOrWhiteSpace(id) || !analysis.TryGetToolCall(id, out var call)) return;
        var output = DetailString(item, "output") ?? DetailString(item, "result") ?? DetailString(item, "content");
        var status = String(item, "status") ?? "completed";
        call.Update(status, timestamp, "result", null, Int32(item, "exit_code"), output, [], null, DetailString(item, "error"));
    }

    private static bool? ReadTerminalWait(JsonElement item, string tool)
    {
        if (tool is not ("wait" or "wait_agent")) return null;
        var states = Object(item, "agents_states");
        if (states is null) return String(item, "status") is "completed" ? null : false;
        return states.Value.EnumerateObject().All(property => String(property.Value, "status") is "completed" or "failed" or "cancelled");
    }

    private static JsonElement? FindUsage(JsonElement root, JsonElement payload) { var info = Object(payload, "info"); return Object(root, "usage") ?? Object(payload, "usage") ?? Object(payload, "total_token_usage") ?? (info is null ? null : Object(info.Value, "total_token_usage")); }
    private static CodexTokenUsage? ReadTokenUsage(JsonElement? usage)
    {
        if (usage is null) return null;
        var input = Integer(usage.Value, "input_tokens") ?? Integer(usage.Value, "inputTokens");
        var cached = Integer(usage.Value, "cached_input_tokens") ?? Integer(usage.Value, "cachedInputTokens");
        var output = Integer(usage.Value, "output_tokens") ?? Integer(usage.Value, "outputTokens");
        var reasoning = Integer(usage.Value, "reasoning_output_tokens") ?? Integer(usage.Value, "reasoningOutputTokens");
        var total = Integer(usage.Value, "total_tokens") ?? Integer(usage.Value, "totalTokens");
        return input is null && cached is null && output is null && reasoning is null && total is null ? null : new(input, cached, output, reasoning, total ?? (input is not null && output is not null ? input + output : null));
    }

    public static string NormalizePath(string path)
    {
        var normalized = path.Trim().Trim('"', '\'').Replace('\\', '/');
        while (normalized.Contains("//", StringComparison.Ordinal)) normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        while (normalized.StartsWith("./", StringComparison.Ordinal)) normalized = normalized[2..];
        return normalized;
    }

    private static bool IsToolCall(string? type) => type is "function_call" or "custom_tool_call" or "local_shell_call" or "collab_tool_call" or "command_execution" or "file_change";
    private static bool IsToolResult(string? type) => type is "function_call_output" or "custom_tool_call_output" or "local_shell_call_output" or "collab_tool_call_output";
    private static JsonElement? Object(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result) && result.ValueKind == JsonValueKind.Object ? result : null;
    private static string? String(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result) && result.ValueKind == JsonValueKind.String ? result.GetString() : null;
    private static string? DetailString(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var result)) return null;
        if (result.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        if (result.ValueKind == JsonValueKind.String) return result.GetString();
        return result.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? result.GetRawText() : result.ToString();
    }
    private static double? Number(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result) && result.ValueKind == JsonValueKind.Number && result.TryGetDouble(out var number) ? number : null;
    private static int? Int32(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result) && result.ValueKind == JsonValueKind.Number && result.TryGetInt32(out var number) ? number : null;
    private static long? Integer(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result) && result.ValueKind == JsonValueKind.Number && result.TryGetInt64(out var number) ? number : null;
    private static IEnumerable<string> Strings(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result) && result.ValueKind == JsonValueKind.Array ? result.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToArray() : [];
    private static string? Text(JsonElement item) => String(item, "text") ?? String(item, "content") ?? (item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array ? content.EnumerateArray().Select(v => String(v, "text")).FirstOrDefault(v => v is not null) : null);
    private static DateTimeOffset? ParseDate(string? text) => DateTimeOffset.TryParse(text, out var value) ? value : null;
    private static DateTimeOffset? ParseUnixDate(double? seconds) => seconds is null ? null : DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds.Value * 1000));
}

public sealed record CodexRollout(string Path, string File, string ThreadId, string? ParentThreadId, string? MetadataRole, DateTimeOffset? StartedAt, string? WorkingDirectory);

public sealed class CodexRolloutAnalysis(CodexRollout rollout)
{
    private readonly Dictionary<string, MutableToolCall> toolCalls = new(StringComparer.Ordinal);
    private int sequence;
    public CodexRollout Rollout { get; } = rollout;
    public HashSet<string> SpawnedThreadIds { get; } = new(StringComparer.Ordinal);
    public int ToolCallCount => toolCalls.Count;
    public IReadOnlyList<AgentToolCall> ToolCalls => toolCalls.Values.OrderBy(call => call.Sequence).Select(call => call.ToRecord()).ToArray();
    public Dictionary<string, string> SpawnPrompts { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> SpawnPromptsByCallId { get; } = new(StringComparer.Ordinal);
    public List<AgentFileRead> FileReads { get; } = [];
    public List<DispatchReferenceSize> DispatchReferences { get; } = [];
    public List<TokenUsageSnapshot> TokenProgression { get; } = [];
    public string? DispatchMessage { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int TurnCount { get; set; }
    public bool HasTokenCountTelemetry { get; set; }
    public CodexTokenUsage? TokenUsage { get; set; }
    public int NextSequence() => ++sequence;

    public MutableToolCall RegisterToolCall(string? id, int eventSequence, string tool)
    {
        var key = string.IsNullOrWhiteSpace(id) ? $"anonymous:{eventSequence}" : id;
        if (!toolCalls.TryGetValue(key, out var call)) toolCalls[key] = call = new(eventSequence, id, tool);
        return call;
    }
    public bool TryGetToolCall(string id, out MutableToolCall call) => toolCalls.TryGetValue(id, out call!);

    public void AddTokenSnapshot(int eventSequence, DateTimeOffset? timestamp, string? source, CodexTokenUsage? usage, ICollection<AgentTraceDiagnostic> diagnostics, bool updateLatest)
    {
        if (usage is null) return;
        var previous = TokenProgression.LastOrDefault();
        var inputDelta = Delta(previous?.InputTokens, usage.InputTokens, "input", out var inputReset);
        var cachedDelta = Delta(previous?.CachedInputTokens, usage.CachedInputTokens, "cached input", out var cachedReset);
        var outputDelta = Delta(previous?.OutputTokens, usage.OutputTokens, "output", out var outputReset);
        var discontinuity = inputReset || cachedReset || outputReset;
        long? freshDelta = inputDelta is not null && cachedDelta is not null && cachedDelta <= inputDelta ? inputDelta - cachedDelta : null;
        if (inputDelta is not null && cachedDelta is not null && cachedDelta > inputDelta) discontinuity = true;
        if (discontinuity) diagnostics.Add(new("TOKEN_COUNTER_DISCONTINUITY", "warning", "A cumulative token counter decreased, reset, or produced inconsistent deltas; affected deltas are unavailable.", Rollout.ThreadId, Rollout.File));
        var tools = toolCalls.Values.Where(call => call.Sequence > (previous?.Sequence ?? 0) && call.Sequence <= eventSequence).Select(call => call.CallId ?? $"sequence:{call.Sequence}").ToArray();
        TokenProgression.Add(new(eventSequence, timestamp, source, usage.InputTokens, usage.CachedInputTokens, usage.OutputTokens, usage.ReasoningOutputTokens, usage.TotalTokens, inputReset ? null : inputDelta, cachedReset ? null : cachedDelta, discontinuity ? null : freshDelta, outputReset ? null : outputDelta, discontinuity, tools));
        if (updateLatest) TokenUsage = usage;
    }

    public void ClassifyRetries()
    {
        MutableToolCall? previous = null;
        var waitCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var call in toolCalls.Values.OrderBy(call => call.Sequence))
        {
            var target = call.OperationKey;
            if (previous is not null && previous.IsFailure && target is not null && string.Equals(previous.OperationKey, target, StringComparison.OrdinalIgnoreCase)) call.IsRetryOrFallback = true;
            if (call.Tool is "wait" or "wait_agent")
            {
                var child = call.ChildThreadIds.FirstOrDefault() ?? "unknown";
                call.RepeatedWaitNumber = waitCounts.TryGetValue(child, out var count) ? count + 1 : 1;
                waitCounts[child] = call.RepeatedWaitNumber;
            }
            previous = call;
        }
    }

    private static long? Delta(long? previous, long? current, string counter, out bool reset) { reset = previous is not null && current is not null && current < previous; return previous is null || current is null || reset ? null : current - previous; }
}

public sealed class MutableToolCall(int sequence, string? callId, string tool)
{
    public int Sequence { get; } = sequence;
    public string? CallId { get; } = callId;
    public string Tool { get; } = tool;
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string Status { get; private set; } = "unknown";
    public bool IsFailure { get; private set; }
    public bool IsRejected { get; private set; }
    public bool IsRetryOrFallback { get; set; }
    public string? Command { get; private set; }
    public int? ExitCode { get; private set; }
    public long ResultBytes { get; private set; }
    public List<string> ChildThreadIds { get; } = [];
    public bool? IsTerminalWait { get; private set; }
    public int RepeatedWaitNumber { get; set; }
    public string? ChildRole { get; private set; }
    public int DispatchCharacters { get; private set; }
    public int DispatchUtf8Bytes { get; private set; }
    public string? OperationKey => (Command ?? Tool).Trim().ToLowerInvariant();

    public void Update(string status, DateTimeOffset? timestamp, string? eventType, string? command, int? exitCode, string? output, IEnumerable<string> children, bool? terminalWait, string? error)
    {
        Status = status;
        if (StartedAt is null && (status is "started" or "in_progress" || eventType?.Contains("started", StringComparison.OrdinalIgnoreCase) == true)) StartedAt = timestamp;
        if (status is "completed" or "failed" or "error" or "rejected" or "cancelled" || eventType is "result" || eventType?.Contains("completed", StringComparison.OrdinalIgnoreCase) == true) CompletedAt = timestamp ?? CompletedAt;
        StartedAt ??= timestamp;
        Command ??= command;
        ExitCode = exitCode ?? ExitCode;
        if (output is not null) ResultBytes = Encoding.UTF8.GetByteCount(output);
        foreach (var child in children) if (!ChildThreadIds.Contains(child, StringComparer.Ordinal)) ChildThreadIds.Add(child);
        IsTerminalWait = terminalWait ?? IsTerminalWait;
        IsFailure |= status is "failed" or "error" || exitCode is not null and not 0;
        IsRejected |= status == "rejected" || (error?.Contains("reject", StringComparison.OrdinalIgnoreCase) == true || error?.Contains("policy", StringComparison.OrdinalIgnoreCase) == true || error?.Contains("denied", StringComparison.OrdinalIgnoreCase) == true);
        IsFailure |= IsRejected;
    }

    public void SetDispatch(string? prompt, string? role)
    {
        if (prompt is not null) { DispatchCharacters = prompt.Length; DispatchUtf8Bytes = Encoding.UTF8.GetByteCount(prompt); }
        if (!string.IsNullOrWhiteSpace(role)) ChildRole = role;
    }

    public AgentToolCall ToRecord() => new(Sequence, CallId, Tool, StartedAt, CompletedAt, StartedAt is not null && CompletedAt is not null ? (long?)(CompletedAt.Value - StartedAt.Value).TotalMilliseconds : null, Status, IsFailure, IsRejected, IsRetryOrFallback, Tool == "shell" ? Command?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() : Tool, Command is null ? null : Command.Length <= 200 ? Command : Command[..197] + "...", ExitCode, ResultBytes, ChildThreadIds.ToArray(), IsTerminalWait, RepeatedWaitNumber, ChildRole, DispatchCharacters, DispatchUtf8Bytes);
}

public sealed record CodexTokenUsage(long? InputTokens, long? CachedInputTokens, long? OutputTokens, long? ReasoningOutputTokens, long? TotalTokens);
