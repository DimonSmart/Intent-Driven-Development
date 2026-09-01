using System.Text.Json;
using System.Text.Json.Serialization;
using Idd.Factory.Agents;

namespace Idd.Factory.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<FactoryRunStatus>))]
public enum FactoryRunStatus { Running, Blocked, Completed, Cancelled, Failed }

[JsonConverter(typeof(JsonStringEnumConverter<CurrentWorkPhase>))]
public enum CurrentWorkPhase { Ready, Running, AwaitingVerification, Blocked }

[JsonConverter(typeof(JsonStringEnumConverter<VerificationExpectation>))]
public enum VerificationExpectation
{
    [JsonStringEnumMemberName("must-pass")] MustPass,
    [JsonStringEnumMemberName("may-fail")] MayFail
}

[JsonConverter(typeof(JsonStringEnumConverter<VerificationDecision>))]
public enum VerificationDecision { None, Ok, ExpectedFailure, UnexpectedFailure }

public sealed record FactoryState
{
    public const int CurrentSchemaVersion = 9;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required string MethodologyVersion { get; init; }
    public required string RuntimeVersion { get; init; }
    public required string RunId { get; init; }
    public long Revision { get; set; }
    public long PlanRevision { get; set; }
    public long NextWorkItemNumber { get; set; } = 1;
    public FactoryRunStatus RunStatus { get; set; } = FactoryRunStatus.Running;
    public required string FactoryConfigurationHash { get; init; }
    public required string RequestPath { get; init; }
    public List<CompletedWorkItem> Completed { get; init; } = [];
    public PlannedWorkItem? Current { get; set; }
    public CurrentWorkPhase? CurrentPhase { get; set; }
    public List<PlannedWorkItem> Remaining { get; init; } = [];
    public string? CurrentAttemptId { get; set; }
    public int AttemptSequence { get; set; }
    public int ReplanCount { get; set; }
    public int CorrectiveCycleCount { get; set; }
    public bool InitialPlanningCompleted { get; set; }
    public int PlannedThroughCompletedCount { get; set; }
    public bool FinalVerificationPassed { get; set; }
    public long? FinalVerificationPlanRevision { get; set; }
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

public sealed record PlannedWorkItem
{
    public required string Id { get; init; }
    public required string Capability { get; init; }
    public required string ContractPath { get; init; }
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
}

public sealed record CompletedWorkItem
{
    public required string Id { get; init; }
    public required string Capability { get; init; }
    public required string ContractPath { get; init; }
    public string? ResultRef { get; init; }
    public List<string> ChangedPaths { get; init; } = [];
    public List<string> VerificationEvidenceRefs { get; init; } = [];
    public VerificationDecision VerificationDecision { get; init; }
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

public sealed record PendingReplanTrigger(string SourceCapability, string? SourceWorkItemId, string ResultRef, string? Reason, JsonElement? Payload, List<string> EvidenceRefs);

[JsonConverter(typeof(JsonStringEnumConverter<ContinuationKind>))]
public enum ContinuationKind { SemanticInvocation, VerificationGate, IntentGate, Clarification, Terminal }
[JsonConverter(typeof(JsonStringEnumConverter<VerificationContinuationStage>))]
public enum VerificationContinuationStage { ExecuteCheck, AwaitingConfirmation, AwaitingManualResult }
[JsonConverter(typeof(JsonStringEnumConverter<VerificationConfirmation>))]
public enum VerificationConfirmation { None, Approve, Decline }
[JsonConverter(typeof(JsonStringEnumConverter<SemanticOperationKind>))]
public enum SemanticOperationKind { None, Planning, WorkItemExecution, FinalReview }

public sealed record FinalReviewState(string Verdict, string? ResultRef, int AttemptCount, long? ReviewedPlanRevision = null);

[JsonConverter(typeof(JsonStringEnumConverter<AgentExecutionProfile>))]
public enum AgentExecutionProfile
{
    [JsonStringEnumMemberName("read-only")] ReadOnly,
    [JsonStringEnumMemberName("workspace-write")] WorkspaceWrite
}

public sealed record FactoryAgentContract(string Role, string SkillName, AgentExecutionProfile ExecutionProfile);
public sealed record FactoryCapabilityContract(
    string Capability,
    FactoryAgentContract Agent,
    bool WorkItemCapability,
    SemanticOperationKind SemanticOperation);

public static class FactoryCapabilityCatalog
{
    private static readonly IReadOnlyDictionary<string, FactoryCapabilityContract> Contracts = new[]
    {
        new FactoryCapabilityContract("planning", new("task-decomposer", "idd-factory-decompose-task", AgentExecutionProfile.ReadOnly), false, SemanticOperationKind.Planning),
        new FactoryCapabilityContract("implementation", new("implementer", "idd-factory-execute-subtask", AgentExecutionProfile.WorkspaceWrite), true, SemanticOperationKind.WorkItemExecution),
        new FactoryCapabilityContract("research", new("researcher", "idd-factory-research", AgentExecutionProfile.ReadOnly), true, SemanticOperationKind.WorkItemExecution),
        new FactoryCapabilityContract("semantic-review", new("checkpoint-reviewer", "idd-factory-review-checkpoint", AgentExecutionProfile.ReadOnly), true, SemanticOperationKind.WorkItemExecution),
        new FactoryCapabilityContract("final-review", new("final-reviewer", "idd-factory-review-task", AgentExecutionProfile.ReadOnly), false, SemanticOperationKind.FinalReview)
    }.ToDictionary(x => x.Capability, StringComparer.Ordinal);

