using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.State;

namespace Idd.Factory.Runtime;

internal sealed record FactoryRuntimeLockDescriptor(
    int ProcessId,
    string MachineName,
    DateTimeOffset StartedAt,
    string Operation);

internal sealed class FactoryRuntimeLock : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(FactoryJson.Options) { WriteIndented = true };
    private readonly string path;
    private readonly FileStream stream;
    private bool disposed;

    private FactoryRuntimeLock(string path, FileStream stream)
    {
        this.path = path;
        this.stream = stream;
    }

    public static FactoryRuntimeLock Acquire(string path, string operation, DateTimeOffset startedAt)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete);
        }
        catch (IOException)
        {
            throw AlreadyRunning(path);
        }

        try
        {
            var descriptor = new FactoryRuntimeLockDescriptor(Environment.ProcessId, Environment.MachineName, startedAt, operation);
            stream.SetLength(0);
            JsonSerializer.Serialize(stream, descriptor, JsonOptions);
            stream.Flush(flushToDisk: true);
            return new(path, stream);
        }
        catch
        {
            TryDelete(path);
            stream.Dispose();
            throw;
        }
    }

    internal static FactoryRuntimeLockDescriptor? TryReadDescriptor(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<FactoryRuntimeLockDescriptor>(stream, JsonOptions);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        TryDelete(path);
        await stream.DisposeAsync();
    }

    private static FactoryStateException AlreadyRunning(string path)
    {
        var owner = TryReadDescriptor(path);
        return owner is null
            ? new("FACTORY_ALREADY_RUNNING", "Another Factory runtime owns this workspace.")
            : new("FACTORY_ALREADY_RUNNING",
                $"Another Factory runtime owns this workspace. PID {owner.ProcessId} on {owner.MachineName}; operation '{owner.Operation}'; started {owner.StartedAt:O}.");
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
