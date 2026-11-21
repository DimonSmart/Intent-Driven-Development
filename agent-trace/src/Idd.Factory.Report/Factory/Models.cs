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
    public int SchemaVersion { get; init; } = 3;
    public string Repository { get; init; } = "";
    public HostInfo Host { get; init; } = new();
    public RunInfo Run { get; init; } = new();
    public List<AgentReport> Agents { get; init; } = [];
    public List<TaskReport> Tasks { get; init; } = [];
    public List<FactoryBatchReport> Batches { get; init; } = [];
    public ReportMetrics Metrics { get; init; } = new();
    public List<TimelineEvent> Timeline { get; init; } = [];
    public List<Diagnostic> Diagnostics { get; init; } = [];
    public FactoryProjectState FactoryProjectState { get; init; } = new();
    public CompletionReport Completion { get; init; } = new();
}

public sealed class CompletionReport
{
    public bool? PlannerDone { get; init; }
    public string ProjectVerification { get; init; } = "unavailable";
    public string DeclaredResult { get; init; } = "unavailable";
    public string ProtocolValidation { get; init; } = "unavailable";
}

public sealed class RunInfo
{
    public string RootThreadId { get; init; } = "";
    public string? SupervisorThreadId { get; init; }
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
    public string? NativeTaskName { get; init; }
    public int? BatchOrdinal { get; init; }
    public int Sequence { get; set; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public long? DurationMilliseconds =>
        StartedAt is not null && FinishedAt is not null
            ? (long)(FinishedAt.Value - StartedAt.Value).TotalMilliseconds
            : null;
    public string? Task { get; set; }
    public string? TaskTitle { get; set; }
    public string? ExecutionProfile { get; set; }
    public string? SpawnExecutionProfile { get; set; }
    public string? RequestedModel { get; set; }
    public string? RequestedReasoningEffort { get; set; }
    public string? ActualModel { get; set; }
    public string? ActualReasoningEffort { get; set; }
    public string? PlannerOutcome { get; set; }
    public DateTimeOffset? PlannerOutcomeAt { get; set; }
    public TokenMetrics Tokens { get; set; } = new();
    [JsonIgnore]
    public bool TokensAuthoritative { get; set; }
    public ToolMetrics Tools { get; set; } = new();
    [JsonIgnore]
    public string? RolloutPath { get; init; }
}

public sealed class TaskReport
{
    public int Number { get; init; }
    public string AgentThreadId { get; init; } = "";
    public int? BatchOrdinal { get; init; }
    public string? Text { get; init; }
    public string? ExecutionProfile { get; init; }
    public string? SpawnExecutionProfile { get; init; }
    public string? RequestedModel { get; init; }
    public string? RequestedReasoningEffort { get; init; }
    public string? ActualModel { get; init; }
    public string? ActualReasoningEffort { get; init; }
    public string Status { get; init; } = "unknown";
    public long? DurationMilliseconds { get; init; }
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
    public int FactoryRootInvocations { get; init; }
    public int SupervisorInvocations { get; init; }
    public int WorkerInvocations { get; init; }
    public int OtherChildAgents { get; init; }
    public int MaximumDepth { get; init; }
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
