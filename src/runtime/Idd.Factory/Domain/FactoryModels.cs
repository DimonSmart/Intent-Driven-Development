using System.Text;
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
    public const int CurrentSchemaVersion = 10;
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
    public int PlanningCycleCount { get; set; }
    public int PlannedThroughCompletedCount { get; set; }
    public bool RepositoryFallbackBaselineAccepted { get; set; }
    public bool FinalVerificationPassed { get; set; }
    public long? FinalVerificationPlanRevision { get; set; }
    public FactoryBlocker? Blocker { get; set; }
    public PendingContinuation? PendingContinuation { get; set; }
    public PendingVerificationSession? PendingVerificationSession { get; set; }
    public List<string> VerificationEvidenceRefs { get; init; } = [];
    public List<string> FactoryRunChangedPaths { get; init; } = [];
}

public sealed record PlannedWorkItem
{
    public required string Id { get; init; }
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
}

public sealed record CompletedWorkItem
{
    public required string Id { get; init; }
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

[JsonConverter(typeof(JsonStringEnumConverter<ContinuationKind>))]
public enum ContinuationKind { SemanticInvocation, VerificationGate, Terminal }
[JsonConverter(typeof(JsonStringEnumConverter<VerificationContinuationStage>))]
public enum VerificationContinuationStage { ExecuteCheck, AwaitingConfirmation, AwaitingManualResult }
[JsonConverter(typeof(JsonStringEnumConverter<VerificationConfirmation>))]
public enum VerificationConfirmation { None, Approve, Decline }
[JsonConverter(typeof(JsonStringEnumConverter<SemanticOperationKind>))]
public enum SemanticOperationKind { None, Planning, WorkItemExecution }

[JsonConverter(typeof(JsonStringEnumConverter<AgentExecutionProfile>))]
public enum AgentExecutionProfile
{
    [JsonStringEnumMemberName("read-only")] ReadOnly,
    [JsonStringEnumMemberName("workspace-write")] WorkspaceWrite
}

public sealed record FactoryAgentContract(string Role, string SkillName, AgentExecutionProfile ExecutionProfile);

public static class FactoryCapabilityCatalog
{
    private static readonly IReadOnlyDictionary<string, FactoryAgentContract> Contracts =
        new Dictionary<string, FactoryAgentContract>(StringComparer.Ordinal)
        {
            ["planning"] = new("planner", "idd-factory-decompose-task", AgentExecutionProfile.ReadOnly),
            ["implementation"] = new("executor", "idd-factory-execute-subtask", AgentExecutionProfile.WorkspaceWrite)
        };

    public static FactoryAgentContract Resolve(string capability) => Contracts.TryGetValue(capability, out var value)
        ? value
        : throw new AgentProtocolException("UNKNOWN_CAPABILITY", $"Unknown Factory capability '{capability}'.");
}

public sealed record AgentInvocation
{
    public const int CurrentSchemaVersion = 2;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required string RunId { get; init; }
    public required string AttemptId { get; init; }
    public required string Capability { get; init; }
    public required string Role { get; init; }
    public string? WorkItemId { get; init; }
    public required string Workspace { get; init; }
    public required string SemanticOutputPath { get; init; }
    public required string SkillName { get; init; }
    public required AgentExecutionProfile ExecutionProfile { get; init; }
    public required string Input { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
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
    public const int CurrentSchemaVersion = 3;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required AttemptIdentity Invocation { get; init; }
    public required string SemanticResultPath { get; init; }
    public required DateTimeOffset ReceivedAt { get; init; }
    public required AgentTerminationKind TerminationKind { get; init; }
}

public sealed record BoundSemanticResult(string AttemptId, string SemanticResult, string SemanticResultPath);

public sealed record AgentRunHandle(string AttemptId, int ProcessId, string BackendHandle);
[JsonConverter(typeof(JsonStringEnumConverter<AgentTerminationKind>))]
public enum AgentTerminationKind { CleanExit, ForcedAfterResult, Cancelled, TransportFailure }
public sealed record AgentProcessResult(
    int? ExitCode,
    [property: JsonIgnore] string Stdout,
    [property: JsonIgnore] string Stderr,
    bool CompleteResultObserved,
    bool KillRequired,
    AgentTerminationKind TerminationKind)
{
    public string StdoutLogPath => "stdout.log";
    public string StderrLogPath => "stderr.log";
    public int StdoutBytes => Encoding.UTF8.GetByteCount(Stdout ?? string.Empty);
    public int StderrBytes => Encoding.UTF8.GetByteCount(Stderr ?? string.Empty);
}
public sealed record AgentExecutionConfiguration(string? Model = null, string? ReasoningEffort = null, string? WindowsSandbox = null)
{
    public string RequestedModel => string.IsNullOrWhiteSpace(Model) ? "default/unpinned" : Model;
    public string RequestedReasoningEffort => string.IsNullOrWhiteSpace(ReasoningEffort) ? "default/unpinned" : ReasoningEffort;
}
public sealed record AgentCapabilityPolicy(bool InheritUserSkills, string Profile) { public static AgentCapabilityPolicy ProductionDefault { get; } = new(true, "production-default"); }
public sealed record AgentAttemptTelemetry(string Role, string SkillName, string Backend, AgentExecutionProfile ExecutionProfile, string SkillInvocationMode, int InputChars, string RequestedModel, string RequestedReasoningEffort, string EffectiveModel, string EffectiveReasoningEffort, string SkillSource, string SkillSourceVersion, string UserSkillInheritancePolicy, int ProjectLocalSkillCount, int InheritedUserSkillCount, string CapabilityProfile, string? WindowsSandbox, int WindowsAppsPathEntriesRemoved);
public sealed record AgentExecutionResult(BoundSemanticResult Result, AgentProcessResult Process);
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
