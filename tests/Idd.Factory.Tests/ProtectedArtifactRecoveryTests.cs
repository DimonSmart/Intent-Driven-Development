using Idd.Factory.Agents;
using Idd.Factory.Domain;

namespace Idd.Factory.Tests;

public sealed class ProtectedArtifactRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterruptedWorkerCannotBypassProtectedArtifactGuard(bool deleteArtifact)
    {
        using var temp = new TestWorkspace();
        var protectedPath = temp.Write(".idd/factory/current/state.json", "state");
        var placeholder = temp.Write(".idd/factory/current/attempts/A000001/placeholder", "x");
        var agent = FactoryCapabilityCatalog.ResolveWorkItem("implementation").Agent;
        var invocation = new AgentInvocation
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

        var exception = await Assert.ThrowsAsync<AgentProtocolException>(() =>
            new FactoryAgentExecutor(
                new InterruptedMutatingBackend(invocation.AttemptId, protectedPath, deleteArtifact),
                new FactoryAgentResultValidator()).ExecuteAsync(invocation, default));

        Assert.Equal("WORKER_CHANGED_RUNNER_STATE", exception.Code);
    }

    private sealed class InterruptedMutatingBackend(string attemptId, string protectedPath, bool deleteArtifact) : IAgentBackend
    {
        public Task<AgentRunHandle> StartAsync(AgentInvocation invocation, CancellationToken cancellationToken)
        {
            if (deleteArtifact) File.Delete(protectedPath);
            else File.WriteAllText(protectedPath, "changed");
            return Task.FromResult(new AgentRunHandle(attemptId, 1, attemptId));
        }

        public Task<AgentProcessResult> WaitAsync(AgentRunHandle handle, CancellationToken cancellationToken) =>
            Task.FromResult(new AgentProcessResult(1, "", "worker crashed", false, false, AgentTerminationKind.TransportFailure));

        public Task CancelAsync(AgentRunHandle handle, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
