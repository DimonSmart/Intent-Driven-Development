using System.Text;
using Idd.Factory.Verification;

namespace Idd.Factory.Tests;

public sealed class VerificationProcessFailureTests
{
    [Fact]
    public async Task TimeoutCapturesPartialOutputAndTerminationMetadata()
    {
        var command = OperatingSystem.IsWindows()
            ? "[Console]::Out.Write('partial'); Start-Sleep -Seconds 5"
            : "printf partial; sleep 5";
        using var test = new VerificationTestContext().WithCheck("timeout", command, timeout: "1s");

        var result = await test.Engine().RunAsync(["timeout"], default);

        Assert.Equal(VerificationStatus.InfrastructureFailure, result.Status);
        var evidence = Assert.Single(result.Evidence);
        Assert.True(evidence.TimedOut);
        Assert.Null(evidence.ExitCode);
        Assert.Contains("partial", evidence.StdoutTail);
        Assert.Equal("timeout", evidence.PrimaryFailure?.Kind);
        Assert.NotNull(evidence.Termination);
        Assert.StartsWith(".idd/factory/current/verification/", evidence.StdoutPath);
        Assert.True(File.Exists(Path.Combine(test.WorkspacePath, evidence.StdoutPath!.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public async Task OutputWrittenImmediatelyBeforeTimeoutIsDrained()
    {
        var command = OperatingSystem.IsWindows()
            ? "1..100 | ForEach-Object { Write-Output 'late-output'; Start-Sleep -Milliseconds 100 }"
            : "i=0; while [ $i -lt 100 ]; do printf 'late-output\\n'; sleep .1; i=$((i+1)); done";
        using var test = new VerificationTestContext().WithCheck("check", command, timeout: "2s");

        var evidence = Assert.Single((await test.Engine().RunAsync(["check"], default)).Evidence);

        Assert.True(evidence.TimedOut);
        Assert.Contains("late-output", evidence.StdoutTail);
    }

    [Fact]
    public async Task ProcessStartAndEvidencePersistenceFailuresAreBothReported()
    {
        using var test = new VerificationTestContext().WithCheck("check", "exit 0");
        var hooks = new VerificationRuntimeHooks
        {
            StartProcess = _ => throw new System.ComponentModel.Win32Exception("simulated start failure"),
            WriteEvidence = (_, _, _) => Task.FromException(new IOException("simulated evidence failure"))
        };

        var evidence = Assert.Single((await test.Engine(hooks).RunAsync(["check"], default)).Evidence);

        Assert.False(evidence.EvidencePersisted);
        Assert.Equal("process-start-failure", evidence.PrimaryFailure?.Kind);
        Assert.Equal("start", evidence.PrimaryFailure?.Stage);
        Assert.Equal(typeof(System.ComponentModel.Win32Exception).FullName, evidence.PrimaryFailure?.ExceptionType);
        Assert.Contains(evidence.DiagnosticIssues, issue => issue.Kind == "evidence-persistence");
        Assert.DoesNotContain("exit 0", System.Text.Json.JsonSerializer.Serialize(evidence.Command));
    }

    [Fact]
    public async Task IndividualLogFailureDoesNotPreventOtherStreamCapture()
    {
        var command = OperatingSystem.IsWindows()
            ? "Write-Output out; [Console]::Error.Write('err')"
            : "printf out; printf err >&2";
        using var test = new VerificationTestContext().WithCheck("check", command);
        var hooks = new VerificationRuntimeHooks
        {
            CreateLog = path => path.EndsWith(".stdout.log", StringComparison.Ordinal)
                ? throw new IOException("no stdout log")
                : new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, true)
        };

        var evidence = Assert.Single((await test.Engine(hooks).RunAsync(["check"], default)).Evidence);

        Assert.Equal("output-capture-failure", evidence.PrimaryFailure?.Kind);
        Assert.Equal("capture-output", evidence.PrimaryFailure?.Stage);
        Assert.Contains("out", evidence.StdoutTail);
        Assert.Contains("err", evidence.StderrTail);
        Assert.Null(evidence.StdoutPath);
        Assert.Equal(Encoding.UTF8.GetByteCount(evidence.StdoutTail), evidence.Stdout.ByteLength);
        Assert.Contains(evidence.DiagnosticIssues, issue => issue.Stage == "open-stdout-log");
    }

    [Fact]
    public async Task EvidencePersistenceFailureHasPrimaryDiagnosticAndNoReferenceFile()
    {
        using var test = new VerificationTestContext().WithCheck("check", "exit 0");
        var hooks = new VerificationRuntimeHooks
        {
            WriteEvidence = (_, _, _) => Task.FromException(new IOException("cannot persist evidence"))
        };

        var evidence = Assert.Single((await test.Engine(hooks).RunAsync(["check"], default)).Evidence);

        Assert.False(evidence.EvidencePersisted);
        Assert.Equal("evidence-persistence-failure", evidence.PrimaryFailure?.Kind);
        Assert.Equal("persist-evidence", evidence.PrimaryFailure?.Stage);
        Assert.Equal("cannot persist evidence", evidence.PrimaryFailure?.ExceptionMessage);
        Assert.False(File.Exists(test.EvidencePath(evidence)));
    }

    [Fact]
    public async Task TerminationFailureIsSecondaryToTimeout()
    {
        var command = OperatingSystem.IsWindows() ? "Start-Sleep -Seconds 5" : "sleep 5";
        using var test = new VerificationTestContext().WithCheck("check", command, timeout: "0s");
        var hooks = new VerificationRuntimeHooks
        {
            TerminateProcess = (process, _) =>
            {
                if (!process.HasExited) process.Kill(true);
                throw new IOException("simulated termination failure");
            }
        };

        var evidence = Assert.Single((await test.Engine(hooks).RunAsync(["check"], default)).Evidence);

        Assert.Equal("timeout", evidence.PrimaryFailure?.Kind);
        Assert.False(evidence.Termination?.Succeeded);
        Assert.Contains(evidence.DiagnosticIssues, issue => issue.Kind == "termination-failure");
    }

    [Fact]
    public async Task BoundedDrainFailureKeepsCapturedOutput()
    {
        var command = OperatingSystem.IsWindows() ? "Write-Output captured" : "printf captured";
        using var test = new VerificationTestContext().WithCheck("check", command);
        var hooks = new VerificationRuntimeHooks
        {
            WaitForDrain = async (task, _) => { await task; return false; }
        };

        var evidence = Assert.Single((await test.Engine(hooks).RunAsync(["check"], default)).Evidence);

        Assert.Equal("output-capture-failure", evidence.PrimaryFailure?.Kind);
        Assert.Contains("captured", evidence.StdoutTail);
        Assert.Contains(evidence.DiagnosticIssues, issue => issue.Kind == "bounded-drain");
    }

    [Fact]
    public async Task BoundedCleanupFreezesEvidenceAfterOutputWasObserved()
    {
        var command = OperatingSystem.IsWindows() ? "Write-Output captured" : "printf captured";
        using var test = new VerificationTestContext().WithCheck("check", command);
        var blockingLog = new SignalingCancellationBoundStream();
        var hooks = new VerificationRuntimeHooks
        {
            CreateLog = _ => blockingLog,
            WaitForDrain = async (_, _) =>
            {
                await blockingLog.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
                return false;
            }
        };

        var evidence = Assert.Single((await test.Engine(hooks).RunAsync(["check"], default)).Evidence);
        var snapshot = evidence.StdoutTail;
        await Task.Delay(100);

        Assert.Contains("captured", snapshot);
        Assert.Equal(snapshot, evidence.StdoutTail);
        Assert.Contains(evidence.DiagnosticIssues, issue => issue.Kind == "bounded-drain");
    }

    [Fact]
    public async Task InfrastructureFailureStopsRemainingChecks()
    {
        using var test = new VerificationTestContext().WithPolicy("""
            version: 1
            checks:
              first:
                run: exit 0
              second:
                run: exit 0
            default:
              use:
                - first
                - second
            """);
        var starts = 0;
        var hooks = new VerificationRuntimeHooks
        {
            StartProcess = _ => { starts++; throw new IOException("cannot start"); }
        };

        var result = await test.Engine(hooks).RunAsync(["first", "second"], default);

        Assert.Equal(VerificationStatus.InfrastructureFailure, result.Status);
        Assert.Equal("first", Assert.Single(result.Evidence).CheckId);
        Assert.Equal(1, starts);
    }

    [Fact]
    public async Task CancellationStopsTheRunningCheckProcess()
    {
        var command = OperatingSystem.IsWindows()
            ? "$PID | Set-Content -NoNewline verification.pid; Start-Sleep -Seconds 30"
            : "echo $$ > verification.pid; sleep 30";
        using var test = new VerificationTestContext().WithCheck("wait", command, timeout: "1m");
        using var cancellation = new CancellationTokenSource();
        var run = test.Engine().RunAsync(["wait"], cancellation.Token);
        var pidPath = Path.Combine(test.WorkspacePath, "verification.pid");

        for (var attempt = 0; attempt < 100 && !File.Exists(pidPath); attempt++)
            await Task.Delay(25);
        Assert.True(File.Exists(pidPath));
        var pid = int.Parse(await File.ReadAllTextAsync(pidPath));

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.False(IsProcessRunning(pid));
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private sealed class SignalingCancellationBoundStream : MemoryStream
    {
        public TaskCompletionSource<bool> WriteStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            WriteStarted.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
