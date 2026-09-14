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
    private static readonly UTF8Encoding TransportUtf8 = new(encoderShouldEmitUTF8Identifier: false);
    private readonly ProcessExecutor processExecutor = ProcessExecutor.Shared;

    public async Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory, string stdoutPath, string stderrPath, TimeSpan timeout, CancellationToken cancellationToken, string? standardInput = null, IReadOnlyDictionary<string, string>? environmentOverrides = null, string? completionSignalPath = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(stdoutPath)!);
        var request = new ProcessExecutionRequest(executable, arguments, workingDirectory)
        {
            EnvironmentOverrides = environmentOverrides,
            StandardInput = standardInput,
            StandardInputEncoding = standardInput is null ? null : TransportUtf8,
            OutputDrainTimeout = TimeSpan.FromSeconds(5),
            StandardOutput = new()
            {
                Capture = false,
                FilePath = stdoutPath
            },
            StandardError = new()
            {
                Capture = false,
                FilePath = stderrPath
            }
        };

        var startedAt = DateTimeOffset.UtcNow;
        ProcessExecutionHandle process;
        try
        {
            process = await processExecutor.StartAsync(request, cancellationToken);
        }
        catch (ProcessExecutionException exception)
        {
            if (exception.Result.CompletionReason == ProcessCompletionReason.Cancelled)
                throw new OperationCanceledException(cancellationToken);
            throw new InvalidOperationException($"Could not start '{executable}'. Ensure it is installed and available on PATH.", exception);
        }

        await using (process)
        {
            var timedOut = false;
            var completionSignaled = false;
            long? observedCompletionLength = null;
            DateTimeOffset? completionStableSince = null;
            var deadline = startedAt + timeout;
            var processExit = process.WaitForExitAsync(timeout: null, CancellationToken.None);

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
                await process.TerminateAsync();
                await process.CompleteAsync(ProcessCompletionReason.Cancelled);
                throw;
            }

            if (timedOut || completionSignaled)
                await process.TerminateAsync();
            else
                await processExit;

            var technical = await process.CompleteAsync(
                timedOut ? ProcessCompletionReason.TimedOut : ProcessCompletionReason.Exited);
            cancellationToken.ThrowIfCancellationRequested();
            return new ProcessResult(
                technical.ExitCode ?? -1,
                startedAt,
                DateTimeOffset.UtcNow,
                timedOut,
                stdoutPath,
                stderrPath,
                completionSignaled);
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
}
