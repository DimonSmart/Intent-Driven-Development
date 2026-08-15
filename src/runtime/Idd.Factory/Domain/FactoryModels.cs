using System.Text.Json;
using System.Text.Json.Serialization;

namespace Idd.Factory.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<FactoryRunStatus>))]
public enum FactoryRunStatus { Running, Blocked, Completed, Cancelled, Failed }

[JsonConverter(typeof(JsonStringEnumConverter<WorkItemKind>))]
public enum WorkItemKind { Subtask, ReviewCheckpoint, CorrectiveSubtask }

[JsonConverter(typeof(JsonStringEnumConverter<WorkItemStatus>))]
public enum WorkItemStatus
{
    Planned, Ready, Dispatching, Running, AwaitingVerification, Completed,
    Blocked, Failed, Superseded, Cancelled
}

public sealed record FactoryState
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required string MethodologyVersion { get; init; }
    public required string RuntimeVersion { get; init; }
    public required string RunId { get; init; }
    public long Revision { get; set; }
    public FactoryRunStatus RunStatus { get; set; } = FactoryRunStatus.Running;
    public required string CurrentWorkflowStep { get; set; }
    public required string WorkflowName { get; init; }
    public required string WorkflowHash { get; init; }
    public required string RequestPath { get; init; }
    public required string BaselineRevision { get; init; }
    public List<WorkItemState> WorkItems { get; init; } = [];
    public string? CurrentAttemptId { get; set; }
    public int AttemptSequence { get; set; }
    public int ReplanCount { get; set; }
    public int CorrectiveCycleCount { get; set; }
    public int FinalVerificationFixAttemptCount { get; set; }
    public FactoryBlocker? Blocker { get; set; }
    public FinalReviewState? FinalReview { get; set; }
    public List<string> VerificationEvidenceRefs { get; init; } = [];
    public string? IntentSnapshotHash { get; set; }
    public List<string> ClarificationRefs { get; init; } = [];
}

public sealed record WorkItemState
{
    public required string Id { get; init; }
    public required int Sequence { get; set; }
    public required WorkItemKind Kind { get; init; }
    public WorkItemStatus Status { get; set; } = WorkItemStatus.Planned;
    public required string ContractPath { get; init; }
    public List<string> Dependencies { get; init; } = [];
    public List<string> CoveredWorkItems { get; init; } = [];
    public int AttemptCount { get; set; }
    public string? CurrentAttemptId { get; set; }
    public List<string> VerificationCheckIds { get; init; } = [];
    public List<string> VerificationEvidenceRefs { get; init; } = [];
    public int VerificationFixAttemptCount { get; set; }
    public string? LastResultRef { get; set; }
}

public sealed record FactoryBlocker(string Code, string Reason, string ResumeWhen, JsonElement? Payload = null);
public sealed record FinalReviewState(string Verdict, string? ResultRef, int AttemptCount);

[JsonConverter(typeof(JsonStringEnumConverter<AgentExecutionProfile>))]
public enum AgentExecutionProfile
{
    [JsonStringEnumMemberName("read-only")]
    ReadOnly,
    [JsonStringEnumMemberName("workspace-write")]
    WorkspaceWrite
}

public sealed record FactoryAgentContract(string Role, string SkillName, AgentExecutionProfile ExecutionProfile);

public static class FactoryAgentCatalog
{
    private static readonly IReadOnlyDictionary<string, FactoryAgentContract> Contracts =
        new[]
        {
            new FactoryAgentContract("task-decomposer", "idd-factory-decompose-task", AgentExecutionProfile.ReadOnly),
            new FactoryAgentContract("implementer", "idd-factory-execute-subtask", AgentExecutionProfile.WorkspaceWrite),
            new FactoryAgentContract("checkpoint-reviewer", "idd-factory-review-checkpoint", AgentExecutionProfile.ReadOnly),
            new FactoryAgentContract("final-reviewer", "idd-factory-review-task", AgentExecutionProfile.ReadOnly),
            new FactoryAgentContract("factory-replanner", "idd-factory-replan", AgentExecutionProfile.ReadOnly)
        }.ToDictionary(contract => contract.Role, StringComparer.Ordinal);

    public static FactoryAgentContract Resolve(string role) =>
        Contracts.TryGetValue(role, out var contract)
            ? contract
            : throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown Factory agent role.");
}

public sealed record AgentInvocation
{
    public const int CurrentProtocolVersion = 1;
    public int ProtocolVersion { get; init; } = CurrentProtocolVersion;
    public required string RunId { get; init; }
    public required string AttemptId { get; init; }
    public required string Role { get; init; }
    public string? WorkItemId { get; init; }
    public required string Workspace { get; init; }
    public required string ResultPath { get; init; }
    public required string SkillName { get; init; }
    public required AgentExecutionProfile ExecutionProfile { get; init; }
    public required string Input { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required string WorkspaceFingerprint { get; init; }
}

public sealed record AgentResultEnvelope
{
    public int ProtocolVersion { get; init; }
    public required string RunId { get; init; }
    public required string AttemptId { get; init; }
    public required string Role { get; init; }
    public required string Outcome { get; init; }
    public string? Reason { get; init; }
    public JsonElement? Payload { get; init; }
    public JsonElement? Metrics { get; init; }
}

public sealed record AgentRunHandle(string AttemptId, int ProcessId, string BackendHandle);

[JsonConverter(typeof(JsonStringEnumConverter<AgentTerminationKind>))]
public enum AgentTerminationKind { CleanExit, ForcedAfterResult, Cancelled, TransportFailure }

public sealed record AgentProcessResult(
    int? ExitCode,
    string Stdout,
    string Stderr,
    bool CompleteResultObserved,
    bool KillRequired,
    AgentTerminationKind TerminationKind);

public sealed record AgentExecutionConfiguration(string? Model = null, string? ReasoningEffort = null, string? WindowsSandbox = null)
{
    public string RequestedModel => string.IsNullOrWhiteSpace(Model) ? "default/unpinned" : Model;
    public string RequestedReasoningEffort => string.IsNullOrWhiteSpace(ReasoningEffort) ? "default/unpinned" : ReasoningEffort;
}

public sealed record AgentCapabilityPolicy(bool InheritUserSkills, string Profile)
{
    public static AgentCapabilityPolicy ProductionDefault { get; } = new(true, "production-default");
}

public sealed record AgentAttemptTelemetry(
    string Role,
    string SkillName,
    string Backend,
    AgentExecutionProfile ExecutionProfile,
    string SkillInvocationMode,
    int InputChars,
    string RequestedModel,
    string RequestedReasoningEffort,
    string EffectiveModel,
    string EffectiveReasoningEffort,
    string SkillSource,
    string SkillSourceVersion,
    string UserSkillInheritancePolicy,
    int ProjectLocalSkillCount,
    int InheritedUserSkillCount,
    string CapabilityProfile,
    string? WindowsSandbox,
    int WindowsAppsPathEntriesRemoved);

public sealed record AgentExecutionResult(AgentResultEnvelope Result, AgentProcessResult Process);

public sealed record FactoryCliOutcome(
    string FactoryOutcome,
    string RunId,
    string? Reason = null,
    string? ResumeWhen = null,
    string? ResultDirectory = null,
    JsonElement? Payload = null);

public static class FactoryJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
}
