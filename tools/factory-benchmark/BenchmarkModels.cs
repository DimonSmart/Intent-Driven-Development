using System.Text.Json.Serialization;

namespace Idd.Factory.Benchmark;

public sealed class BenchmarkDefinition
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Model { get; set; } = "";
    public ReasoningDefinition Reasoning { get; set; } = new();
    public int Repeat { get; set; } = 3;
    public int TimeoutMinutes { get; set; } = 20;
    public string WindowsSandbox { get; set; } = "elevated";
    public string Task { get; set; } = "task.md";
    public List<string> IdealWorkItems { get; set; } = [];
    public AcceptanceDefinition Acceptance { get; set; } = new();
    public List<string> Modes { get; set; } = [];
}

public sealed class ReasoningDefinition { public string Effort { get; set; } = "high"; }

public sealed class AcceptanceDefinition
{
    public string Command { get; set; } = "powershell";
    public List<string> Arguments { get; set; } = [];
}

public static class BenchmarkModes
{
    public const string Direct = "direct";
    public const string StructuredSingle = "structured-single";
    public const string ManualIsolated = "manual-isolated";
    public const string FactorySplitReplay = "factory-split-replay";
    public const string Factory = "factory";
    public static readonly IReadOnlyList<string> All = [Direct, StructuredSingle, ManualIsolated, FactorySplitReplay, Factory];
}

public sealed record BenchmarkOptions(
    string BenchmarkDirectory,
    int? Repeat,
    string? Model,
    string? Output,
    IReadOnlyList<string>? Modes,
    bool KeepWorkspaces,
    int? TimeoutMinutes,
    string? WindowsSandbox,
    bool Force);

public sealed record InvocationMetrics(
    long InputTokens,
    long CachedInputTokens,
    long NewInputTokens,
    long OutputTokens,
    long ToolBatches,
    long Commands,
    long ToolOutputChars,
    long FailedCommands,
    long FailedToolOutputChars,
    long DurationMilliseconds,
    int ExitCode,
    string Role,
    string EventsPath,
    string? Error = null);

public sealed record AcceptanceResult(int ExitCode, long DurationMilliseconds, string StdoutPath, string StderrPath);

public sealed class BenchmarkRunResult
{
    public int SchemaVersion { get; init; } = 1;
    public required string Mode { get; init; }
    public required int Iteration { get; init; }
    public required string Status { get; init; }
    public required bool Successful { get; init; }
    public string? Failure { get; init; }
    public required IReadOnlyList<InvocationMetrics> Invocations { get; init; }
    public required AggregateMetrics Metrics { get; init; }
    public required AcceptanceResult Acceptance { get; init; }
    public required long AgentDurationMilliseconds { get; init; }
    public required long TotalDurationMilliseconds { get; init; }
    public required int CodexProcessCount { get; init; }
    public bool WorkspaceRetained { get; init; }
    public required EnvironmentRecord Environment { get; init; }
    public FactoryDecompositionRecord? FactoryDecomposition { get; init; }
}

public sealed record AggregateMetrics(
    long GrossInputTokens,
    long CachedInputTokens,
    long NewInputTokens,
    long OutputTokens,
    long ToolBatches,
    long Commands,
    long ToolOutputChars,
    long FailedCommands,
    long FailedToolOutputChars)
{
    public static AggregateMetrics From(IEnumerable<InvocationMetrics> source)
    {
        var values = source.ToArray();
        return new(values.Sum(x => x.InputTokens), values.Sum(x => x.CachedInputTokens), values.Sum(x => x.NewInputTokens),
            values.Sum(x => x.OutputTokens), values.Sum(x => x.ToolBatches), values.Sum(x => x.Commands),
            values.Sum(x => x.ToolOutputChars), values.Sum(x => x.FailedCommands), values.Sum(x => x.FailedToolOutputChars));
    }
}

public sealed record EnvironmentRecord(
    string Os,
    string DotnetVersion,
    string CodexVersion,
    string FactoryVersion,
    string FactoryPluginVersion,
    string Model,
    string ReasoningEffort,
    string? IddFactoryModel,
    string? WindowsSandbox,
    string GitRevision,
    bool GitDirty,
    string BenchmarkDefinitionSha256,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, string> SkillVersions);

public sealed record FactoryWorkItemRecord(string Id, string Kind, string Title, string ContractPath);
public sealed record FactoryDecompositionRecord(bool SharedWithFactoryRun, long InputTokens, long CachedInputTokens, long OutputTokens, IReadOnlyList<FactoryWorkItemRecord> WorkItems);

public sealed record ModeAggregate(
    int Runs,
    int SuccessfulRuns,
    double SuccessRate,
    AggregateMetrics? Median,
    AggregateMetrics? Minimum,
    AggregateMetrics? Maximum,
    long? MedianCodexProcessCount,
    long? MedianAgentDurationMilliseconds,
    long? MedianAcceptanceDurationMilliseconds,
    long? MedianTotalDurationMilliseconds);

public sealed record ComparisonReport(
    long? StructuringOverhead,
    long? IsolationOverhead,
    long? DecompositionChoiceOverhead,
    long? FactoryOrchestrationOverhead,
    long? TotalFactoryOverhead,
    double? FactoryToDirect,
    double? ManualIsolatedToDirect,
    double? FactorySplitReplayToDirect,
    double? FactoryToFactorySplitReplay);

public sealed class BenchmarkReport
{
    public int SchemaVersion { get; init; } = 1;
    public required string Benchmark { get; init; }
    public required EnvironmentRecord Environment { get; init; }
    public required int Repeats { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<BenchmarkRunResult>> Modes { get; init; }
    public required IReadOnlyDictionary<string, ModeAggregate> Aggregates { get; init; }
    public required ComparisonReport Comparisons { get; init; }
    public required IReadOnlyList<string> ComparabilityWarnings { get; init; }
    public required long TotalBenchmarkDurationMilliseconds { get; init; }
}

[JsonSerializable(typeof(BenchmarkReport))]
internal partial class BenchmarkJsonContext : JsonSerializerContext;
