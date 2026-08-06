namespace Idd.Factory.LiveTests.Models;

public sealed record AgentTrace(int SchemaVersion, string? RootThreadId, IReadOnlyList<AgentTraceNode> Agents, IReadOnlyList<AgentTraceDiagnostic> Diagnostics);

public sealed record AgentTraceNode(
    string ThreadId,
    string? ParentThreadId,
    string Role,
    string? WorkItem,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    long? DurationMs,
    int? ToolCallCount);

public sealed record AgentTraceDiagnostic(string Code, string Severity, string Message, string? ThreadId, string? File);
