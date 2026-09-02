using Idd.Factory.Agents;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.Runtime;
using Idd.Factory.State;
using Idd.Factory.Telemetry;
using Idd.Factory.Verification;
using static Idd.Factory.Tests.FactoryRuntimeTestHarness;

namespace Idd.Factory.Tests;

public sealed class TransportFailureAttemptBudgetTests
{
    [Fact]
    public async Task TransportFailureDoesNotConsumeSemanticAttemptBudget()
    {
        using var temp = new TestWorkspace();
        var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        var clock = new FakeClock();
        var backend = new PlanningThenTransportFailureBackend();
        var runtime = new FactoryRuntime(
            temp.Path,
            CreateConfiguration(),
            new FileFactoryStateStore(current, new FactoryStateValidator()),
            new FactoryAgentExecutor(backend, new FactoryAgentResultValidator()),
            new VerificationEngine(temp.Path, current),
            new FactoryEventWriter(current, clock),
            clock);

        var outcome = await runtime.RunRequestAsync("Do research work", "test", default);
        var state = await LoadState(temp.Path);

        Assert.Equal("AGENT_TRANSPORT_FAILURE", outcome.FactoryOutcome);
        Assert.NotNull(state.Current);
        Assert.Equal("research", state.Current.Capability);
        Assert.Equal(0, state.Current.AttemptCount);
        Assert.Equal(state.CurrentAttemptId, state.Current.CurrentAttemptId);
        Assert.True(state.PendingContinuation?.IsResumable);
    }

    private sealed class PlanningThenTransportFailureBackend : IAgentBackend
    {
        private readonly HashSet<string> transportAttempts = [];

        public Task<AgentRunHandle> StartAsync(AgentInvocation invocation, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(invocation.RawResultPath)!);
            if (invocation.Capability == "planning")
            {
                File.WriteAllText(
                    invocation.RawResultPath,
                    """
                    {"outcome":"ready","tasks":[{"capability":"research","task":"# Research task"}]}
                    """);
            }
            else if (invocation.Capability == "research")
            {
                transportAttempts.Add(invocation.AttemptId);
            }
            else
            {
                throw new InvalidOperationException($"Unexpected capability {invocation.Capability}.");
            }

            return Task.FromResult(new AgentRunHandle(invocation.AttemptId, 1, invocation.AttemptId));
        }

        public Task<AgentProcessResult> WaitAsync(AgentRunHandle handle, CancellationToken cancellationToken) =>
            Task.FromResult(transportAttempts.Contains(handle.AttemptId)
                ? new AgentProcessResult(1, "transport failed", "", false, false, AgentTerminationKind.TransportFailure)
                : new AgentProcessResult(0, "", "", true, false, AgentTerminationKind.CleanExit));

        public Task CancelAsync(AgentRunHandle handle, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
