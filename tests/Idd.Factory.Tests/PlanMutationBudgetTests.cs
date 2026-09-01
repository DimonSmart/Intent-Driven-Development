using static Idd.Factory.Tests.FactoryRuntimeTestHarness;

namespace Idd.Factory.Tests;

public sealed class PlanMutationBudgetTests
{
    [Fact]
    public async Task CheckpointCorrectionWorkLimitDoesNotConsumeCorrectiveCycleOrMutatePlan()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        var defaults = CreateConfiguration();
        var configuration = defaults with
        {
            Limits = defaults.Limits with { MaxCorrectiveCycles = 1, MaxWorkItems = 1 }
        };
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = new[] { Work("Review", "semantic-review") } }));
        backend.Enqueue(x => Envelope(x, "correction-required", new
        {
            capability = "research",
            task = "Investigate the defect",
            reason = "Review found a defect"
        }));

        var outcome = await CreateRuntime(temp.Path, backend, configuration: configuration)
            .RunRequestAsync("Keep checkpoint correction atomic", "test", default);
        var state = await LoadState(temp.Path);

        Assert.Equal("WORK_EXPANSION_BUDGET_EXHAUSTED", outcome.FactoryOutcome);
        Assert.Equal(0, state.CorrectiveCycleCount);
        Assert.Equal(2, state.NextWorkItemNumber);
        Assert.Equal(1, state.PlanRevision);
        Assert.NotNull(state.Current);
        Assert.Equal("W000001", state.Current!.Id);
        Assert.Equal("semantic-review", state.Current.Capability);
        Assert.Empty(state.Remaining);
        Assert.False(File.Exists(Path.Combine(temp.Path, ".idd", "factory", "current", "work-items", "W000002", "contract.md")));
        Assert.Empty(backend.Invocations.Where(x => x.Role == "researcher"));
    }

    [Fact]
    public async Task FinalReviewCorrectionWorkLimitDoesNotConsumeCorrectiveCycleOrMutatePlan()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        var defaults = CreateConfiguration();
        var configuration = defaults with
        {
            Limits = defaults.Limits with { MaxCorrectiveCycles = 1, MaxWorkItems = 1 }
        };
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = new[] { Work("A", "research") } }));
        backend.Enqueue(x => Envelope(x, "completed"));
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = Array.Empty<object>() }));
        backend.Enqueue(x => Envelope(x, "correction-required", new
        {
            capability = "research",
            task = "Correct the integrated defect",
            reason = "Final review found a defect"
        }));

        var outcome = await CreateRuntime(temp.Path, backend, configuration: configuration)
            .RunRequestAsync("Keep final correction atomic", "test", default);
        var state = await LoadState(temp.Path);

        Assert.Equal("WORK_EXPANSION_BUDGET_EXHAUSTED", outcome.FactoryOutcome);
        Assert.Equal(0, state.CorrectiveCycleCount);
        Assert.Equal(2, state.NextWorkItemNumber);
        Assert.Equal(3, state.PlanRevision);
        Assert.Equal(1, state.PlannedThroughCompletedCount);
        Assert.Single(state.Completed);
        Assert.Null(state.Current);
        Assert.Empty(state.Remaining);
        Assert.Null(state.FinalReview);
        Assert.False(File.Exists(Path.Combine(temp.Path, ".idd", "factory", "current", "work-items", "W000002", "contract.md")));
    }

    [Fact]
    public async Task ReplanWorkLimitCountsCompletedWorkAndLeavesReplacementUnapplied()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        var defaults = CreateConfiguration();
        var configuration = defaults with { Limits = defaults.Limits with { MaxWorkItems = 2 } };
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = new[] { Work("A", "research") } }));
        backend.Enqueue(x => Envelope(x, "completed"));
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = Array.Empty<object>() }));
        backend.Enqueue(x => Envelope(x, "global-replan-required", new
        {
            finding = "The integrated strategy must change"
        }, "Replace the remaining strategy"));
        backend.Enqueue(x => Envelope(x, "ready", new
        {
            tasks = new[]
            {
                Work("B", "research"),
                Work("C", "research")
            }
        }));

        var outcome = await CreateRuntime(temp.Path, backend, configuration: configuration)
            .RunRequestAsync("Keep replanning within the total work budget", "test", default);
        var state = await LoadState(temp.Path);

        Assert.Equal("WORK_EXPANSION_BUDGET_EXHAUSTED", outcome.FactoryOutcome);
        Assert.Equal(0, state.ReplanCount);
        Assert.Equal(2, state.NextWorkItemNumber);
        Assert.Equal(3, state.PlanRevision);
        Assert.Equal(1, state.PlannedThroughCompletedCount);
        Assert.Single(state.Completed);
        Assert.Null(state.Current);
        Assert.Empty(state.Remaining);
        Assert.NotNull(state.PendingReplanTrigger);
        Assert.False(File.Exists(Path.Combine(temp.Path, ".idd", "factory", "current", "work-items", "W000002", "contract.md")));
        Assert.Equal(3, backend.Invocations.Count(x => x.Role == "task-decomposer"));
    }
}
