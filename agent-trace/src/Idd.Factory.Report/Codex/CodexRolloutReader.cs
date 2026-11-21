using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Idd.Factory.Report;

public sealed class CodexRolloutReader
{
    // A message or a completed tool call is not a child-turn terminal.
    // Keep this host-specific fact separate from diagnostic activity/timing.
    public static bool HasConfirmedTerminal(CodexRollout rollout)
    {
        if (rollout.Diagnostics.Any(d => d.Code is
            "malformed_rollout_line" or "conflicting_turn_ids"))
            return false;
        var terminal = rollout.Events.LastOrDefault(e =>
            e.Type is "turn.completed" or "turn_completed" or "task_complete" &&
            (e.ThreadId is null || e.ThreadId == rollout.ThreadId));
        return terminal is not null &&
            !rollout.Events.Any(e => e.Ordinal > terminal.Ordinal &&
                e.Role == "assistant" && !string.IsNullOrWhiteSpace(e.Text)) &&
            !rollout.Events.Any(e =>
                e.Type is "turn.failed" or "turn_failed" or "task_failed" ||
                e.ToolName is null && e.Status is
                    "failed" or "error" or "cancelled" or "canceled" or "interrupted");
    }

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
                    rollout.AgentRoleHint = FindString(payload, "agent_role", "agentRole") ?? rollout.AgentRoleHint;
                    rollout.AgentPath = FindString(payload, "agent_path", "agentPath") ?? rollout.AgentPath;

