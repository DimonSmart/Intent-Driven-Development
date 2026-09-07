using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Idd.Factory.Domain;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

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
    public VerificationExceptionDetails? Exception => PrimaryFailure?.ExceptionType is null && PrimaryFailure?.ExceptionMessage is null && PrimaryFailure?.StackTrace is null
        ? null : new(PrimaryFailure.ExceptionType, PrimaryFailure.ExceptionMessage, PrimaryFailure.StackTrace);
    public VerificationTerminationOutcome? Termination { get; init; }
    [JsonPropertyName("diagnosticIssues")] public IReadOnlyList<VerificationDiagnosticIssue> DiagnosticIssues { get; init; } = [];
    [JsonIgnore] public IReadOnlyList<VerificationDiagnosticIssue> SecondaryIssues => DiagnosticIssues;
    [JsonIgnore] public bool EvidencePersisted { get; init; } = true;

    public VerificationEvidence() { }

    public VerificationEvidence(int schemaVersion, string evidenceId, string checkId, string checkDefinitionHash,
        DateTimeOffset startedAt, DateTimeOffset finishedAt, int exitCode, string status, string output)
    {
        SchemaVersion = schemaVersion; EvidenceId = evidenceId; CheckId = checkId; CheckDefinitionHash = checkDefinitionHash;
        StartedAt = startedAt; FinishedAt = finishedAt; ExitCode = exitCode; Status = status; Output = output;
    }
}

public sealed record VerificationStreamMetadata(string? Path, long ByteLength, string Tail, bool Truncated);
public sealed record VerificationCommandMetadata(string Display, string WorkingDirectory);
public sealed record VerificationFailure(string Kind, string Stage, string Message, string? ExceptionType = null, string? ExceptionMessage = null, string? StackTrace = null);
public sealed record VerificationExceptionDetails(string? Type, string? Message, string? StackTrace);
public sealed record VerificationTerminationOutcome(bool Requested, bool EntireProcessTree, bool? Succeeded, VerificationExceptionDetails? Error = null)
{
    [JsonIgnore] public bool Attempted => Requested;
    [JsonIgnore] public bool GracePeriodExpired => Error?.Type == typeof(TimeoutException).FullName;
    [JsonIgnore] public string? Detail => Error?.Message;
}
public sealed record VerificationDiagnosticIssue(string Kind, string Stage, string Message, string? ExceptionType = null, string? StackTrace = null);

internal sealed class VerificationRuntimeHooks
{
    internal Func<ProcessStartInfo, Process?> StartProcess { get; init; } = Process.Start;
    internal Func<string, Stream> CreateLog { get; init; } = path => new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous);
    internal Func<string, string, CancellationToken, Task> WriteEvidence { get; init; } = File.WriteAllTextAsync;
    internal Func<Task, TimeSpan, Task<bool>> WaitForDrain { get; init; } = static async (task, gracePeriod) =>
    {
        using var grace = new CancellationTokenSource(gracePeriod);
        try { await task.WaitAsync(grace.Token); return true; }
        catch (OperationCanceledException) when (grace.IsCancellationRequested) { return false; }
    };
    internal Func<Process, CancellationToken, Task> TerminateProcess { get; init; } = static async (process, token) =>
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync(token);
    };
}

public enum VerificationStatus { Passed, Failed, ConfirmationRequired, ResultRequired, Declined, InfrastructureFailure, NoChecks }

public sealed record VerificationResult(VerificationStatus Status, IReadOnlyList<VerificationEvidence> Evidence,
    string? PendingCheckId = null, string? PendingCommand = null, string? PendingInstructions = null)
{
    public bool Passed => Status == VerificationStatus.Passed;
}

public sealed record ResolvedVerificationSelection(IReadOnlyList<string> CheckIds, string PolicyHash);

public class VerificationEngine
{
    private readonly string workspace;
    private readonly string currentDirectory;
    private readonly VerificationRuntimeHooks hooks;
    private static readonly TimeSpan CleanupGrace = TimeSpan.FromSeconds(2);
    private const int TailBytes = 4 * 1024;
    private const int TailLines = 40;

