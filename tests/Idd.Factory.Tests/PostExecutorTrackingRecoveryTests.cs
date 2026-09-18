using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.Processes;
using Idd.Factory.State;

namespace Idd.Factory.Tests;

public sealed class PostExecutorTrackingRecoveryTests
{
    [Fact]
    public async Task TrackingFailureAfterExecutorResultDoesNotRerunExecutorAfterReload()
    {
        using var workspace = new TestWorkspace();
        var backend = new ScriptedAgentBackend();
        backend.Reply("# Task\n\nImplement A.");
        backend.Reply(invocation =>
        {
            var path = Path.Combine(invocation.Workspace, "src", "tracked-after-executor.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "executor change");
            return "Implementation completed.";
        });
        backend.Reply("# Done");

        var current = Path.Combine(workspace.Path, ".idd", "factory", "current");
        var failingGit = new FailAfterAttemptResultOnceExecutor(
            Path.Combine(current, "attempts", "A000002", "result.json"));
        var firstRuntime = FactoryTestRuntime.Create(
            workspace.Path,
            backend,
            workspaceProcessExecutor: failingGit);

        var blocked = await firstRuntime.RunRequestAsync("Implement A.", "test", default);

        Assert.Equal("WORKSPACE_TRACKING_FAILED", blocked.FactoryOutcome);
        Assert.True(failingGit.Failed);
        Assert.True(File.Exists(Path.Combine(current, "attempts", "A000002", "result.json")));
        Assert.False(File.Exists(Path.Combine(current, "attempts", "A000002", "workspace-changes.json")));
        Assert.Single(backend.Invocations, invocation => invocation.Capability == "implementation");

        var persisted = await new FileFactoryStateStore(current, new FactoryStateValidator()).LoadAsync(default);
        Assert.NotNull(persisted);
        Assert.Equal(FactoryRunStatus.Blocked, persisted!.RunStatus);
        Assert.Equal("WORKSPACE_TRACKING_FAILED", persisted.Blocker?.Code);
        Assert.Equal("A000002", persisted.CurrentAttemptId);
        Assert.Equal("A000002", persisted.Current?.CurrentAttemptId);

        var recoveredRuntime = FactoryTestRuntime.Create(workspace.Path, backend);
        var completed = await recoveredRuntime.ContinueAsync(default);

        Assert.Equal("COMPLETED", completed.FactoryOutcome);
        Assert.Single(backend.Invocations, invocation => invocation.Capability == "implementation");

        var resultDirectory = completed.ResultDirectory!;
        Assert.True(File.Exists(Path.Combine(
            resultDirectory,
            "attempts",
            "A000002",
            "workspace-changes.json")));
        var finalState = await new FileFactoryStateStore(
            resultDirectory,
            new FactoryStateValidator()).LoadAsync(default);
        Assert.Contains(
            "src/tracked-after-executor.txt",
            finalState!.Completed.Single().ChangedPaths);
        Assert.Contains(
            "src/tracked-after-executor.txt",
            finalState.FactoryRunChangedPaths);
    }

    private sealed class FailAfterAttemptResultOnceExecutor(string resultPath) : IProcessExecutor
    {
        public bool Failed { get; private set; }

        public Task<ProcessExecutionResult> RunAsync(
            ProcessExecutionRequest request,
            CancellationToken cancellationToken)
        {
            if (!Failed && File.Exists(resultPath))
            {
                Failed = true;
                return Task.FromResult(new ProcessExecutionResult(
                    null,
                    1,
                    ProcessCompletionReason.Exited,
                    "",
                    "injected post-executor tracking failure",
                    ProcessTerminationOutcome.NotRequested,
                    []));
            }

            return ProcessExecutor.Shared.RunAsync(request, cancellationToken);
        }
    }
}
