using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.State;

namespace Idd.Factory.Tests;

public sealed class StateStoreTests
{
    [Fact]
    public async Task RevisionIsCasAndGraphRevisionDoesNotChangeForLifecycleSave()
    {
        using var temp = new TestWorkspace();
        var store = new FileFactoryStateStore(temp.Path, new FactoryStateValidator());
        var state = State();
        state.WorkItems.Add(Executable("one", WorkItemStatus.Ready));
        state.GraphRevision = 1;
        await store.CreateAsync(state, default);

        state.WorkItems[0].Status = WorkItemStatus.Dispatching;
        await store.SaveAsync(state, 0, default);

        var loaded = (await store.LoadAsync(default))!;
        Assert.Equal(1, loaded.Revision);
        Assert.Equal(1, loaded.GraphRevision);
        Assert.Equal(WorkItemStatus.Dispatching, loaded.WorkItems[0].Status);
        Assert.Equal("STALE_STATE_REVISION", (await Assert.ThrowsAsync<FactoryStateException>(() => store.SaveAsync(state, 0, default))).Code);
    }

    [Fact]
    public async Task CompletedWorkIsImmutable()
    {
        using var temp = new TestWorkspace();
        var store = new FileFactoryStateStore(temp.Path, new FactoryStateValidator());
        var state = State();
        state.WorkItems.Add(Executable("one", WorkItemStatus.Completed) with
        {
            LastResultRef = "attempts/A000001/result.json",
            LastSemanticOutcome = "completed"
        });
        state.GraphRevision = 1;
        await store.CreateAsync(state, default);

        state.WorkItems[0].LastResultRef = "attempts/changed/result.json";

        Assert.Equal("COMPLETED_ITEM_MUTATED", (await Assert.ThrowsAsync<FactoryStateException>(() => store.SaveAsync(state, 0, default))).Code);
    }

    [Fact]
    public async Task ActivePriorSchemaReturnsLegacyFactoryState()
    {
        using var temp = new TestWorkspace();
        var json = JsonSerializer.Serialize(State() with { SchemaVersion = FactoryState.CurrentSchemaVersion - 1 }, FactoryJson.Options);
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "state.json"), json);

        var exception = await Assert.ThrowsAsync<FactoryStateException>(() => new FileFactoryStateStore(temp.Path, new FactoryStateValidator()).LoadAsync(default));

        Assert.Equal("LEGACY_FACTORY_STATE", exception.Code);
        Assert.Contains("cancel/restart", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SerializationContainsNoGlobalWorkflowGraph()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(State(), FactoryJson.Options));
        var root = document.RootElement;

        Assert.False(root.TryGetProperty("currentWorkflowStep", out _));
        Assert.False(root.TryGetProperty("workflowName", out _));
        Assert.False(root.TryGetProperty("workflowHash", out _));
    }

    [Fact]
    public void GraphDefinitionChangeRequiresGraphRevisionAndLifecycleChangeDoesNot()
    {
        var validator = new FactoryStateValidator();
        var previous = State();
        previous.WorkItems.Add(Executable("one", WorkItemStatus.Ready));
        previous.GraphRevision = 1;

        var lifecycle = Clone(previous);
        lifecycle.WorkItems[0].Status = WorkItemStatus.Dispatching;
        validator.ValidateMutation(previous, lifecycle);

        var definition = Clone(previous);
        definition.WorkItems[0].ContractRevision = 2;
        definition.WorkItems[0].ContractPath = "work-items/one/contracts/000002.md";
        Assert.Equal("INVALID_GRAPH_REVISION", Assert.Throws<FactoryStateException>(() => validator.ValidateMutation(previous, definition)).Code);

        definition.GraphRevision = 2;
        validator.ValidateMutation(previous, definition);
    }

    [Fact]
    public void CyclesAreRejected()
    {
        var state = State();
        state.GraphRevision = 1;
        state.WorkItems.Add(Executable("a", WorkItemStatus.Planned, ["b"]));
        state.WorkItems.Add(Executable("b", WorkItemStatus.Planned, ["a"], sequence: 2));

        Assert.Equal("CORRUPT_FACTORY_STATE", Assert.Throws<FactoryStateException>(() => new FactoryStateValidator().Validate(state)).Code);
    }

    internal static FactoryState State() => new()
    {
        MethodologyVersion = "test",
        RuntimeVersion = "test",
        RunId = "run",
        Revision = 0,
        GraphRevision = 0,
        FactoryConfigurationHash = "config-hash",
        RequestPath = "request.md"
    };

    internal static WorkItemState Executable(string id, WorkItemStatus status, IEnumerable<string>? dependencies = null, int sequence = 1) => new()
    {
        Id = id,
        Sequence = sequence,
        Kind = WorkItemKind.Subtask,
        Capability = "implementation",
        DefinitionState = WorkDefinitionState.Executable,
        Status = status,
        ContractPath = $"work-items/{id}/contracts/000001.md",
        ContractRevision = 1,
        Dependencies = dependencies?.ToList() ?? []
    };

    private static FactoryState Clone(FactoryState state) =>
        JsonSerializer.Deserialize<FactoryState>(JsonSerializer.Serialize(state, FactoryJson.Options), FactoryJson.Options)!;
}
