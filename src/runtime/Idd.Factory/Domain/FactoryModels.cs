using System.Text.Json;
using System.Text.Json.Serialization;
using Idd.Factory.Agents;

namespace Idd.Factory.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<FactoryRunStatus>))]
public enum FactoryRunStatus { Running, Blocked, Completed, Cancelled, Failed }

[JsonConverter(typeof(JsonStringEnumConverter<WorkItemKind>))]
public enum WorkItemKind { Subtask, ReviewCheckpoint, CorrectiveSubtask }

[JsonConverter(typeof(JsonStringEnumConverter<WorkDefinitionState>))]
public enum WorkDefinitionState { Outline, Executable }

[JsonConverter(typeof(JsonStringEnumConverter<WorkItemStatus>))]
public enum WorkItemStatus
{
    Planned, Ready, Dispatching, Running, Waiting, AwaitingVerification, Completed,
    Blocked, Failed, Superseded, Cancelled
}

[JsonConverter(typeof(JsonStringEnumConverter<VerificationExpectation>))]
public enum VerificationExpectation
{
    [JsonStringEnumMemberName("must-pass")]
    MustPass,
    [JsonStringEnumMemberName("may-fail")]
    MayFail
}

[JsonConverter(typeof(JsonStringEnumConverter<VerificationDecision>))]
public enum VerificationDecision { None, Ok, ExpectedFailure, UnexpectedFailure }

public sealed record FactoryState
{
    public const int CurrentSchemaVersion = 8;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required string MethodologyVersion { get; init; }
    public required string RuntimeVersion { get; init; }
    public required string RunId { get; init; }
    public long Revision { get; set; }
    public long GraphRevision { get; set; }
    public FactoryRunStatus RunStatus { get; set; } = FactoryRunStatus.Running;
    public required string FactoryConfigurationHash { get; init; }
    public required string RequestPath { get; init; }
    public List<WorkItemState> WorkItems { get; init; } = [];
    public string? CurrentAttemptId { get; set; }
    public int AttemptSequence { get; set; }
    public int ReplanCount { get; set; }
    public int CorrectiveCycleCount { get; set; }
    public bool FinalVerificationPassed { get; set; }
    public long? FinalVerificationGraphRevision { get; set; }
    public FactoryBlocker? Blocker { get; set; }
    public PendingContinuation? PendingContinuation { get; set; }
    public PendingVerificationSession? PendingVerificationSession { get; set; }
    public PendingReplanTrigger? PendingReplanTrigger { get; set; }
    public FinalReviewState? FinalReview { get; set; }
    public List<string> VerificationEvidenceRefs { get; init; } = [];
    public string? IntentSnapshotHash { get; set; }
    public List<string> ClarificationRefs { get; init; } = [];
    public List<string> FactoryRunChangedPaths { get; init; } = [];
}

public sealed record WorkItemState
{
    public required string Id { get; init; }
    public required int Sequence { get; set; }
    public required WorkItemKind Kind { get; init; }
    public string? Capability { get; set; }
    public WorkDefinitionState DefinitionState { get; set; } = WorkDefinitionState.Executable;
    public WorkItemStatus Status { get; set; } = WorkItemStatus.Planned;
    public required string ContractPath { get; set; }
    public int ContractRevision { get; set; } = 1;
    public List<string> Dependencies { get; init; } = [];
    public List<string> CoveredWorkItems { get; init; } = [];
    public int AttemptCount { get; set; }
    public string? CurrentAttemptId { get; set; }
    public List<string> VerificationCheckIds { get; init; } = [];
    public Dictionary<string, VerificationExpectation> VerificationExpectations { get; init; } = new(StringComparer.Ordinal);
    public List<string> VerificationEvidenceRefs { get; init; } = [];
    public VerificationDecision LastVerificationDecision { get; set; }
    public string? LastResultRef { get; set; }
    public List<string> PriorResultRefs { get; init; } = [];
    public List<string> ChangedPaths { get; init; } = [];
    public string? LastSemanticOutcome { get; set; }
    public bool IsFinalReview { get; init; }
    public long? ReviewTargetGraphRevision { get; init; }
}

public sealed record FactoryBlocker(string Code, string Reason, string ResumeWhen, JsonElement? Payload = null);

public sealed record PendingContinuation(
    ContinuationKind Kind,
    string? WorkItemId,
    string? VerificationContext,
    string Code,
    bool IsResumable,
    SemanticOperationKind Operation = SemanticOperationKind.None,
    string? OperationInput = null,
    string? VerificationCheckId = null,
    VerificationContinuationStage VerificationStage = VerificationContinuationStage.ExecuteCheck);

public sealed record PendingVerificationSession(
    string Context,
    string? WorkItemId,
    List<string> CheckIds,
    List<string> ChangedPaths,
    int NextCheckIndex,
    List<string> CompletedCheckIds,
    List<string> FailedCheckIds,
    List<string> EvidenceRefs,
    string? PendingCheckId,
    string? PendingCheckDefinitionHash,
    string PolicyHash,
    VerificationContinuationStage Stage);

