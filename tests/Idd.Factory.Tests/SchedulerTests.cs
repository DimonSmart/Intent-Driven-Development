using Idd.Factory.Domain;
using Idd.Factory.Runtime;

namespace Idd.Factory.Tests;

public sealed class SchedulerTests
{
    [Fact]
    public void PartialGraphDispatchesExecutableBeforeDependentOutlineRefinement()
    {
        var state = StateStoreTests.State();
        state.GraphRevision = 1;
        state.WorkItems.Add(StateStoreTests.Executable("A", WorkItemStatus.Ready));
        state.WorkItems.Add(new WorkItemState
        {
            Id = "B",
            Sequence = 2,
            Kind = WorkItemKind.Subtask,
            DefinitionState = WorkDefinitionState.Outline,
            Status = WorkItemStatus.Planned,
            ContractPath = "work-items/B/contracts/000001.md",
            Dependencies = ["A"]
        });
        var scheduler = new FactoryScheduler();

        Assert.Equal(new FactoryCommand(FactoryCommandKind.DispatchWork, "A"), scheduler.Decide(state));

        state.WorkItems[0].Status = WorkItemStatus.Completed;
        Assert.Equal(new FactoryCommand(FactoryCommandKind.RefineWork, "B"), scheduler.Decide(state));
    }

    [Fact]
    public void SameAuthoritativeStateAlwaysProducesSameCommand()
    {
        var state = StateStoreTests.State();
        state.GraphRevision = 1;
        state.WorkItems.Add(StateStoreTests.Executable("B", WorkItemStatus.Ready, sequence: 2));
        state.WorkItems.Add(StateStoreTests.Executable("A", WorkItemStatus.Ready, sequence: 1));
        var scheduler = new FactoryScheduler();

        var first = scheduler.Decide(state);
        var second = scheduler.Decide(state);

        Assert.Equal(first, second);
        Assert.Equal(new FactoryCommand(FactoryCommandKind.DispatchWork, "A"), first);
    }

    [Fact]
    public void StrictFinalVerificationPrecedesFinalReviewMaterialization()
    {
        var state = StateStoreTests.State();
        state.GraphRevision = 3;
        state.WorkItems.Add(StateStoreTests.Executable("A", WorkItemStatus.Completed));
        var scheduler = new FactoryScheduler();

        Assert.Equal(FactoryCommandKind.RunFinalVerification, scheduler.Decide(state).Kind);

        state.FinalVerificationPassed = true;
        state.FinalVerificationGraphRevision = 3;
        Assert.Equal(FactoryCommandKind.CreateFinalReview, scheduler.Decide(state).Kind);
    }

    [Fact]
    public void OutlineDoesNotDispatchUntilItsDependenciesComplete()
    {
        var state = StateStoreTests.State();
        state.GraphRevision = 1;
        state.WorkItems.Add(StateStoreTests.Executable("A", WorkItemStatus.Running));
        state.WorkItems.Add(new WorkItemState
        {
            Id = "B",
            Sequence = 2,
            Kind = WorkItemKind.Subtask,
            DefinitionState = WorkDefinitionState.Outline,
            Status = WorkItemStatus.Planned,
            ContractPath = "work-items/B/contracts/000001.md",
            Dependencies = ["A"]
        });

        Assert.Equal(FactoryCommandKind.StopBlocked, new FactoryScheduler().Decide(state).Kind);
    }
}
