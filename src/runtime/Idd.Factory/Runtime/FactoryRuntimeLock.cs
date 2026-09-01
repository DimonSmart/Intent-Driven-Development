using System.Collections.Concurrent;
using System.Diagnostics;
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
    private static readonly ConcurrentDictionary<string, byte> HeldPaths = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
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
        path = Path.GetFullPath(path);
        if (!HeldPaths.TryAdd(path, 0)) throw AlreadyRunning(path);

        var existingOwner = TryReadDescriptor(path);
        if (existingOwner is not null && IsLocalOwnerAlive(existingOwner))
        {
            HeldPaths.TryRemove(path, out _);
            throw AlreadyRunning(path);
        }

        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete);
        }
        catch (IOException)
        {
            HeldPaths.TryRemove(path, out _);
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
            HeldPaths.TryRemove(path, out _);
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

    internal static bool IsHeld(string path)
    {
        path = Path.GetFullPath(path);
        if (HeldPaths.ContainsKey(path)) return true;

        var owner = TryReadDescriptor(path);
        if (owner is not null && IsLocalOwnerAlive(owner)) return true;
        if (owner is not null && StringComparer.OrdinalIgnoreCase.Equals(owner.MachineName, Environment.MachineName)) return false;
        if (!File.Exists(path)) return false;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            return false;
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            TryDelete(path);
            await stream.DisposeAsync();
        }
        finally
        {
            HeldPaths.TryRemove(path, out _);
        }
    }

    private static bool IsLocalOwnerAlive(FactoryRuntimeLockDescriptor owner)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(owner.MachineName, Environment.MachineName)) return false;
        try
        {
            using var process = Process.GetProcessById(owner.ProcessId);
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static FactoryStateException AlreadyRunning(string path)
    {
        const string reason = "A Factory runtime is already active for this workspace. A timed-out or disconnected caller does not imply that the runtime stopped.";
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
