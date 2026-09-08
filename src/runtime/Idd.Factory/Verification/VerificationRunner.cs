using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Idd.Factory.Verification;

internal sealed class VerificationRunner(
    string workspace,
    string currentDirectory,
    VerificationRuntimeHooks hooks,
    VerificationEvidenceStore evidenceStore)
{
    private static readonly TimeSpan CleanupGrace = TimeSpan.FromSeconds(2);
    private const int TailBytes = 4 * 1024;
    private const int TailLines = 40;

    public async Task<VerificationResult> RunPolicyChecksAsync(
        VerificationPolicy policy,
        IEnumerable<string> checkIds,
        CancellationToken cancellationToken)
    {
        var selected = new List<(string Id, VerificationCheck Check)>();
        foreach (var id in checkIds.Distinct(StringComparer.Ordinal))
        {
            if (!policy.Checks.TryGetValue(id, out var check))
                throw new VerificationException("UNKNOWN_VERIFICATION_CHECK", $"Unknown check ID {id}.");
            selected.Add((id, check));
        }
        return await RunChecksAsync(selected, cancellationToken);
    }

    public Task<VerificationResult> RunChecksAsync(
        IEnumerable<(string Id, VerificationCheck Check)> checks,
        CancellationToken cancellationToken) =>
        RunChecksAsync(checks, confirmed: false, manualPassed: null, cancellationToken);

    public async Task<VerificationResult> RunChecksAsync(
        IEnumerable<(string Id, VerificationCheck Check)> checks,
        bool confirmed,
        bool? manualPassed,
        CancellationToken cancellationToken)
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
                evidence.Add(await evidenceStore.PersistManualAsync(
                    id,
                    check.Instructions,
                    DateTimeOffset.UtcNow,
                    passed ? 0 : 1,
                    passed ? "passed" : "failed",
                    check.Instructions,
                    cancellationToken));
            }
            else
            {
                evidence.Add(await ExecuteCheckAsync(id, check, cancellationToken));
            }

            if (evidence[^1].Status == "infrastructure-failure")
                break;
            confirmed = false;
            manualPassed = null;
        }

        var status = evidence.Any(x => x.Status == "infrastructure-failure")
            ? VerificationStatus.InfrastructureFailure
            : evidence.Any(x => x.Status == "failed")
                ? VerificationStatus.Failed
                : evidence.Count == 0
                    ? VerificationStatus.NoChecks
                    : VerificationStatus.Passed;
        return new(status, evidence);
    }

    private async Task<VerificationEvidence> ExecuteCheckAsync(
        string id,
        VerificationCheck check,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var evidenceId = VerificationEvidenceStore.NewEvidenceId();
        var issues = new List<VerificationDiagnosticIssue>();
        var shell = OperatingSystem.IsWindows() ? "powershell" : "/bin/sh";
        var info = new ProcessStartInfo(shell)
        {
            WorkingDirectory = workspace,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        info.Environment["MSBUILDUSESERVER"] = "0";
        info.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        if (OperatingSystem.IsWindows())
        {
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-Command");
            info.ArgumentList.Add(check.Run!);
        }
        else
        {
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add(check.Run!);
        }

        var command = new VerificationCommandMetadata(Path.GetFileName(shell), ".");
        Process? startedProcess;
        try
        {
            startedProcess = hooks.StartProcess(info);
        }
        catch (Exception exception)
        {
            return await evidenceStore.PersistAsync(
                NewEvidence(
                    evidenceId,
                    id,
                    check.Run!,
                    started,
                    null,
                    "infrastructure-failure",
                    "",
                    "",
                    false,
                    command,
                    VerificationDiagnostics.Failure(
                        "process-start-failure",
                        "start",
                        $"Could not start check {id}.",
                        exception),
                    null,
                    issues,
                    timeoutMilliseconds: (long)check.Timeout.TotalMilliseconds),
                CancellationToken.None);
        }

        if (startedProcess is null)
        {
            return await evidenceStore.PersistAsync(
                NewEvidence(
                    evidenceId,
                    id,
                    check.Run!,
                    started,
                    null,
                    "infrastructure-failure",
                    "",
                    "",
                    false,
                    command,
                    new("process-start-failure", "start", $"Could not start check {id}."),
                    null,
                    issues,
                    timeoutMilliseconds: (long)check.Timeout.TotalMilliseconds),
                CancellationToken.None);
        }

        using var process = startedProcess;
        var directory = Path.Combine(currentDirectory, "verification");
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception exception)
        {
            issues.Add(VerificationDiagnostics.Issue("stream-log", "prepare-logs", exception));
        }

        var stdoutFile = Path.Combine(directory, evidenceId + ".stdout.log");
        var stderrFile = Path.Combine(directory, evidenceId + ".stderr.log");
        var stdout = new CaptureState();
        var stderr = new CaptureState();
        using var captureCancellation = new CancellationTokenSource();
        var stdoutTask = ConsumeAsync(
            process.StandardOutput,
            stdoutFile,
            "stdout",
            stdout,
            issues,
            captureCancellation.Token);
        var stderrTask = ConsumeAsync(
            process.StandardError,
            stderrFile,
            "stderr",
            stderr,
            issues,
            captureCancellation.Token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(check.Timeout);
        var timedOut = false;
        VerificationFailure? primary = null;
        VerificationTerminationOutcome? termination = null;
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
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
            primary = VerificationDiagnostics.Failure(
                "unknown",
                "execute",
                "Verification process failed unexpectedly.",
                exception);
            termination = await StopProcessTreeAsync(process, issues);
        }

        var pumps = Task.WhenAll(stdoutTask, stderrTask);
        if (!await hooks.WaitForDrain(pumps, CleanupGrace))
        {
            issues.Add(new(
                "bounded-drain",
                "drain-output",
                "Output streams did not finish within the cleanup grace period."));
            primary ??= new(
                "output-capture-failure",
                "capture-output",
                "Verification output could not be drained completely.");
            captureCancellation.Cancel();
            process.StandardOutput.Dispose();
            process.StandardError.Dispose();
            if (!await WaitForCleanupAsync(pumps))
            {
                _ = pumps.ContinueWith(
                    static task => _ = task.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }

        stdout.Freeze();
        stderr.Freeze();
        if (stdout.Failure is not null || stderr.Failure is not null)
        {
            primary ??= VerificationDiagnostics.Failure(
                "output-capture-failure",
                "capture-output",
                "One or more verification output streams could not be persisted completely.",
                stdout.Failure ?? stderr.Failure!);
        }

        int? exitCode = null;
        try
        {
            if (!timedOut && process.HasExited)
                exitCode = process.ExitCode;
        }
        catch (Exception exception)
        {
            issues.Add(VerificationDiagnostics.Issue("exit-code", "inspect-process", exception));
        }

        var status = primary is not null
            ? "infrastructure-failure"
            : exitCode == 0
                ? "passed"
                : "failed";
        return await evidenceStore.PersistAsync(
            NewEvidence(
                evidenceId,
                id,
                check.Run!,
                started,
                exitCode,
                status,
                stdout.Tail,
                stderr.Tail,
                timedOut,
                command,
                primary,
                termination,
                issues,
                stdout.Persisted && stdout.ByteLength > 0 ? PublicPath(stdoutFile) : null,
                stderr.Persisted && stderr.ByteLength > 0 ? PublicPath(stderrFile) : null,
                (long)check.Timeout.TotalMilliseconds,
                stdout.ByteLength,
                stderr.ByteLength,
                stdout.Truncated,
                stderr.Truncated),
            CancellationToken.None);
    }

    private async Task<VerificationTerminationOutcome> StopProcessTreeAsync(
        Process process,
        List<VerificationDiagnosticIssue> issues)
    {
        using var grace = new CancellationTokenSource(CleanupGrace);
        try
        {
            await hooks.TerminateProcess(process, grace.Token).WaitAsync(grace.Token);
            return new(true, true, true);
        }
        catch (OperationCanceledException) when (grace.IsCancellationRequested)
        {
            issues.Add(new(
                "termination-timeout",
                "terminate-process-tree",
                "Process-tree termination exceeded the cleanup grace period."));
            return new(
                true,
                true,
                false,
                new(typeof(TimeoutException).FullName, "Termination grace period expired.", null));
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            return new(true, true, true);
        }
        catch (Exception exception)
        {
            issues.Add(VerificationDiagnostics.Issue(
                "termination-failure",
                "terminate-process-tree",
                exception));
            return new(
                true,
                true,
                false,
                new(exception.GetType().FullName, exception.Message, exception.StackTrace));
        }
    }

    private static async Task<bool> WaitForCleanupAsync(Task task)
    {
        using var grace = new CancellationTokenSource(CleanupGrace);
        try
        {
            await task.WaitAsync(grace.Token);
            return true;
        }
        catch (OperationCanceledException) when (grace.IsCancellationRequested)
        {
            return false;
        }
        catch
        {
            return true;
        }
    }

    private async Task ConsumeAsync(
        StreamReader reader,
        string path,
        string streamName,
        CaptureState state,
        List<VerificationDiagnosticIssue> issues,
        CancellationToken cancellationToken)
    {
        Stream? log = null;
        try
        {
            log = hooks.CreateLog(path);
        }
        catch (Exception exception)
        {
            state.Failure = exception;
            lock (issues)
                issues.Add(VerificationDiagnostics.Issue("stream-log", $"open-{streamName}-log", exception));
        }

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
                    state.Failure ??= exception;
                    lock (issues)
                        issues.Add(VerificationDiagnostics.Issue("stream-log", $"write-{streamName}-log", exception));
                    try { await log.DisposeAsync(); } catch { }
                    log = null;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            state.Failure ??= exception;
            lock (issues)
                issues.Add(VerificationDiagnostics.Issue("output-capture", $"read-{streamName}", exception));
        }
        finally
        {
            if (log is not null)
            {
                try
                {
                    await log.DisposeAsync();
                    state.Persisted = true;
                }
                catch (Exception exception)
                {
                    state.Failure ??= exception;
                    lock (issues)
                        issues.Add(VerificationDiagnostics.Issue("stream-log", $"close-{streamName}-log", exception));
                }
            }
        }
    }

    private VerificationEvidence NewEvidence(
        string evidenceId,
        string id,
        string definition,
        DateTimeOffset started,
        int? exitCode,
        string status,
        string stdout,
        string stderr,
        bool timedOut,
        VerificationCommandMetadata command,
        VerificationFailure? primary,
        VerificationTerminationOutcome? termination,
        IReadOnlyList<VerificationDiagnosticIssue> issues,
        string? stdoutPath = null,
        string? stderrPath = null,
        long? timeoutMilliseconds = null,
        long stdoutLength = 0,
        long stderrLength = 0,
        bool stdoutTruncated = false,
        bool stderrTruncated = false)
    {
        var finished = DateTimeOffset.UtcNow;
        return new()
        {
            SchemaVersion = 3,
            EvidenceId = evidenceId,
            CheckId = id,
            CheckDefinitionHash = Hash(definition),
            StartedAt = started,
            FinishedAt = finished,
            DurationMilliseconds = Math.Max(0, (long)(finished - started).TotalMilliseconds),
            TimeoutMilliseconds = timeoutMilliseconds,
            ExitCode = exitCode,
            Status = status,
            Stdout = new(stdoutPath, stdoutLength, stdout, stdoutTruncated),
            Stderr = new(stderrPath, stderrLength, stderr, stderrTruncated),
            TimedOut = timedOut,
            Command = command,
            PrimaryFailure = primary,
            Termination = termination,
            DiagnosticIssues = issues.ToArray()
        };
    }

    private string PublicPath(string path) =>
        Path.GetRelativePath(workspace, path).Replace('\\', '/');

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

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
                if (lines.Length > TailLines)
                {
                    Tail = string.Join('\n', lines[^TailLines..]);
                    Truncated = true;
                }
                if (Encoding.UTF8.GetByteCount(Tail) <= TailBytes) return;
                Truncated = true;
                var low = 0;
                var high = Tail.Length;
                while (low < high)
                {
                    var candidate = low + (high - low) / 2;
                    if (Encoding.UTF8.GetByteCount(Tail.AsSpan(candidate)) <= TailBytes)
                        high = candidate;
                    else
                        low = candidate + 1;
                }
                if (low < Tail.Length && char.IsLowSurrogate(Tail[low])) low++;
                Tail = Tail[low..];
            }
        }

        public void Freeze()
        {
            lock (sync)
                frozen = true;
        }
    }
}
