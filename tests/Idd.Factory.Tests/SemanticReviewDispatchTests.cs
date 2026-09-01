using static Idd.Factory.Tests.FactoryRuntimeTestHarness;

namespace Idd.Factory.Tests;

public sealed class SemanticReviewDispatchTests
{
    [Fact]
    public async Task CheckpointAndFinalReviewUseDistinctCanonicalWorkers()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(invocation => Envelope(invocation, "ready", new
        {
            tasks = new object[]
            {
                Work("A", "implementation"),
                new
                {
                    capability = "semantic-review",
                    task = "# Review A"
                }
            }
        }));
        backend.Enqueue(invocation => Envelope(invocation, "completed"));
        backend.Enqueue(invocation =>
        {
            Assert.Equal("checkpoint-reviewer", invocation.Role);
            Assert.Equal("idd-factory-review-checkpoint", invocation.SkillName);
            return Envelope(invocation, "approved");
        });
        backend.Enqueue(invocation =>
        {
            Assert.Equal("final-reviewer", invocation.Role);
            Assert.Equal("idd-factory-review-task", invocation.SkillName);
            return Envelope(invocation, "approved");
        });

        var outcome = await CreateRuntime(temp.Path, backend).RunRequestAsync("Use checkpoint and final semantic review", "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal(
            new[] { "task-decomposer", "implementer", "checkpoint-reviewer", "final-reviewer" },
            backend.Invocations.Select(x => x.Role));
    }

    [Fact]
    public async Task CheckpointReviewCorrectionStopsAtCorrectiveCycleLimit()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        var defaults = CreateConfiguration();
        var configuration = defaults with { Limits = defaults.Limits with { MaxCorrectiveCycles = 1 } };
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = new[] { Work("Review", "semantic-review") } }));
        backend.Enqueue(x => Envelope(x, "correction-required", new { capability = "research", task = "First correction", reason = "First defect" }));
        backend.Enqueue(x => Envelope(x, "completed"));
        backend.Enqueue(x => Envelope(x, "correction-required", new { capability = "research", task = "Second correction", reason = "Second defect" }));

        var outcome = await CreateRuntime(temp.Path, backend, configuration: configuration).RunRequestAsync("Bound checkpoint corrections", "test", default);
        var state = await LoadState(temp.Path);

        Assert.Equal("CORRECTIVE_BUDGET_EXHAUSTED", outcome.FactoryOutcome);
        Assert.Equal(1, state.CorrectiveCycleCount);
        Assert.Empty(state.Remaining);
        Assert.Equal(2, backend.Invocations.Count(x => x.Role == "checkpoint-reviewer"));
        Assert.Single(backend.Invocations.Where(x => x.Role == "researcher"));
    }
}
