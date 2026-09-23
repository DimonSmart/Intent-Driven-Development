using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Idd.Factory.Report;

public sealed class FactoryRunReport
{
    public int SchemaVersion { get; init; } = 1;
    public string Repository { get; init; } = "";
    public HostInfo Host { get; init; } = new();
    public RunInfo Run { get; init; } = new();
    public List<AgentReport> Agents { get; init; } = [];
    public List<TaskReport> Tasks { get; init; } = [];
    public ReportMetrics Metrics { get; init; } = new();
    public List<TimelineEvent> Timeline { get; init; } = [];
    public List<Diagnostic> Diagnostics { get; init; } = [];
    public FactoryProjectState FactoryProjectState { get; init; } = new();
}

public sealed class HostInfo
{
    public string Name { get; init; } = "codex";
    public string? CodexHome { get; init; }
    public CodexCapabilities Capabilities { get; init; } = new();
}

public sealed class CodexCapabilities
{
    public bool StateDb { get; set; }
    public bool SpawnEdges { get; set; }
    public bool AgentRole { get; set; }
    public string TokenAccounting { get; set; } = "unavailable";
}

public sealed class RunInfo
{
    public string RootThreadId { get; init; } = "";
    public int RunIndex { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public BoundaryInfo StartBoundary { get; init; } = new();
    public BoundaryInfo EndBoundary { get; init; } = new();
    public string Result { get; init; } = "unknown";
    public string? Reason { get; init; }
    public List<string> ResultEvidence { get; init; } = [];
}

public sealed class BoundaryInfo
{
    public string Confidence { get; init; } = "unknown";
    public string? Evidence { get; init; }
}

public sealed class AgentReport
{
    public string ThreadId { get; init; } = "";
    public string? ParentThreadId { get; init; }
    public string Role { get; set; } = "unknown";
    public int Sequence { get; set; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public long? DurationMilliseconds =>
        StartedAt is not null && FinishedAt is not null
            ? (long)(FinishedAt.Value - StartedAt.Value).TotalMilliseconds
            : null;
    public string? Task { get; set; }
    public string? PlannerOutcome { get; set; }
    public TokenMetrics Tokens { get; set; } = new();
    public ToolMetrics Tools { get; set; } = new();
    [JsonIgnore]
    public string? RolloutPath { get; init; }
}

public sealed class TaskReport
{
    public int Number { get; init; }
    public string AgentThreadId { get; init; } = "";
    public string? Text { get; init; }
    public string Status { get; init; } = "unknown";
    public long? DurationMilliseconds { get; init; }
}

public sealed class TokenMetrics
{
    public long? InputTokens { get; init; }
    public long? CachedInputTokens { get; init; }
    public long? NewInputTokens { get; init; }
    public long? OutputTokens { get; init; }

    public bool Available =>
        InputTokens is not null || CachedInputTokens is not null || OutputTokens is not null;

    public static TokenMetrics Subtract(TokenMetrics end, TokenMetrics start)
    {
        static long? Delta(long? a, long? b) => a is not null && b is not null && a >= b ? a - b : null;
        var input = Delta(end.InputTokens, start.InputTokens);
        var cached = Delta(end.CachedInputTokens, start.CachedInputTokens);
        return new TokenMetrics
        {
            InputTokens = input,
            CachedInputTokens = cached,
            NewInputTokens = input is not null && cached is not null && input >= cached ? input - cached : null,
            OutputTokens = Delta(end.OutputTokens, start.OutputTokens)
        };
    }

    public static TokenMetrics Sum(IEnumerable<TokenMetrics> values)
    {
        var array = values.ToArray();
        if (array.Length == 0 || array.Any(x => !x.Available))
            return new TokenMetrics();

        long Sum(Func<TokenMetrics, long?> selector) => array.Sum(x => selector(x) ?? 0);
        var input = Sum(x => x.InputTokens);
        var cached = Sum(x => x.CachedInputTokens);
        return new TokenMetrics
        {
            InputTokens = input,
            CachedInputTokens = cached,
            NewInputTokens = input >= cached ? input - cached : null,
            OutputTokens = Sum(x => x.OutputTokens)
        };
    }
}

public sealed class ToolMetrics
{
    public long ToolCalls { get; set; }
    public long ToolBatches { get; set; }
    public long Commands { get; set; }
    public long FailedCommands { get; set; }
    public long FileOperations { get; set; }
    public long SearchOperations { get; set; }
    public long SpawnAgentCalls { get; set; }
    public long WaitAgentCalls { get; set; }
    public long OtherToolCalls { get; set; }
    public long ToolOutputCharacters { get; set; }
    public long FailedToolOutputCharacters { get; set; }
    public List<FailedCommand> FailedCommandItems { get; init; } = [];
}

public sealed class FailedCommand
{
    public string Agent { get; init; } = "";
    public DateTimeOffset? Timestamp { get; init; }
    public string? Status { get; init; }
    public int? ExitCode { get; init; }
    public string? Command { get; init; }
}

public sealed class ReportMetrics
{
    public TokenMetrics Tokens { get; init; } = new();
    public TokenMetrics RootReportedTokens { get; init; } = new();
    public string TokenAggregationMethod { get; init; } = "unavailable";
    public string TokenAggregationStatus { get; init; } = "unavailable";
    public bool TokenAggregationComplete { get; init; }
    public ToolMetrics Tools { get; init; } = new();
    public long? DurationMilliseconds { get; init; }
    public long? TotalPlannerWallTimeMilliseconds { get; init; }
    public long? TotalWorkerWallTimeMilliseconds { get; init; }
    public long? SumAgentDurationsMilliseconds { get; init; }
    public int PlannerInvocations { get; init; }
    public int WorkerInvocations { get; init; }
    public int OtherChildAgents { get; init; }
    public int MaximumDepth { get; init; }
}

public sealed class TimelineEvent
{
    public DateTimeOffset? Timestamp { get; init; }
    public long Ordinal { get; init; }
    public string Kind { get; init; } = "";
    public string Text { get; init; } = "";
}

public sealed class Diagnostic
{
    public string Severity { get; init; } = "info";
    public string Category { get; init; } = "reporter";
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";
}

public sealed class FactoryProjectState
{
    public bool CurrentRequestPresent { get; init; }
    public bool CurrentPlanPresent { get; init; }
    public bool CurrentCompletedPresent { get; init; }
    public bool CurrentAnswersPresent { get; init; }
    public bool CurrentQuestionPresent { get; init; }
    public bool VerificationFailurePresent { get; init; }
    public bool ArchivedResultPresent { get; init; }
}

public sealed class CodexEvent
{
    public long Ordinal { get; init; }
    public DateTimeOffset? Timestamp { get; init; }
    public string Type { get; init; } = "";
    public string? Role { get; init; }
    public string? Text { get; init; }
    public string? ToolId { get; init; }
    public string? ToolName { get; init; }
    public string? ToolPhase { get; init; }
    public string? ToolArguments { get; init; }
    public string? ToolOutput { get; init; }
    public int? ExitCode { get; init; }
    public string? Status { get; init; }
    public TokenMetrics? Usage { get; init; }
    public bool UsageIsCumulative { get; init; }
    public string? ChildThreadId { get; set; }
    public string? SpawnTask { get; init; }

