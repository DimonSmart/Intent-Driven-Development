using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Idd.Factory.Processes;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed record ProcessResult(int ExitCode, DateTimeOffset StartedAtUtc, DateTimeOffset EndedAtUtc, bool TimedOut, string StdoutPath, string StderrPath, bool CompletionSignaled = false)
{
    public TimeSpan Duration => EndedAtUtc - StartedAtUtc;
}

public sealed class ProcessRunner
{
    private static readonly TimeSpan OutputDrainTimeout = TimeSpan.FromSeconds(5);
    private readonly ProcessSupervisor processSupervisor = ProcessSupervisor.Shared;

    public async Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory, string stdoutPath, string stderrPath, TimeSpan timeout, CancellationToken cancellationToken, string? standardInput = null, IReadOnlyDictionary<string, string>? environmentOverrides = null, string? completionSignalPath = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(stdoutPath)!);
        var start = new ProcessStartInfo(executable) { WorkingDirectory = workingDirectory, RedirectStandardInput = standardInput is not null, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (standardInput is not null) start.StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (environmentOverrides is not null)
            foreach (var (name, value) in environmentOverrides)
                start.Environment[name] = value;

        Process? process;
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            process = processSupervisor.Start(start);
            if (process is null) throw new InvalidOperationException($"Could not start '{executable}'.");
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Could not start '{executable}'. Ensure it is installed and available on PATH.", exception);
        }

        using (process)
        {
            await using var stdout = new FileStream(stdoutPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            await using var stderr = new FileStream(stderrPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            using var outputCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var stdoutTask = processSupervisor.CopyAsync(process.StandardOutput.BaseStream, stdout, outputCancellation.Token);
            var stderrTask = processSupervisor.CopyAsync(process.StandardError.BaseStream, stderr, outputCancellation.Token);
            var stdinTask = standardInput is null ? Task.CompletedTask : WriteStandardInputAsync(process, standardInput, cancellationToken);
            var timedOut = false;
            var completionSignaled = false;
            long? observedCompletionLength = null;
            DateTimeOffset? completionStableSince = null;
            var deadline = startedAt + timeout;
            var processExit = processSupervisor.WaitForExitAsync(process, timeout: null, CancellationToken.None);

            try
            {
                while (!processExit.IsCompleted)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var remaining = deadline - DateTimeOffset.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        timedOut = true;
                        break;
                    }
                    if (completionSignalPath is not null && File.Exists(completionSignalPath))
                    {
                        var length = new FileInfo(completionSignalPath).Length;
                        if (length > 0 && observedCompletionLength == length)
                        {
                            if (DateTimeOffset.UtcNow - completionStableSince >= TimeSpan.FromSeconds(2) && IsValidJson(completionSignalPath))
                            {
                                completionSignaled = true;
                                break;
                            }
                        }
                        else
                        {
                            observedCompletionLength = length;
                            completionStableSince = DateTimeOffset.UtcNow;
                        }
                    }
                    await Task.WhenAny(processExit, Task.Delay(TimeSpan.FromSeconds(Math.Min(1, remaining.TotalSeconds)), cancellationToken));
                }
            }
            catch (OperationCanceledException)
            {
                await processSupervisor.TerminateProcessTreeAsync(process, CancellationToken.None);
                await processExit;
                outputCancellation.Cancel();
                throw;
            }

            if (timedOut || completionSignaled)
            {
                await processSupervisor.TerminateProcessTreeAsync(process, CancellationToken.None);
                await processExit;
            }

            var drain = Task.WhenAll(stdoutTask, stderrTask);
            if (!await processSupervisor.WaitAsync(drain, OutputDrainTimeout))
            {
                outputCancellation.Cancel();
                try { await drain; }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            }

            cancellationToken.ThrowIfCancellationRequested();
            await stdinTask;
            return new ProcessResult(process.ExitCode, startedAt, DateTimeOffset.UtcNow, timedOut, stdoutPath, stderrPath, completionSignaled);
        }
    }

    private static bool IsValidJson(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var _ = JsonDocument.Parse(stream);
            return true;
        }
        catch (JsonException) { return false; }
        catch (IOException) { return false; }
    }

    private static async Task WriteStandardInputAsync(Process process, string input, CancellationToken cancellationToken)
    {
        await process.StandardInput.WriteAsync(input.AsMemory(), cancellationToken);
        process.StandardInput.Close();
    }
}
