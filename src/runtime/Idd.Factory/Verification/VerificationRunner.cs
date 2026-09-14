using System.Security.Cryptography;
using System.Text;
using Idd.Factory.Processes;

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
        var arguments = OperatingSystem.IsWindows()
            ? new[] { "-NoProfile", "-Command", check.Run! }
            : ["-c", check.Run!];
        var environment = new Dictionary<string, string?>
        {
            ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0",
            ["MSBUILDUSESERVER"] = "0",
            ["MSBUILDDISABLENODEREUSE"] = "1"
        };
        var command = new VerificationCommandMetadata(Path.GetFileName(shell), ".");

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
        await using var stdoutSink = new VerificationOutputSink(
            stdoutFile,
            "stdout",
            stdout,
            issues,
            hooks);
        await using var stderrSink = new VerificationOutputSink(
            stderrFile,
            "stderr",
            stderr,
            issues,
            hooks);

        var result = await hooks.ProcessExecutor.RunAsync(
            new(shell, arguments, workspace)
            {
                EnvironmentOverrides = environment,
                Timeout = check.Timeout,
                TerminationOptions = new(
                    TryGracefulClose: false,
                    GracefulCloseDelay: TimeSpan.Zero,
                    TerminationTimeout: CleanupGrace),
                OutputDrainTimeout = CleanupGrace,
                StandardOutput = new()
                {
                    Capture = false,
                    ChunkObserver = stdoutSink.AppendAsync
                },
                StandardError = new()
                {
                    Capture = false,
                    ChunkObserver = stderrSink.AppendAsync
                }
            },
            cancellationToken);

        await stdoutSink.CompleteAsync();
        await stderrSink.CompleteAsync();
        stdout.Freeze();
        stderr.Freeze();

        if (result.CompletionReason == ProcessCompletionReason.Cancelled)
            throw new OperationCanceledException(cancellationToken);

        if (result.CompletionReason == ProcessCompletionReason.StartFailed)
        {
            var exception = result.Failure ?? new InvalidOperationException($"Could not start check {id}.");
            return await evidenceStore.PersistAsync(
                NewEvidence(
                    evidenceId,
                    id,
                    check.Run!,
                    started,
                    null,
                    "infrastructure-failure",
                    stdout.Tail,
                    stderr.Tail,
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

        VerificationFailure? primary = null;
        var timedOut = result.CompletionReason == ProcessCompletionReason.TimedOut;
        if (timedOut)
            primary = new("timeout", "execute", $"Check {id} timed out.");
        else if (result.CompletionReason == ProcessCompletionReason.InfrastructureFailed)
        {
            var exception = result.Failure ?? new InvalidOperationException("Verification process infrastructure failed.");
            primary = VerificationDiagnostics.Failure(
                "unknown",
                "execute",
                "Verification process failed unexpectedly.",
                exception);
        }

        var termination = MapTermination(result.Termination, issues);
        ApplyProcessDiagnostics(result.Diagnostics, issues, ref primary);

        if (stdout.Failure is not null || stderr.Failure is not null)
        {
            primary ??= VerificationDiagnostics.Failure(
                "output-capture-failure",
                "capture-output",
                "One or more verification output streams could not be persisted completely.",
                stdout.Failure ?? stderr.Failure!);
        }

        var exitCode = result.CompletionReason == ProcessCompletionReason.Exited
            ? result.ExitCode
            : null;
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

    private static VerificationTerminationOutcome? MapTermination(
        ProcessTerminationOutcome termination,
        List<VerificationDiagnosticIssue> issues)
    {
        if (!termination.Requested)
            return null;

        VerificationExceptionDetails? error = null;
        if (termination.TimedOut)
        {
            error = new(
                typeof(TimeoutException).FullName,
                "Termination grace period expired.",
                null);
            issues.Add(new(
                "termination-timeout",
                "terminate-process-tree",
                "Process-tree termination exceeded the cleanup grace period."));
        }
        else if (termination.Error is { } exception)
        {
            error = new(
                exception.GetType().FullName,
                exception.Message,
                exception.StackTrace);
            issues.Add(VerificationDiagnostics.Issue(
                "termination-failure",
                "terminate-process-tree",
                exception));
        }

        return new(true, true, termination.Succeeded, error);
    }

    private static void ApplyProcessDiagnostics(
        IReadOnlyList<ProcessExecutionDiagnostic> diagnostics,
        List<VerificationDiagnosticIssue> issues,
        ref VerificationFailure? primary)
    {
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Kind == ProcessExecutionDiagnosticKinds.DrainTimeout)
            {
                issues.Add(new(
                    "bounded-drain",
                    "drain-output",
                    diagnostic.Message,
                    diagnostic.Error?.GetType().FullName,
                    diagnostic.Error?.StackTrace));
                primary ??= new(
                    "output-capture-failure",
                    "capture-output",
                    "Verification output could not be drained completely.");
                continue;
            }

            if (diagnostic.Kind is ProcessExecutionDiagnosticKinds.OutputReadFailure
                or ProcessExecutionDiagnosticKinds.OutputObserverFailure
                or ProcessExecutionDiagnosticKinds.OutputFileFailure)
            {
                issues.Add(new(
                    diagnostic.Kind == ProcessExecutionDiagnosticKinds.OutputFileFailure
                        ? "stream-log"
                        : "output-capture",
                    diagnostic.Stage,
                    diagnostic.Message,
                    diagnostic.Error?.GetType().FullName,
                    diagnostic.Error?.StackTrace));
                primary ??= VerificationDiagnostics.Failure(
                    "output-capture-failure",
                    "capture-output",
                    "One or more verification output streams could not be captured completely.",
                    diagnostic.Error ?? new IOException(diagnostic.Message));
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

    private sealed class VerificationOutputSink : IAsyncDisposable
    {
        private readonly string streamName;
        private readonly CaptureState state;
        private readonly List<VerificationDiagnosticIssue> issues;
        private Stream? log;
        private bool completed;

        public VerificationOutputSink(
            string path,
            string streamName,
            CaptureState state,
            List<VerificationDiagnosticIssue> issues,
            VerificationRuntimeHooks hooks)
        {
            this.streamName = streamName;
            this.state = state;
            this.issues = issues;
            try
            {
                log = hooks.CreateLog(path);
            }
            catch (Exception exception)
            {
                state.Failure = exception;
                lock (issues)
                    issues.Add(VerificationDiagnostics.Issue(
                        "stream-log",
                        $"open-{streamName}-log",
                        exception));
            }
        }

        public async ValueTask AppendAsync(
            ReadOnlyMemory<char> value,
            CancellationToken cancellationToken)
        {
            state.Append(value.Span);
            if (log is null)
                return;

            try
            {
                var bytes = Encoding.UTF8.GetBytes(value.Span.ToString());
                await log.WriteAsync(bytes, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                state.Failure ??= exception;
                lock (issues)
                    issues.Add(VerificationDiagnostics.Issue(
                        "stream-log",
                        $"write-{streamName}-log",
                        exception));
                try { await log.DisposeAsync(); } catch { }
                log = null;
            }
        }

        public async Task CompleteAsync()
        {
            if (completed)
                return;
            completed = true;
            if (log is null)
                return;
            try
            {
                await log.DisposeAsync();
                state.Persisted = true;
            }
            catch (Exception exception)
            {
                state.Failure ??= exception;
                lock (issues)
                    issues.Add(VerificationDiagnostics.Issue(
                        "stream-log",
                        $"close-{streamName}-log",
                        exception));
            }
            finally
            {
                log = null;
            }
        }

        public async ValueTask DisposeAsync() => await CompleteAsync();
    }

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
