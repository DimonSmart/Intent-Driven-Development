using System.Diagnostics;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed record ProcessResult(int ExitCode, DateTimeOffset StartedAtUtc, DateTimeOffset EndedAtUtc, bool TimedOut, string StdoutPath, string StderrPath)
{
    public TimeSpan Duration => EndedAtUtc - StartedAtUtc;
}

public sealed class ProcessRunner
{
    public async Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory, string stdoutPath, string stderrPath, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(stdoutPath)!);
        var start = new ProcessStartInfo(executable) { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var startedAt = DateTimeOffset.UtcNow;
        try { process.Start(); }
        catch (Exception exception) { throw new InvalidOperationException($"Could not start '{executable}'. Ensure it is installed and available on PATH.", exception); }
        await using var stdout = File.Create(stdoutPath);
        await using var stderr = File.Create(stderrPath);
        var stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(stdout, cancellationToken);
        var stderrTask = process.StandardError.BaseStream.CopyToAsync(stderr, cancellationToken);
        var timedOut = false;
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellation.Token);
        try { await process.WaitForExitAsync(linked.Token); }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
        {
            timedOut = true;
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
        }
        await Task.WhenAll(stdoutTask, stderrTask);
        return new ProcessResult(process.ExitCode, startedAt, DateTimeOffset.UtcNow, timedOut, stdoutPath, stderrPath);
    }
}
