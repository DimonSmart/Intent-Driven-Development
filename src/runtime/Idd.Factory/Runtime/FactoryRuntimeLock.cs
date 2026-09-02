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
    private readonly FactoryRuntimeLockDescriptor descriptor;
    private bool disposed;

    private FactoryRuntimeLock(string path, FactoryRuntimeLockDescriptor descriptor)
    {
        this.path = path;
        this.descriptor = descriptor;
    }

    public static FactoryRuntimeLock Acquire(string path, string operation, DateTimeOffset startedAt)
    {
        path = Path.GetFullPath(path);
        var descriptor = new FactoryRuntimeLockDescriptor(Environment.ProcessId, Environment.MachineName, startedAt, operation);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, descriptor, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(tempPath, path, overwrite: false);
            }
            catch (IOException) when (File.Exists(path))
            {
                throw AlreadyRunning(path);
            }

            return new(path, descriptor);
        }
        finally
        {
            TryDelete(tempPath);
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

    internal static bool IsHeld(string path) => File.Exists(Path.GetFullPath(path));

    public ValueTask DisposeAsync()
    {
        if (disposed) return ValueTask.CompletedTask;
        disposed = true;

        if (TryReadDescriptor(path) == descriptor)
        {
            TryDelete(path);
        }

        return ValueTask.CompletedTask;
    }

    private static FactoryStateException AlreadyRunning(string path)
    {
        const string reason = "A Factory runtime lock already exists for this workspace. A timed-out or disconnected caller does not imply that the runtime stopped.";
        var owner = TryReadDescriptor(path);
        return owner is null
            ? new("FACTORY_ALREADY_RUNNING", reason)
            : new("FACTORY_ALREADY_RUNNING",
                $"{reason} PID {owner.ProcessId} on {owner.MachineName}; operation '{owner.Operation}'; started {owner.StartedAt:O}.");
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