                    // Codex MultiAgentV2 nests child identity in session_meta.source.subagent.thread_spawn.
                    var source = Object(payload, "source");
                    var subagent = source is { } sourceValue ? Object(sourceValue, "subagent") : null;
                    var threadSpawn = subagent is { } subagentValue
                        ? Object(subagentValue, "thread_spawn")
                        : null;
                    if (threadSpawn is { } spawnMetadata)
                    {
                        rollout.ParentThreadId ??= FindString(spawnMetadata,
                            "parent_thread_id", "parentThreadId", "parent_id");
                        rollout.AgentRoleHint ??= FindString(spawnMetadata, "agent_role", "agentRole");
                        rollout.AgentPath ??= FindString(spawnMetadata, "agent_path", "agentPath");
                    }
                }

                var timestamp = Timestamp(root) ?? Timestamp(payload);
                var item = Object(root, "item") ?? Object(payload, "item");
                var eventObject = item ?? payload;

                var lifecycle = NormalizeLifecycle(topType, payloadType);
                var rawItemType = String(eventObject, "type") ?? type;
                var itemKind = NormalizeItemKind(rawItemType);
                var nativeOperation = itemKind is "CollabAgentToolCall" or "CommandExecution" or "FileChange";
                var hostWrapper = topType == "response_item" ||
                                  rawItemType is "function_call" or "custom_tool_call" or "mcp_tool_call" or "local_shell_call";

                var role = String(eventObject, "role");
                if (type == "user_message") role = "user";
                if (type == "agent_message") role = "assistant";

                var text = ExtractText(eventObject);
                if (text is null && item is not null)
                    text = ExtractText(payload);

                var threadId = FindStringAcross([eventObject, payload, root], "thread_id", "threadId");
                // Native response_item messages carry their turn in metadata, whereas
                // usage records carry it directly on the payload. Reject contradictions.
                var identifiers = new[] { eventObject, payload, root };
                var turnIds = identifiers.SelectMany(element => new[]
                    {
                        String(element, "turn_id"),
                        String(element, "turnId"),
                        String(Object(element, "internal_chat_message_metadata_passthrough") ?? default, "turn_id"),
                        String(Object(element, "internal_chat_message_metadata_passthrough") ?? default, "turnId")
                    })
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                var turnConflict = turnIds.Length > 1;
                var turnId = turnConflict ? null : turnIds.FirstOrDefault();
                if (turnConflict)
                    rollout.Diagnostics.Add(new Diagnostic
                    {
                        Severity = "warning", Category = "host-trace", Code = "conflicting_turn_ids",
                        Message = $"Conflicting native turn identifiers in {Path.GetFileName(path)} at line {ordinal}; the event cannot establish turn correlation."
                    });

                var metadata = Object(eventObject, "internal_chat_message_metadata_passthrough")
                    ?? Object(payload, "internal_chat_message_metadata_passthrough");
                var environmentContext = metadata is { } m &&
                    m.TryGetProperty("content_item_kinds", out var kinds) &&
                    kinds.ValueKind == JsonValueKind.Array &&
                    kinds.EnumerateArray().Any(kind =>
                        kind.ValueKind == JsonValueKind.String &&
                        kind.GetString() == "environments.environment_context");
                var initialization = topType is "session_meta" or "world_state" or "turn_context" ||
                    type is "task_started" or "thread_settings_applied" ||
                    topType == "response_item" && rawItemType == "message" &&
                    (role is "developer" or "system" || role == "user" && environmentContext);
                var rootTurnId = FindStringAcross([eventObject, payload, root], "root_turn_id", "rootTurnId");
                var responseId = FindStringAcross([eventObject, payload, root], "response_id", "responseId");
                var senderThreadId = FindStringAcross([eventObject, payload, root], "sender_thread_id", "senderThreadId");
                var receiverThreadIds = ExtractReceiverThreadIds(eventObject);

                string? toolId = null;
                string? toolName = null;
                string? toolPhase = null;
                string? toolArguments = null;
                string? toolOutput = null;
                string? spawnTask = null;
                string? childThreadId = null;

                if (lifecycle is "ItemStarted" or "ItemCompleted" && IsToolType(rawItemType))
                {
                    toolId = String(eventObject, "id") ?? String(eventObject, "call_id");
                    toolName = NormalizeToolName(itemKind, String(eventObject, "name") ?? String(eventObject, "tool"));
                    toolPhase = lifecycle == "ItemStarted" ? "started" : "completed";
                    toolArguments = ExtractArguments(eventObject);
                    toolOutput = ExtractOutput(eventObject);
                }
                else if (topType == "response_item" || type is "function_call" or "mcp_tool_call" or "custom_tool_call" or "local_shell_call" or "command_execution")
                {
                    if (rawItemType is "function_call_output" or "custom_tool_call_output")
                    {
                        toolId = String(eventObject, "call_id") ?? String(eventObject, "id");
                        toolPhase = "output";
                        toolOutput = ExtractOutput(eventObject);
                    }
                    else if (IsToolType(rawItemType))
                    {
                        toolId = String(eventObject, "call_id") ?? String(eventObject, "id");
                        toolName = NormalizeToolName(itemKind, String(eventObject, "name") ?? String(eventObject, "tool") ?? rawItemType);
                        toolPhase = "call";
                        toolArguments = ExtractArguments(eventObject);
                        toolOutput = ExtractOutput(eventObject);
                    }
                }

                if (toolName == "spawn_agent")
                {
                    if (toolOutput is null && eventObject.ValueKind == JsonValueKind.Object &&
                        eventObject.TryGetProperty("error", out _))
                        toolOutput = eventObject.GetRawText();
                    // Native structured items can put task_name beside an opaque
                    // message instead of inside the serialized arguments field.
                    if (eventObject.ValueKind == JsonValueKind.Object &&
                        (eventObject.TryGetProperty("task_name", out _) ||
                         eventObject.TryGetProperty("fork_turns", out _) ||
                         eventObject.TryGetProperty("model", out _)))
                        toolArguments = eventObject.GetRawText();
                    spawnTask = ExtractSpawnTask(toolArguments);
                    if (IsOpaqueSpawnTask(spawnTask))
                    {
                        rollout.Diagnostics.Add(new Diagnostic
                        {
                            Severity = "warning",
                            Category = "host-trace",
                            Code = "spawn_prompt_unreadable",
                            Message = $"Spawn {toolId} in {Path.GetFileName(path)} at line {ordinal} contains an encrypted prompt. The reporter cannot read its task or profile; readable planner output is used when available."
                        });
                        spawnTask = null;
                    }
                    childThreadId = receiverThreadIds.FirstOrDefault() ?? ExtractSpawnChildThreadId(eventObject);
                }
                else if (rawItemType is "function_call_output" or "custom_tool_call_output")
                {
                    childThreadId = ExtractSpawnChildThreadId(eventObject);
                }

                var usage = ExtractUsage(root, payload, out var cumulative, out var usageScope);
                var actualModel = FindString(eventObject, "actual_model", "actualModel", "model_used", "modelUsed", "resolved_model", "resolvedModel");
                var actualReasoning = FindString(eventObject, "actual_reasoning_effort", "actualReasoningEffort", "reasoning_effort_used", "reasoningEffortUsed", "resolved_reasoning_effort", "resolvedReasoningEffort");
                // Only the direct fields of this thread's turn context describe its model.
                // Nested collaboration settings or spawn arguments may describe other agents.
                if (topType == "turn_context")
                {
                    actualModel = ReadContextSetting(payload, "model", rollout, ordinal);
                    actualReasoning = ReadContextSetting(payload, "effort", rollout, ordinal);
                }

                rollout.Events.Add(new CodexEvent
                {
                    Ordinal = ordinal,
                    Timestamp = timestamp,
                    Type = type,
                    Lifecycle = lifecycle,
                    ItemKind = itemKind,
                    ThreadId = threadId,
                    TurnId = turnId,
                    RootTurnId = rootTurnId,
                    ResponseId = responseId,
                    SenderThreadId = senderThreadId,
                    ReceiverThreadIds = receiverThreadIds,
                    IsNativeOperation = nativeOperation,
                    IsHostWrapper = hostWrapper,
                    UsageScope = usageScope,
                    Role = role,
                    Text = text,
                    ToolId = toolId,
                    ToolName = toolName,
                    ToolPhase = toolPhase,
                    ToolArguments = toolArguments,
                    ToolOutput = toolOutput,
                    CommandWorkingDirectory = ExtractCommandWorkingDirectory(eventObject, toolArguments),
                    ExitCode = FindInt(eventObject, "exit_code", "exitCode"),
                    Status = FindString(eventObject, "status", "outcome"),
                    Usage = usage,
                    UsageIsCumulative = cumulative,
                    IsInitialization = initialization,
                    TurnIdentityConflict = turnConflict,
                    ChildThreadId = childThreadId,
                    SpawnTask = spawnTask,
                    ActualModel = actualModel,
                    ActualReasoningEffort = actualReasoning
                });

                if (metadataOnly && !string.IsNullOrWhiteSpace(rollout.ThreadId) && rollout.Cwd is not null && ordinal >= 16)
                    break;
            }
            catch (JsonException)
            {
                rollout.Diagnostics.Add(new Diagnostic
                {
                    Severity = "warning",
                    Category = "host-trace",
                    Code = "malformed_rollout_line",
                    Message = $"Could not read JSONL line {ordinal} in {Path.GetFileName(path)}{(isLastLine ? " (final line may be truncated)" : "")}. Its data was omitted."
                });
            }
        }

        CorrelateSpawns(rollout);
        return rollout;
    }

    private static string? ReadContextSetting(JsonElement payload, string name, CodexRollout rollout, long ordinal)
    {
        if (!payload.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
            return value.GetString();
        rollout.Diagnostics.Add(new Diagnostic
        {
            Severity = "warning",
            Category = "host-trace",
            Code = "invalid_turn_context_setting",
            Message = $"Could not read turn_context.{name} at line {ordinal} in {Path.GetFileName(rollout.Path)}: expected a non-empty string, got {value.ValueKind}."
        });
        return null;
    }

    private static bool IsOpaqueSpawnTask(string? task) =>
        task is not null && Regex.IsMatch(task.Trim(), @"\AgAAAAA[A-Za-z0-9_-]+={0,2}\z");

    private static void CorrelateSpawns(CodexRollout rollout)
    {
        var spawnByCall = new Dictionary<string, SpawnRecord>(StringComparer.Ordinal);
        foreach (var e in rollout.Events)
        {
            if (e.ToolId is null)
                continue;

            if (e.ToolName == "spawn_agent")
            {
                if (!spawnByCall.TryGetValue(e.ToolId, out var record))
                {
                    record = new SpawnRecord
                    {
                        CallId = e.ToolId,
                        SenderThreadId = e.SenderThreadId ?? rollout.ThreadId,
                        Task = e.SpawnTask,
                        ChildAgentPath = ExtractSpawnArgument(e.ToolOutput, "task_name"),
                        RequestedTaskName = ExtractSpawnArgument(e.ToolArguments, "task_name"),
                        ExecutionProfile = ExtractExecutionProfile(e.SpawnTask),
                        RequestedModel = ExtractSpawnArgument(e.ToolArguments, "model"),
                        RequestedReasoningEffort = ExtractSpawnArgument(e.ToolArguments, "reasoning_effort", "reasoningEffort"),
                        ActualModel = e.ActualModel ?? ExtractSpawnArgument(e.ToolOutput, "actual_model", "actualModel", "model_used", "modelUsed", "resolved_model", "resolvedModel"),
                        ActualReasoningEffort = e.ActualReasoningEffort ?? ExtractSpawnArgument(e.ToolOutput, "actual_reasoning_effort", "actualReasoningEffort", "reasoning_effort_used", "reasoningEffortUsed", "resolved_reasoning_effort", "resolvedReasoningEffort"),
                        Timestamp = e.Timestamp
                    };
                    spawnByCall[e.ToolId] = record;
                    rollout.SpawnRecords[e.ToolId] = record;
                }
                else
                {
                    record.SenderThreadId ??= e.SenderThreadId ?? rollout.ThreadId;
                    if (record.Task is null && e.SpawnTask is not null)
                        record.Task = e.SpawnTask;
                    record.ChildAgentPath ??= ExtractSpawnArgument(e.ToolOutput, "task_name");
                    record.RequestedTaskName ??= ExtractSpawnArgument(e.ToolArguments, "task_name");
                    record.ExecutionProfile ??= ExtractExecutionProfile(e.SpawnTask);
                    record.RequestedModel ??= ExtractSpawnArgument(e.ToolArguments, "model");
                    record.RequestedReasoningEffort ??= ExtractSpawnArgument(e.ToolArguments, "reasoning_effort", "reasoningEffort");
                    record.ActualModel ??= e.ActualModel ?? ExtractSpawnArgument(e.ToolOutput, "actual_model", "actualModel", "model_used", "modelUsed", "resolved_model", "resolvedModel");
                    record.ActualReasoningEffort ??= e.ActualReasoningEffort ?? ExtractSpawnArgument(e.ToolOutput, "actual_reasoning_effort", "actualReasoningEffort", "reasoning_effort_used", "reasoningEffortUsed", "resolved_reasoning_effort", "resolvedReasoningEffort");
                }

                foreach (var id in e.ReceiverThreadIds)
                {
                    if (!record.ChildThreadIds.Contains(id, StringComparer.Ordinal))
                        record.ChildThreadIds.Add(id);
                }

                ApplySpawnOutcome(record, e.ToolOutput);
                var fallbackId = record.SpawnOutcome == "rejected" ? null : e.ChildThreadId;
                if (record.SpawnOutcome != "rejected" && record.ChildThreadIds.Count == 0 && string.IsNullOrWhiteSpace(fallbackId))
                    fallbackId = ExtractThreadId(e.ToolOutput);
                if (!string.IsNullOrWhiteSpace(fallbackId) &&
                    !record.ChildThreadIds.Contains(fallbackId, StringComparer.Ordinal))
                    record.ChildThreadIds.Add(fallbackId);

                record.ChildThreadId = record.ChildThreadIds.FirstOrDefault();
                e.ChildThreadId = record.ChildThreadId;
            }
            else if (e.ToolPhase == "output" && spawnByCall.TryGetValue(e.ToolId, out var record))
            {
                record.ChildAgentPath ??= ExtractSpawnArgument(e.ToolOutput, "task_name");
                record.ActualModel ??= e.ActualModel ?? ExtractSpawnArgument(e.ToolOutput, "actual_model", "actualModel", "model_used", "modelUsed", "resolved_model", "resolvedModel");
                record.ActualReasoningEffort ??= e.ActualReasoningEffort ?? ExtractSpawnArgument(e.ToolOutput, "actual_reasoning_effort", "actualReasoningEffort", "reasoning_effort_used", "reasoningEffortUsed", "resolved_reasoning_effort", "resolvedReasoningEffort");
                ApplySpawnOutcome(record, e.ToolOutput);
                var id = record.SpawnOutcome == "rejected" ? null : e.ChildThreadId ?? ExtractThreadId(e.ToolOutput);
                if (!string.IsNullOrWhiteSpace(id) &&
                    !record.ChildThreadIds.Contains(id, StringComparer.Ordinal))
                    record.ChildThreadIds.Add(id);
                record.ChildThreadId = record.ChildThreadIds.FirstOrDefault();
                e.ChildThreadId = record.ChildThreadId;
            }
        }
        // Native started/completed and response_item wrappers share one call ID.
        // Rejections without a child are retained, never synthesized as workers.
    }

    private static void ApplySpawnOutcome(SpawnRecord record, string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return;
        string? code = null, message = null;
        try
        {
            using var parsed = JsonDocument.Parse(output);
            var root = parsed.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error))
            {
                code = error.ValueKind == JsonValueKind.Object ? FindString(error, "code", "type") : null;
                message = error.ValueKind == JsonValueKind.Object
                    ? FindString(error, "message", "detail") : error.ValueKind == JsonValueKind.String ? error.GetString() : null;
            }
            if (root.ValueKind == JsonValueKind.Object &&
                FindString(root, "status", "outcome") is "rejected" or "failed" or "error")
            {
                code ??= FindString(root, "code", "error_code");
                message ??= FindString(root, "message", "reason", "detail") ?? "Host explicitly rejected the spawn.";
            }
        }
        catch (JsonException)
        {
            if (Regex.IsMatch(output, @"\A\s*Unknown model (?<quote>[`'""])[^`'""\r\n]+\k<quote> for spawn_agent\.(?:[ \t]+[^\r\n]*)?(?:\r?\n[\s\S]*)?\z"))
            {
                code = "model_not_found";
                message = output;
            }
            else if (Regex.IsMatch(output, @"(?i)\b(error|rejected|unavailable|not found|unsupported|denied)\b"))
                message = output;
        }
        if (!string.IsNullOrWhiteSpace(code) || !string.IsNullOrWhiteSpace(message))
        {
            record.SpawnOutcome = "rejected";
            record.RejectionCode = code;
            record.RejectionMessage = message;
        }
    }

    public static string? ExtractThreadId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        return ThreadIdRegex.Match(text).Success ? ThreadIdRegex.Match(text).Value : null;
    }

    private static TokenMetrics? ExtractUsage(
        JsonElement root,
        JsonElement payload,
        out bool cumulative,
        out string? scope)
    {
        cumulative = false;
        scope = null;

        foreach (var container in new[] { payload, root })
        {
            if (container.ValueKind != JsonValueKind.Object)
                continue;

            if (container.TryGetProperty("thread_token_usage", out var threadUsage) &&
                threadUsage.ValueKind == JsonValueKind.Object)
            {
                cumulative = true;
                scope = "thread";
                return ParseUsage(threadUsage);
            }

            if (container.TryGetProperty("turn_token_usage", out var turnUsage) &&
                turnUsage.ValueKind == JsonValueKind.Object)
            {
                scope = "turn";
                return ParseUsage(turnUsage);
            }
        }

        JsonElement usage;
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("info", out var info) &&
            info.ValueKind == JsonValueKind.Object)
        {
            if (info.TryGetProperty("total_token_usage", out usage) && usage.ValueKind == JsonValueKind.Object)
            {
                cumulative = true;
                scope = "legacy-total";
                return ParseUsage(usage);
            }
            if (info.TryGetProperty("last_token_usage", out usage) && usage.ValueKind == JsonValueKind.Object)
            {
                scope = "legacy-turn";
                return ParseUsage(usage);
            }
        }

        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("usage", out usage) &&
            usage.ValueKind == JsonValueKind.Object)
        {
            cumulative = FindString(payload, "usage_scope", "usageScope") == "cumulative";
            scope = cumulative ? "legacy-total" : "legacy-turn";
            return ParseUsage(usage);
        }

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("usage", out usage) &&
            usage.ValueKind == JsonValueKind.Object)
        {
            cumulative = FindString(root, "usage_scope", "usageScope") == "cumulative";
            scope = cumulative ? "legacy-total" : "legacy-turn";
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

    private static bool IsToolType(string? type)
    {
        var normalized = NormalizeItemKind(type);
        return normalized is "CollabAgentToolCall" or "CommandExecution" or "FileChange" ||
               type is "function_call" or "mcp_tool_call" or "custom_tool_call"
                   or "local_shell_call" or "local_shell" or "shell_command" or "exec_command"
                   or "write_stdin";
    }

    private static string? NormalizeLifecycle(string? topType, string? payloadType)
    {
        foreach (var value in new[] { payloadType, topType })
        {
            var key = NormalizeKey(value);
            if (key == "itemstarted") return "ItemStarted";
            if (key == "itemcompleted") return "ItemCompleted";
        }
        return null;
    }

    private static string NormalizeItemKind(string? value) => NormalizeKey(value) switch
    {
        "collabagenttoolcall" or "collabtoolcall" => "CollabAgentToolCall",
        "commandexecution" => "CommandExecution",
        "filechange" => "FileChange",
        _ => value ?? ""
    };

    private static string? NormalizeToolName(string itemKind, string? rawName)
    {
        if (itemKind == "CommandExecution") return "command_execution";
        if (itemKind == "FileChange") return "file_change";

        var key = NormalizeKey(rawName);
        if (key == "spawnagent") return "spawn_agent";
        if (key is "waitagent" or "wait") return "wait_agent";
        return rawName;
    }

    private static string NormalizeKey(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? ""
            : new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string? FindStringAcross(IEnumerable<JsonElement> elements, params string[] names)
    {
        foreach (var element in elements)
        {
            var value = FindString(element, names);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        return null;
    }

    private static List<string> ExtractReceiverThreadIds(JsonElement element)
    {
        var result = new List<string>();
        if (element.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var name in new[] { "receiver_thread_ids", "receiverThreadIds" })
        {
            if (!element.TryGetProperty(name, out var ids))
                continue;
            if (ids.ValueKind == JsonValueKind.Array)
            {
                foreach (var id in ids.EnumerateArray())
                {
                    if (id.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(id.GetString()))
                        result.Add(id.GetString()!);
                }
            }
            else if (ids.ValueKind == JsonValueKind.String &&
                     !string.IsNullOrWhiteSpace(ids.GetString()))
            {
                result.Add(ids.GetString()!);
            }
        }
        return result.Distinct(StringComparer.Ordinal).ToList();
    }

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

    private static string? ExtractCommandWorkingDirectory(JsonElement eventObject, string? toolArguments)
    {
        // Native command items may expose cwd directly; function-call wrappers
        // put workdir in the JSON arguments alongside cmd/command.
        var direct = FindString(eventObject, "cwd", "workdir", "working_directory", "workingDirectory");
        if (direct is not null || string.IsNullOrWhiteSpace(toolArguments))
            return direct;
        try
        {
            using var json = JsonDocument.Parse(toolArguments);
            return json.RootElement.ValueKind == JsonValueKind.Object
                ? FindString(json.RootElement, "cwd", "workdir", "working_directory", "workingDirectory")
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractSpawnArgument(string? payload, params string[] names)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;
        try
        {
            using var document = JsonDocument.Parse(payload);
            return FindString(document.RootElement, names);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractExecutionProfile(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var heading = Regex.Match(text, @"(?im)^\s*#\s*ExecutionProfile\s*$\s*^(?<value>economy|standard|strong)\s*$");
        if (heading.Success)
            return heading.Groups["value"].Value.ToLowerInvariant();
        var inline = Regex.Match(text, @"(?im)\bExecutionProfile\s*:\s*(?<value>economy|standard|strong)\b");
        return inline.Success ? inline.Groups["value"].Value.ToLowerInvariant() : null;
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
