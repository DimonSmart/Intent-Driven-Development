using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.State;
using static Idd.Factory.Tests.FactoryRuntimeTestHarness;

namespace Idd.Factory.Tests;

public sealed class RecoveryHistoryTests
{
    [Fact]
    public async Task OrphanGraphMutationHistoryDoesNotInfluenceRecovery()
    {
        using var temp = new TestWorkspace();
        var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        temp.Write(".idd/factory/current/request.md", "Resume from authoritative state");
        temp.Write(".idd/factory/current/work-items/A/contracts/000001.md", "# A");
        temp.Write(".idd/factory/current/graph/mutations/orphan.json", "{\"fromGraphRevision\":1,\"toGraphRevision\":99}");
        var state = StateStoreTests.State() with { RunId = "resume", GraphRevision = 1, FactoryConfigurationHash = CreateConfiguration().Hash };
        state.WorkItems.Add(StateStoreTests.Executable("A", WorkItemStatus.Ready));
        await new FileFactoryStateStore(current, new FactoryStateValidator()).CreateAsync(state, default);
        var backend = new FakeAgentBackend();
        backend.Enqueue(invocation => Envelope(invocation, "completed"));
        backend.Enqueue(invocation => Envelope(invocation, "approved"));

        var outcome = await CreateRuntime(temp.Path, backend).ContinueAsync(default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal("A", backend.Invocations.First().WorkItemId);
    }
}
