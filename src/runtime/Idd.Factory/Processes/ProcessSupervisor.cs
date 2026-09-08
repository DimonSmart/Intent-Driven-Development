using System.Diagnostics;
using System.Text;

namespace Idd.Factory.Processes;

internal sealed record ProcessTerminationOptions(
    bool TryGracefulClose,
    TimeSpan GracefulCloseDelay,
    TimeSpan TerminationTimeout);

internal sealed record ProcessTerminationResult(
    bool Succeeded,
    bool TimedOut = false,
    Exception? Error = null);

internal sealed class ProcessSupervisor
{
    public static ProcessSupervisor Shared { get; } = new();

    public Process? Start(ProcessStartInfo startInfo)
    {
        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        try
        {
            if (process.Start()) return process;
            process.Dispose();
            return null;
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    public async Task<bool> WaitForExitAsync(
        Process process,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        if (timeout is null)
        {
            await process.WaitForExitAsync(cancellationToken);
            return true;
        }

        using var timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeout.Value);
        try
        {
            await process.WaitForExitAsync(timeoutCancellation.Token);
            return true;
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested
            && timeoutCancellation.IsCancellationRequested)
        {
            return false;
        }
    }

    public async Task<bool> WaitAsync(
        Task task,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeout);
        try
        {
            await task.WaitAsync(timeoutCancellation.Token);
            return true;
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested
            && timeoutCancellation.IsCancellationRequested)
        {
            return false;
        }
    }

    public async Task<string> CaptureAsync(
        StreamReader reader,
        string path,
        CancellationToken cancellationToken)
    {
        var text = await reader.ReadToEndAsync(cancellationToken);
        await File.WriteAllTextAsync(path, text, cancellationToken);
        return text;
    }

    public async Task<string> CaptureLinesAsync(
        StreamReader reader,
        string path,
        Action<string>? observeLine,
        CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        await using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            observeLine?.Invoke(line);
            text.AppendLine(line);
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
        }
        return text.ToString();
    }

    public async Task PumpAsync(
        StreamReader reader,
        Func<char[], int, CancellationToken, Task> consume,
        CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = await reader.ReadAsync(buffer);
            if (count == 0) return;
            await consume(buffer, count, cancellationToken);
        }
    }

    public async Task TerminateProcessTreeAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        if (process.HasExited) return;
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            return;
        }
        if (!process.HasExited)
            await process.WaitForExitAsync(cancellationToken);
    }

    public async Task<ProcessTerminationResult> TerminateAsync(
        Process process,
        ProcessTerminationOptions options)
    {
        if (process.HasExited)
            return new(true);

        try
        {
            if (options.TryGracefulClose && !process.HasExited)
            {
                try
                {
                    process.CloseMainWindow();
                    if (options.GracefulCloseDelay > TimeSpan.Zero)
                        await Task.Delay(options.GracefulCloseDelay);
                }
                catch
                {
                    // Best-effort graceful shutdown. Forced tree termination below is authoritative.
                }
            }

            using var timeout = new CancellationTokenSource(options.TerminationTimeout);
            try
            {
                await TerminateProcessTreeAsync(process, timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                return new(false, TimedOut: true);
            }

            return new(process.HasExited);
        }
        catch (Exception exception)
        {
            return new(process.HasExited, Error: exception);
        }
    }
}
