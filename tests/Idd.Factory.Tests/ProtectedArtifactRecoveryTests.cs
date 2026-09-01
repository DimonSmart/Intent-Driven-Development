using Idd.Factory.Agents;
using Idd.Factory.Configuration;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.Runtime;
using Idd.Factory.State;
using Idd.Factory.Telemetry;
using Idd.Factory.Verification;
using static Idd.Factory.Tests.FactoryRuntimeTestHarness;

namespace Idd.Factory.Tests;

public sealed class ProtectedArtifactRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterruptedWorkerRestoresChangedOrDeletedRunnerState(bool deleteArtifact)
    {
        using var temp = new TestWorkspace();
        var protectedPath = temp.Write(".idd/factory/current/state.json", "state");
        var invocation = Invocation(temp);

        var exception = await Assert.ThrowsAsync<AgentProtocolException>(() =>
            Executor(new MutatingBackend(invocation.AttemptId, () =>
            {
                if (deleteArtifact) File.Delete(protectedPath);
                else File.WriteAllText(protectedPath, "changed");
            })).ExecuteAsync(invocation, default));

        Assert.Equal("WORKER_CHANGED_RUNNER_STATE", exception.Code);
        Assert.Equal("state", File.ReadAllText(protectedPath));
        Assert.Contains("snapshot was restored", exception.Message);
    }

    [Theory]
    [InlineData(".idd/intent/spec.md", "WORKER_CHANGED_PRODUCT_INTENT")]
    [InlineData(".idd/verification.yaml", "WORKER_CHANGED_PRODUCT_INTENT")]
    [InlineData(".idd/factory.yaml", "WORKER_CHANGED_FACTORY_POLICY")]
    [InlineData(".idd/factory/current/plan-revisions/P000001.json", "WORKER_CHANGED_RUNNER_STATE")]
    public async Task ProtectedPoliciesRestoreOriginalContentAndKeepSpecificErrorCode(string relativePath, string expectedCode)
    {
        using var temp = new TestWorkspace();
        var protectedPath = temp.Write(relativePath, "original");
        var invocation = Invocation(temp);

        var exception = await Assert.ThrowsAsync<AgentProtocolException>(() =>
            Executor(new MutatingBackend(invocation.AttemptId, () => File.WriteAllText(protectedPath, "worker mutation")))
                .ExecuteAsync(invocation, default));

        Assert.Equal(expectedCode, exception.Code);
        Assert.Equal("original", File.ReadAllText(protectedPath));
    }

    [Fact]
    public async Task NewFileInsideProtectedDirectoryIsRemoved()
    {
        using var temp = new TestWorkspace();
        temp.Write(".idd/factory/current/work-items/W000001/contract.md", "original contract");
        var addedPath = Path.Combine(temp.Path, ".idd", "factory", "current", "work-items", "worker-added.md");
        var invocation = Invocation(temp);

        var exception = await Assert.ThrowsAsync<AgentProtocolException>(() =>
            Executor(new MutatingBackend(invocation.AttemptId, () => File.WriteAllText(addedPath, "not allowed")))
                .ExecuteAsync(invocation, default));

        Assert.Equal("WORKER_CHANGED_RUNNER_STATE", exception.Code);
        Assert.False(File.Exists(addedPath));
        Assert.Equal("original contract", File.ReadAllText(Path.Combine(temp.Path, ".idd", "factory", "current", "work-items", "W000001", "contract.md")));
    }

    [Fact]
    public async Task CorruptedStateIsRestoredBeforeRuntimeRecordsTheViolation()
    {
        using var temp = new TestWorkspace();
        var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        var clock = new FakeClock();
        var runtime = new FactoryRuntime(
            temp.Path,
            CreateConfiguration(),
            new FileFactoryStateStore(current, new FactoryStateValidator()),
            Executor(new StateCorruptingBackend(current)),
            new VerificationEngine(temp.Path, current),
            new FactoryEventWriter(current, clock),
            clock);

        var outcome = await runtime.RunRequestAsync("Protect authoritative state", "test", default);
        var state = await LoadState(temp.Path);

        Assert.Equal("WORKER_CHANGED_RUNNER_STATE", outcome.FactoryOutcome);
        Assert.Equal(FactoryRunStatus.Blocked, state.RunStatus);
        Assert.Equal("WORKER_CHANGED_RUNNER_STATE", state.Blocker!.Code);
        Assert.NotNull(state.PendingContinuation);
        Assert.Equal("A000001", state.CurrentAttemptId);
    }

    private static FactoryAgentExecutor Executor(IAgentBackend backend) => new(backend, new FactoryAgentResultValidator());

    private static AgentInvocation Invocation(TestWorkspace temp)
    {
        var placeholder = temp.Write(".idd/factory/current/attempts/A000001/placeholder", "x");
        var agent = FactoryCapabilityCatalog.ResolveWorkItem("implementation").Agent;
        return new AgentInvocation
        {
            RunId = "run",
            AttemptId = "A000001",
            Capability = "implementation",
            Role = agent.Role,
            WorkItemId = "W1",
            Workspace = temp.Path,
            RawResultPath = Path.Combine(Path.GetDirectoryName(placeholder)!, "raw-result.json"),
            SkillName = agent.SkillName,
            ExecutionProfile = agent.ExecutionProfile,
            SemanticResultSchema = "implementation-v1",
            Input = "focused input",
            StartedAt = DateTimeOffset.UnixEpoch
        };
    }

    private sealed class MutatingBackend(string attemptId, Action mutate) : IAgentBackend
    {
        public Task<AgentRunHandle> StartAsync(AgentInvocation invocation, CancellationToken cancellationToken)
        {
            mutate();
            return Task.FromResult(new AgentRunHandle(attemptId, 1, attemptId));
        }

        public Task<AgentProcessResult> WaitAsync(AgentRunHandle handle, CancellationToken cancellationToken) =>
            Task.FromResult(new AgentProcessResult(1, "", "worker crashed", false, false, AgentTerminationKind.TransportFailure));

        public Task CancelAsync(AgentRunHandle handle, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StateCorruptingBackend(string currentDirectory) : IAgentBackend
    {
        public Task<AgentRunHandle> StartAsync(AgentInvocation invocation, CancellationToken cancellationToken)
        {
            File.WriteAllText(Path.Combine(currentDirectory, "state.json"), "corrupted by worker");
            return Task.FromResult(new AgentRunHandle(invocation.AttemptId, 1, invocation.AttemptId));
        }

        public Task<AgentProcessResult> WaitAsync(AgentRunHandle handle, CancellationToken cancellationToken) =>
            Task.FromResult(new AgentProcessResult(1, "", "worker crashed", false, false, AgentTerminationKind.TransportFailure));

        public Task CancelAsync(AgentRunHandle handle, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
