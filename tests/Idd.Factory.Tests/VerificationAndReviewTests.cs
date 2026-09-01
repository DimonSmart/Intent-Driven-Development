using static Idd.Factory.Tests.FactoryRuntimeTestHarness;

namespace Idd.Factory.Tests;

public sealed class VerificationAndReviewTests
{
    [Fact]
    public async Task StrictFinalVerificationPrecedesFinalReview()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = new[] { Work("A", "research") } }));
        backend.Enqueue(x => Envelope(x, "completed"));
        backend.Enqueue(x => Envelope(x, "approved"));
        var outcome = await CreateRuntime(temp.Path, backend).RunRequestAsync("Verify and review", "test", default);
        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal("final-reviewer", backend.Invocations.Last().Role);
    }

    [Fact]
    public async Task FinalReviewCorrectionCreatesFutureWorkAndFreshReview()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = new[] { Work("A", "research") } }));
        backend.Enqueue(x => Envelope(x, "completed"));
        backend.Enqueue(x => Envelope(x, "correction-required", new { capability = "research", task = "Correct the integrated defect", reason = "Integrated review found a defect" }));
        backend.Enqueue(x => Envelope(x, "completed"));
        backend.Enqueue(x => Envelope(x, "approved"));
        var outcome = await CreateRuntime(temp.Path, backend).RunRequestAsync("Review and correct", "test", default);
        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal(2, backend.Invocations.Count(x => x.Role == "final-reviewer"));
        Assert.Equal(2, backend.Invocations.Count(x => x.Role == "researcher"));
    }

    [Fact]
    public async Task FinalReviewGlobalReplanRunsPlanningAndContinuesWithReplacementWork()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = new[] { Work("A", "research") } }));
        backend.Enqueue(x => Envelope(x, "completed"));
        backend.Enqueue(x => Envelope(x, "global-replan-required", new { finding = "Integrated review invalidated the remaining strategy" }, "Global strategy must change"));
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = new[] { Work("X", "research") } }));
        backend.Enqueue(x => Envelope(x, "completed"));
        backend.Enqueue(x => Envelope(x, "approved"));

        var outcome = await CreateRuntime(temp.Path, backend).RunRequestAsync("Review and replan", "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal(2, backend.Invocations.Count(x => x.Role == "task-decomposer"));
        Assert.Equal(2, backend.Invocations.Count(x => x.Role == "final-reviewer"));
        var replanInvocation = backend.Invocations.Where(x => x.Role == "task-decomposer").ElementAt(1);
        Assert.Contains("Global strategy must change", replanInvocation.Input);
        Assert.Contains("final-review", replanInvocation.Input);
    }
}
