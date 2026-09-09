using Idd.Factory.Verification;

namespace Idd.Factory.Tests;

public sealed class VerificationProcessFailureTests
{
    [Fact]
    public async Task TimeoutCapturesPartialOutputAndTerminationMetadata()
    {
        var command = OperatingSystem.IsWindows()
            ? "[Console]::Out.WriteLine('partial'); [Console]::Out.Flush(); Start-Sleep -Seconds 10"
            : "printf partial; sleep 5";
        var timeout = OperatingSystem.IsWindows() ? "5s" : "1s";
        using var test = new VerificationTestContext().WithCheck("timeout", command, timeout: timeout);

        var result = await test.Engine().RunAsync(["timeout"], default);

        Assert.Equal(VerificationStatus.InfrastructureFailure, result.Status);
        var evidence = Assert.Single(result.Evidence);
        Assert.True(evidence.TimedOut);
        Assert.Null(evidence.ExitCode);
        Assert.Contains("partial", evidence.StdoutTail);
        Assert.Equal("timeout", evidence.PrimaryFailure?.Kind);
        Assert.True(evidence.Termination?.Requested);
        Assert.True(File.Exists(Path.Combine(test.WorkspacePath, evidence.StdoutPath!.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public async Task ProcessStartFailureReturnsPersistedInfrastructureEvidence()
    {
        using var test = new VerificationTestContext().WithCheck("check", "exit 0");
        var hooks = new VerificationRuntimeHooks
        {
            StartProcess = _ => throw new System.ComponentModel.Win32Exception("simulated start failure")
        };

        var result = await test.Engine(hooks).RunAsync(["check"], default);

        Assert.Equal(VerificationStatus.InfrastructureFailure, result.Status);
        var evidence = Assert.Single(result.Evidence);
        Assert.Equal("process-start-failure", evidence.PrimaryFailure?.Kind);
        Assert.True(evidence.EvidencePersisted);
        Assert.True(File.Exists(test.EvidencePath(evidence)));
    }

    [Fact]
    public async Task EvidencePersistenceFailureIsReturnedInlineWithoutDanglingReference()
    {
        using var test = new VerificationTestContext().WithCheck("check", "exit 0");
        var hooks = new VerificationRuntimeHooks
        {
            WriteEvidence = (_, _, _) => Task.FromException(new IOException("cannot persist evidence"))
        };

        var result = await test.Engine(hooks).RunAsync(["check"], default);

        Assert.Equal(VerificationStatus.InfrastructureFailure, result.Status);
        var evidence = Assert.Single(result.Evidence);
        Assert.False(evidence.EvidencePersisted);
        Assert.Equal("evidence-persistence-failure", evidence.PrimaryFailure?.Kind);
        Assert.False(File.Exists(test.EvidencePath(evidence)));
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

        for (var attempt = 0; attempt < 100 && !File.Exists(pidPath); attempt++) await Task.Delay(25);
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
        catch (ArgumentException) { return false; }
    }
}
