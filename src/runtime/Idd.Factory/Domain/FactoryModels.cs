using System.Text;
using System.Text.Encodings.Web;
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

[JsonConverter(typeof(JsonStringEnumConverter<WorkItemInvocationKind>))]
public enum WorkItemInvocationKind { Initial, SemanticRetry, TechnicalRestart }

public sealed record ChangeSetSummary
{
    public required string Reference { get; init; }
    public int Count { get; init; }
    public List<string> Preview { get; init; } = [];
}

public sealed record FactoryState
{
    public const int CurrentSchemaVersion = 16;
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
    public ChangeSetSummary? RunChanges { get; set; }

    [JsonIgnore]
    public List<string> FactoryRunChangedPaths { get; init; } = [];
}

public sealed record PlannedWorkItem
{
    private string? currentAttemptId;
    private VerificationDecision lastVerificationDecision;

    public required string Id { get; init; }
    public required string ContractPath { get; init; }
    public List<string> TaskRelatedIntentIds { get; init; } = [];
    public List<string> RelevantCompletedWorkIds { get; init; } = [];
    public int SemanticAttemptCount { get; set; }
    public int TechnicalRestartCount { get; set; }
    public int AdditionalSemanticAttemptBudget { get; set; }

    [JsonIgnore]
    public int AttemptCount
    {
        get => InvocationCounterKind == WorkItemInvocationKind.TechnicalRestart
            ? TechnicalRestartCount
            : SemanticAttemptCount;
        set
        {
            if (InvocationCounterKind == WorkItemInvocationKind.TechnicalRestart)
                TechnicalRestartCount = value;
            else
                SemanticAttemptCount = value;
        }
    }

    [JsonIgnore]
    public int AdditionalAttemptBudget
    {
        get => AdditionalSemanticAttemptBudget;
        set => AdditionalSemanticAttemptBudget = value;
    }

    public string? CurrentAttemptId
    {
        get => currentAttemptId;
        set
        {
            currentAttemptId = value;
            if (value is null)
                CurrentInvocationKind = null;
            else
                CurrentInvocationKind ??= NextInvocationKind;
        }
    }

    public List<string> VerificationCheckIds { get; init; } = [];
    public Dictionary<string, VerificationExpectation> VerificationExpectations { get; init; } = new(StringComparer.Ordinal);
    public List<string> VerificationEvidenceRefs { get; init; } = [];
    public List<string> LastVerificationEvidenceRefs { get; init; } = [];

    public VerificationDecision LastVerificationDecision
    {
        get => lastVerificationDecision;
        set
        {
            lastVerificationDecision = value;
            if (value == VerificationDecision.UnexpectedFailure && currentAttemptId is null)
                NextInvocationKind = WorkItemInvocationKind.SemanticRetry;
        }
    }

    public WorkItemInvocationKind NextInvocationKind { get; set; } = WorkItemInvocationKind.Initial;
    public WorkItemInvocationKind? CurrentInvocationKind { get; set; }
    public string? LastResultRef { get; set; }
    public List<string> PriorResultRefs { get; init; } = [];
    public List<TechnicalFailureDiagnostic> PriorTechnicalFailures { get; init; } = [];
    public List<string> PriorAttemptDiagnosticRefs { get; init; } = [];
    public ChangeSetSummary? Changes { get; set; }

    [JsonIgnore]
    public List<string> ChangedPaths { get; init; } = [];

    private WorkItemInvocationKind InvocationCounterKind => CurrentInvocationKind ?? NextInvocationKind;
}

public sealed record TechnicalFailureDiagnostic
{
    public TechnicalFailureDiagnostic() { }

    public TechnicalFailureDiagnostic(
        string failedAttemptId,
        string failureCode,
        string diagnosticReference,
        string message,
        int semanticAttemptNumber,
        List<string> changedPaths)
    {
        FailedAttemptId = failedAttemptId;
        FailureCode = failureCode;
        DiagnosticReference = diagnosticReference;
        Message = message;
        SemanticAttemptNumber = semanticAttemptNumber;
        ChangedPaths = changedPaths;
    }

    public string FailedAttemptId { get; init; } = string.Empty;
    public string FailureCode { get; init; } = string.Empty;
    public string DiagnosticReference { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public int SemanticAttemptNumber { get; init; }
    public ChangeSetSummary? Changes { get; init; }

    [JsonIgnore]
    public List<string> ChangedPaths { get; init; } = [];
}

public sealed record CompletedWorkItem
{
    public required string Id { get; init; }
    public required string ContractPath { get; init; }
    public List<string> TaskRelatedIntentIds { get; init; } = [];
    public List<string> RelevantCompletedWorkIds { get; init; } = [];
    public string? ResultRef { get; init; }
    public ChangeSetSummary? Changes { get; init; }

    [JsonIgnore]
    public List<string> ChangedPaths { get; init; } = [];