    public static VerificationEvidence Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("schemaVersion", out var schema) && schema.GetInt32() == 2)
        {
            var root = document.RootElement;
            return new VerificationEvidence(2, root.GetProperty("evidenceId").GetString()!, root.GetProperty("checkId").GetString()!,
                root.GetProperty("checkDefinitionHash").GetString()!, root.GetProperty("startedAt").GetDateTimeOffset(),
                root.GetProperty("finishedAt").GetDateTimeOffset(), root.GetProperty("exitCode").GetInt32(),
                root.GetProperty("status").GetString()!, root.TryGetProperty("output", out var output) ? output.GetString() ?? "" : "");
        }
        var evidence = JsonSerializer.Deserialize<VerificationEvidence>(json, FactoryJson.Options) ?? throw new JsonException("Verification evidence was empty.");
        var v3 = document.RootElement;
        VerificationFailure? failure = null;
        if (v3.TryGetProperty("failureKind", out var kind) && kind.ValueKind != JsonValueKind.Null)
        {
            v3.TryGetProperty("exception", out var exception);
            failure = new(kind.GetString()!, v3.GetProperty("failureStage").GetString()!, v3.GetProperty("summary").GetString()!,
                exception.ValueKind == JsonValueKind.Object && exception.TryGetProperty("type", out var type) ? type.GetString() : null,
                exception.ValueKind == JsonValueKind.Object && exception.TryGetProperty("message", out var message) ? message.GetString() : null,
                exception.ValueKind == JsonValueKind.Object && exception.TryGetProperty("stackTrace", out var stack) ? stack.GetString() : null);
        }
        var secondary = v3.TryGetProperty("diagnosticIssues", out var issues) || v3.TryGetProperty("secondaryIssues", out issues)
            ? JsonSerializer.Deserialize<VerificationDiagnosticIssue[]>(issues.GetRawText(), FactoryJson.Options) ?? [] : [];
        return evidence with { PrimaryFailure = failure, DiagnosticIssues = secondary };
    }

    public VerificationEngine(string workspace, string currentDirectory) : this(workspace, currentDirectory, new()) { }
    internal VerificationEngine(string workspace, string currentDirectory, VerificationRuntimeHooks hooks)
    { this.workspace = workspace; this.currentDirectory = currentDirectory; this.hooks = hooks; }
    public void ValidateCheckIds(IEnumerable<string> checkIds)
    {
        var ids = checkIds.Distinct(StringComparer.Ordinal).ToArray();
        var policy = LoadPolicy();
        if (policy is null)
        {
            if (ids.Length > 0) throw new VerificationException("UNKNOWN_VERIFICATION_CHECK", "Explicit verification IDs require .idd/verification.yaml.");
            return;
        }
        var unknown = ids.Where(id => !policy.Checks.ContainsKey(id)).ToArray();
        if (unknown.Length > 0) throw new VerificationException("UNKNOWN_VERIFICATION_CHECK", $"Unknown check IDs: {string.Join(", ", unknown)}.");
    }

    public virtual async Task<VerificationResult> RunContextAsync(string context, CancellationToken cancellationToken)
        => await RunContextAsync(context, [], cancellationToken);

    public virtual async Task<VerificationResult> RunContextAsync(string context, IEnumerable<string> changedPaths, CancellationToken cancellationToken)
    {
        var policy = await LoadPolicyAsync(cancellationToken);
            return policy is null
            ? await RunRepositoryFallbackAsync(cancellationToken)
            : await RunPolicyChecksAsync(policy, policy.ResolveContext(context, changedPaths), cancellationToken);
    }

    public virtual async Task<VerificationResult> RunSubtaskAsync(IEnumerable<string> explicitCheckIds, CancellationToken cancellationToken)
    {
        var policy = await LoadPolicyAsync(cancellationToken);
        return policy is null
            ? await RunRepositoryFallbackAsync(cancellationToken)
            : await RunPolicyChecksAsync(policy, explicitCheckIds, cancellationToken);
    }

    public async Task<VerificationResult> RunAsync(IEnumerable<string> checkIds, CancellationToken cancellationToken)
    {
        var ids = checkIds.Distinct(StringComparer.Ordinal).ToArray();
        var policy = await LoadPolicyAsync(cancellationToken);
            if (policy is null)
        {
            if (ids.Length > 0) throw new VerificationException("UNKNOWN_VERIFICATION_CHECK", "Explicit verification IDs require .idd/verification.yaml.");
            return new(VerificationStatus.NoChecks, []);
        }
        return await RunPolicyChecksAsync(policy, ids, cancellationToken);
    }

    private async Task<VerificationResult> RunPolicyChecksAsync(VerificationPolicy policy, IEnumerable<string> checkIds, CancellationToken cancellationToken)
    {
        var selected = new List<(string Id, VerificationCheck Check)>();
        foreach (var id in checkIds.Distinct(StringComparer.Ordinal))
        {
            if (!policy.Checks.TryGetValue(id, out var check)) throw new VerificationException("UNKNOWN_VERIFICATION_CHECK", $"Unknown check ID {id}.");
            selected.Add((id, check));
        }
        return await RunChecksAsync(selected, cancellationToken);
    }

    private VerificationPolicy? LoadPolicy()
    {
        var path = Path.Combine(workspace, ".idd", "verification.yaml");
        return File.Exists(path) ? VerificationPolicyParser.Parse(File.ReadAllText(path)) : null;
    }

    private async Task<VerificationPolicy?> LoadPolicyAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(workspace, ".idd", "verification.yaml");
        return File.Exists(path) ? VerificationPolicyParser.Parse(await File.ReadAllTextAsync(path, cancellationToken)) : null;
    }

    private async Task<VerificationResult> RunRepositoryFallbackAsync(CancellationToken cancellationToken)
    {
        var fallback = RepositoryFallback();
        return fallback is null ? new(VerificationStatus.NoChecks, []) : await RunChecksAsync([new("repository-fallback", fallback)], cancellationToken);
    }

    public async Task<VerificationResult> RunCheckAsync(string checkId, bool confirmed, bool? manualPassed, CancellationToken cancellationToken)
    {
        var policy = await LoadPolicyAsync(cancellationToken) ?? throw new VerificationException("UNKNOWN_VERIFICATION_CHECK", "Explicit verification IDs require .idd/verification.yaml.");
        if (!policy.Checks.TryGetValue(checkId, out var check)) throw new VerificationException("UNKNOWN_VERIFICATION_CHECK", $"Unknown check ID {checkId}.");
        return await RunChecksAsync([(checkId, check)], confirmed, manualPassed, cancellationToken);
    }

    public async Task<VerificationResult> RunCheckAsync(string checkId, bool confirmed, bool? manualPassed, string? expectedDefinitionHash, string? expectedPolicyHash, CancellationToken cancellationToken)
    {
        var policy = await LoadPolicyAsync(cancellationToken) ?? throw new VerificationException("VERIFICATION_POLICY_CHANGED", "Verification policy is no longer available.");
        if (expectedPolicyHash is not null && !string.Equals(expectedPolicyHash, PolicyHash(policy), StringComparison.Ordinal))
            throw new VerificationException("VERIFICATION_POLICY_CHANGED", "Verification policy changed while user action was pending.");
        if (!policy.Checks.TryGetValue(checkId, out var check) || expectedDefinitionHash is not null && !string.Equals(expectedDefinitionHash, DefinitionHash(check), StringComparison.Ordinal))
            throw new VerificationException("VERIFICATION_POLICY_CHANGED", $"Verification check {checkId} changed while user action was pending.");
        return await RunChecksAsync([(checkId, check)], confirmed, manualPassed, cancellationToken);
    }

    public async Task<VerificationResult> DeclineCheckAsync(string checkId, string? expectedDefinitionHash, string? expectedPolicyHash, CancellationToken cancellationToken)
    {
        var policy = await LoadPolicyAsync(cancellationToken) ?? throw new VerificationException("VERIFICATION_POLICY_CHANGED", "Verification policy is no longer available.");
        if (expectedPolicyHash is not null && !string.Equals(expectedPolicyHash, PolicyHash(policy), StringComparison.Ordinal))
            throw new VerificationException("VERIFICATION_POLICY_CHANGED", "Verification policy changed while user action was pending.");
        if (!policy.Checks.TryGetValue(checkId, out var check) || expectedDefinitionHash is not null && !string.Equals(expectedDefinitionHash, DefinitionHash(check), StringComparison.Ordinal))
            throw new VerificationException("VERIFICATION_POLICY_CHANGED", $"Verification check {checkId} changed while user action was pending.");
        if (!check.ConfirmationRequired) throw new VerificationException("VERIFICATION_POLICY_CHANGED", $"Verification check {checkId} no longer requires confirmation.");
        var evidence = await PersistAsync(checkId, check.Run!, DateTimeOffset.UtcNow, -1, "not-verified", "User explicitly declined confirmation.", cancellationToken);
        return new(VerificationStatus.Declined, [evidence], checkId, check.Run, null);
    }

    public async Task<ResolvedVerificationSelection> ResolveContextAsync(string context, IEnumerable<string> changedPaths, CancellationToken cancellationToken)
    {
        var policy = await LoadPolicyAsync(cancellationToken);
        return policy is null ? new([], "not-configured") : new(policy.ResolveContext(context, changedPaths), PolicyHash(policy));
    }

    public async Task<string> GetCheckDefinitionHashAsync(string checkId, CancellationToken cancellationToken)
    {
        var policy = await LoadPolicyAsync(cancellationToken) ?? throw new VerificationException("VERIFICATION_POLICY_CHANGED", "Verification policy is no longer available.");
        if (!policy.Checks.TryGetValue(checkId, out var check)) throw new VerificationException("VERIFICATION_POLICY_CHANGED", $"Verification check {checkId} no longer exists.");
        return DefinitionHash(check);
    }

    private async Task<VerificationResult> RunChecksAsync(IEnumerable<(string Id, VerificationCheck Check)> checks, CancellationToken cancellationToken)
        => await RunChecksAsync(checks, confirmed: false, manualPassed: null, cancellationToken);

    private async Task<VerificationResult> RunChecksAsync(IEnumerable<(string Id, VerificationCheck Check)> checks, bool confirmed, bool? manualPassed, CancellationToken cancellationToken)
    {
        var evidence = new List<VerificationEvidence>();
        foreach (var (id, check) in checks)
        {
            if (check.ConfirmationRequired && !confirmed)
                return new(VerificationStatus.ConfirmationRequired, evidence, id, check.Run, null);
            if (check.Instructions is not null)
            {
                if (manualPassed is null)
                    return new(VerificationStatus.ResultRequired, evidence, id, null, check.Instructions);
                var passed = manualPassed.Value;
                evidence.Add(await PersistAsync(id, check.Instructions, DateTimeOffset.UtcNow, passed ? 0 : 1, passed ? "passed" : "failed", check.Instructions, cancellationToken));
            }
            else
            {
                evidence.Add(await ExecuteCheckAsync(id, check, cancellationToken));
            }
            if (evidence[^1].Status == "infrastructure-failure") break;
            confirmed = false;
            manualPassed = null;
        }
        var status = evidence.Any(x => x.Status == "infrastructure-failure") ? VerificationStatus.InfrastructureFailure
            : evidence.Any(x => x.Status == "failed") ? VerificationStatus.Failed
            : evidence.Count == 0 ? VerificationStatus.NoChecks : VerificationStatus.Passed;
        return new(status, evidence);
    }

    private async Task<VerificationEvidence> ExecuteCheckAsync(string id, VerificationCheck check, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var evidenceId = NewEvidenceId();
        var issues = new List<VerificationDiagnosticIssue>();
        var shell = OperatingSystem.IsWindows() ? "powershell" : "/bin/sh";
        var info = new ProcessStartInfo(shell) { WorkingDirectory = workspace, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        info.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        info.Environment["MSBUILDUSESERVER"] = "0";
        info.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        if (OperatingSystem.IsWindows()) { info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-Command"); info.ArgumentList.Add(check.Run!); }
        else { info.ArgumentList.Add("-c"); info.ArgumentList.Add(check.Run!); }
        var command = new VerificationCommandMetadata(Path.GetFileName(shell), ".");
        Process? startedProcess;
        try { startedProcess = hooks.StartProcess(info); }
        catch (Exception exception)
        {
            return await PersistAsync(NewEvidence(evidenceId, id, check.Run!, started, null, "infrastructure-failure", "", "", false,
                command, Failure("process-start-failure", "start", $"Could not start check {id}.", exception), null, issues,
                timeoutMilliseconds: (long)check.Timeout.TotalMilliseconds), CancellationToken.None);
        }
        if (startedProcess is null)
            return await PersistAsync(NewEvidence(evidenceId, id, check.Run!, started, null, "infrastructure-failure", "", "", false,
                command, new("process-start-failure", "start", $"Could not start check {id}."), null, issues,
                timeoutMilliseconds: (long)check.Timeout.TotalMilliseconds), CancellationToken.None);
        using var process = startedProcess;
        var directory = Path.Combine(currentDirectory, "verification");
        try { Directory.CreateDirectory(directory); }
        catch (Exception exception) { issues.Add(Issue("stream-log", "prepare-logs", exception)); }
        var stdoutFile = Path.Combine(directory, evidenceId + ".stdout.log");
        var stderrFile = Path.Combine(directory, evidenceId + ".stderr.log");
        var stdout = new CaptureState(); var stderr = new CaptureState();
        using var captureCancellation = new CancellationTokenSource();
        var stdoutTask = ConsumeAsync(process.StandardOutput, stdoutFile, "stdout", stdout, issues, captureCancellation.Token);
        var stderrTask = ConsumeAsync(process.StandardError, stderrFile, "stderr", stderr, issues, captureCancellation.Token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(check.Timeout);
        bool timedOut = false;
        VerificationFailure? primary = null;
        VerificationTerminationOutcome? termination = null;
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            primary = new("timeout", "execute", $"Check {id} timed out.");
            termination = await StopProcessTreeAsync(process, issues);
        }
        catch (OperationCanceledException)
        {
            _ = await StopProcessTreeAsync(process, issues);
            throw;
        }
        catch (Exception exception)
        {
            primary = Failure("unknown", "execute", "Verification process failed unexpectedly.", exception);
            termination = await StopProcessTreeAsync(process, issues);
        }

        var pumps = Task.WhenAll(stdoutTask, stderrTask);
        if (!await hooks.WaitForDrain(pumps, CleanupGrace))
        {
            issues.Add(new("bounded-drain", "drain-output", "Output streams did not finish within the cleanup grace period."));
            primary ??= new("output-capture-failure", "capture-output", "Verification output could not be drained completely.");
            captureCancellation.Cancel();
            process.StandardOutput.Dispose();
            process.StandardError.Dispose();
            if (!await WaitForCleanupAsync(pumps))
                _ = pumps.ContinueWith(static task => _ = task.Exception, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        stdout.Freeze(); stderr.Freeze();
        if (stdout.Failure is not null || stderr.Failure is not null)
            primary ??= Failure("output-capture-failure", "capture-output", "One or more verification output streams could not be persisted completely.", stdout.Failure ?? stderr.Failure!);

        int? exitCode = null;
        try { if (!timedOut && process.HasExited) exitCode = process.ExitCode; }
        catch (Exception exception) { issues.Add(Issue("exit-code", "inspect-process", exception)); }
        var status = primary is not null ? "infrastructure-failure" : exitCode == 0 ? "passed" : "failed";
        return await PersistAsync(NewEvidence(evidenceId, id, check.Run!, started, exitCode, status, stdout.Tail, stderr.Tail,
            timedOut, command, primary, termination, issues,
            stdout.Persisted && stdout.ByteLength > 0 ? PublicPath(stdoutFile) : null,
            stderr.Persisted && stderr.ByteLength > 0 ? PublicPath(stderrFile) : null,
            (long)check.Timeout.TotalMilliseconds, stdout.ByteLength, stderr.ByteLength, stdout.Truncated, stderr.Truncated), CancellationToken.None);
    }

    private async Task<VerificationTerminationOutcome> StopProcessTreeAsync(Process process, List<VerificationDiagnosticIssue> issues)
    {
        using var grace = new CancellationTokenSource(CleanupGrace);
        try
        {
            await hooks.TerminateProcess(process, grace.Token).WaitAsync(grace.Token);
            return new(true, true, true);
        }
        catch (OperationCanceledException) when (grace.IsCancellationRequested)
        {
            issues.Add(new("termination-timeout", "terminate-process-tree", "Process-tree termination exceeded the cleanup grace period."));
            return new(true, true, false, new(typeof(TimeoutException).FullName, "Termination grace period expired.", null));
        }
        catch (InvalidOperationException) when (process.HasExited) { return new(true, true, true); }
        catch (Exception exception)
        {
            issues.Add(Issue("termination-failure", "terminate-process-tree", exception));
            return new(true, true, false, new(exception.GetType().FullName, exception.Message, exception.StackTrace));
        }
    }

    private async Task<VerificationEvidence> PersistAsync(string id, string definition, DateTimeOffset started, int exitCode, string status, string output, CancellationToken cancellationToken)
    {
        var result = NewEvidence(NewEvidenceId(), id, definition, started, exitCode, status, "", "", false,
            new("manual", "."), null, null, []) with { Output = output };
        return await PersistAsync(result, cancellationToken);
    }

    private async Task<VerificationEvidence> PersistAsync(VerificationEvidence result, CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.Combine(currentDirectory, "verification"); Directory.CreateDirectory(directory);
            await hooks.WriteEvidence(Path.Combine(directory, result.EvidenceId + ".json"), JsonSerializer.Serialize(result, FactoryJson.Options), cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            var issues = result.DiagnosticIssues.Concat([Issue("evidence-persistence", "persist-evidence", exception)]).ToArray();
            return result with
            {
                Status = "infrastructure-failure", EvidencePersisted = false, DiagnosticIssues = issues,
                PrimaryFailure = result.PrimaryFailure ?? Failure("evidence-persistence-failure", "persist-evidence", "Verification evidence JSON could not be persisted.", exception)
            };
        }
    }

    private static async Task<bool> WaitForCleanupAsync(Task task)
    {
        using var grace = new CancellationTokenSource(CleanupGrace);
        try { await task.WaitAsync(grace.Token); return true; }
        catch (OperationCanceledException) when (grace.IsCancellationRequested) { return false; }
        catch { return true; }
    }

    private async Task ConsumeAsync(StreamReader reader, string path, string streamName, CaptureState state, List<VerificationDiagnosticIssue> issues, CancellationToken cancellationToken)
    {
        Stream? log = null;
        try { log = hooks.CreateLog(path); }
        catch (Exception exception) { state.Failure = exception; lock (issues) issues.Add(Issue("stream-log", $"open-{streamName}-log", exception)); }
        var buffer = new char[4096];
        try
        {
            while (true)
            {
                var count = await reader.ReadAsync(buffer);
                if (count == 0) break;
                state.Append(buffer.AsSpan(0, count));
                if (log is null) continue;
                try
                {
                    var bytes = Encoding.UTF8.GetBytes(buffer, 0, count);
                    await log.WriteAsync(bytes, cancellationToken);
                }
                catch (Exception exception)
                {
                    state.Failure ??= exception; lock (issues) issues.Add(Issue("stream-log", $"write-{streamName}-log", exception));
                    try { await log.DisposeAsync(); } catch { }
                    log = null;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            state.Failure ??= exception; lock (issues) issues.Add(Issue("output-capture", $"read-{streamName}", exception));
        }
        finally
        {
            if (log is not null) try { await log.DisposeAsync(); state.Persisted = true; }
            catch (Exception exception) { state.Failure ??= exception; lock (issues) issues.Add(Issue("stream-log", $"close-{streamName}-log", exception)); }
        }
    }

    private VerificationEvidence NewEvidence(string evidenceId, string id, string definition, DateTimeOffset started, int? exitCode,
        string status, string stdout, string stderr, bool timedOut, VerificationCommandMetadata command, VerificationFailure? primary,
        VerificationTerminationOutcome? termination, IReadOnlyList<VerificationDiagnosticIssue> issues, string? stdoutPath = null, string? stderrPath = null,
        long? timeoutMilliseconds = null, long stdoutLength = 0, long stderrLength = 0, bool stdoutTruncated = false, bool stderrTruncated = false)
    {
        var finished = DateTimeOffset.UtcNow;
        return new()
        {
            SchemaVersion = 3, EvidenceId = evidenceId, CheckId = id, CheckDefinitionHash = Hash(definition), StartedAt = started,
            FinishedAt = finished, DurationMilliseconds = Math.Max(0, (long)(finished - started).TotalMilliseconds), TimeoutMilliseconds = timeoutMilliseconds, ExitCode = exitCode,
            Status = status, Stdout = new(stdoutPath, stdoutLength, stdout, stdoutTruncated),
            Stderr = new(stderrPath, stderrLength, stderr, stderrTruncated), TimedOut = timedOut, Command = command, PrimaryFailure = primary, Termination = termination,
            DiagnosticIssues = issues.ToArray()
        };
    }

    private string PublicPath(string path) => Path.GetRelativePath(workspace, path).Replace('\\', '/');
    private static string NewEvidenceId() => $"V{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..36];
    private static VerificationDiagnosticIssue Issue(string kind, string stage, Exception exception) => new(kind, stage, exception.Message, exception.GetType().FullName, exception.StackTrace);
    private static VerificationFailure Failure(string kind, string stage, string message, Exception exception)
        => new(kind, stage, message, exception.GetType().FullName, exception.Message, exception.StackTrace);

    private sealed class CaptureState
    {
        private readonly object sync = new();
        private bool frozen;
        public string Tail { get; private set; } = "";
        public long ByteLength { get; private set; }
        public bool Truncated { get; private set; }
        public bool Persisted { get; set; }
        public Exception? Failure { get; set; }
        public void Append(ReadOnlySpan<char> value)
        {
            lock (sync)
            {
            if (frozen) return;
            Tail += value.ToString();
            ByteLength += Encoding.UTF8.GetByteCount(value);
            var lines = Tail.Split('\n');
            if (lines.Length > TailLines) { Tail = string.Join('\n', lines[^TailLines..]); Truncated = true; }
            if (Encoding.UTF8.GetByteCount(Tail) <= TailBytes) return;
            Truncated = true;
            var low = 0; var high = Tail.Length;
            while (low < high)
            {
                var candidate = low + (high - low) / 2;
                if (Encoding.UTF8.GetByteCount(Tail.AsSpan(candidate)) <= TailBytes) high = candidate; else low = candidate + 1;
            }
            if (low < Tail.Length && char.IsLowSurrogate(Tail[low])) low++;
            Tail = Tail[low..];
            }
        }
        public void Freeze() { lock (sync) frozen = true; }
    }

    private static string DefinitionHash(VerificationCheck check) => Hash($"run={check.Run}\ninstructions={check.Instructions}\ntimeout={check.Timeout:c}\nconfirmation={check.ConfirmationRequired}");
    private static string PolicyHash(VerificationPolicy policy)
    {
        var canonical = new
        {
            version = 1,
            checks = policy.Checks.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new
            {
                id = x.Key,
                run = x.Value.Run,
                instructions = x.Value.Instructions,
                timeoutTicks = x.Value.Timeout.Ticks,
                confirmationRequired = x.Value.ConfirmationRequired
            }).ToArray(),
            contexts = policy.Contexts.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new
            {
                name = x.Key,
                hasUse = x.Value.HasUse,
                use = x.Value.Use.ToArray(),
                rules = x.Value.Rules.Select(rule => new
                {
                    paths = rule.Paths.ToArray(),
                    fallback = rule.Fallback,
                    use = rule.Use.ToArray()
                }).ToArray()
            }).ToArray()
        };
        return Hash(JsonSerializer.Serialize(canonical));
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private VerificationCheck? RepositoryFallback()
    {
        if (File.Exists(Path.Combine(workspace, "scripts", "Check.ps1")))
        {
            var run = OperatingSystem.IsWindows()
                ? "& './scripts/Check.ps1'"
                : "pwsh -NoProfile -File './scripts/Check.ps1'";
            return new(run, null, TimeSpan.FromMinutes(30));
        }
        if (File.Exists(Path.Combine(workspace, "scripts", "check.sh"))) return new("./scripts/check.sh", null, TimeSpan.FromMinutes(30), false);
        var solution = Directory.GetFiles(workspace, "*.slnx").Concat(Directory.GetFiles(workspace, "*.sln")).OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault();
        if (solution is not null) return new($"dotnet test '{Path.GetFileName(solution)}'", null, TimeSpan.FromMinutes(30));
        var project = Directory.GetFiles(workspace, "*.csproj", SearchOption.TopDirectoryOnly).OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault();
        if (project is not null) return new($"dotnet test '{Path.GetRelativePath(workspace, project).Replace('\\', '/')}'", null, TimeSpan.FromMinutes(30));
        return null;
    }
}

public sealed record VerificationCheck(string? Run, string? Instructions, TimeSpan Timeout, bool ConfirmationRequired = false);

internal static class VerificationPolicyParser
{
    private static readonly HashSet<string> ContextNames = ["direct", "subtask", "checkpoint", "final"];

    public static VerificationPolicy Parse(string yaml)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            if (stream.Documents.Count != 1) Invalid("Policy must be one YAML mapping document.");
            var root = AsMapping(stream.Documents[0].RootNode, "policy");
            var rootValues = Mapping(root, "policy");
            var allowedRoot = new HashSet<string>(ContextNames, StringComparer.Ordinal) { "version", "checks", "default" };
            RejectUnknown(rootValues, allowedRoot, "policy");
            if (RequiredScalar(rootValues, "version", "policy") != "1") Invalid("Only verification policy version 1 is supported.");

            var checksNode = RequiredMapping(rootValues, "checks", "policy");
            var checks = new Dictionary<string, VerificationCheck>(StringComparer.Ordinal);
            foreach (var (id, node) in Mapping(checksNode, "checks"))
            {
                if (string.IsNullOrWhiteSpace(id)) Invalid("Check IDs must not be empty.");
                var definition = Mapping(AsMapping(node, $"check {id}"), $"check {id}");
                RejectUnknown(definition, ["run", "instructions", "timeout", "confirmation"], $"check {id}");
                var run = OptionalScalar(definition, "run", $"check {id}");
                var instructions = OptionalScalar(definition, "instructions", $"check {id}");
                if ((run is null) == (instructions is null)) Invalid($"Check {id} must have exactly one of run or instructions.");
                if (run is not null && string.IsNullOrWhiteSpace(run) || instructions is not null && string.IsNullOrWhiteSpace(instructions)) Invalid($"Check {id} has an empty definition.");
                var timeout = definition.TryGetValue("timeout", out var timeoutNode) ? ParseTimeout(Scalar(timeoutNode, $"check {id}.timeout")) : TimeSpan.FromMinutes(10);
                var confirmationRequired = false;
                if (instructions is not null && definition.ContainsKey("timeout")) Invalid($"Check {id} cannot set timeout without run.");
                if (definition.TryGetValue("confirmation", out var confirmationNode))
                {
                    if (run is null || Scalar(confirmationNode, $"check {id}.confirmation") != "required") Invalid($"Check {id} confirmation must be 'required' on a run check.");
                    confirmationRequired = true;
                }
                checks.Add(id, new(run, instructions, timeout, confirmationRequired));
            }

            var contexts = new Dictionary<string, VerificationContext>(StringComparer.Ordinal)
            {
                ["default"] = ParseContext(RequiredMapping(rootValues, "default", "policy"), "default", checks, allowRules: false)
            };
            foreach (var context in ContextNames)
                if (rootValues.TryGetValue(context, out var node))
                    contexts[context] = ParseContext(AsMapping(node, context), context, checks, allowRules: true);
            return new(checks, contexts);
        }
        catch (VerificationException) { throw; }
        catch (YamlException exception) { throw new VerificationException("INVALID_VERIFICATION_POLICY", $"Malformed verification policy YAML: {exception.Message}"); }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        { throw new VerificationException("INVALID_VERIFICATION_POLICY", exception.Message); }
    }

    internal static bool IsKnownContext(string context) => ContextNames.Contains(context);

    private static VerificationContext ParseContext(YamlMappingNode node, string name, IReadOnlyDictionary<string, VerificationCheck> checks, bool allowRules)
    {
        var values = Mapping(node, name); RejectUnknown(values, allowRules ? ["use", "rules"] : ["use"], name);
        if (values.ContainsKey("use") == values.ContainsKey("rules")) Invalid($"Context {name} must have exactly one of use or rules.");
        if (values.TryGetValue("use", out var use)) return new(ParseIds(use, $"{name}.use", checks), [], true);
        var rules = AsSequence(values["rules"], $"{name}.rules");
        var parsedRules = new List<VerificationRule>();
        var fallbackSeen = false;
        foreach (var (ruleNode, index) in rules.Children.Select((value, index) => (value, index)))
        {
            var ruleName = $"{name}.rules[{index}]"; var rule = Mapping(AsMapping(ruleNode, ruleName), ruleName);
            RejectUnknown(rule, ["paths", "fallback", "use"], ruleName);
            if (!rule.TryGetValue("use", out var ruleUse)) Invalid($"{ruleName} must define use.");
            ParseIds(ruleUse, $"{ruleName}.use", checks);
            var hasPaths = rule.TryGetValue("paths", out var paths);
            var fallback = rule.TryGetValue("fallback", out var fallbackNode);
            if (fallback && Scalar(fallbackNode!, $"{ruleName}.fallback") != "true") Invalid($"{ruleName}.fallback must be true.");
            if (fallback == hasPaths) Invalid($"{ruleName} must define exactly one of paths or fallback: true.");
            if (hasPaths)
            {
                if (fallbackSeen) Invalid($"{ruleName} appears after a pathless fallback rule.");
                var pathValues = Scalars(AsSequence(paths!, $"{ruleName}.paths"), $"{ruleName}.paths");
                if (pathValues.Count == 0 || pathValues.Any(string.IsNullOrWhiteSpace)) Invalid($"{ruleName}.paths must contain non-empty paths.");
                parsedRules.Add(new(pathValues, ParseIds(ruleUse, $"{ruleName}.use", checks), false));
            }
            else
            {
                if (fallbackSeen) Invalid($"{ruleName} is a second pathless fallback rule.");
                fallbackSeen = true;
                parsedRules.Add(new([], ParseIds(ruleUse, $"{ruleName}.use", checks), true));
            }
        }
        if (parsedRules.Count == 0) Invalid($"{name}.rules must not be empty.");
        return new([], parsedRules, false);
    }

    private static IReadOnlyList<string> ParseIds(YamlNode node, string location, IReadOnlyDictionary<string, VerificationCheck> checks)
    {
        var ids = Scalars(AsSequence(node, location), location);
        if (ids.Any(string.IsNullOrWhiteSpace)) Invalid($"{location} contains an empty check ID.");
        var unknown = ids.Where(id => !checks.ContainsKey(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (unknown.Length > 0) Invalid($"{location} references unknown checks: {string.Join(", ", unknown)}.");
        return ids;
    }

    private static Dictionary<string, YamlNode> Mapping(YamlMappingNode node, string location)
    {
        var result = new Dictionary<string, YamlNode>(StringComparer.Ordinal);
        foreach (var pair in node.Children)
        {
            var key = Scalar(pair.Key, location);
            if (!result.TryAdd(key, pair.Value)) Invalid($"Duplicate key {key} in {location}.");
        }
        return result;
    }
    private static void RejectUnknown(IReadOnlyDictionary<string, YamlNode> values, IEnumerable<string> allowed, string location)
    {
        var set = allowed.ToHashSet(StringComparer.Ordinal); var unknown = values.Keys.Where(key => !set.Contains(key)).ToArray();
        if (unknown.Length > 0) Invalid($"Unknown fields in {location}: {string.Join(", ", unknown)}.");
    }
    private static YamlMappingNode RequiredMapping(IReadOnlyDictionary<string, YamlNode> values, string key, string location)
        => values.TryGetValue(key, out var node) ? AsMapping(node, $"{location}.{key}") : throw new VerificationException("INVALID_VERIFICATION_POLICY", $"Missing {location}.{key}.");
    private static string RequiredScalar(IReadOnlyDictionary<string, YamlNode> values, string key, string location)
        => values.TryGetValue(key, out var node) ? Scalar(node, $"{location}.{key}") : throw new VerificationException("INVALID_VERIFICATION_POLICY", $"Missing {location}.{key}.");
    private static string? OptionalScalar(IReadOnlyDictionary<string, YamlNode> values, string key, string location)
        => values.TryGetValue(key, out var node) ? Scalar(node, $"{location}.{key}") : null;
    private static YamlMappingNode AsMapping(YamlNode node, string location) => node as YamlMappingNode ?? throw new VerificationException("INVALID_VERIFICATION_POLICY", $"{location} must be a mapping.");
    private static YamlSequenceNode AsSequence(YamlNode node, string location) => node as YamlSequenceNode ?? throw new VerificationException("INVALID_VERIFICATION_POLICY", $"{location} must be a sequence.");
    private static string Scalar(YamlNode node, string location) => node is YamlScalarNode { Value: not null } scalar ? scalar.Value : throw new VerificationException("INVALID_VERIFICATION_POLICY", $"{location} must be a scalar.");
    private static IReadOnlyList<string> Scalars(YamlSequenceNode node, string location) => node.Children.Select((value, index) => Scalar(value, $"{location}[{index}]" )).ToArray();
    private static TimeSpan ParseTimeout(string value)
    {
        if (value.Length < 2) Invalid($"Invalid timeout {value}.");
        if (!int.TryParse(value[..^1], out var amount) || amount < 0) Invalid($"Invalid timeout {value}.");
        return value[^1] switch { 's' => TimeSpan.FromSeconds(amount), 'm' => TimeSpan.FromMinutes(amount), 'h' => TimeSpan.FromHours(amount), _ => throw new VerificationException("INVALID_VERIFICATION_POLICY", $"Invalid timeout {value}.") };
    }
    [DoesNotReturn] private static void Invalid(string message) => throw new VerificationException("INVALID_VERIFICATION_POLICY", message);
}

internal sealed record VerificationRule(IReadOnlyList<string> Paths, IReadOnlyList<string> Use, bool Fallback);
internal sealed record VerificationContext(IReadOnlyList<string> Use, IReadOnlyList<VerificationRule> Rules, bool HasUse);

internal sealed record VerificationPolicy(
    IReadOnlyDictionary<string, VerificationCheck> Checks,
    IReadOnlyDictionary<string, VerificationContext> Contexts)
{
    public IReadOnlyList<string> ResolveContext(string context, IEnumerable<string> changedPaths)
    {
        if (context != "default" && !VerificationPolicyParser.IsKnownContext(context))
            throw new VerificationException("INVALID_VERIFICATION_POLICY", $"Unknown verification context {context}.");
        var selected = Contexts.TryGetValue(context, out var value) ? value : Contexts["default"];
        if (selected.HasUse) return selected.Use;
        var paths = changedPaths.Select(path => path.Replace('\\', '/').TrimStart('/')).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var rule in selected.Rules)
            if (rule.Fallback || (paths.Length > 0 && paths.All(path => rule.Paths.Any(pattern => Glob(pattern, path)))))
                return rule.Use;
        return Contexts["default"].Use;
    }

    private static bool Glob(string pattern, string path)
    {
        var expression = "^" + System.Text.RegularExpressions.Regex.Escape(pattern.Replace('\\', '/'))
            .Replace("\\*\\*", ".*").Replace("\\*", "[^/]*").Replace("\\?", "[^/]") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(path, expression, System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }
}

public sealed class VerificationException(string code, string message) : Exception(message) { public string Code { get; } = code; }