public sealed record PendingReplanTrigger(
    string SourceCapability,
    string? SourceWorkItemId,
    string ResultRef,
    string? Reason,
    JsonElement? Payload,
    List<string> EvidenceRefs);

[JsonConverter(typeof(JsonStringEnumConverter<ContinuationKind>))]
public enum ContinuationKind { SemanticInvocation, VerificationGate, IntentGate, Clarification, Terminal }

[JsonConverter(typeof(JsonStringEnumConverter<VerificationContinuationStage>))]
public enum VerificationContinuationStage { ExecuteCheck, AwaitingConfirmation, AwaitingManualResult }

[JsonConverter(typeof(JsonStringEnumConverter<VerificationConfirmation>))]
public enum VerificationConfirmation { None, Approve, Decline }

[JsonConverter(typeof(JsonStringEnumConverter<SemanticOperationKind>))]
public enum SemanticOperationKind
{
    None,
    Decomposition,
    ScopedRefinement,
    WorkItemExecution,
    GlobalReplan
}

public sealed record FinalReviewState(
    string Verdict,
    string? ResultRef,
    int AttemptCount,
    string? WorkItemId = null,
    long? ReviewedGraphRevision = null);

[JsonConverter(typeof(JsonStringEnumConverter<AgentExecutionProfile>))]
public enum AgentExecutionProfile
{
    [JsonStringEnumMemberName("read-only")]
    ReadOnly,
    [JsonStringEnumMemberName("workspace-write")]
    WorkspaceWrite
}

public sealed record FactoryAgentContract(string Role, string SkillName, AgentExecutionProfile ExecutionProfile);

public sealed record FactoryCapabilityContract(
    string Capability,
    FactoryAgentContract Agent,
    bool WorkItemCapability);

public static class FactoryCapabilityCatalog
{
    private static readonly IReadOnlyDictionary<string, FactoryCapabilityContract> Contracts =
        new[]
        {
            new FactoryCapabilityContract("initial-decomposition", new("task-decomposer", "idd-factory-decompose-task", AgentExecutionProfile.ReadOnly), false),
            new FactoryCapabilityContract("scoped-refinement", new("task-decomposer", "idd-factory-decompose-task", AgentExecutionProfile.ReadOnly), false),
            new FactoryCapabilityContract("global-replan", new("factory-replanner", "idd-factory-replan", AgentExecutionProfile.ReadOnly), false),
            new FactoryCapabilityContract("implementation", new("implementer", "idd-factory-execute-subtask", AgentExecutionProfile.WorkspaceWrite), true),
            new FactoryCapabilityContract("research", new("researcher", "idd-factory-research", AgentExecutionProfile.ReadOnly), true),
            new FactoryCapabilityContract("semantic-review", new("final-reviewer", "idd-factory-review-task", AgentExecutionProfile.ReadOnly), true),
            new FactoryCapabilityContract("documentation", new("implementer", "idd-factory-execute-subtask", AgentExecutionProfile.WorkspaceWrite), true)
        }.ToDictionary(contract => contract.Capability, StringComparer.Ordinal);

    public static IReadOnlyCollection<string> WorkItemCapabilities { get; } =
        Contracts.Values.Where(x => x.WorkItemCapability).Select(x => x.Capability).ToArray();

    public static FactoryCapabilityContract Resolve(string capability) =>
        Contracts.TryGetValue(capability, out var contract)
            ? contract
            : throw new AgentProtocolException("UNKNOWN_CAPABILITY", $"Unknown Factory capability '{capability}'.");

    public static FactoryCapabilityContract ResolveWorkItem(string capability)
    {
        var contract = Resolve(capability);
        if (!contract.WorkItemCapability)
            throw new AgentProtocolException("UNKNOWN_CAPABILITY", $"Capability '{capability}' cannot be dispatched as work-item work.");
        return contract;
    }
}

// Retained only as a role-level adapter compatibility helper. Production scheduling is capability-based.
public static class FactoryAgentCatalog
{
    private static readonly IReadOnlyDictionary<string, FactoryAgentContract> Contracts =
        new[]
        {
            new FactoryAgentContract("task-decomposer", "idd-factory-decompose-task", AgentExecutionProfile.ReadOnly),
            new FactoryAgentContract("implementer", "idd-factory-execute-subtask", AgentExecutionProfile.WorkspaceWrite),
            new FactoryAgentContract("checkpoint-reviewer", "idd-factory-review-checkpoint", AgentExecutionProfile.ReadOnly),
            new FactoryAgentContract("final-reviewer", "idd-factory-review-task", AgentExecutionProfile.ReadOnly),
            new FactoryAgentContract("factory-replanner", "idd-factory-replan", AgentExecutionProfile.ReadOnly),
            new FactoryAgentContract("researcher", "idd-factory-research", AgentExecutionProfile.ReadOnly)
        }.ToDictionary(contract => contract.Role, StringComparer.Ordinal);

    public static FactoryAgentContract Resolve(string role) =>
        Contracts.TryGetValue(role, out var contract)
            ? contract
            : throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown Factory agent role.");
}

public sealed record AgentInvocation
{
    public const int CurrentProtocolVersion = 2;
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
