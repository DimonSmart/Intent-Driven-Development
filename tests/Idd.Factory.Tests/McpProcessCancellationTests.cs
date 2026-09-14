using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Processes;
using Idd.Factory.Runtime;

namespace Idd.Factory.Tests;

public sealed class McpProcessCancellationTests
{
    [Fact]
    public async Task CancelledInvocationReleasesLockOwnedByTerminatedRuntimeAndRethrowsCancellation()
    {
        using var workspace = new TestWorkspace();
        var lockPath = Path.Combine(workspace.Path, ".idd", "factory", "runtime.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        const int processId = 424242;
        var descriptor = new FactoryRuntimeLockDescriptor(
            processId,
            Environment.MachineName,
            DateTimeOffset.UtcNow,
            "run");
        await File.WriteAllTextAsync(
            lockPath,
            JsonSerializer.Serialize(descriptor, FactoryJson.Options));
        var executor = new StubProcessExecutor((_, _) => Task.FromResult(new ProcessExecutionResult(
            processId,
            null,
            ProcessCompletionReason.Cancelled,
            "partial stdout",
            "partial stderr",
            new(true, true),
            [])));
        var invoker = new SystemFactoryProcessInvoker(executor);
        var invocation = new FactoryProcessInvocation(
            "dotnet",
            [],
            workspace.Path,
            null);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            invoker.RunAsync(invocation, cancellation.Token));

        Assert.False(File.Exists(lockPath));
    }
}
