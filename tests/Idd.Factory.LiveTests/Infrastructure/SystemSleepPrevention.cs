using System.Runtime.InteropServices;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed class SystemSleepPrevention : IDisposable
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;

    private readonly ManualResetEventSlim? stop;
    private readonly Thread? thread;

    private SystemSleepPrevention(ManualResetEventSlim? stop, Thread? thread)
    {
        this.stop = stop;
        this.thread = thread;
    }

    public static SystemSleepPrevention Acquire()
    {
        if (!OperatingSystem.IsWindows()) return new(null, null);

        var ready = new ManualResetEventSlim();
        var stop = new ManualResetEventSlim();
        Exception? startupError = null;
        var thread = new Thread(() =>
        {
            if (SetThreadExecutionState(EsContinuous | EsSystemRequired) == 0)
                startupError = new InvalidOperationException("Windows refused the live-eval system sleep prevention request.");
            ready.Set();
            if (startupError is not null) return;
            stop.Wait();
            SetThreadExecutionState(EsContinuous);
        })
        {
            IsBackground = true,
            Name = "IDD Factory live-eval sleep prevention"
        };
        thread.Start();
        ready.Wait();
        ready.Dispose();
        if (startupError is not null)
        {
            stop.Dispose();
            thread.Join();
            throw startupError;
        }
        return new(stop, thread);
    }

    public void Dispose()
    {
        if (stop is null || thread is null) return;
        stop.Set();
        thread.Join();
        stop.Dispose();
    }

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint executionState);
}
