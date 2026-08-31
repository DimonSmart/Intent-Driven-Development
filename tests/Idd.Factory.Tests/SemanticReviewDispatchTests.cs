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
}
