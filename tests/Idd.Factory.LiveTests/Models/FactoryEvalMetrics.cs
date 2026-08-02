namespace Idd.Factory.LiveTests.Models;

public sealed class FactoryEvalMetrics
{
    public int SchemaVersion { get; init; } = 1;
    public string? ModelEffective { get; set; }
    public string? ReasoningEffortEffective { get; set; }
    public string? SessionId { get; set; }
    public long ModelTurnCount { get; set; }
    public long ToolCallCount { get; set; }
    public long SpawnAgentCallCount { get; set; }
    public long WaitAgentCallCount { get; set; }
    public long? InputTokens { get; set; }
    public long? CachedInputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public long? TotalTokens { get; set; }
    public long? WallTimeMs { get; set; }
    public long? ImplementationAgentCount { get; set; }
    public long? ReviewAgentCount { get; set; }
    public long? CoordinatorAgentCount { get; set; }
    public long? FullHistoryForkCount { get; set; }
    public long? RootProductWriteCallCount { get; set; }
    public long? CoordinatorProductWriteCallCount { get; set; }
    public long? ReviewerWriteCallCount { get; set; }
    public long? LeafSpawnCallCount { get; set; }
    public long UnknownEventCount { get; set; }
    public long MalformedLineCount { get; set; }
}
