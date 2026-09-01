using static Idd.Factory.Tests.FactoryRuntimeTestHarness;

namespace Idd.Factory.Tests;

public sealed class IncrementalPlanningTests
{
    [Fact]
    public async Task ExhaustedKnownWorkRunsPlanningAgainWithoutConsumingReplanBudget()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        var defaults = CreateConfiguration();
        var configuration = defaults with { Limits = defaults.Limits with { MaxReplans = 0 } };

        backend.Enqueue(x => Envelope(x, "ready", new { tasks = new[] { Work("Research A", "research") } }));
        backend.Enqueue(x => Envelope(x, "completed", new { finding = "A reveals the next task" }));
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = new[] { Work("Research B", "research") } }));
        backend.Enqueue(x => Envelope(x, "completed", new { finding = "B completes the product work" }));
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = Array.Empty<object>() }));
        backend.Enqueue(x => Envelope(x, "blocked", reason: "Stop after proving planning quiescence"));

        var outcome = await CreateRuntime(temp.Path, backend, configuration: configuration)
            .RunRequestAsync("Discover dependent work incrementally", "test", default);
        var state = await LoadState(temp.Path);

        Assert.Equal("BLOCKED", outcome.FactoryOutcome);
        Assert.Equal(0, state.ReplanCount);
        Assert.Equal(2, state.Completed.Count);
        Assert.Equal(2, state.PlannedThroughCompletedCount);
        Assert.Empty(state.Remaining);
        Assert.Null(state.Current);

        var planners = backend.Invocations.Where(x => x.Role == "task-decomposer").ToArray();
        Assert.Equal(3, planners.Length);
        Assert.Contains("initial request", planners[0].Input);
        Assert.Contains("new completed work since previous planning: 1 item(s)", planners[1].Input);
        Assert.Contains("W000001", planners[1].Input);
        Assert.Contains("new completed work since previous planning: 1 item(s)", planners[2].Input);
        Assert.Contains("W000002", planners[2].Input);
    }
}