    public bool Contains(string value) =>
        Text?.Contains(value, StringComparison.OrdinalIgnoreCase) == true ||
        ToolArguments?.Contains(value, StringComparison.OrdinalIgnoreCase) == true;
}

public sealed class CodexRollout
{
    public string Path { get; init; } = "";
    public string ThreadId { get; set; } = "";
    public string? Cwd { get; set; }
    public string? ParentThreadId { get; set; }
    public string? AgentRoleHint { get; set; }
    public List<CodexEvent> Events { get; init; } = [];
    public List<Diagnostic> Diagnostics { get; init; } = [];
    public Dictionary<string, SpawnRecord> SpawnRecords { get; init; } = new(StringComparer.Ordinal);
    public DateTimeOffset? StartedAt => Events.Select(x => x.Timestamp).Where(x => x is not null).Min();
    public DateTimeOffset? FinishedAt => Events.Select(x => x.Timestamp).Where(x => x is not null).Max();
}

public sealed class SpawnRecord
{
    public string CallId { get; init; } = "";
    public string? ChildThreadId { get; set; }
    public string? Task { get; set; }
    public DateTimeOffset? Timestamp { get; init; }
}

public sealed class CodexStateSnapshot
{
    public bool DatabaseAvailable { get; set; }
    public bool SpawnEdgesAvailable { get; set; }
    public Dictionary<string, string> ParentByChild { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> RolloutByThread { get; } = new(StringComparer.Ordinal);
    public List<Diagnostic> Diagnostics { get; } = [];
}

public sealed class CodexRolloutReader
{
    private static readonly Regex ThreadIdRegex = new(
        @"(?<![0-9a-f])(?:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}|[0-9a-f]{24,40})(?![0-9a-f])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public CodexRollout Read(string path, bool metadataOnly = false)
    {
        var rollout = new CodexRollout { Path = path };
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var ordinal = 0L;
        while (true)
        {
            var line = reader.ReadLine();
            if (line is null)
                break;

            ordinal++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var isLastLine = reader.EndOfStream;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var payload = Object(root, "payload") ?? root;
                var topType = String(root, "type");
                var payloadType = String(payload, "type");
                var type = payloadType ?? topType ?? "unknown";

                if (type == "session_meta" || topType == "session_meta")
                {
                    rollout.ThreadId = String(payload, "id") ?? String(root, "id") ?? rollout.ThreadId;
                    rollout.Cwd = String(payload, "cwd") ?? rollout.Cwd;
                    rollout.ParentThreadId = FindString(payload, "parent_thread_id", "parentThreadId", "parent_id") ?? rollout.ParentThreadId;
                    rollout.AgentRoleHint = FindString(payload, "agent_role", "agentRole", "agent_path", "agentPath") ?? rollout.AgentRoleHint;
                }

                var timestamp = Timestamp(root) ?? Timestamp(payload);
                var item = Object(root, "item") ?? Object(payload, "item");
                var eventObject = item ?? payload;

                var role = String(eventObject, "role");
                if (type == "user_message") role = "user";
                if (type == "agent_message") role = "assistant";

                var text = ExtractText(eventObject);
                if (text is null && item is not null)
                    text = ExtractText(payload);

                string? toolId = null;
                string? toolName = null;
                string? toolPhase = null;
                string? toolArguments = null;
                string? toolOutput = null;
                string? spawnTask = null;
                string? childThreadId = null;

                var eventTypeForLifecycle = topType ?? type;
                var itemType = String(eventObject, "type") ?? type;
                var isToolItem = IsToolType(itemType);
                if (eventTypeForLifecycle is "item.started" or "item.completed" && isToolItem)
                {
                    toolId = String(eventObject, "id") ?? String(eventObject, "call_id");
                    toolName = String(eventObject, "name") ?? String(eventObject, "tool") ?? itemType;
                    toolPhase = eventTypeForLifecycle == "item.started" ? "started" : "completed";
                    toolArguments = ExtractArguments(eventObject);
                    toolOutput = ExtractOutput(eventObject);
                }
                else if (topType == "response_item" || type is "function_call" or "mcp_tool_call" or "custom_tool_call" or "local_shell_call" or "command_execution")
                {
                    if (itemType is "function_call_output" or "custom_tool_call_output")
                    {
                        toolId = String(eventObject, "call_id") ?? String(eventObject, "id");
                        toolPhase = "output";
                        toolOutput = ExtractOutput(eventObject);
                    }
                    else if (IsToolType(itemType))
                    {
                        toolId = String(eventObject, "call_id") ?? String(eventObject, "id");
                        toolName = String(eventObject, "name") ?? String(eventObject, "tool") ?? itemType;
                        toolPhase = "call";
                        toolArguments = ExtractArguments(eventObject);
                        toolOutput = ExtractOutput(eventObject);
                    }
                }

                if (toolName?.Contains("spawn_agent", StringComparison.OrdinalIgnoreCase) == true)
                {
                    spawnTask = ExtractSpawnTask(toolArguments);
                    childThreadId = ExtractSpawnChildThreadId(eventObject);
                }
                else if (itemType is "function_call_output" or "custom_tool_call_output")
                {
                    childThreadId = ExtractSpawnChildThreadId(eventObject);
                }

                var usage = ExtractUsage(root, payload, out var cumulative);

                rollout.Events.Add(new CodexEvent
                {
                    Ordinal = ordinal,
                    Timestamp = timestamp,
                    Type = type,
                    Role = role,
                    Text = text,
                    ToolId = toolId,
                    ToolName = toolName,
                    ToolPhase = toolPhase,
                    ToolArguments = toolArguments,
                    ToolOutput = toolOutput,
                    ExitCode = FindInt(eventObject, "exit_code", "exitCode"),
                    Status = FindString(eventObject, "status", "outcome"),
                    Usage = usage,
                    UsageIsCumulative = cumulative,
                    ChildThreadId = childThreadId,
                    SpawnTask = spawnTask
                });

                if (metadataOnly && !string.IsNullOrWhiteSpace(rollout.ThreadId) && rollout.Cwd is not null && ordinal >= 16)
                    break;
            }
            catch (JsonException)
            {
                if (!isLastLine)
                {
                    rollout.Diagnostics.Add(new Diagnostic
                    {
                        Severity = "warning",
                        Category = "host-trace",
                        Code = "malformed_rollout_line",
                        Message = $"Malformed JSONL line {ordinal} in {Path.GetFileName(path)}."
                    });
                }
            }
        }

        CorrelateSpawns(rollout);
        return rollout;
    }

    private static void CorrelateSpawns(CodexRollout rollout)
    {
        var spawnByCall = new Dictionary<string, SpawnRecord>(StringComparer.Ordinal);
        foreach (var e in rollout.Events)
        {
            if (e.ToolId is null)
                continue;

            if (e.ToolName?.Contains("spawn_agent", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (!spawnByCall.TryGetValue(e.ToolId, out var record))
                {
                    record = new SpawnRecord
                    {
                        CallId = e.ToolId,
                        Task = e.SpawnTask,
                        Timestamp = e.Timestamp
                    };
                    spawnByCall[e.ToolId] = record;
                    rollout.SpawnRecords[e.ToolId] = record;
                }
                else if (record.Task is null && e.SpawnTask is not null)
                {
                    record.Task = e.SpawnTask;
                }

                var id = e.ChildThreadId ?? ExtractThreadId(e.ToolOutput);
                if (!string.IsNullOrWhiteSpace(id))
                {
                    record.ChildThreadId = id;
                    e.ChildThreadId = id;
                }
            }
            else if (e.ToolPhase == "output" && spawnByCall.TryGetValue(e.ToolId, out var record))
            {
                var id = e.ChildThreadId ?? ExtractThreadId(e.ToolOutput);
                if (!string.IsNullOrWhiteSpace(id))
                {
                    record.ChildThreadId = id;
                    e.ChildThreadId = id;
                }
            }
        }
    }

    public static string? ExtractThreadId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        return ThreadIdRegex.Match(text).Success ? ThreadIdRegex.Match(text).Value : null;
    }

    private static TokenMetrics? ExtractUsage(JsonElement root, JsonElement payload, out bool cumulative)
    {
        cumulative = false;
        JsonElement usage;

        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("info", out var info) &&
            info.ValueKind == JsonValueKind.Object)
        {
            if (info.TryGetProperty("total_token_usage", out usage) && usage.ValueKind == JsonValueKind.Object)
            {
                cumulative = true;
                return ParseUsage(usage);
            }

            if (info.TryGetProperty("last_token_usage", out usage) && usage.ValueKind == JsonValueKind.Object)
                return ParseUsage(usage);
        }

        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("usage", out usage) &&
            usage.ValueKind == JsonValueKind.Object)
        {
            cumulative = FindString(payload, "usage_scope", "usageScope") == "cumulative";
            return ParseUsage(usage);
        }

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("usage", out usage) &&
            usage.ValueKind == JsonValueKind.Object)
        {
            cumulative = FindString(root, "usage_scope", "usageScope") == "cumulative";
            return ParseUsage(usage);
        }

        return null;
    }

    private static TokenMetrics ParseUsage(JsonElement usage)
    {
        var input = FindLong(usage, "input_tokens", "inputTokens");
        var cached = FindLong(usage, "cached_input_tokens", "cachedInputTokens");
        var output = FindLong(usage, "output_tokens", "outputTokens");
        if (cached is > 0 && input is not null && cached > input)
            cached = null;
        return new TokenMetrics
        {
            InputTokens = input,
            CachedInputTokens = cached,
            NewInputTokens = input is not null && cached is not null && input >= cached ? input - cached : null,
            OutputTokens = output
        };
    }

    private static bool IsToolType(string? type) =>
        type is "function_call" or "mcp_tool_call" or "collab_tool_call" or "custom_tool_call"
            or "local_shell_call" or "local_shell" or "shell_command" or "exec_command"
            or "write_stdin" or "command_execution";

    private static string? ExtractSpawnTask(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(arguments);
            return FindString(doc.RootElement, "message", "task", "prompt", "input");
        }
        catch (JsonException)
        {
            return arguments;
        }
    }

    private static string? ExtractArguments(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var name in new[] { "arguments", "prompt", "message", "task", "input", "command", "cmd" })
        {
            if (!element.TryGetProperty(name, out var value) ||
                value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                continue;
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        }
        return null;
    }

    private static string? ExtractSpawnChildThreadId(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
            return ExtractThreadId(element.GetString());

        if (element.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var name in new[] { "receiver_thread_ids", "receiverThreadIds" })
        {
            if (!element.TryGetProperty(name, out var ids))
                continue;

            if (ids.ValueKind == JsonValueKind.Array)
            {
                foreach (var id in ids.EnumerateArray())
                {
                    if (id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString()))
                        return id.GetString();
                }
            }
            else if (ids.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(ids.GetString()))
            {
                return ids.GetString();
            }
        }

        foreach (var name in new[] { "child_thread_id", "childThreadId", "receiver_thread_id", "receiverThreadId" })
        {
            if (element.TryGetProperty(name, out var id) &&
                id.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(id.GetString()))
                return id.GetString();
        }

        foreach (var name in new[] { "output", "result", "content" })
        {
            if (element.TryGetProperty(name, out var nested))
            {
                var id = ExtractSpawnChildThreadId(nested);
                if (!string.IsNullOrWhiteSpace(id))
                    return id;
            }
        }

        return null;
    }

    private static string? ExtractOutput(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var name in new[] { "aggregated_output", "output", "result", "content" })
        {
            if (!element.TryGetProperty(name, out var value))
                continue;
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        }
        return null;
    }

    private static string? ExtractText(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return element.ValueKind == JsonValueKind.String ? element.GetString() : null;

        foreach (var name in new[] { "text", "message" })
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }

        if (element.TryGetProperty("content", out var content))
        {
            if (content.ValueKind == JsonValueKind.String)
                return content.GetString();
            if (content.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var part in content.EnumerateArray())
                {
                    if (part.ValueKind == JsonValueKind.String)
                        parts.Add(part.GetString()!);
                    else if (part.ValueKind == JsonValueKind.Object)
                    {
                        var text = FindString(part, "text", "input_text", "output_text");
                        if (!string.IsNullOrWhiteSpace(text))
                            parts.Add(text);
                    }
                }
                return parts.Count == 0 ? null : string.Join("\n", parts);
            }
        }

        return null;
    }

    private static DateTimeOffset? Timestamp(JsonElement element)
    {
        var value = String(element, "timestamp");
        return value is not null && DateTimeOffset.TryParse(value, out var result) ? result : null;
    }

    private static JsonElement? Object(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? FindString(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }
        return null;
    }

    private static int? FindInt(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result))
                return result;
        }
        return null;
    }

    private static long? FindLong(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.TryGetInt64(out var result))
                return result;
        }
        return null;
    }
}

