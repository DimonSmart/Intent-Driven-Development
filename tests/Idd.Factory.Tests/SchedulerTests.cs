using Idd.Factory.Domain;
using Idd.Factory.Runtime;

namespace Idd.Factory.Tests;

public sealed class SchedulerTests
{
    [Fact]
    public void FirstRemainingItemIsAlwaysSelectedNext()
    {
        var state = StateStoreTests.State(); state.InitialPlanningCompleted = true;
        state.Remaining.Add(StateStoreTests.Planned("W000002")); state.Remaining.Add(StateStoreTests.Planned("W000001"));
        Assert.Equal(FactoryCommandKind.SelectNextWork, new FactoryScheduler().Decide(state).Kind);
    }

    [Fact]
    public void CurrentPrecedesRemainingAndUsesItsPhase()
    {
        var state = StateStoreTests.State(); state.InitialPlanningCompleted = true;
        state.Current = StateStoreTests.Planned("W000001"); state.CurrentPhase = CurrentWorkPhase.AwaitingVerification; state.Remaining.Add(StateStoreTests.Planned("W000002"));
        Assert.Equal(new FactoryCommand(FactoryCommandKind.RunVerification, "W000001", "subtask"), new FactoryScheduler().Decide(state));
    }

    [Fact]
    public void ReadyCurrentIsDispatchedWithoutVerificationSpecificCommand()
    {
        var state = StateStoreTests.State(); state.InitialPlanningCompleted = true;
        state.Current = StateStoreTests.Planned("W000001"); state.CurrentPhase = CurrentWorkPhase.Ready;

        Assert.Equal(new FactoryCommand(FactoryCommandKind.DispatchWork, "W000001"), new FactoryScheduler().Decide(state));
    }

    [Fact]
    public void NewCompletedKnowledgeRequestsPlanningBeforeFinalVerification()
    {
        var state = StateStoreTests.State(); state.InitialPlanningCompleted = true; state.PlanRevision = 2;
        state.Completed.Add(StateStoreTests.Completed("W000001", "research"));

        Assert.Equal(FactoryCommandKind.Plan, new FactoryScheduler().Decide(state).Kind);

        state.PlannedThroughCompletedCount = 1;
        Assert.Equal(FactoryCommandKind.RunFinalVerification, new FactoryScheduler().Decide(state).Kind);
    }

    [Fact]
    public void FinalVerificationThenReviewThenFinalize()
    {
        var state = StateStoreTests.State(); state.InitialPlanningCompleted = true; state.PlanRevision = 3;
        var scheduler = new FactoryScheduler();
        Assert.Equal(FactoryCommandKind.RunFinalVerification, scheduler.Decide(state).Kind);
        state.FinalVerificationPassed = true; state.FinalVerificationPlanRevision = 3;
        Assert.Equal(FactoryCommandKind.RunFinalReview, scheduler.Decide(state).Kind);
        state.FinalReview = new("approved", "attempts/A/result.json", 1, 3);
        Assert.Equal(FactoryCommandKind.Finalize, scheduler.Decide(state).Kind);
    }
}
