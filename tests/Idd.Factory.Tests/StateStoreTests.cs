using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.State;
using System.Text.Json.Nodes;

namespace Idd.Factory.Tests;

public sealed class StateStoreTests
{
    [Fact] public async Task CreateSaveAndStaleRevisionAreDeterministic()
    {
        using var temp = new TestWorkspace(); var store = new FileFactoryStateStore(temp.Path, new FactoryStateValidator()); var state = State();
        await store.CreateAsync(state, default); state.CurrentWorkflowStep = "execute"; await store.SaveAsync(state, 0, default);
        Assert.Equal(1, (await store.LoadAsync(default))!.Revision);
        Assert.Equal("STALE_STATE_REVISION", (await Assert.ThrowsAsync<FactoryStateException>(() => store.SaveAsync(state, 0, default))).Code);
        Assert.False(File.Exists(System.IO.Path.Combine(temp.Path, "state.json.tmp")));
    }

    [Fact] public async Task CompletedItemCannotBeMutated()
    {
        using var temp = new TestWorkspace(); var store = new FileFactoryStateStore(temp.Path, new FactoryStateValidator()); var state = State();
        state.WorkItems.Add(new WorkItemState { Id = "one", Sequence = 1, Kind = WorkItemKind.Subtask, Status = WorkItemStatus.Completed, ContractPath = "work-items/one.md", LastResultRef = "attempts/A/result.json" });
        await store.CreateAsync(state, default); state.WorkItems[0].LastResultRef = "attempts/changed/result.json";
        Assert.Equal("COMPLETED_ITEM_MUTATED", (await Assert.ThrowsAsync<FactoryStateException>(() => store.SaveAsync(state, 0, default))).Code);
    }

    [Fact] public async Task ExistingBlockerWithoutPayloadRemainsReadable()
    {
        using var temp = new TestWorkspace(); var state = State(); state.Blocker = new("NEEDS_CLARIFICATION", "Choose one.", "Continue with an answer.");
        var json = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(state, FactoryJson.Options))!.AsObject();
        json["blocker"]!.AsObject().Remove("payload");
        await File.WriteAllTextAsync(System.IO.Path.Combine(temp.Path, "state.json"), json.ToJsonString(FactoryJson.Options));

        var loaded = await new FileFactoryStateStore(temp.Path, new FactoryStateValidator()).LoadAsync(default);

        Assert.NotNull(loaded); Assert.Equal("Choose one.", loaded.Blocker!.Reason); Assert.Null(loaded.Blocker.Payload);
    }

    [Fact] public void UnknownDependencyAndInvalidTransitionAreRejected()
    {
        var validator = new FactoryStateValidator(); var state = State(); state.WorkItems.Add(new WorkItemState { Id = "one", Sequence = 1, Kind = WorkItemKind.Subtask, ContractPath = "one.md", Dependencies = ["missing"] });
        Assert.Equal("CORRUPT_FACTORY_STATE", Assert.Throws<FactoryStateException>(() => validator.Validate(state)).Code);
        state.WorkItems[0].Dependencies.Clear(); var next = Clone(state); next.WorkItems[0].Status = WorkItemStatus.Completed;
        Assert.Equal("INVALID_STATE_TRANSITION", Assert.Throws<FactoryStateException>(() => validator.ValidateMutation(state, next)).Code);
    }

    [Fact] public void PriorStateSchemaIsRejected()
    {
        var state = State() with { SchemaVersion = FactoryState.CurrentSchemaVersion - 1 };

        Assert.Equal("UNSUPPORTED_STATE_SCHEMA", Assert.Throws<FactoryStateException>(() => new FactoryStateValidator().Validate(state)).Code);
    }

    [Fact] public void StateSerializationDoesNotContainBaselineRevision()
    {
        using var document = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(State(), FactoryJson.Options));

        Assert.False(document.RootElement.TryGetProperty("baselineRevision", out _));
    }

    internal static FactoryState State() => new() { MethodologyVersion = "1", RuntimeVersion = "1", RunId = "run", Revision = 0, CurrentWorkflowStep = "decompose", WorkflowName = "test", WorkflowHash = "hash", RequestPath = "request.md" };
    private static FactoryState Clone(FactoryState state) => System.Text.Json.JsonSerializer.Deserialize<FactoryState>(System.Text.Json.JsonSerializer.Serialize(state, FactoryJson.Options), FactoryJson.Options)!;
}