public sealed class CodexStateDbReader
{
    public CodexStateSnapshot Read(string codexHome)
    {
        var snapshot = new CodexStateSnapshot();
        var files = Directory.Exists(codexHome)
            ? Directory.EnumerateFiles(codexHome, "state_*.sqlite", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToArray()
            : [];

        if (files.Length == 0)
            return snapshot;

        foreach (var file in files)
        {
            try
            {
                var builder = new SqliteConnectionStringBuilder
                {
                    DataSource = file,
                    Mode = SqliteOpenMode.ReadOnly
                };
                using var connection = new SqliteConnection(builder.ToString());
                connection.Open();
                snapshot.DatabaseAvailable = true;

                var tables = ReadTables(connection);
                if (tables.Contains("thread_spawn_edges", StringComparer.OrdinalIgnoreCase))
                {
                    snapshot.SpawnEdgesAvailable = ReadSpawnEdges(connection, snapshot);
                }

                if (tables.Contains("threads", StringComparer.OrdinalIgnoreCase))
                    ReadThreadIndex(connection, snapshot);

                return snapshot;
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
            {
                snapshot.Diagnostics.Add(new Diagnostic
                {
                    Severity = "warning",
                    Category = "reporter",
                    Code = "state_db_unavailable",
                    Message = $"Could not read {Path.GetFileName(file)} read-only: {ex.Message}"
                });
            }
        }

        return snapshot;
    }

    private static HashSet<string> ReadTables(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
        using var reader = command.ExecuteReader();
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
            result.Add(reader.GetString(0));
        return result;
    }

    private static bool ReadSpawnEdges(SqliteConnection connection, CodexStateSnapshot snapshot)
    {
        var columns = ReadColumns(connection, "thread_spawn_edges");
        var parent = Pick(columns, "parent_thread_id", "parent_id", "parent_thread");
        var child = Pick(columns, "child_thread_id", "child_id", "child_thread");
        if (parent is null || child is null)
            return false;

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Q(parent)}, {Q(child)} FROM {Q("thread_spawn_edges")}";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1))
                continue;
            var p = reader.GetValue(0).ToString();
            var c = reader.GetValue(1).ToString();
            if (!string.IsNullOrWhiteSpace(p) && !string.IsNullOrWhiteSpace(c))
                snapshot.ParentByChild[c!] = p!;
        }
        return true;
    }

    private static void ReadThreadIndex(SqliteConnection connection, CodexStateSnapshot snapshot)
    {
        var columns = ReadColumns(connection, "threads");
        var id = Pick(columns, "id", "thread_id");
        var rollout = Pick(columns, "rollout_path", "rollout", "path");
        if (id is null || rollout is null)
            return;

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Q(id)}, {Q(rollout)} FROM {Q("threads")}";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1))
                continue;
            var thread = reader.GetValue(0).ToString();
            var path = reader.GetValue(1).ToString();
            if (!string.IsNullOrWhiteSpace(thread) && !string.IsNullOrWhiteSpace(path))
                snapshot.RolloutByThread[thread!] = path!;
        }
    }

    private static HashSet<string> ReadColumns(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({Q(table)})";
        using var reader = command.ExecuteReader();
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
            result.Add(reader.GetString(1));
        return result;
    }

    private static string? Pick(HashSet<string> columns, params string[] candidates) =>
        candidates.FirstOrDefault(columns.Contains);

    private static string Q(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
}

public sealed class FactoryReportEngine
{
    private const string RunSkill = "idd-factory-run";
    private const string PlannerSkill = "idd-factory-decompose-task";
    private const string WorkerSkill = "idd-factory-execute-subtask";
    private readonly CodexRolloutReader _reader = new();

    public IReadOnlyList<FactoryRunReport> FindRuns(string repository, string codexHome, bool verbose = false)
    {
        var repo = NormalizePath(repository);
        var state = new CodexStateDbReader().Read(codexHome);
        var all = LoadRepositoryRollouts(repo, codexHome, state);
        var byId = all.Where(x => !string.IsNullOrWhiteSpace(x.ThreadId))
            .GroupBy(x => x.ThreadId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

        foreach (var child in all)
        {
            if (child.ParentThreadId is null && state.ParentByChild.TryGetValue(child.ThreadId, out var parent))
                child.ParentThreadId = parent;
        }

        foreach (var spawningRollout in all)
        {
            foreach (var spawn in spawningRollout.SpawnRecords.Values)
            {
                if (string.IsNullOrWhiteSpace(spawn.ChildThreadId) ||
                    !byId.TryGetValue(spawn.ChildThreadId, out var child))
                    continue;

                child.ParentThreadId ??= spawningRollout.ThreadId;
            }
        }

        var roots = all.Where(x => string.IsNullOrWhiteSpace(x.ParentThreadId)).ToArray();
        var reports = new List<FactoryRunReport>();

        foreach (var root in roots)
        {
            var starts = DetectRunStarts(root);
            for (var i = 0; i < starts.Count; i++)
            {
                var start = starts[i];
                var nextStartOrdinal = i + 1 < starts.Count ? starts[i + 1].Ordinal : long.MaxValue;
                reports.Add(BuildReport(repo, codexHome, root, i + 1, start, nextStartOrdinal, all, byId, state, verbose));
            }
        }

        return reports
            .OrderBy(x => x.Run.StartedAt ?? DateTimeOffset.MinValue)
            .ThenBy(x => x.Run.RootThreadId, StringComparer.Ordinal)
            .ThenBy(x => x.Run.RunIndex)
            .ToArray();
    }

    private List<CodexRollout> LoadRepositoryRollouts(string repo, string codexHome, CodexStateSnapshot state)
    {
        var paths = EnumerateRollouts(codexHome).Distinct(PathComparer()).ToArray();
        var candidates = new List<string>();

        foreach (var path in paths)
        {
            try
            {
                var meta = _reader.Read(path, metadataOnly: true);
                if (PathBelongsToRepository(meta.Cwd, repo))
                    candidates.Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                state.Diagnostics.Add(new Diagnostic
                {
                    Severity = "warning",
                    Category = "reporter",
                    Code = "rollout_metadata_unavailable",
                    Message = $"Could not read rollout metadata: {Path.GetFileName(path)}."
                });
            }
        }

        var result = new List<CodexRollout>();
        foreach (var path in candidates)
        {
            try
            {
                result.Add(_reader.Read(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                state.Diagnostics.Add(new Diagnostic
                {
                    Severity = "warning",
                    Category = "reporter",
                    Code = "missing_rollout",
                    Message = $"Could not read rollout {Path.GetFileName(path)}: {ex.Message}"
                });
            }
        }
        return result;
    }

    private static IEnumerable<string> EnumerateRollouts(string codexHome)
    {
        foreach (var dir in new[] { "sessions", "archived_sessions" })
        {
            var root = Path.Combine(codexHome, dir);
            if (!Directory.Exists(root))
                continue;
            foreach (var path in Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories))
                yield return path;
        }
    }

    private static List<CodexEvent> DetectRunStarts(CodexRollout root)
    {
        var explicitStarts = root.Events
            .Where(x => string.Equals(x.Role, "user", StringComparison.OrdinalIgnoreCase) && x.Contains(RunSkill))
            .ToList();
        if (explicitStarts.Count > 0)
            return explicitStarts;

        var fallback = root.Events.FirstOrDefault(x =>
            x.Contains(PlannerSkill) || x.Contains(WorkerSkill) ||
            x.ToolName?.Contains("spawn_agent", StringComparison.OrdinalIgnoreCase) == true &&
            (x.SpawnTask?.Contains(PlannerSkill, StringComparison.OrdinalIgnoreCase) == true ||
             x.SpawnTask?.Contains(WorkerSkill, StringComparison.OrdinalIgnoreCase) == true));
        return fallback is null ? [] : [fallback];
    }

    private FactoryRunReport BuildReport(
        string repository,
        string codexHome,
        CodexRollout root,
        int runIndex,
        CodexEvent start,
        long nextStartOrdinal,
        IReadOnlyList<CodexRollout> all,
        IReadOnlyDictionary<string, CodexRollout> byId,
        CodexStateSnapshot state,
        bool verbose)
    {
        var segmentRootEvents = root.Events
            .Where(x => x.Ordinal >= start.Ordinal && x.Ordinal < nextStartOrdinal)
            .ToArray();

        var descendants = SelectDescendants(root, start.Timestamp, nextStartOrdinal == long.MaxValue ? null :
            root.Events.FirstOrDefault(x => x.Ordinal == nextStartOrdinal)?.Timestamp, all, byId, state);

        var agents = BuildAgents(root, segmentRootEvents, descendants, state, verbose);
        var plannerAgents = agents.Where(x => x.Role == "planner").OrderBy(x => x.StartedAt).ToArray();
        var workerAgents = agents.Where(x => x.Role == "worker").OrderBy(x => x.StartedAt).ToArray();
        var latestPlanner = plannerAgents.LastOrDefault();

        var resultEvidence = new List<string>();
        var result = "unknown";
        string? resultReason = null;
        DateTimeOffset? end = null;
        string? endEvidence = null;
        var endConfidence = "unknown";

        var structuredResult = FindStructuredFactoryResult(segmentRootEvents);
        if (structuredResult is not null)
        {
            result = structuredResult.Status;
            resultReason = structuredResult.Reason;
            resultEvidence.Add("structured_final_response");
            end = structuredResult.Event.Timestamp;
            endEvidence = $"structured final response status={structuredResult.Status.ToUpperInvariant()}";
            endConfidence = "exact";
        }
        else if (latestPlanner?.PlannerOutcome == "Question")
        {
            result = "question";
            resultEvidence.Add("planner_question");
            end = latestPlanner.FinishedAt;
            endEvidence = "planner returned # Question";
            endConfidence = "derived";
        }
        else
        {
            var completion = segmentRootEvents.LastOrDefault(IsFactoryCompletion);
            if (completion is not null)
            {
                result = "completed";
                resultEvidence.Add("factory_completion");
                if (latestPlanner?.PlannerOutcome == "Done")
                    resultEvidence.Insert(0, "planner_done");
                end = completion.Timestamp;
                endEvidence = "explicit Factory completion event";
                endConfidence = "exact";
            }
            else if (latestPlanner?.PlannerOutcome == "Done")
            {
                var verification = segmentRootEvents
                    .Where(x => IsCommand(x) && x.Timestamp >= latestPlanner.FinishedAt)
                    .LastOrDefault();
                if (verification is not null && verification.ExitCode == 0)
                {
                    result = "completed";
                    resultEvidence.Add("planner_done");
                    resultEvidence.Add("verification_success");
                    end = verification.Timestamp;
                    endEvidence = "planner # Done followed by successful verification command";
                    endConfidence = "derived";
                }
            }

            var blocked = segmentRootEvents.LastOrDefault(x =>
                x.Text?.Contains("Factory blocked", StringComparison.OrdinalIgnoreCase) == true);
            if (blocked is not null)
            {
                result = "blocked";
                resultEvidence.Clear();
                resultEvidence.Add("explicit_blocked");
                end = blocked.Timestamp;
                endEvidence = "explicit Factory blocked event";
                endConfidence = "exact";
            }

            var interrupted = segmentRootEvents.LastOrDefault(IsInterruption);
            if (interrupted is not null)
            {
                result = "interrupted";
                resultEvidence.Clear();
                resultEvidence.Add("host_interruption");
                end = interrupted.Timestamp;
                endEvidence = "host interruption/cancellation evidence";
                endConfidence = "exact";
            }
        }

        if (result == "unknown")
        {
            var incompleteFactoryAgent = agents.FirstOrDefault(x =>
                x.Role is "planner" or "worker" &&
                byId.TryGetValue(x.ThreadId, out var childRollout) &&
                !HasTerminalEvidence(childRollout));
            if (incompleteFactoryAgent is not null)
            {
                result = "interrupted";
                resultEvidence.Add("child_without_completion");
                end = incompleteFactoryAgent.FinishedAt;
                endEvidence = $"{incompleteFactoryAgent.Role} child has no terminal completion evidence";
                endConfidence = "derived";
            }
        }

        if (end is null)
        {
            var owned = segmentRootEvents.Where(IsFactoryOwned).Select(x => x.Timestamp)
                .Concat(descendants.Select(x => x.FinishedAt))
                .Where(x => x is not null)
                .Max();
            if (owned is not null)
            {
                end = owned;
                endEvidence = "last deterministic Factory-owned event";
                endConfidence = "derived";
            }
        }

        if (end is not null)
        {
            agents.RemoveAll(x => x.StartedAt is not null && x.StartedAt > end);
            plannerAgents = agents.Where(x => x.Role == "planner").OrderBy(x => x.StartedAt).ToArray();
            workerAgents = agents.Where(x => x.Role == "worker").OrderBy(x => x.StartedAt).ToArray();
        }

        var diagnostics = new List<Diagnostic>();
        diagnostics.AddRange(state.Diagnostics);
        diagnostics.AddRange(root.Diagnostics);
        diagnostics.AddRange(descendants.SelectMany(x => x.Diagnostics));

        foreach (var rollout in descendants)
        {
            if (!HasTerminalEvidence(rollout))
            {
                diagnostics.Add(new Diagnostic
                {
                    Severity = "warning",
                    Category = "host-trace",
                    Code = "child_without_completion",
                    Message = $"Child thread {rollout.ThreadId} has no recognized completion event."
                });
            }
        }

        foreach (var unresolved in agents.Where(x => x.Role == "unknown"))
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = "info",
                Category = "reporter",
                Code = "agent_role_unresolved",
                Message = $"Could not determine the Factory role of child thread {unresolved.ThreadId} from explicit metadata or spawn instructions."
            });
        }