    public List<string> VerificationEvidenceRefs { get; init; } = [];
    public VerificationDecision VerificationDecision { get; init; }
}

internal static class DurableIntentId
{
    public static bool IsCanonical(string? value)
    {
        if (value is null || value.Length != 8 || !value.StartsWith("IDD-", StringComparison.Ordinal))
            return false;

        for (var index = 4; index < value.Length; index++)
            if (value[index] is < '0' or > '9')
                return false;

        return true;
    }
}

internal static class WorkItemId
{
    public static bool IsCanonical(string? value)
    {
        if (value is null || value.Length != 7 || value[0] != 'W')
            return false;

        for (var index = 1; index < value.Length; index++)
            if (value[index] is < '0' or > '9')
                return false;

        return value != "W000000";
    }
}

public sealed record FactoryBlocker(string Code, string Reason, string ResumeWhen, JsonElement? Payload = null);

public sealed record VerificationFailureReference(
    string Context,
    string? WorkItemId,
    string CheckId,
    string FailureKind,
    string FailureStage,
    string? EvidencePath);

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

public sealed record PendingVerificationSession
{
    public PendingVerificationSession() { }

    public PendingVerificationSession(
        string context,
        string? workItemId,
        List<string> checkIds,
        List<string> changedPaths,
        int nextCheckIndex,
        List<string> completedCheckIds,
        List<string> failedCheckIds,
        List<string> evidenceRefs,
        string? pendingCheckId,
        string? pendingCheckDefinitionHash,
        string policyHash,
        VerificationContinuationStage stage,
        ChangeSetSummary? changes = null)
    {
        Context = context;
        WorkItemId = workItemId;
        CheckIds = checkIds;
        ChangedPaths = changedPaths;
        NextCheckIndex = nextCheckIndex;
        CompletedCheckIds = completedCheckIds;
        FailedCheckIds = failedCheckIds;
        EvidenceRefs = evidenceRefs;
        PendingCheckId = pendingCheckId;
        PendingCheckDefinitionHash = pendingCheckDefinitionHash;
        PolicyHash = policyHash;
        Stage = stage;
        Changes = changes;
    }

    public string Context { get; init; } = string.Empty;
    public string? WorkItemId { get; init; }
    public List<string> CheckIds { get; init; } = [];

    [JsonIgnore]
    public List<string> ChangedPaths { get; init; } = [];

    public int NextCheckIndex { get; init; }
    public List<string> CompletedCheckIds { get; init; } = [];
    public List<string> FailedCheckIds { get; init; } = [];
    public List<string> EvidenceRefs { get; init; } = [];
    public string? PendingCheckId { get; init; }
    public string? PendingCheckDefinitionHash { get; init; }
    public string PolicyHash { get; init; } = string.Empty;
    public VerificationContinuationStage Stage { get; init; }
    public ChangeSetSummary? Changes { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<ContinuationKind>))]
public enum ContinuationKind { SemanticInvocation, VerificationGate, UserQuestion, Terminal }
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
    public const int CurrentSchemaVersion = 3;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required string RunId { get; init; }
    public required string AttemptId { get; init; }
    public required string Capability { get; init; }
    public required string Role { get; init; }
    public string? WorkItemId { get; init; }
    public WorkItemInvocationKind? InvocationKind { get; init; }
    public int? SemanticAttemptNumber { get; init; }
    public int? TechnicalRestartNumber { get; init; }
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
public enum AgentTerminationKind { CleanExit, ForcedAfterResult, Cancelled, TransportFailure, CommandTimeout, IncompleteCommand }
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
public sealed record AgentExecutionConfiguration(
    string? Model = null,
    string? ReasoningEffort = null,
    string? WindowsSandbox = null,
    TimeSpan? CommandTimeout = null)
{
    public static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromMinutes(10);
    public string RequestedModel => string.IsNullOrWhiteSpace(Model) ? "default/unpinned" : Model;
    public string RequestedReasoningEffort => string.IsNullOrWhiteSpace(ReasoningEffort) ? "default/unpinned" : ReasoningEffort;
    public TimeSpan EffectiveCommandTimeout => CommandTimeout ?? DefaultCommandTimeout;
}
public sealed record AgentCapabilityPolicy(bool InheritUserSkills, string Profile) { public static AgentCapabilityPolicy ProductionDefault { get; } = new(true, "production-default"); }
public sealed record AgentAttemptTelemetry(string Role, string SkillName, string Backend, AgentExecutionProfile ExecutionProfile, string SkillInvocationMode, int InputChars, string RequestedModel, string RequestedReasoningEffort, string EffectiveModel, string EffectiveReasoningEffort, string SkillSource, string SkillSourceVersion, string UserSkillInheritancePolicy, int ProjectLocalSkillCount, int InheritedUserSkillCount, string CapabilityProfile, string? WindowsSandbox, int WindowsAppsPathEntriesRemoved, long CommandTimeoutMilliseconds);
public sealed record AgentExecutionResult(BoundSemanticResult Result, AgentProcessResult Process);
public sealed record FactoryCliOutcome(string FactoryOutcome, string RunId, string? Reason = null, string? ResumeWhen = null, string? ResultDirectory = null, JsonElement? Payload = null);

public static class FactoryJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };
}
