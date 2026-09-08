using System.Diagnostics;
using System.Text.Json.Serialization;
using Idd.Factory.Processes;

namespace Idd.Factory.Verification;

public sealed record VerificationEvidence
{
    public int SchemaVersion { get; init; }
    public string EvidenceId { get; init; } = "";
    public string CheckId { get; init; } = "";
    public string CheckDefinitionHash { get; init; } = "";
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset FinishedAt { get; init; }
    public int? ExitCode { get; init; }
    public string Status { get; init; } = "";
    [JsonIgnore] public string Output { get; init; } = "";
    [JsonPropertyName("durationMs")] public long? DurationMilliseconds { get; init; }
    [JsonPropertyName("timeoutMs")] public long? TimeoutMilliseconds { get; init; }
    public bool TimedOut { get; init; }
    public VerificationStreamMetadata Stdout { get; init; } = new(null, 0, "", false);
    public VerificationStreamMetadata Stderr { get; init; } = new(null, 0, "", false);
    [JsonIgnore] public string? StdoutPath => Stdout.Path;
    [JsonIgnore] public string? StderrPath => Stderr.Path;
    [JsonIgnore] public string StdoutTail => Stdout.Tail;
    [JsonIgnore] public string StderrTail => Stderr.Tail;
    public VerificationCommandMetadata? Command { get; init; }
    [JsonIgnore] public VerificationFailure? PrimaryFailure { get; init; }
    public string? FailureKind => PrimaryFailure?.Kind;
    public string? FailureStage => PrimaryFailure?.Stage;
    public string? Summary => PrimaryFailure?.Message;
    public VerificationExceptionDetails? Exception => PrimaryFailure?.ExceptionType is null
        && PrimaryFailure?.ExceptionMessage is null
        && PrimaryFailure?.StackTrace is null
            ? null
            : new(PrimaryFailure.ExceptionType, PrimaryFailure.ExceptionMessage, PrimaryFailure.StackTrace);
    public VerificationTerminationOutcome? Termination { get; init; }
    [JsonPropertyName("diagnosticIssues")]
    public IReadOnlyList<VerificationDiagnosticIssue> DiagnosticIssues { get; init; } = [];
    [JsonIgnore] public IReadOnlyList<VerificationDiagnosticIssue> SecondaryIssues => DiagnosticIssues;
    [JsonIgnore] public bool EvidencePersisted { get; init; } = true;

    public VerificationEvidence() { }

    public VerificationEvidence(
        int schemaVersion,
        string evidenceId,
        string checkId,
        string checkDefinitionHash,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt,
        int exitCode,
        string status,
        string output)
    {
        SchemaVersion = schemaVersion;
        EvidenceId = evidenceId;
        CheckId = checkId;
        CheckDefinitionHash = checkDefinitionHash;
        StartedAt = startedAt;
        FinishedAt = finishedAt;
        ExitCode = exitCode;
        Status = status;
        Output = output;
    }
}

public sealed record VerificationStreamMetadata(string? Path, long ByteLength, string Tail, bool Truncated);
public sealed record VerificationCommandMetadata(string Display, string WorkingDirectory);
public sealed record VerificationFailure(
    string Kind,
    string Stage,
    string Message,
    string? ExceptionType = null,
    string? ExceptionMessage = null,
    string? StackTrace = null);
public sealed record VerificationExceptionDetails(string? Type, string? Message, string? StackTrace);
public sealed record VerificationTerminationOutcome(
    bool Requested,
    bool EntireProcessTree,
    bool? Succeeded,
    VerificationExceptionDetails? Error = null)
{
    [JsonIgnore] public bool Attempted => Requested;
    [JsonIgnore] public bool GracePeriodExpired => Error?.Type == typeof(TimeoutException).FullName;
    [JsonIgnore] public string? Detail => Error?.Message;
}
public sealed record VerificationDiagnosticIssue(
    string Kind,
    string Stage,
    string Message,
    string? ExceptionType = null,
    string? StackTrace = null);

internal sealed class VerificationRuntimeHooks
{
    internal Func<ProcessStartInfo, Process?> StartProcess { get; init; } = Process.Start;
    internal Func<string, Stream> CreateLog { get; init; } = path =>
        new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous);
    internal Func<string, string, CancellationToken, Task> WriteEvidence { get; init; } = File.WriteAllTextAsync;
    internal Func<Task, TimeSpan, Task<bool>> WaitForDrain { get; init; } = static async (task, gracePeriod) =>
    {
        using var grace = new CancellationTokenSource(gracePeriod);
        try
        {
            await task.WaitAsync(grace.Token);
            return true;
        }
        catch (OperationCanceledException) when (grace.IsCancellationRequested)
        {
            return false;
        }
    };
    internal Func<Process, CancellationToken, Task> TerminateProcess { get; init; } =
        static (process, token) => new ProcessSupervisor().TerminateProcessTreeAsync(process, token);
}

public enum VerificationStatus
{
    Passed,
    Failed,
    ConfirmationRequired,
    ResultRequired,
    Declined,
    InfrastructureFailure,
    NoChecks
}

public sealed record VerificationResult(
    VerificationStatus Status,
    IReadOnlyList<VerificationEvidence> Evidence,
    string? PendingCheckId = null,
    string? PendingCommand = null,
    string? PendingInstructions = null)
{
    public bool Passed => Status == VerificationStatus.Passed;
}

public sealed record ResolvedVerificationSelection(IReadOnlyList<string> CheckIds, string PolicyHash);
public sealed record VerificationCheck(string? Run, string? Instructions, TimeSpan Timeout, bool ConfirmationRequired = false);
public sealed class VerificationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