        foreach (var spawn in root.SpawnRecords.Values.Where(x =>
                     x.Timestamp >= start.Timestamp && (end is null || x.Timestamp <= end)))
        {
            if (spawn.ChildThreadId is not null && !byId.ContainsKey(spawn.ChildThreadId))
            {
                diagnostics.Add(new Diagnostic
                {
                    Severity = "warning",
                    Category = "host-trace",
                    Code = "spawn_without_discoverable_child",
                    Message = $"Spawned child {spawn.ChildThreadId} has no discoverable rollout."
                });
            }
        }

        var stateInfo = ReadFactoryProjectState(repository);
        if (result == "completed" && stateInfo.CurrentRequestPresent)
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = "warning",
                Category = "factory",
                Code = "completed_with_active_request",
                Message = "Factory result is COMPLETED but .idd/factory/current/request.md still exists."
            });
        }

        if (workerAgents.Length == 0 && !agents.Any(x => x.Role == "unknown"))
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = "warning",
                Category = "factory",
                Code = "factory_run_without_worker",
                Message = "Factory run contains no recognized worker agent."
            });
        }

        if (endConfidence != "exact")
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = "info",
                Category = "reporter",
                Code = "factory_boundary_not_exact",
                Message = "Factory end boundary was derived rather than read from an explicit terminal event."
            });
        }

        var rootAgent = BuildRootAgent(root, start.Timestamp, end, segmentRootEvents, verbose);
        agents.Insert(0, rootAgent);

        AssignSequences(agents);
        var tasks = workerAgents.Select((x, index) =>
        {
            var rollout = byId.GetValueOrDefault(x.ThreadId);
            var status = rollout is null
                ? "unknown"
                : HasThreadFailure(rollout)
                    ? "failed"
                    : HasTerminalEvidence(rollout)
                        ? "completed"
                        : "interrupted";
            return new TaskReport
            {
                Number = index + 1,
                AgentThreadId = x.ThreadId,
                Text = x.Task,
                Status = status,
                DurationMilliseconds = x.DurationMilliseconds
            };
        }).ToList();

        var rootReportedTokens = ComputeRootReportedTokens(root.Events, end);
        var tokenMetrics = AggregateTokens(agents, out var tokenMethod, out var tokenComplete);
        if (rootReportedTokens.Available && agents.Any(x => x.Role != "root" && x.Tokens.Available))
        {
            tokenMetrics = new TokenMetrics();
            tokenMethod = "overlap-unknown";
            tokenComplete = false;
        }

        var tokenStatus = tokenMethod == "overlap-unknown"
            ? "overlap-unknown"
            : tokenComplete ? "complete" : "unavailable";
        if (tokenStatus == "overlap-unknown")
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = "info",
                Category = "reporter",
                Code = "token_aggregation_overlap_unknown",
                Message = "Root and child token accounting may overlap; aggregate Total is intentionally unavailable."
            });
        }

        var tools = AggregateTools(agents);
        foreach (var failed in tools.FailedCommandItems)
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = "warning",
                Category = "factory",
                Code = "failed_command",
                Message = $"Command failed in {failed.Agent}: exit/status {failed.ExitCode?.ToString() ?? failed.Status ?? "unknown"}."
            });
        }

        var timeline = BuildTimeline(start, end, result, agents, tools);

        var maxDepth = ComputeMaximumDepth(agents);
        var otherAgents = agents.Count(x => x.Role is "subagent" or "unknown" && x.ThreadId != root.ThreadId);
        var plannerWall = SumDuration(agents.Where(x => x.Role == "planner"));
        var workerWall = SumDuration(agents.Where(x => x.Role == "worker"));
        var sumAgent = SumDuration(agents.Where(x => x.ThreadId != root.ThreadId));

        var capabilities = new CodexCapabilities
        {
            StateDb = state.DatabaseAvailable,
            SpawnEdges = state.SpawnEdgesAvailable,
            AgentRole = descendants.Any(x => !string.IsNullOrWhiteSpace(x.AgentRoleHint)),
            TokenAccounting = agents.Any(x => x.Tokens.Available) ? tokenMethod : "unavailable"
        };

        return new FactoryRunReport
        {
            Repository = repository,
            Host = new HostInfo
            {
                CodexHome = verbose ? codexHome : null,
                Capabilities = capabilities
            },
            Run = new RunInfo
            {
                RootThreadId = root.ThreadId,
                RunIndex = runIndex,
                StartedAt = start.Timestamp,
                FinishedAt = end,
                StartBoundary = new BoundaryInfo
                {
                    Confidence = start.Role == "user" && start.Contains(RunSkill) ? "exact" : "derived",
                    Evidence = start.Role == "user" && start.Contains(RunSkill)
                        ? "explicit idd-factory-run invocation/reference"
                        : "Factory planner/worker evidence"
                },
                EndBoundary = new BoundaryInfo { Confidence = endConfidence, Evidence = endEvidence },
                Result = result,
                Reason = resultReason,
                ResultEvidence = resultEvidence
            },
            Agents = agents,
            Tasks = tasks,
            Metrics = new ReportMetrics
            {
                Tokens = tokenMetrics,
                RootReportedTokens = rootReportedTokens,
                TokenAggregationMethod = tokenMethod,
                TokenAggregationStatus = tokenStatus,
                TokenAggregationComplete = tokenComplete,
                Tools = tools,
                DurationMilliseconds = start.Timestamp is not null && end is not null
                    ? (long)(end.Value - start.Timestamp.Value).TotalMilliseconds
                    : null,
                TotalPlannerWallTimeMilliseconds = plannerWall,
                TotalWorkerWallTimeMilliseconds = workerWall,
                SumAgentDurationsMilliseconds = sumAgent,
                PlannerInvocations = plannerAgents.Length,
                WorkerInvocations = workerAgents.Length,
                OtherChildAgents = otherAgents,
                MaximumDepth = maxDepth
            },
            Timeline = timeline,
            Diagnostics = diagnostics
                .GroupBy(x => (x.Category, x.Code, x.Message))
                .Select(x => x.First())
                .ToList(),
            FactoryProjectState = stateInfo
        };
    }

    private static List<CodexRollout> SelectDescendants(
        CodexRollout root,
        DateTimeOffset? runStart,
        DateTimeOffset? nextRunStart,
        IReadOnlyList<CodexRollout> all,
        IReadOnlyDictionary<string, CodexRollout> byId,
        CodexStateSnapshot state)
    {
        var parentMap = new Dictionary<string, string>(state.ParentByChild, StringComparer.Ordinal);
        foreach (var rollout in all)
        {
            if (!string.IsNullOrWhiteSpace(rollout.ParentThreadId))
                parentMap[rollout.ThreadId] = rollout.ParentThreadId!;
        }
        foreach (var spawningRollout in all)
        {
            foreach (var spawn in spawningRollout.SpawnRecords.Values)
            {
                if (!string.IsNullOrWhiteSpace(spawn.ChildThreadId))
                    parentMap[spawn.ChildThreadId!] = spawningRollout.ThreadId;
            }
        }

        var result = new List<CodexRollout>();
        var queue = new Queue<string>();
        queue.Enqueue(root.ThreadId);
        var seen = new HashSet<string>(StringComparer.Ordinal) { root.ThreadId };

        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            foreach (var edge in parentMap.Where(x => x.Value == parent))
            {
                if (!seen.Add(edge.Key))
                    continue;
                queue.Enqueue(edge.Key);
                if (!byId.TryGetValue(edge.Key, out var child))
                    continue;
                child.ParentThreadId ??= parent;
                var childActivityStart = ActivityStartedAt(child);
                if (runStart is not null && childActivityStart is not null && childActivityStart < runStart)
                    continue;
                if (nextRunStart is not null && childActivityStart is not null && childActivityStart >= nextRunStart)
                    continue;
                result.Add(child);
            }
        }

        return result;
    }

    private static DateTimeOffset? ActivityStartedAt(CodexRollout rollout) =>
        rollout.Events.FirstOrDefault(x => x.Type != "session_meta")?.Timestamp ?? rollout.StartedAt;

    private static List<AgentReport> BuildAgents(
        CodexRollout root,
        IReadOnlyList<CodexEvent> rootEvents,
        IReadOnlyList<CodexRollout> descendants,
        CodexStateSnapshot state,
        bool verbose)
    {
        var agents = new List<AgentReport>();
        var spawningRollouts = new[] { root }.Concat(descendants).ToArray();

        foreach (var child in descendants.OrderBy(x => x.StartedAt))
        {
            var spawn = FindSpawnRecord(spawningRollouts, child.ThreadId);
            var role = ClassifyRole(root, child, spawn);
            var task = spawn?.Task is { } spawnTask ? Bound(spawnTask, 1000) : null;
            var outcome = role == "planner" ? PlannerOutcome(child) : null;
            agents.Add(new AgentReport
            {
                ThreadId = child.ThreadId,
                ParentThreadId = child.ParentThreadId ?? state.ParentByChild.GetValueOrDefault(child.ThreadId),
                Role = role,
                StartedAt = ActivityStartedAt(child),
                FinishedAt = child.FinishedAt,
                Task = task,
                PlannerOutcome = outcome,
                Tokens = ComputeWholeThreadTokens(child),
                Tools = AnalyzeTools(child.Events, role),
                RolloutPath = verbose ? child.Path : null
            });
        }
        return agents;
    }

    private static AgentReport BuildRootAgent(
        CodexRollout root,
        DateTimeOffset? start,
        DateTimeOffset? end,
        IReadOnlyList<CodexEvent> segmentEvents,
        bool verbose)
    {
        return new AgentReport
        {
            ThreadId = root.ThreadId,
            Role = "root",
            StartedAt = start,
            FinishedAt = end,
            Tokens = ComputeSegmentTokens(root.Events, start, end),
            Tools = AnalyzeTools(
                segmentEvents.Where(x =>
                    (start is null || x.Timestamp is null || x.Timestamp >= start) &&
                    (end is null || x.Timestamp is null || x.Timestamp <= end)),
                "Factory root"),
            RolloutPath = verbose ? root.Path : null
        };
    }

    private static string ClassifyRole(CodexRollout root, CodexRollout child, SpawnRecord? spawn)
    {
        if (child.AgentRoleHint?.Contains("planner", StringComparison.OrdinalIgnoreCase) == true)
            return "planner";
        if (child.AgentRoleHint?.Contains("worker", StringComparison.OrdinalIgnoreCase) == true)
            return "worker";

        var spawnTask = spawn?.Task;
        if (spawnTask?.Contains(PlannerSkill, StringComparison.OrdinalIgnoreCase) == true)
            return "planner";
        if (spawnTask?.Contains(WorkerSkill, StringComparison.OrdinalIgnoreCase) == true)
            return "worker";

        var initialInstructions = string.Join("\n",
            child.Events
                .Where(x => string.Equals(x.Role, "user", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(x.Role, "system", StringComparison.OrdinalIgnoreCase))
                .Take(4)
                .Select(x => x.Text ?? ""));

        var mentionsPlanner = initialInstructions.Contains(PlannerSkill, StringComparison.OrdinalIgnoreCase);
        var mentionsWorker = initialInstructions.Contains(WorkerSkill, StringComparison.OrdinalIgnoreCase);
        if (mentionsPlanner && !mentionsWorker)
            return "planner";
        if (mentionsWorker && !mentionsPlanner)
            return "worker";

        if (child.ParentThreadId is not null && child.ParentThreadId != root.ThreadId)
            return "subagent";
        return "unknown";
    }

    private static SpawnRecord? FindSpawnRecord(IEnumerable<CodexRollout> rollouts, string childId)
    {
        foreach (var rollout in rollouts)
        {
            var record = rollout.SpawnRecords.Values.FirstOrDefault(
                x => string.Equals(x.ChildThreadId, childId, StringComparison.Ordinal));
            if (record is not null)
                return record;
        }
        return null;
    }

    private static string PlannerOutcome(CodexRollout rollout)
    {
        foreach (var text in rollout.Events.Select(x => x.Text).Where(x => !string.IsNullOrWhiteSpace(x)).Reverse())
        {
            if (Regex.IsMatch(text!, @"(?m)^#\s*Question\s*$", RegexOptions.IgnoreCase))
                return "Question";
            if (Regex.IsMatch(text!, @"(?m)^#\s*Done\s*$", RegexOptions.IgnoreCase))
                return "Done";
            if (Regex.IsMatch(text!, @"(?m)^#\s*Task\s*$", RegexOptions.IgnoreCase))
                return "Tasks";
        }
        return "Unknown";
    }

    private static TokenMetrics ComputeWholeThreadTokens(CodexRollout rollout)
    {
        var usageEvents = rollout.Events.Where(x => x.Usage is not null).ToArray();
        var cumulative = usageEvents.Where(x => x.UsageIsCumulative).LastOrDefault();
        if (cumulative?.Usage is not null)
            return cumulative.Usage;

        var perTurn = usageEvents.Where(x => !x.UsageIsCumulative).Select(x => x.Usage!).ToArray();
        return perTurn.Length == 0 ? new TokenMetrics() : TokenMetrics.Sum(perTurn);
    }

    private static TokenMetrics ComputeSegmentTokens(
        IReadOnlyList<CodexEvent> events,
        DateTimeOffset? start,
        DateTimeOffset? end)
    {
        if (start is null || end is null)
            return new TokenMetrics();

        var cumulative = events.Where(x => x.UsageIsCumulative && x.Usage is not null).ToArray();
        if (cumulative.Length > 0)
        {
            var before = cumulative.LastOrDefault(x => x.Timestamp < start);
            var finish = cumulative.LastOrDefault(x => x.Timestamp <= end);
            if (before?.Usage is not null && finish?.Usage is not null)
                return TokenMetrics.Subtract(finish.Usage, before.Usage);
            return new TokenMetrics();
        }

        var perTurn = events.Where(x =>
                !x.UsageIsCumulative && x.Usage is not null &&
                x.Timestamp >= start && x.Timestamp <= end)
            .Select(x => x.Usage!)
            .ToArray();
        return perTurn.Length == 0 ? new TokenMetrics() : TokenMetrics.Sum(perTurn);
    }

    private static TokenMetrics ComputeRootReportedTokens(
        IReadOnlyList<CodexEvent> events,
        DateTimeOffset? end)
    {
        var eligible = events
            .Where(x => x.Usage is not null && (end is null || x.Timestamp is null || x.Timestamp <= end))
            .ToArray();

        var cumulative = eligible.LastOrDefault(x => x.UsageIsCumulative);
        if (cumulative?.Usage is not null)
            return cumulative.Usage;

        var perTurn = eligible.Where(x => !x.UsageIsCumulative).Select(x => x.Usage!).ToArray();
        return perTurn.Length == 0 ? new TokenMetrics() : TokenMetrics.Sum(perTurn);
    }

    private static ToolMetrics AnalyzeTools(IEnumerable<CodexEvent> source, string agent)
    {
        var events = source.OrderBy(x => x.Ordinal).ToArray();
        var calls = new Dictionary<string, ToolCallState>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        long batches = 0;
        var anonymous = 0;

        foreach (var e in events)
        {
            if (e.ToolPhase is null)
                continue;

            var id = e.ToolId ?? $"anonymous-{++anonymous}";
            if (!calls.TryGetValue(id, out var call))
            {
                call = new ToolCallState { Id = id };
                calls[id] = call;
            }

            if (!string.IsNullOrWhiteSpace(e.ToolName))
                call.Name = e.ToolName;
            if (!string.IsNullOrWhiteSpace(e.ToolArguments))
                call.Arguments = e.ToolArguments;
            if (!string.IsNullOrWhiteSpace(e.ToolOutput))
                call.Output = e.ToolOutput;
            call.Timestamp ??= e.Timestamp;
            call.Status = e.Status ?? call.Status;
            call.ExitCode = e.ExitCode ?? call.ExitCode;

            if (e.ToolPhase == "started")
            {
                if (active.Count == 0)
                    batches++;
                active.Add(id);
                call.HadLifecycle = true;
            }
            else if (e.ToolPhase == "completed")
            {
                active.Remove(id);
                call.HadLifecycle = true;
            }
        }

        var noLifecycle = calls.Values.Count(x => !x.HadLifecycle);
        batches += noLifecycle;

        var metrics = new ToolMetrics
        {
            ToolCalls = calls.Count,
            ToolBatches = batches
        };

        foreach (var call in calls.Values)
        {
            var name = call.Name ?? "";
            var outputChars = call.Output?.Length ?? 0;
            metrics.ToolOutputCharacters += outputChars;

            var failed = IsFailed(call);
            if (failed)
                metrics.FailedToolOutputCharacters += outputChars;

            if (IsCommandName(name))
            {
                metrics.Commands++;
                if (failed)
                {
                    metrics.FailedCommands++;
                    metrics.FailedCommandItems.Add(new FailedCommand
                    {
                        Agent = agent,
                        Timestamp = call.Timestamp,
                        ExitCode = call.ExitCode,
                        Status = call.Status,
                        Command = Bound(ExtractCommand(call.Arguments), 500)
                    });
                }
            }
            else if (name.Contains("spawn_agent", StringComparison.OrdinalIgnoreCase))
                metrics.SpawnAgentCalls++;
            else if (name.Contains("wait_agent", StringComparison.OrdinalIgnoreCase))
                metrics.WaitAgentCalls++;
            else if (name.Contains("search", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("find", StringComparison.OrdinalIgnoreCase))
                metrics.SearchOperations++;
            else if (name.Contains("file", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("read", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("write", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("patch", StringComparison.OrdinalIgnoreCase))
                metrics.FileOperations++;
            else
                metrics.OtherToolCalls++;
        }

        return metrics;
    }

    private static bool IsFailed(ToolCallState call) =>
        call.ExitCode is not null && call.ExitCode != 0 ||
        call.Status is not null && call.Status is "failed" or "error" or "declined";

    private static bool IsCommand(CodexEvent e) => IsCommandName(e.ToolName ?? "");

    private static bool IsCommandName(string name) =>
        name.Contains("exec_command", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("write_stdin", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("local_shell", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("command_execution", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractCommand(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments) ||
            string.Equals(arguments.Trim(), "null", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(arguments);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return arguments;

            foreach (var name in new[] { "cmd", "command", "input" })
            {
                if (doc.RootElement.TryGetProperty(name, out var value) &&
                    value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
                    return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
            }
        }
        catch (JsonException)
        {
        }
        return arguments;
    }

    private static StructuredFactoryResult? FindStructuredFactoryResult(IEnumerable<CodexEvent> events)
    {
        foreach (var e in events.Reverse())
        {
            if (TryParseStructuredFactoryResult(e, out var status, out var reason))
                return new StructuredFactoryResult(status, reason, e);
        }
        return null;
    }

    private static bool TryParseStructuredFactoryResult(
        CodexEvent e,
        out string status,
        out string? reason)
    {
        status = "";
        reason = null;

        if (!string.Equals(e.Role, "assistant", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(e.Text))
            return false;

        var text = e.Text.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = text.IndexOf('\n');
            var closingFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine >= 0 && closingFence > firstNewLine)
                text = text[(firstNewLine + 1)..closingFence].Trim();
        }

        if (!text.StartsWith("{", StringComparison.Ordinal) ||
            !text.EndsWith("}", StringComparison.Ordinal))
            return false;

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            string? rawStatus = null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name.Equals("status", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                    rawStatus = property.Value.GetString();
                else if (property.Name.Equals("reason", StringComparison.OrdinalIgnoreCase) &&
                         property.Value.ValueKind == JsonValueKind.String)
                    reason = property.Value.GetString();
            }

            status = rawStatus?.Trim().ToUpperInvariant() switch
            {
                "COMPLETED" => "completed",
                "INTERRUPTED" => "interrupted",
                "BLOCKED" => "blocked",
                "QUESTION" => "question",
                _ => ""
            };
            return status.Length > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsFactoryCompletion(CodexEvent e)
    {
        var text = e.Text;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        return text.Contains("Factory completed", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Factory run completed", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Factory is complete", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInterruption(CodexEvent e)
    {
        var type = e.Type;
        var status = e.Status;
        return type.Contains("cancel", StringComparison.OrdinalIgnoreCase) ||
               type.Contains("interrupt", StringComparison.OrdinalIgnoreCase) ||
               status is "cancelled" or "canceled" or "interrupted";
    }

    private static bool IsFactoryOwned(CodexEvent e) =>
        e.Contains(RunSkill) || e.Contains(PlannerSkill) || e.Contains(WorkerSkill) ||
        e.ToolName?.Contains("spawn_agent", StringComparison.OrdinalIgnoreCase) == true ||
        e.ToolName?.Contains("wait_agent", StringComparison.OrdinalIgnoreCase) == true ||
        IsFactoryCompletion(e) ||
        TryParseStructuredFactoryResult(e, out _, out _);

    private static bool HasTerminalEvidence(CodexRollout rollout) =>
        rollout.Events.Any(x =>
            x.Type is "turn.completed" or "turn_completed" or "task_complete" ||
            x.Type.Contains("completed", StringComparison.OrdinalIgnoreCase) ||
            x.Type.Contains("failed", StringComparison.OrdinalIgnoreCase)) ||
        rollout.Events.Any(x => string.Equals(x.Role, "assistant", StringComparison.OrdinalIgnoreCase) &&
                                !string.IsNullOrWhiteSpace(x.Text));

    private static bool HasThreadFailure(CodexRollout? rollout) =>
        rollout?.Events.Any(x =>
            x.Type.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            x.Status is "failed" or "error") == true;

    private static TokenMetrics AggregateTokens(
        IReadOnlyList<AgentReport> agents,
        out string method,
        out bool complete)
    {
        var distinct = agents.GroupBy(x => x.ThreadId, StringComparer.Ordinal).Select(x => x.First()).ToArray();
        if (distinct.Length == 0)
        {
            complete = false;
            method = "unavailable";
            return new TokenMetrics();
        }

        var root = distinct.FirstOrDefault(x => x.Role == "root");
        var children = distinct.Where(x => x.Role != "root").ToArray();
        if (root?.Tokens.Available == true && children.Any(x => x.Tokens.Available))
        {
            complete = false;
            method = "overlap-unknown";
            return new TokenMetrics();
        }

        complete = distinct.All(x => x.Tokens.Available);
        if (!complete)
        {
            method = "partial-distinct-thread-sum";
            return new TokenMetrics();
        }

        method = "sum-distinct-threads";
        return TokenMetrics.Sum(distinct.Select(x => x.Tokens));
    }

    private static ToolMetrics AggregateTools(IEnumerable<AgentReport> agents)
    {
        var total = new ToolMetrics();
        foreach (var x in agents)
        {
            total.ToolCalls += x.Tools.ToolCalls;
            total.ToolBatches += x.Tools.ToolBatches;
            total.Commands += x.Tools.Commands;
            total.FailedCommands += x.Tools.FailedCommands;
            total.FileOperations += x.Tools.FileOperations;
            total.SearchOperations += x.Tools.SearchOperations;
            total.SpawnAgentCalls += x.Tools.SpawnAgentCalls;
            total.WaitAgentCalls += x.Tools.WaitAgentCalls;
            total.OtherToolCalls += x.Tools.OtherToolCalls;
            total.ToolOutputCharacters += x.Tools.ToolOutputCharacters;
            total.FailedToolOutputCharacters += x.Tools.FailedToolOutputCharacters;
            total.FailedCommandItems.AddRange(x.Tools.FailedCommandItems);
        }
        return total;
    }

    private static List<TimelineEvent> BuildTimeline(
        CodexEvent start,
        DateTimeOffset? end,
        string result,
        IEnumerable<AgentReport> agents,
        ToolMetrics tools)
    {
        var events = new List<TimelineEvent>
        {
            new() { Timestamp = start.Timestamp, Ordinal = start.Ordinal, Kind = "factory-start", Text = "Factory run started" }
        };

        var ordinal = start.Ordinal + 1;
        foreach (var agent in agents.Where(x => x.Role != "root").OrderBy(x => x.StartedAt))
        {
            events.Add(new TimelineEvent
            {
                Timestamp = agent.StartedAt,
                Ordinal = ordinal++,
                Kind = $"{agent.Role}-start",
                Text = $"{DisplayRole(agent)} started"
            });
            if (agent.Role == "planner" && agent.PlannerOutcome is not null)
            {
                events.Add(new TimelineEvent
                {
                    Timestamp = agent.FinishedAt,
                    Ordinal = ordinal++,
                    Kind = "planner-outcome",
                    Text = $"{DisplayRole(agent)} -> {agent.PlannerOutcome}"
                });
            }
            else
            {
                events.Add(new TimelineEvent
                {
                    Timestamp = agent.FinishedAt,
                    Ordinal = ordinal++,
                    Kind = $"{agent.Role}-finish",
                    Text = $"{DisplayRole(agent)} completed"
                });
            }
        }

        foreach (var failed in tools.FailedCommandItems)
        {
            events.Add(new TimelineEvent
            {
                Timestamp = failed.Timestamp,
                Ordinal = ordinal++,
                Kind = "failed-command",
                Text = $"Failed command in {failed.Agent}"
            });
        }

        events.Add(new TimelineEvent
        {
            Timestamp = end,
            Ordinal = long.MaxValue,
            Kind = "factory-result",
            Text = $"Factory result: {result.ToUpperInvariant()}"
        });

        return events.OrderBy(x => x.Timestamp ?? DateTimeOffset.MaxValue).ThenBy(x => x.Ordinal).ToList();
    }

    private static void AssignSequences(List<AgentReport> agents)
    {
        foreach (var group in agents.Where(x => x.Role != "root").GroupBy(x => x.Role))
        {
            var n = 0;
            foreach (var agent in group.OrderBy(x => x.StartedAt))
                agent.Sequence = ++n;
        }
    }

    private static string DisplayRole(AgentReport agent) =>
        agent.Role == "root" ? "Factory root" :
        $"{char.ToUpperInvariant(agent.Role[0])}{agent.Role[1..]} #{agent.Sequence}";

    private static int ComputeMaximumDepth(IReadOnlyList<AgentReport> agents)
    {
        var map = agents.ToDictionary(x => x.ThreadId, StringComparer.Ordinal);
        var max = 0;
        foreach (var agent in agents)
        {
            var depth = 0;
            var current = agent;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (current.ParentThreadId is not null && seen.Add(current.ThreadId) &&
                   map.TryGetValue(current.ParentThreadId, out var parent))
            {
                depth++;
                current = parent;
            }
            max = Math.Max(max, depth);
        }
        return max;
    }

    private static long? SumDuration(IEnumerable<AgentReport> agents)
    {
        var values = agents.Select(x => x.DurationMilliseconds).Where(x => x is not null).Select(x => x!.Value).ToArray();
        return values.Length == 0 ? null : values.Sum();
    }

    private static FactoryProjectState ReadFactoryProjectState(string repository)
    {
        var current = Path.Combine(repository, ".idd", "factory", "current");
        var results = Path.Combine(repository, ".idd", "factory", "results");
        bool Has(string name) => File.Exists(Path.Combine(current, name));
        return new FactoryProjectState
        {
            CurrentRequestPresent = Has("request.md"),
            CurrentPlanPresent = Has("plan.md"),
            CurrentCompletedPresent = Has("completed.md"),
            CurrentAnswersPresent = Has("answers.md"),
            CurrentQuestionPresent = Has("question.md"),
            VerificationFailurePresent = Has("verification-failure.md"),
            ArchivedResultPresent = Directory.Exists(results) &&
                                    Directory.EnumerateFileSystemEntries(results).Any()
        };
    }

    private static string Bound(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value ?? "";
        var normalized = Regex.Replace(value, @"\s+", " ").Trim();
        return normalized.Length <= max ? normalized : normalized[..max] + " [truncated]";
    }

    private static bool PathBelongsToRepository(string? cwd, string repository)
    {
        if (string.IsNullOrWhiteSpace(cwd))
            return false;
        try
        {
            var normalizedCwd = NormalizePath(cwd);
            var normalizedRepository = NormalizePath(repository);
            if (PathComparer().Equals(normalizedCwd, normalizedRepository))
                return true;

            var relative = Path.GetRelativePath(normalizedRepository, normalizedCwd);
            if (Path.IsPathRooted(relative) || relative == "..")
                return false;

            var parentPrefix = ".." + Path.DirectorySeparatorChar;
            var alternateParentPrefix = ".." + Path.AltDirectorySeparatorChar;
            return !relative.StartsWith(parentPrefix, PathComparison()) &&
                   !relative.StartsWith(alternateParentPrefix, PathComparison());
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static StringComparer PathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison PathComparison() =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed record StructuredFactoryResult(string Status, string? Reason, CodexEvent Event);

    private sealed class ToolCallState
    {
        public string Id { get; init; } = "";
        public string? Name { get; set; }
        public string? Arguments { get; set; }
        public string? Output { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public string? Status { get; set; }
        public int? ExitCode { get; set; }
        public bool HadLifecycle { get; set; }
    }
}

public static class ReportApplication
{
    public static int Run(string[] args)
    {
        try
        {
            var options = CliOptions.Parse(args);
            if (options.ShowHelp)
            {
                Console.WriteLine(CliOptions.Help);
                return 0;
            }
            if (options.ShowVersion)
            {
                Console.WriteLine(Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0");
                return 0;
            }

            var repository = RepositoryLocator.Resolve(options.Repository);
            var codexHome = CodexHomeLocator.Resolve(options.CodexHome);
            var reports = new FactoryReportEngine().FindRuns(repository, codexHome, options.Verbose);

            if (options.Command == "list")
            {
                ReportWriters.WriteList(reports, Console.Out);
                return 0;
            }

            FactoryRunReport? report;
            if (options.Command == "latest")
            {
                report = reports.OrderByDescending(x => x.Run.StartedAt ?? DateTimeOffset.MinValue).FirstOrDefault();
            }
            else
            {
                var threadReports = reports.Where(x => x.Run.RootThreadId == options.Command)
                    .OrderBy(x => x.Run.RunIndex).ToArray();
                if (options.RunIndex is not null)
                    report = threadReports.FirstOrDefault(x => x.Run.RunIndex == options.RunIndex);
                else
                {
                    report = threadReports.LastOrDefault();
                    if (report is not null && threadReports.Length > 1)
                    {
                        report.Diagnostics.Add(new Diagnostic
                        {
                            Severity = "warning",
                            Category = "reporter",
                            Code = "multiple_runs_in_thread",
                            Message = $"Thread contains {threadReports.Length} Factory runs; the latest was selected. Use --run <N>."
                        });
                    }
                }
            }

            if (report is null)
            {
                Console.Error.WriteLine("No matching Factory run was found.");
                return 2;
            }

            ReportWriters.WriteConsole(report, Console.Out, options.Verbose);
            if (options.JsonPath is not null)
                ReportWriters.WriteJson(report, options.JsonPath);
            if (options.MarkdownPath is not null)
                ReportWriters.WriteMarkdown(report, options.MarkdownPath, options.Verbose);
            return 0;
        }
        catch (CliException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"idd-factory-report failed: {ex.Message}");
            return 1;
        }
    }
}

public sealed class CliOptions
{
    public string Command { get; private set; } = "latest";
    public string Repository { get; private set; } = ".";
    public string? CodexHome { get; private set; }
    public int? RunIndex { get; private set; }
    public string? JsonPath { get; private set; }
    public string? MarkdownPath { get; private set; }
    public bool Verbose { get; private set; }
    public bool ShowHelp { get; private set; }
    public bool ShowVersion { get; private set; }

    public const string Help = """
idd-factory-report - diagnostics for ordinary IDD Factory runs

Usage:
  idd-factory-report latest [options]
  idd-factory-report list [options]
  idd-factory-report <thread-id> [--run N] [options]

Options:
  --repo <path>         Repository or a path inside it (default: .)
  --codex-home <path>   Override CODEX_HOME
  --run <N>             Factory run index inside an explicit thread
  --json <file>         Write normalized JSON report
  --markdown <file>     Write Markdown report
  --verbose             Show linkage/parser details and rollout paths
  --help                Show help
  --version             Show version
""";

    public static CliOptions Parse(string[] args)
    {
        var result = new CliOptions();
        var positionals = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--help":
                case "-h":
                    result.ShowHelp = true;
                    break;
                case "--version":
                    result.ShowVersion = true;
                    break;
                case "--verbose":
                    result.Verbose = true;
                    break;
                case "--repo":
                    result.Repository = Value(args, ref i, arg);
                    break;
                case "--codex-home":
                    result.CodexHome = Value(args, ref i, arg);
                    break;
                case "--json":
                    result.JsonPath = Value(args, ref i, arg);
                    break;
                case "--markdown":
                    result.MarkdownPath = Value(args, ref i, arg);
                    break;
                case "--run":
                    var raw = Value(args, ref i, arg);
                    if (!int.TryParse(raw, out var run) || run < 1)
                        throw new CliException("--run must be a positive integer.");
                    result.RunIndex = run;
                    break;
                default:
                    if (arg.StartsWith("-", StringComparison.Ordinal))
                        throw new CliException($"Unknown option: {arg}");
                    positionals.Add(arg);
                    break;
            }
        }

        if (positionals.Count > 1)
            throw new CliException("Specify one command/thread id.");
        if (positionals.Count == 1)
            result.Command = positionals[0];
        if (result.RunIndex is not null && result.Command is "latest" or "list")
            throw new CliException("--run can only be used with an explicit thread id.");
        return result;
    }

    private static string Value(string[] args, ref int index, string option)
    {
        if (++index >= args.Length)
            throw new CliException($"Missing value for {option}.");
        return args[index];
    }
}

public sealed class CliException(string message) : Exception(message);

public static class RepositoryLocator
{
    public static string Resolve(string path)
    {
        var full = Path.GetFullPath(path);
        var psi = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(full);
        psi.ArgumentList.Add("rev-parse");
        psi.ArgumentList.Add("--show-toplevel");

        using var process = Process.Start(psi) ?? throw new CliException("Could not start git.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            throw new CliException($"Could not determine repository root for '{path}'. {stderr.Trim()}");
        return Path.GetFullPath(stdout.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}

public static class CodexHomeLocator
{
    public static string Resolve(string? explicitPath)
    {
        var candidate = !string.IsNullOrWhiteSpace(explicitPath)
            ? explicitPath
            : !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CODEX_HOME"))
                ? Environment.GetEnvironmentVariable("CODEX_HOME")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        return Path.GetFullPath(candidate!);
    }
}

public static class ReportWriters
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static void WriteList(IEnumerable<FactoryRunReport> reports, TextWriter writer)
    {
        writer.WriteLine("#  Started                      Result       Thread");
        var i = 0;
        foreach (var report in reports.OrderByDescending(x => x.Run.StartedAt ?? DateTimeOffset.MinValue))
        {
            writer.WriteLine($"{++i,-2} {FormatTime(report.Run.StartedAt),-28} {report.Run.Result.ToUpperInvariant(),-12} {report.Run.RootThreadId}");
        }
    }

    public static void WriteConsole(FactoryRunReport report, TextWriter writer, bool verbose)
    {
        writer.WriteLine("IDD Factory Run Report");
        writer.WriteLine();
        writer.WriteLine("Repository");
        writer.WriteLine("----------");
        writer.WriteLine(report.Repository);
        writer.WriteLine();
        writer.WriteLine("Factory run");
        writer.WriteLine("-----------");
        writer.WriteLine($"Thread:   {report.Run.RootThreadId}");
        writer.WriteLine($"Run:      {report.Run.RunIndex}");
        writer.WriteLine($"Result:   {report.Run.Result.ToUpperInvariant()}");
        writer.WriteLine($"Started:  {FormatTime(report.Run.StartedAt)}");
        writer.WriteLine($"Finished: {FormatTime(report.Run.FinishedAt)}");
        writer.WriteLine($"Duration: {FormatDuration(report.Metrics.DurationMilliseconds)}");
        if (report.Run.ResultEvidence.Count > 0)
            writer.WriteLine($"Evidence: {string.Join(", ", report.Run.ResultEvidence)}");
        if (!string.IsNullOrWhiteSpace(report.Run.Reason))
            writer.WriteLine($"Reason:   {report.Run.Reason}");

        writer.WriteLine();
        writer.WriteLine("Agents");
        writer.WriteLine("------");
        foreach (var agent in report.Agents)
        {
            var label = agent.Role == "root" ? "Factory root" :
                $"{Cap(agent.Role)} #{agent.Sequence}";
            writer.WriteLine($"{label,-20} {FormatDuration(agent.DurationMilliseconds),10}" +
                             (verbose ? $"  {agent.ThreadId}" : ""));
        }
        writer.WriteLine();
        writer.WriteLine($"Planner invocations: {report.Metrics.PlannerInvocations}");
        writer.WriteLine($"Worker invocations:  {report.Metrics.WorkerInvocations}");
        writer.WriteLine($"Other child agents:  {report.Metrics.OtherChildAgents}");
        writer.WriteLine($"Maximum depth:        {report.Metrics.MaximumDepth}");

        writer.WriteLine();
        writer.WriteLine("Tasks");
        writer.WriteLine("-----");
        if (report.Tasks.Count == 0)
            writer.WriteLine("unavailable");
        foreach (var task in report.Tasks)
        {
            writer.WriteLine($"{task.Number}. {task.Status.ToUpperInvariant(),-11} {FormatDuration(task.DurationMilliseconds),10}");
            writer.WriteLine($"   {Short(task.Text, 180)}");
            if (verbose)
                writer.WriteLine($"   agent: {task.AgentThreadId}");
        }

        writer.WriteLine();
        writer.WriteLine("Tokens");
        writer.WriteLine("------");
        if (report.Metrics.RootReportedTokens.Available)
        {
            writer.WriteLine("Root reported usage");
            writer.WriteLine("-------------------");
            writer.WriteLine($"Input:      {Num(report.Metrics.RootReportedTokens.InputTokens)}");
            writer.WriteLine($"Cached:     {Num(report.Metrics.RootReportedTokens.CachedInputTokens)}");
            writer.WriteLine($"New input:  {Num(report.Metrics.RootReportedTokens.NewInputTokens)}");
            writer.WriteLine($"Output:     {Num(report.Metrics.RootReportedTokens.OutputTokens)}");
            writer.WriteLine();
        }

        writer.WriteLine($"{"Agent",-20} {"Input",10} {"Cached",10} {"New input",10} {"Output",10}");
        foreach (var agent in report.Agents)
        {
            var label = agent.Role == "root" ? "Factory root" : $"{Cap(agent.Role)} #{agent.Sequence}";
            writer.WriteLine($"{label,-20} {Num(agent.Tokens.InputTokens),10} {Num(agent.Tokens.CachedInputTokens),10} {Num(agent.Tokens.NewInputTokens),10} {Num(agent.Tokens.OutputTokens),10}");
        }
        writer.WriteLine($"{"Total",-20} {Num(report.Metrics.Tokens.InputTokens),10} {Num(report.Metrics.Tokens.CachedInputTokens),10} {Num(report.Metrics.Tokens.NewInputTokens),10} {Num(report.Metrics.Tokens.OutputTokens),10}");
        writer.WriteLine($"Aggregation: {report.Metrics.TokenAggregationStatus}");

        writer.WriteLine();
        writer.WriteLine("Tools");
        writer.WriteLine("-----");
        writer.WriteLine($"Tool calls:       {report.Metrics.Tools.ToolCalls}");
        writer.WriteLine($"Tool batches:     {report.Metrics.Tools.ToolBatches}");
        writer.WriteLine($"Commands:         {report.Metrics.Tools.Commands}");
        writer.WriteLine($"Failed commands:  {report.Metrics.Tools.FailedCommands}");
        writer.WriteLine($"Spawn agent calls:{report.Metrics.Tools.SpawnAgentCalls,4}");
        writer.WriteLine($"Wait agent calls: {report.Metrics.Tools.WaitAgentCalls,4}");
        writer.WriteLine($"File operations:  {report.Metrics.Tools.FileOperations,4}");
        writer.WriteLine($"Search operations:{report.Metrics.Tools.SearchOperations,4}");
        writer.WriteLine($"Other:            {report.Metrics.Tools.OtherToolCalls,4}");

        if (report.Metrics.Tools.FailedCommandItems.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("Failed commands");
            writer.WriteLine("---------------");
            foreach (var failed in report.Metrics.Tools.FailedCommandItems)
                writer.WriteLine($"{failed.Agent}  {failed.ExitCode?.ToString() ?? failed.Status ?? "failed"}  {failed.Command ?? "unavailable"}");
        }

        writer.WriteLine();
        writer.WriteLine("Timeline");
        writer.WriteLine("--------");
        foreach (var e in report.Timeline)
            writer.WriteLine($"{FormatClock(e.Timestamp),8}  {e.Text}");

        writer.WriteLine();
        writer.WriteLine("Factory project state");
        writer.WriteLine("---------------------");
        writer.WriteLine($"current/request.md:       {Present(report.FactoryProjectState.CurrentRequestPresent)}");
        writer.WriteLine($"current/plan.md:          {Present(report.FactoryProjectState.CurrentPlanPresent)}");
        writer.WriteLine($"current/question.md:      {Present(report.FactoryProjectState.CurrentQuestionPresent)}");
        writer.WriteLine($"verification-failure.md:  {Present(report.FactoryProjectState.VerificationFailurePresent)}");
        writer.WriteLine($"archived result:          {Present(report.FactoryProjectState.ArchivedResultPresent)}");

        if (verbose)
        {
            writer.WriteLine();
            writer.WriteLine("Codex capabilities");
            writer.WriteLine("------------------");
            writer.WriteLine($"State DB:          {Available(report.Host.Capabilities.StateDb)}");
            writer.WriteLine($"Spawn edges:       {Available(report.Host.Capabilities.SpawnEdges)}");
            writer.WriteLine($"Agent role:        {Available(report.Host.Capabilities.AgentRole)}");
            writer.WriteLine($"Token accounting:  {report.Host.Capabilities.TokenAccounting}");
            writer.WriteLine($"Codex home:         {report.Host.CodexHome ?? "unavailable"}");
            writer.WriteLine($"Start boundary:     {report.Run.StartBoundary.Confidence} ({report.Run.StartBoundary.Evidence ?? "unavailable"})");
            writer.WriteLine($"End boundary:       {report.Run.EndBoundary.Confidence} ({report.Run.EndBoundary.Evidence ?? "unavailable"})");
            foreach (var agent in report.Agents.Where(x => x.RolloutPath is not null))
                writer.WriteLine($"{agent.ThreadId}: {agent.RolloutPath}");
        }

        if (report.Diagnostics.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("Diagnostics");
            writer.WriteLine("-----------");
            foreach (var diagnostic in report.Diagnostics)
                writer.WriteLine($"{diagnostic.Severity.ToUpperInvariant()} [{diagnostic.Category}/{diagnostic.Code}] {diagnostic.Message}");
        }
    }

    public static void WriteJson(FactoryRunReport report, string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine, Encoding.UTF8);
    }

    public static void WriteMarkdown(FactoryRunReport report, string path, bool verbose)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("# IDD Factory Run Report");
        writer.WriteLine();
        writer.WriteLine($"- Repository: `{report.Repository}`");
        writer.WriteLine($"- Thread: `{report.Run.RootThreadId}`");
        writer.WriteLine($"- Run: {report.Run.RunIndex}");
        writer.WriteLine($"- Result: **{report.Run.Result.ToUpperInvariant()}**");
        if (!string.IsNullOrWhiteSpace(report.Run.Reason))
            writer.WriteLine($"- Reason: {report.Run.Reason}");
        writer.WriteLine($"- Started: {FormatTime(report.Run.StartedAt)}");
        writer.WriteLine($"- Finished: {FormatTime(report.Run.FinishedAt)}");
        writer.WriteLine($"- Duration: {FormatDuration(report.Metrics.DurationMilliseconds)}");
        writer.WriteLine();
        writer.WriteLine("## Agents");
        writer.WriteLine();
        writer.WriteLine("| Agent | Duration | Input | Cached | New input | Output |");
        writer.WriteLine("|---|---:|---:|---:|---:|---:|");
        foreach (var agent in report.Agents)
        {
            var label = agent.Role == "root" ? "Factory root" : $"{Cap(agent.Role)} #{agent.Sequence}";
            writer.WriteLine($"| {label} | {FormatDuration(agent.DurationMilliseconds)} | {Num(agent.Tokens.InputTokens)} | {Num(agent.Tokens.CachedInputTokens)} | {Num(agent.Tokens.NewInputTokens)} | {Num(agent.Tokens.OutputTokens)} |");
        }
        writer.WriteLine();
        writer.WriteLine("## Tasks");
        writer.WriteLine();
        if (report.Tasks.Count == 0)
            writer.WriteLine("unavailable");
        foreach (var task in report.Tasks)
            writer.WriteLine($"{task.Number}. **{task.Status.ToUpperInvariant()}** ({FormatDuration(task.DurationMilliseconds)}) — {Short(task.Text, 300)}");
        writer.WriteLine();
        writer.WriteLine("## Token accounting");
        writer.WriteLine();
        writer.WriteLine($"- Root reported input: {Num(report.Metrics.RootReportedTokens.InputTokens)}");
        writer.WriteLine($"- Root reported cached: {Num(report.Metrics.RootReportedTokens.CachedInputTokens)}");
        writer.WriteLine($"- Root reported new input: {Num(report.Metrics.RootReportedTokens.NewInputTokens)}");
        writer.WriteLine($"- Root reported output: {Num(report.Metrics.RootReportedTokens.OutputTokens)}");
        writer.WriteLine($"- Aggregate status: {report.Metrics.TokenAggregationStatus}");
        writer.WriteLine();
        writer.WriteLine("## Tools");
        writer.WriteLine();
        writer.WriteLine($"- Tool calls: {report.Metrics.Tools.ToolCalls}");
        writer.WriteLine($"- Tool batches: {report.Metrics.Tools.ToolBatches}");
        writer.WriteLine($"- Commands: {report.Metrics.Tools.Commands}");
        writer.WriteLine($"- Failed commands: {report.Metrics.Tools.FailedCommands}");
        writer.WriteLine($"- Spawn agent calls: {report.Metrics.Tools.SpawnAgentCalls}");
        writer.WriteLine($"- Wait agent calls: {report.Metrics.Tools.WaitAgentCalls}");
        writer.WriteLine($"- File operations: {report.Metrics.Tools.FileOperations}");
        writer.WriteLine($"- Search operations: {report.Metrics.Tools.SearchOperations}");
        writer.WriteLine($"- Other: {report.Metrics.Tools.OtherToolCalls}");
        writer.WriteLine();
        writer.WriteLine("## Timeline");
        writer.WriteLine();
        foreach (var e in report.Timeline)
            writer.WriteLine($"- {FormatTime(e.Timestamp)} — {e.Text}");
        writer.WriteLine();
        writer.WriteLine("## Diagnostics");
        writer.WriteLine();
        if (report.Diagnostics.Count == 0)
            writer.WriteLine("None.");
        foreach (var d in report.Diagnostics)
            writer.WriteLine($"- **{d.Severity.ToUpperInvariant()}** `{d.Category}/{d.Code}`: {d.Message}");

        if (verbose)
        {
            writer.WriteLine();
            writer.WriteLine("## Reporter details");
            writer.WriteLine();
            writer.WriteLine($"- Codex home: `{report.Host.CodexHome ?? "unavailable"}`");
            writer.WriteLine($"- Start boundary: {report.Run.StartBoundary.Confidence} — {report.Run.StartBoundary.Evidence ?? "unavailable"}");
            writer.WriteLine($"- End boundary: {report.Run.EndBoundary.Confidence} — {report.Run.EndBoundary.Evidence ?? "unavailable"}");
        }
    }

    private static string FormatTime(DateTimeOffset? value) =>
        value is null ? "unavailable" : value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");

    private static string FormatClock(DateTimeOffset? value) =>
        value is null ? "--:--:--" : value.Value.ToLocalTime().ToString("HH:mm:ss");

    private static string FormatDuration(long? ms)
    {
        if (ms is null)
            return "unavailable";
        var duration = TimeSpan.FromMilliseconds(ms.Value);
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s"
            : duration.TotalMinutes >= 1
                ? $"{duration.Minutes}m {duration.Seconds}s"
                : $"{duration.TotalSeconds:0.#}s";
    }

    private static string Num(long? value) => value?.ToString() ?? "unavailable";
    private static string Short(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unavailable";
        var normalized = Regex.Replace(value, @"\s+", " ").Trim();
        return normalized.Length <= max ? normalized : normalized[..max] + " [truncated]";
    }

    private static string Present(bool value) => value ? "PRESENT" : "absent";
    private static string Available(bool value) => value ? "available" : "unavailable";
    private static string Cap(string value) => string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
