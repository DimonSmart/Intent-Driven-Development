using Idd.Factory.Domain;
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
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = Array.Empty<object>() }));
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
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = Array.Empty<object>() }));
        backend.Enqueue(x => Envelope(x, "correction-required", new { capability = "research", task = "Correct the integrated defect", reason = "Integrated review found a defect" }));
        backend.Enqueue(x => Envelope(x, "completed"));
        backend.Enqueue(x => Envelope(x, "approved"));
        var outcome = await CreateRuntime(temp.Path, backend).RunRequestAsync("Review and correct", "test", default);
        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal(2, backend.Invocations.Count(x => x.Role == "task-decomposer"));
        Assert.Equal(2, backend.Invocations.Count(x => x.Role == "final-reviewer"));
        Assert.Equal(2, backend.Invocations.Count(x => x.Role == "researcher"));
    }

    [Theory]
    [InlineData("correction-required", PostCompletionRoute.FinalPipeline)]
    [InlineData("additional-work-required", PostCompletionRoute.IncrementalPlanning)]
    public async Task FinalReviewFutureWorkUsesOutcomeSpecificPostCompletionRoute(string reviewOutcome, PostCompletionRoute expectedRoute)
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = Array.Empty<object>() }));
        backend.Enqueue(x => Envelope(x, reviewOutcome, new { capability = "research", task = "Inspect route materialization", reason = "Test route" }));
        backend.Enqueue(x => Envelope(x, "blocked", reason: "Stop after route materialization"));

        await CreateRuntime(temp.Path, backend).RunRequestAsync("Materialize final review work", "test", default);
        var state = await LoadState(temp.Path);

        Assert.NotNull(state.Current);
        Assert.Equal(expectedRoute, state.Current!.PostCompletionRoute);
    }

    [Fact]
    public async Task FinalReviewAdditionalWorkKeepsNormalIncrementalPlanning()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = Array.Empty<object>() }));
        backend.Enqueue(x => Envelope(x, "additional-work-required", new { capability = "research", task = "Investigate prerequisite", reason = "More information is required" }));
        backend.Enqueue(x => Envelope(x, "completed"));
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = Array.Empty<object>() }));
        backend.Enqueue(x => Envelope(x, "approved"));

        var outcome = await CreateRuntime(temp.Path, backend).RunRequestAsync("Review with prerequisite", "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal(2, backend.Invocations.Count(x => x.Role == "task-decomposer"));
        Assert.Equal(2, backend.Invocations.Count(x => x.Role == "final-reviewer"));
        Assert.Single(backend.Invocations, x => x.Role == "researcher");
    }

    [Fact]
    public async Task RepeatedFinalReviewCorrectionsDoNotInvokeIncrementalPlanningBetweenCycles()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = Array.Empty<object>() }));
        backend.Enqueue(x => Envelope(x, "correction-required", new { capability = "research", task = "First correction", reason = "First defect" }));
        backend.Enqueue(x => Envelope(x, "completed"));
        backend.Enqueue(x => Envelope(x, "correction-required", new { capability = "research", task = "Second correction", reason = "Second defect" }));
        backend.Enqueue(x => Envelope(x, "completed"));
        backend.Enqueue(x => Envelope(x, "approved"));

        var outcome = await CreateRuntime(temp.Path, backend).RunRequestAsync("Repeat bounded corrections", "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Single(backend.Invocations, x => x.Role == "task-decomposer");
        Assert.Equal(3, backend.Invocations.Count(x => x.Role == "final-reviewer"));
        Assert.Equal(2, backend.Invocations.Count(x => x.Role == "researcher"));
    }

    [Fact]
    public async Task FinalReviewCorrectionRetainsFastPathAcrossDynamicPrerequisite()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = Array.Empty<object>() }));
        backend.Enqueue(x => Envelope(x, "correction-required", new { capability = "research", task = "Correction C", reason = "Bounded defect" }));
        backend.Enqueue(x => Envelope(x, "additional-work-required", new { capability = "research", task = "Prerequisite A", reason = "Need prerequisite first" }));
        backend.Enqueue(x => Envelope(x, "completed"));
        backend.Enqueue(x => Envelope(x, "completed"));
        backend.Enqueue(x => Envelope(x, "approved"));

        var outcome = await CreateRuntime(temp.Path, backend).RunRequestAsync("Correction with prerequisite", "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Single(backend.Invocations, x => x.Role == "task-decomposer");
        var research = backend.Invocations.Where(x => x.Role == "researcher").ToArray();
        Assert.Equal(3, research.Length);
        Assert.Equal(research[0].WorkItemId, research[2].WorkItemId);
        Assert.NotEqual(research[0].WorkItemId, research[1].WorkItemId);
    }

    [Fact]
    public async Task ImplementationCorrectionKeepsSubtaskAndFinalVerificationWithoutIntermediatePlanning()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = Array.Empty<object>() }));
        backend.Enqueue(x => Envelope(x, "correction-required", new { capability = "implementation", task = "Implement bounded correction", reason = "Integrated defect" }));
        backend.Enqueue(x => Envelope(x, "completed"));
        backend.Enqueue(x => Envelope(x, "approved"));
        var verification = new SequencedVerificationEngine(temp.Path,
            (Idd.Factory.Verification.VerificationStatus.Passed, "initial-final", 0, "passed"),
            (Idd.Factory.Verification.VerificationStatus.Passed, "correction-subtask", 0, "passed"),
            (Idd.Factory.Verification.VerificationStatus.Passed, "corrected-final", 0, "passed"));

        var outcome = await CreateRuntime(temp.Path, backend, verification: verification)
            .RunRequestAsync("Implementation correction", "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal(
            new[] { "task-decomposer", "final-reviewer", "implementer", "final-reviewer" },
            backend.Invocations.Select(x => x.Role).ToArray());
    }

    [Fact]
    public async Task FinalReviewCorrectionStopsAtCorrectiveCycleLimit()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        var defaults = CreateConfiguration();
        var configuration = defaults with { Limits = defaults.Limits with { MaxCorrectiveCycles = 1 } };
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = new[] { Work("A", "research") } }));
        backend.Enqueue(x => Envelope(x, "completed"));
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = Array.Empty<object>() }));
        backend.Enqueue(x => Envelope(x, "correction-required", new { capability = "research", task = "First correction", reason = "First defect" }));
        backend.Enqueue(x => Envelope(x, "completed"));
        backend.Enqueue(x => Envelope(x, "correction-required", new { capability = "research", task = "Second correction", reason = "Second defect" }));

        var outcome = await CreateRuntime(temp.Path, backend, configuration: configuration).RunRequestAsync("Bound final review corrections", "test", default);
        var state = await LoadState(temp.Path);

        Assert.Equal("CORRECTIVE_BUDGET_EXHAUSTED", outcome.FactoryOutcome);
        Assert.Equal(1, state.CorrectiveCycleCount);
        Assert.Empty(state.Remaining);
        Assert.Equal(2, backend.Invocations.Count(x => x.Role == "task-decomposer"));
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
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = Array.Empty<object>() }));
        backend.Enqueue(x => Envelope(x, "global-replan-required", new { finding = "Integrated review invalidated the remaining strategy" }, "Global strategy must change"));
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = new[] { Work("X", "research") } }));
        backend.Enqueue(x => Envelope(x, "completed"));
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = Array.Empty<object>() }));
        backend.Enqueue(x => Envelope(x, "approved"));

        var outcome = await CreateRuntime(temp.Path, backend).RunRequestAsync("Review and replan", "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal(4, backend.Invocations.Count(x => x.Role == "task-decomposer"));
        Assert.Equal(2, backend.Invocations.Count(x => x.Role == "final-reviewer"));
        var replanInvocation = backend.Invocations.Where(x => x.Role == "task-decomposer").ElementAt(2);
        Assert.Contains("Global strategy must change", replanInvocation.Input);
        Assert.Contains("final-review", replanInvocation.Input);
    }
}
