namespace Idd.Factory.LiveTests.Models;

public sealed record EfficiencyTelemetry(
    int SchemaVersion,
    EfficiencySummary Summary,
    IReadOnlyList<EfficiencyRole> Roles,
    IReadOnlyList<EfficiencyAgent> Agents,
    IReadOnlyList<EfficiencyToolCall> ToolCalls,
    IReadOnlyList<EfficiencyFileAccess> FileAccess,
    IReadOnlyList<EfficiencyGroup> Groups,
    EfficiencyHotspots Hotspots,
    IReadOnlyList<AgentTraceDiagnostic> Diagnostics);

public sealed record EfficiencySummary(long? InputTokens, long? CachedInputTokens, long? FreshInputTokens, double? CachedInputPercentage, long? OutputTokens, long? ReasoningOutputTokens, long? TotalTokens, int AgentThreads, int ModelTurns, int ToolCalls, int FailedToolCalls, int RejectedToolCalls, int RetryOrFallbackCalls, long? WallTimeMs);
public sealed record EfficiencyRole(string Role, int Agents, long? InputTokens, long? CachedInputTokens, long? FreshInputTokens, double? CachedInputPercentage, long? OutputTokens, long? TotalTokens, int ToolCalls, long DurationMs, double? InputSharePercentage, double? FreshInputSharePercentage, double? ToolCallSharePercentage, int? MandatoryReferenceCharacters, int? MandatoryReferenceUtf8Bytes);
public sealed record EfficiencyGroup(string Group, int Agents, long? InputTokens, long? FreshInputTokens, int ToolCalls, long DurationMs, double? InputSharePercentage, double? FreshInputSharePercentage);
public sealed record EfficiencyAgent(string ThreadId, string? ParentThreadId, string Role, string? WorkItem, string? Action, long? DurationMs, int TurnCount, int ToolCallCount, long? InputTokens, long? CachedInputTokens, long? FreshInputTokens, double? CachedInputPercentage, long? OutputTokens, long? ReasoningOutputTokens, long? TotalTokens, int DispatchCharacters, int DispatchUtf8Bytes, int FailedToolCalls, int RejectedToolCalls, int RetryOrFallbackCalls, int FileReads, int UniqueFileReads, int RepeatedFileReads, long FileReadBytes, long WaitAgentMs, double? InputPerToolCall, IReadOnlyList<TokenUsageSnapshot> TokenProgression, IReadOnlyList<DispatchReferenceSize> DispatchReferences, AgentTerminalResult? TerminalResult = null);
public sealed record EfficiencyToolCall(int Sequence, string ThreadId, string Role, string? CallId, string Tool, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, long? DurationMs, string Status, bool IsFailure, bool IsRejected, bool IsRetryOrFallback, string? Operation, string? CommandSummary, int? ExitCode, long ResultBytes, IReadOnlyList<string> ChildThreadIds, bool? IsTerminalWait, int RepeatedWaitNumber, string? ChildRole, int DispatchCharacters, int DispatchUtf8Bytes);
public sealed record EfficiencyFileAccess(string Path, int ReadCount, int DistinctAgentCount, long TotalReturnedBytes, IReadOnlyList<string> Agents);
public sealed record EfficiencyHotspots(IReadOnlyList<string> TopAgentsByInput, IReadOnlyList<string> TopAgentsByFreshInput, IReadOnlyList<string> TopAgentsByToolCalls, IReadOnlyList<string> TopRepeatedFiles, IReadOnlyList<string> TopTools, IReadOnlyList<string> LongestToolCalls, IReadOnlyList<string> LongestWaits, IReadOnlyList<string> FailedOrRejectedCalls, IReadOnlyList<string> HighestCacheRatioAgents, IReadOnlyList<string> HighestInputPerToolAgents);