    public static IReadOnlyCollection<string> WorkItemCapabilities { get; } = Contracts.Values.Where(x => x.WorkItemCapability).Select(x => x.Capability).ToArray();
    public static FactoryCapabilityContract Resolve(string capability) => Contracts.TryGetValue(capability, out var value)
        ? value : throw new AgentProtocolException("UNKNOWN_CAPABILITY", $"Unknown Factory capability '{capability}'.");
    public static SemanticOperationKind ResolveSemanticOperation(string capability) => Resolve(capability).SemanticOperation;
    public static FactoryCapabilityContract ResolveWorkItem(string capability)
    {
        var value = Resolve(capability);
        if (!value.WorkItemCapability) throw new AgentProtocolException("UNKNOWN_CAPABILITY", $"Capability '{capability}' cannot be dispatched as work-item work.");
        return value;
    }
}

public sealed record AgentInvocation
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required string RunId { get; init; }
    public required string AttemptId { get; init; }
    public required string Capability { get; init; }
    public required string Role { get; init; }
    public string? WorkItemId { get; init; }
    public required string Workspace { get; init; }
    public required string RawResultPath { get; init; }
    public required string SkillName { get; init; }
    public required AgentExecutionProfile ExecutionProfile { get; init; }
    public required string SemanticResultSchema { get; init; }
    public required string Input { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
}

public sealed record SemanticAgentResult
{
    public required string Outcome { get; init; }
    public string? Summary { get; init; }
    public List<string>? DeclaredChanges { get; init; }
    public List<string>? Concerns { get; init; }
    public List<string>? VerificationClaims { get; init; }
    public JsonElement? Tasks { get; init; }
    public string? Reason { get; init; }
    public JsonElement? Payload { get; init; }
    public JsonElement? Metrics { get; init; }
}

public sealed record SemanticPlannedTask
{
    public required string Capability { get; init; }
    public required string Task { get; init; }
}

public sealed record AttemptIdentity
{
    public required string RunId { get; init; }
    public required string AttemptId { get; init; }
    public required string Capability { get; init; }
    public required string Role { get; init; }
    public string? WorkItemId { get; init; }

    public static AttemptIdentity From(AgentInvocation invocation) => new()
    {
        RunId = invocation.RunId,
        AttemptId = invocation.AttemptId,
        Capability = invocation.Capability,
        Role = invocation.Role,
        WorkItemId = invocation.WorkItemId
    };
}

public sealed record PersistedAttemptResult
{
    public const int CurrentSchemaVersion = 2;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required AttemptIdentity Invocation { get; init; }
    public required SemanticAgentResult SemanticResult { get; init; }
    public required DateTimeOffset ReceivedAt { get; init; }
    public required AgentTerminationKind TerminationKind { get; init; }
}

public sealed record BoundSemanticAgentResult(string AttemptId, SemanticAgentResult SemanticResult)
{
    public string Outcome => SemanticResult.Outcome;
    public string? Summary => SemanticResult.Summary;
    public IReadOnlyList<string>? DeclaredChanges => SemanticResult.DeclaredChanges;
    public IReadOnlyList<string>? Concerns => SemanticResult.Concerns;
    public IReadOnlyList<string>? VerificationClaims => SemanticResult.VerificationClaims;
    public JsonElement? Tasks => SemanticResult.Tasks;
    public string? Reason => SemanticResult.Reason;
    public JsonElement? Payload => SemanticResult.Payload;
    public JsonElement? Metrics => SemanticResult.Metrics;
}

public sealed record AgentRunHandle(string AttemptId, int ProcessId, string BackendHandle);
[JsonConverter(typeof(JsonStringEnumConverter<AgentTerminationKind>))]
public enum AgentTerminationKind { CleanExit, ForcedAfterResult, Cancelled, TransportFailure }
public sealed record AgentProcessResult(int? ExitCode, string Stdout, string Stderr, bool CompleteResultObserved, bool KillRequired, AgentTerminationKind TerminationKind);
public sealed record AgentExecutionConfiguration(string? Model = null, string? ReasoningEffort = null, string? WindowsSandbox = null)
{
    public string RequestedModel => string.IsNullOrWhiteSpace(Model) ? "default/unpinned" : Model;
    public string RequestedReasoningEffort => string.IsNullOrWhiteSpace(ReasoningEffort) ? "default/unpinned" : ReasoningEffort;
}
public sealed record AgentCapabilityPolicy(bool InheritUserSkills, string Profile) { public static AgentCapabilityPolicy ProductionDefault { get; } = new(true, "production-default"); }
public sealed record AgentAttemptTelemetry(string Role, string SkillName, string Backend, AgentExecutionProfile ExecutionProfile, string SkillInvocationMode, int InputChars, string RequestedModel, string RequestedReasoningEffort, string EffectiveModel, string EffectiveReasoningEffort, string SkillSource, string SkillSourceVersion, string UserSkillInheritancePolicy, int ProjectLocalSkillCount, int InheritedUserSkillCount, string CapabilityProfile, string? WindowsSandbox, int WindowsAppsPathEntriesRemoved);
public sealed record AgentExecutionResult(BoundSemanticAgentResult Result, AgentProcessResult Process);
public sealed record FactoryCliOutcome(string FactoryOutcome, string RunId, string? Reason = null, string? ResumeWhen = null, string? ResultDirectory = null, JsonElement? Payload = null);

public static class FactoryJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
}
