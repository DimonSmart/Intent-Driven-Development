using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.Runtime;
using Idd.Factory.State;

namespace Idd.Factory.Tests;

public sealed class StateStoreTests
{
    [Fact]
    public async Task RevisionIsCasAndPlanRevisionIsIndependent()
    {
        using var temp = new TestWorkspace();
        var store = new FileFactoryStateStore(temp.Path, new FactoryStateValidator());
        var state = State();
        state.Remaining.Add(Planned("W000001")); state.PlanningCycleCount = 1; state.PlanRevision = 1;
        await store.CreateAsync(state, default);
        state.Current = state.Remaining[0]; state.Remaining.Clear(); state.CurrentPhase = CurrentWorkPhase.Ready;
        await store.SaveAsync(state, 0, default);
        var loaded = (await store.LoadAsync(default))!;
        Assert.Equal(1, loaded.Revision); Assert.Equal(1, loaded.PlanRevision); Assert.Equal("W000001", loaded.Current!.Id);
        Assert.Equal("STALE_STATE_REVISION", (await Assert.ThrowsAsync<FactoryStateException>(() => store.SaveAsync(state, 0, default))).Code);
    }

    [Fact]
    public async Task CompletedWorkIsImmutable()
    {
        using var temp = new TestWorkspace();
        var store = new FileFactoryStateStore(temp.Path, new FactoryStateValidator());
        var state = State(); state.Completed.Add(Completed("W000001")); await store.CreateAsync(state, default);
        state.Completed[0] = state.Completed[0] with { ResultRef = "changed" };
        Assert.Equal("CORRUPT_FACTORY_STATE", (await Assert.ThrowsAsync<FactoryStateException>(() => store.SaveAsync(state, 0, default))).Code);
    }

    [Fact]
    public async Task ActivePriorSchemaReturnsLegacyFactoryState()
    {
        using var temp = new TestWorkspace();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "state.json"), JsonSerializer.Serialize(State() with { SchemaVersion = FactoryState.CurrentSchemaVersion - 1 }, FactoryJson.Options));
        var exception = await Assert.ThrowsAsync<FactoryStateException>(() => new FileFactoryStateStore(temp.Path, new FactoryStateValidator()).LoadAsync(default));
        Assert.Equal("LEGACY_FACTORY_STATE", exception.Code);
    }

    [Fact]
    public void StatePersistsSeparateSemanticAndTechnicalInvocationState()
    {
        var state = State();
        state.Current = Planned("W000001") with
        {
            SemanticAttemptCount = 2,
            TechnicalRestartCount = 1,
            AdditionalSemanticAttemptBudget = 3,
            NextInvocationKind = WorkItemInvocationKind.TechnicalRestart
        };
        state.CurrentPhase = CurrentWorkPhase.Ready;

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(state, FactoryJson.Options));
        var root = document.RootElement;
        var current = root.GetProperty("current");

        Assert.Equal(15, FactoryState.CurrentSchemaVersion);
        Assert.Equal(2, current.GetProperty("semanticAttemptCount").GetInt32());
        Assert.Equal(1, current.GetProperty("technicalRestartCount").GetInt32());
        Assert.Equal(3, current.GetProperty("additionalSemanticAttemptBudget").GetInt32());
        Assert.Equal("TechnicalRestart", current.GetProperty("nextInvocationKind").GetString());
        Assert.False(current.TryGetProperty("attemptCount", out _));
        Assert.False(current.TryGetProperty("additionalAttemptBudget", out _));
    }

    [Fact]
    public void PersistedNextInvocationKindWinsOverVerificationHistoryOnReload()
    {
        var state = State();
        state.Current = Planned("W000001") with
        {
            SemanticAttemptCount = 1,
            TechnicalRestartCount = 0,
            LastVerificationDecision = VerificationDecision.UnexpectedFailure,
            NextInvocationKind = WorkItemInvocationKind.TechnicalRestart
        };
        state.CurrentPhase = CurrentWorkPhase.Ready;

        var json = JsonSerializer.Serialize(state, FactoryJson.Options);
        var loaded = JsonSerializer.Deserialize<FactoryState>(json, FactoryJson.Options)!;

        Assert.Equal(VerificationDecision.UnexpectedFailure, loaded.Current!.LastVerificationDecision);
        Assert.Equal(WorkItemInvocationKind.TechnicalRestart, loaded.Current.NextInvocationKind);
    }

    [Fact]
    public void StateHasNoObsoleteSemanticControlOrReviewFields()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(State(), FactoryJson.Options));
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("completed", out _));
        Assert.True(root.TryGetProperty("current", out _));
        Assert.True(root.TryGetProperty("remaining", out _));
        foreach (var obsolete in new[] { "pendingReplanTrigger", "finalReview", "correctiveCycleCount", "postCompletionRoute", "lastSemanticOutcome" })
            Assert.False(root.TryGetProperty(obsolete, out _));
    }

    [Fact]
    public async Task PlanningFrontierSurvivesReloadAndRoutesToFinalVerification()
    {
        using var temp = new TestWorkspace();
        var store = new FileFactoryStateStore(temp.Path, new FactoryStateValidator());
        var state = State();
        state.PlanningCycleCount = 2;
        state.PlanRevision = 2;
        state.Completed.Add(Completed("W000001"));
        state.PlannedThroughCompletedCount = 1;
        await store.CreateAsync(state, default);
        var loaded = (await store.LoadAsync(default))!;
        Assert.Equal(
            FactoryRuntimeState.FinalVerifying,
            BatchProtocolTests.StateMachineForRoutingOnly().ResolveState(loaded));
    }

    internal static FactoryState State() => new() { MethodologyVersion = "test", RuntimeVersion = "test", RunId = "run", FactoryConfigurationHash = "config-hash", RequestPath = "request.md" };
    internal static PlannedWorkItem Planned(string id, string capability = "implementation") => new() { Id = id, ContractPath = $"work-items/{id}/contract.md" };
    internal static CompletedWorkItem Completed(string id, string capability = "implementation") => new() { Id = id, ContractPath = $"work-items/{id}/contract.md", ResultRef = $"attempts/{id}/semantic-result.md" };
}
