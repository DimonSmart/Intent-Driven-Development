using System.Diagnostics;

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
