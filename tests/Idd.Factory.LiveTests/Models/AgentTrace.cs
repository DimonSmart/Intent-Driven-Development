namespace Idd.Factory.LiveTests.Models;

public sealed record AgentTrace(int SchemaVersion, string? RootThreadId, IReadOnlyList<AgentTraceNode> Agents, IReadOnlyList<AgentTraceDiagnostic> Diagnostics);

public sealed record AgentTraceNode(
    string ThreadId,
    string? ParentThreadId,
    string Role,
    string? WorkItem,
    string? Action,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    long? DurationMs,
    int TurnCount,
    int ToolCallCount,
    long? InputTokens,
    long? CachedInputTokens,
    long? OutputTokens,
    long? ReasoningOutputTokens,
    long? TotalTokens,
    long? FreshInputTokens = null,
    double? CachedInputPercentage = null,
    int FailedToolCallCount = 0,
    int RejectedToolCallCount = 0,
    int RetryOrFallbackCallCount = 0,
    int FileReadCount = 0,
    int UniqueFileReadCount = 0,
    int RepeatedFileReadCount = 0,
    long FileReadBytes = 0,
    long WaitAgentMs = 0,
    int DispatchCharacters = 0,
    int DispatchUtf8Bytes = 0,
    IReadOnlyList<TokenUsageSnapshot>? TokenProgression = null,
    IReadOnlyList<AgentToolCall>? ToolCalls = null,
    IReadOnlyList<AgentFileRead>? FileReads = null,
    IReadOnlyList<DispatchReferenceSize>? DispatchReferences = null);

public sealed record AgentTraceDiagnostic(string Code, string Severity, string Message, string? ThreadId, string? File);

public sealed record TokenUsageSnapshot(
    int Sequence,
    DateTimeOffset? Timestamp,
    string? SourceEventType,
    long? InputTokens,
    long? CachedInputTokens,
    long? OutputTokens,
    long? ReasoningOutputTokens,
    long? TotalTokens,
    long? InputDelta,
    long? CachedInputDelta,
    long? FreshInputDelta,
    long? OutputDelta,
    bool Discontinuity,
    IReadOnlyList<string> ToolCallIdsInInterval);

public sealed record AgentToolCall(
    int Sequence,
    string? CallId,
    string Tool,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    long? DurationMs,
    string Status,
    bool IsFailure,
    bool IsRejected,
    bool IsRetryOrFallback,
    string? Operation,
    string? CommandSummary,
    int? ExitCode,
    long ResultBytes,
    IReadOnlyList<string> ChildThreadIds,
    bool? IsTerminalWait,
    int RepeatedWaitNumber,
    string? ChildRole,
    int DispatchCharacters,
    int DispatchUtf8Bytes);

public sealed record AgentFileRead(string Path, int Sequence, long ReturnedBytes, int ReturnedCharacters);
public sealed record DispatchReferenceSize(string Path, int? Characters, int? Utf8Bytes, string Kind);
