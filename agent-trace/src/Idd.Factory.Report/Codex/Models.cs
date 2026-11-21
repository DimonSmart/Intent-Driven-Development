using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Idd.Factory.Report;

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
    public long NativeOperations { get; set; }
    public long HostWrapperCalls { get; set; }
    public long? ToolBatches { get; set; }
    public long Commands { get; set; }
    public long FailedCommands { get; set; }
    public long FileChanges { get; set; }
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

public sealed class CodexEvent
{
    public long Ordinal { get; init; }
    public DateTimeOffset? Timestamp { get; init; }
    public string Type { get; init; } = "";
    public string? Lifecycle { get; init; }
    public string? ItemKind { get; init; }
    public string? ThreadId { get; init; }
    public string? TurnId { get; init; }
    public string? RootTurnId { get; init; }
    public string? ResponseId { get; init; }
    public string? SenderThreadId { get; init; }
    public List<string> ReceiverThreadIds { get; init; } = [];
    public bool IsNativeOperation { get; init; }
    public bool IsHostWrapper { get; init; }
    public string? UsageScope { get; init; }
    public string? Role { get; init; }
    public string? Text { get; init; }
    public string? ToolId { get; init; }
    public string? ToolName { get; init; }
    public string? ToolPhase { get; init; }
    public string? ToolArguments { get; init; }
    public string? ToolOutput { get; init; }
    public string? CommandWorkingDirectory { get; init; }
    public int? ExitCode { get; init; }
    public string? Status { get; init; }
    public TokenMetrics? Usage { get; init; }
    public bool UsageIsCumulative { get; init; }
    public bool IsInitialization { get; init; }
    public bool TurnIdentityConflict { get; init; }
    public string? ChildThreadId { get; set; }
    public string? SpawnTask { get; init; }
    public string? ActualModel { get; init; }
    public string? ActualReasoningEffort { get; init; }

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
    public string? AgentPath { get; set; }
    public List<CodexEvent> Events { get; init; } = [];
    public List<Diagnostic> Diagnostics { get; init; } = [];
    public Dictionary<string, SpawnRecord> SpawnRecords { get; init; } = new(StringComparer.Ordinal);
    public DateTimeOffset? StartedAt => Events.Select(x => x.Timestamp).Where(x => x is not null).Min();
    public DateTimeOffset? FinishedAt => Events.Select(x => x.Timestamp).Where(x => x is not null).Max();
}

public sealed class SpawnRecord
{
    public string CallId { get; init; } = "";
    public string? SenderThreadId { get; set; }
    public string? ChildThreadId { get; set; }
    public List<string> ChildThreadIds { get; } = [];
    public string? ChildAgentPath { get; set; }
    public string? RequestedTaskName { get; set; }
    public string SpawnOutcome { get; set; } = "unknown";
    public string? RejectionCode { get; set; }
    public string? RejectionMessage { get; set; }
    [JsonIgnore]
    public bool CorrelationConflict { get; set; }
    public string? Task { get; set; }
    public string? ExecutionProfile { get; set; }
    public string? RequestedModel { get; set; }
    public string? RequestedReasoningEffort { get; set; }
    public string? ActualModel { get; set; }
    public string? ActualReasoningEffort { get; set; }
    public DateTimeOffset? Timestamp { get; init; }

    [JsonIgnore]
    public IEnumerable<string> Children =>
        ChildThreadIds.Count > 0
            ? ChildThreadIds
            : ChildThreadId is null ? [] : [ChildThreadId];
}

public sealed class CodexStateSnapshot
{
    public bool DatabaseAvailable { get; set; }
    public bool SpawnEdgesAvailable { get; set; }
    public Dictionary<string, string> ParentByChild { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> RolloutByThread { get; } = new(StringComparer.Ordinal);
    public List<Diagnostic> Diagnostics { get; } = [];
}
