using Idd.Factory.Runtime;
using Idd.Factory.State;

namespace Idd.Factory.Tests;

public sealed class FactoryRuntimeLockTests
{
    [Fact]
    public async Task LockWritesOwnerDescriptorAndDeletesFileOnDispose()
    {
        using var temp = new TestWorkspace();
        var path = LockPath(temp.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var startedAt = new DateTimeOffset(2026, 9, 1, 18, 43, 21, TimeSpan.Zero);

        await using (var held = FactoryRuntimeLock.Acquire(path, "run", startedAt))
        {
            Assert.True(File.Exists(path));
            Assert.True(FactoryRuntimeLock.IsHeld(path));
            var descriptor = FactoryRuntimeLock.TryReadDescriptor(path);

            Assert.NotNull(descriptor);
            Assert.Equal(Environment.ProcessId, descriptor.ProcessId);
            Assert.Equal(Environment.MachineName, descriptor.MachineName);
            Assert.Equal(startedAt, descriptor.StartedAt);
            Assert.Equal("run", descriptor.Operation);
        }

        Assert.False(File.Exists(path));
        Assert.False(FactoryRuntimeLock.IsHeld(path));
    }

    [Fact]
    public async Task ActiveLockRejectsSecondOwnerAndReportsCurrentOwner()
    {
        using var temp = new TestWorkspace();
        var path = LockPath(temp.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var startedAt = new DateTimeOffset(2026, 9, 1, 18, 43, 21, TimeSpan.Zero);

        await using var held = FactoryRuntimeLock.Acquire(path, "continue", startedAt);
        var exception = Assert.Throws<FactoryStateException>(() =>
            FactoryRuntimeLock.Acquire(path, "run", startedAt.AddMinutes(1)));

        Assert.Equal("FACTORY_ALREADY_RUNNING", exception.Code);
        Assert.Contains("timed-out or disconnected caller", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"PID {Environment.ProcessId}", exception.Message, StringComparison.Ordinal);
        Assert.Contains("operation 'continue'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(startedAt.ToString("O"), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaleLockIsReplacedByNewOwner()
    {
        using var temp = new TestWorkspace();
        var path = LockPath(temp.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "stale lock from terminated process");
        var startedAt = new DateTimeOffset(2026, 9, 1, 19, 0, 0, TimeSpan.Zero);

        Assert.False(FactoryRuntimeLock.IsHeld(path));
        await using (var held = FactoryRuntimeLock.Acquire(path, "run", startedAt))
        {
            Assert.True(FactoryRuntimeLock.IsHeld(path));
            var descriptor = FactoryRuntimeLock.TryReadDescriptor(path);
            Assert.NotNull(descriptor);
            Assert.Equal(startedAt, descriptor.StartedAt);
            Assert.Equal("run", descriptor.Operation);
        }

        Assert.False(File.Exists(path));
    }

    private static string LockPath(string workspace) => Path.Combine(workspace, ".idd", "factory", "runtime.lock");
}
