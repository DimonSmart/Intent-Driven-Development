using Idd.Factory.Agents;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.Runtime;
using Idd.Factory.State;
using Idd.Factory.Telemetry;
using Idd.Factory.Verification;
using static Idd.Factory.Tests.FactoryRuntimeTestHarness;

namespace Idd.Factory.Tests;

public sealed class FinalReviewFastPathVerificationTests
{
    [Fact]
    public async Task FailedCorrectionVerificationDoesNotAdvancePlanningFrontier()
    {
        using var temp = new TestWorkspace();
        using var cancellation = new CancellationTokenSource();
        var backend = new FakeAgentBackend();
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = Array.Empty<object>() }));
        backend.Enqueue(x => Envelope(x, "correction-required", new
        {
            capability = "implementation",
            task = "Implement bounded correction",
            reason = "Integrated defect"
        }));
        backend.Enqueue(x => Envelope(x, "completed"));

        var currentDirectory = Path.Combine(temp.Path, ".idd", "factory", "current");
        var fileStore = new FileFactoryStateStore(currentDirectory, new FactoryStateValidator());
        var stateStore = new CancelAfterFailedVerificationStore(fileStore, cancellation);
        var clock = new FakeClock();
        var runtime = new FactoryRuntime(
            temp.Path,
            CreateConfiguration(),
            stateStore,
            new FactoryAgentExecutor(backend, new FactoryAgentResultValidator()),
            new SequencedVerificationEngine(temp.Path,
                (VerificationStatus.Passed, "initial-final", 0, "passed"),
                (VerificationStatus.Failed, "correction-subtask", 1, "failed")),
            new FactoryEventWriter(currentDirectory, clock),
            clock);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runtime.RunRequestAsync("Fail correction verification", "test", cancellation.Token));
        var state = (await fileStore.LoadAsync(default))!;

        Assert.Empty(state.Completed);
        Assert.Equal(0, state.PlannedThroughCompletedCount);
        Assert.NotNull(state.Current);
        Assert.Equal(PostCompletionRoute.FinalPipeline, state.Current!.PostCompletionRoute);
        Assert.Equal(CurrentWorkPhase.Ready, state.CurrentPhase);
        Assert.Equal(VerificationDecision.UnexpectedFailure, state.Current.LastVerificationDecision);
        Assert.Single(backend.Invocations, x => x.Role == "task-decomposer");
        Assert.Single(backend.Invocations, x => x.Role == "final-reviewer");
        Assert.Single(backend.Invocations, x => x.Role == "implementer");
    }
}
