using Idd.Factory.Domain;
using Idd.Factory.State;

namespace Idd.Factory.Tests;

public sealed class StateInvariantTests
{
    private readonly FactoryStateValidator validator = new();

    [Theory]
    [InlineData("attempt-sequence")]
    [InlineData("replan-count")]
    [InlineData("corrective-cycle-count")]
    public void RuntimeCountersCannotBeNegative(string counter)
    {
        var state = StateStoreTests.State();
        switch (counter)
        {
            case "attempt-sequence": state.AttemptSequence = -1; break;
            case "replan-count": state.ReplanCount = -1; break;
            case "corrective-cycle-count": state.CorrectiveCycleCount = -1; break;
            default: throw new ArgumentOutOfRangeException(nameof(counter));
        }

        AssertCorrupt(state);
    }

    [Fact]
    public void CurrentWorkAttemptCountCannotBeNegative()
    {
        var state = StateStoreTests.State();
        state.Current = StateStoreTests.Planned("W000001") with { AttemptCount = -1 };
        state.CurrentPhase = CurrentWorkPhase.Ready;

        AssertCorrupt(state);
    }

    [Fact]
    public void RemainingWorkAttemptCountCannotBeNegative()
    {
        var state = StateStoreTests.State();
        state.Remaining.Add(StateStoreTests.Planned("W000001") with { AttemptCount = -1 });

        AssertCorrupt(state);
    }

    [Fact]
    public void FinalReviewAttemptCountCannotBeNegative()
    {
        var state = StateStoreTests.State();
        state.FinalReview = new FinalReviewState("approved", "attempts/A000001/result.json", -1, null);

        AssertCorrupt(state);
    }

    [Fact]
    public void FinalEvidenceRevisionCannotBeNegative()
    {
        var state = StateStoreTests.State();
        state.FinalReview = new FinalReviewState("approved", "attempts/A000001/result.json", 1, -1);

        AssertCorrupt(state);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, 0L)]
    public void FinalVerificationStatusAndRevisionMustMoveTogether(bool passed, long? planRevision)
    {
        var state = StateStoreTests.State();
        state.FinalVerificationPassed = passed;
        state.FinalVerificationPlanRevision = planRevision;

        AssertCorrupt(state);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void VerificationProgressMustStayWithinSelectedChecks(int nextCheckIndex)
    {
        var state = StateStoreTests.State();
        state.PendingVerificationSession = Session(nextCheckIndex);

        AssertCorrupt(state);
    }

    [Theory]
    [InlineData(null, "hash")]
    [InlineData("build", null)]
    public void VerificationActionStageRequiresCompletePendingCheckMetadata(string? pendingCheckId, string? pendingCheckDefinitionHash)
    {
        var state = StateStoreTests.State();
        state.PendingVerificationSession = Session(
            0,
            VerificationContinuationStage.AwaitingConfirmation,
            pendingCheckId,
            pendingCheckDefinitionHash);

        AssertCorrupt(state);
    }

    [Theory]
    [InlineData("build", "hash")]
    [InlineData(null, "hash")]
    [InlineData("build", null)]
    public void VerificationExecuteStageCannotRetainPendingCheckMetadata(string? pendingCheckId, string? pendingCheckDefinitionHash)
    {
        var state = StateStoreTests.State();
        state.PendingVerificationSession = Session(
            0,
            VerificationContinuationStage.ExecuteCheck,
            pendingCheckId,
            pendingCheckDefinitionHash);

        AssertCorrupt(state);
    }

    [Fact]
    public void ValidVerificationSessionAndCountersAreAccepted()
    {
        var state = StateStoreTests.State();
        state.AttemptSequence = 3;
        state.ReplanCount = 1;
        state.CorrectiveCycleCount = 2;
        state.PendingVerificationSession = Session(0);

        validator.Validate(state);
    }

    private void AssertCorrupt(FactoryState state)
    {
        var exception = Assert.Throws<FactoryStateException>(() => validator.Validate(state));
        Assert.Equal("CORRUPT_FACTORY_STATE", exception.Code);
    }

    private static PendingVerificationSession Session(
        int nextCheckIndex,
        VerificationContinuationStage stage = VerificationContinuationStage.ExecuteCheck,
        string? pendingCheckId = null,
        string? pendingCheckDefinitionHash = null) =>
        new(
            "final",
            null,
            ["build"],
            [],
            nextCheckIndex,
            [],
            [],
            [],
            pendingCheckId,
            pendingCheckDefinitionHash,
            "policy-hash",
            stage);
}
