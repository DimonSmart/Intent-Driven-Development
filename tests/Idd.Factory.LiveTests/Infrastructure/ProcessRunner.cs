using System.Diagnostics;
using System.Text;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed record ProcessResult(int ExitCode, DateTimeOffset StartedAtUtc, DateTimeOffset EndedAtUtc, bool TimedOut, string StdoutPath, string StderrPath)
{
    public TimeSpan Duration => EndedAtUtc - StartedAtUtc;
}

public sealed class ProcessRunner
{
    public async Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory, string stdoutPath, string stderrPath, TimeSpan timeout, CancellationToken cancellationToken, string? standardInput = null, IReadOnlyDictionary<string, string>? environmentOverrides = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(stdoutPath)!);
        var start = new ProcessStartInfo(executable) { WorkingDirectory = workingDirectory, RedirectStandardInput = standardInput is not null, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (standardInput is not null) start.StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (environmentOverrides is not null)
            foreach (var (name, value) in environmentOverrides)
                start.Environment[name] = value;
        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var startedAt = DateTimeOffset.UtcNow;
        try { process.Start(); }
        catch (Exception exception) { throw new InvalidOperationException($"Could not start '{executable}'. Ensure it is installed and available on PATH.", exception); }
        await using var stdout = File.Create(stdoutPath);
        await using var stderr = File.Create(stderrPath);
        var stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(stdout, cancellationToken);
        var stderrTask = process.StandardError.BaseStream.CopyToAsync(stderr, cancellationToken);
        var stdinTask = standardInput is null ? Task.CompletedTask : WriteStandardInputAsync(process, standardInput, cancellationToken);
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
        await Task.WhenAll(stdinTask, stdoutTask, stderrTask);
        return new ProcessResult(process.ExitCode, startedAt, DateTimeOffset.UtcNow, timedOut, stdoutPath, stderrPath);
    }

    private static async Task WriteStandardInputAsync(Process process, string input, CancellationToken cancellationToken)
    {
        await process.StandardInput.WriteAsync(input.AsMemory(), cancellationToken);
        process.StandardInput.Close();
    }
}
