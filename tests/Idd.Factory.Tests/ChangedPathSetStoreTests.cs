using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.State;

namespace Idd.Factory.Tests;

public sealed class ChangedPathSetStoreTests
{
    [Fact]
    public async Task StateKeepsOnlyBoundedSummaryWhileArtifactsKeepFullSet()
    {
        using var temp = new TestWorkspace();
        var store = new FileFactoryStateStore(temp.Path, new FactoryStateValidator());
        var state = StateStoreTests.State();
        state.Current = StateStoreTests.Planned("W000001");
        state.CurrentPhase = CurrentWorkPhase.Ready;
        var paths = Enumerable.Range(0, 5_000)
            .Select(index => $"src/File{index:D4}.cs")
            .ToArray();
        state.Current.ChangedPaths.AddRange(paths);
        state.FactoryRunChangedPaths.AddRange(paths);

        await store.CreateAsync(state, default);

        using var persisted = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(temp.Path, "state.json")));
        var currentChanges = persisted.RootElement.GetProperty("current").GetProperty("changes");
        Assert.Equal(5_000, currentChanges.GetProperty("count").GetInt32());
        Assert.Equal(50, currentChanges.GetProperty("preview").GetArrayLength());
        Assert.Equal("changes/W000001.json", currentChanges.GetProperty("reference").GetString());
        Assert.False(persisted.RootElement.GetProperty("current").TryGetProperty("changedPaths", out _));
        Assert.False(persisted.RootElement.TryGetProperty("factoryRunChangedPaths", out _));

        using var workItemArtifact = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(temp.Path, "changes", "W000001.json")));
        using var runArtifact = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(temp.Path, "changes", "run.json")));
        Assert.Equal(5_000, workItemArtifact.RootElement.GetProperty("changedPaths").GetArrayLength());
        Assert.Equal(5_000, runArtifact.RootElement.GetProperty("changedPaths").GetArrayLength());

        var loaded = await store.LoadAsync(default);
        Assert.NotNull(loaded);
        Assert.Equal(paths, loaded!.Current!.ChangedPaths);
        Assert.Equal(paths, loaded.FactoryRunChangedPaths);
    }

    [Fact]
    public async Task AggregateCanBeRebuiltFromImmutableAttemptArtifactAfterCrash()
    {
        using var temp = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(temp.Path, "attempts", "A000001"));
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "attempts", "A000001", "invocation.json"),
            JsonSerializer.Serialize(new AgentInvocation
            {
                RunId = "run",
                AttemptId = "A000001",
                Capability = "implementation",
                Role = "executor",
                WorkItemId = "W000001",
                Workspace = temp.Path,
                SemanticOutputPath = Path.Combine(temp.Path, "attempts", "A000001", "semantic-result.md"),
                SkillName = "idd-factory-execute-subtask",
                ExecutionProfile = AgentExecutionProfile.WorkspaceWrite,
                Input = "test",
                StartedAt = DateTimeOffset.UtcNow
            }, FactoryJson.Options));
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "attempts", "A000001", "workspace-changes.json"),
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                changedPaths = new[] { "src/A.cs", "tests/A.Tests.cs" }
            }, FactoryJson.Options));

        var state = StateStoreTests.State();
        state.Current = StateStoreTests.Planned("W000001");
        state.CurrentPhase = CurrentWorkPhase.Ready;
        state.RunChanges = new ChangeSetSummary
        {
            Reference = "changes/run.json",
            Count = 0,
            Preview = []
        };
        state.Current.Changes = new ChangeSetSummary
        {
            Reference = "changes/W000001.json",
            Count = 0,
            Preview = []
        };
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "state.json"),
            JsonSerializer.Serialize(state, FactoryJson.Options));

        var loaded = await new FileFactoryStateStore(temp.Path, new FactoryStateValidator()).LoadAsync(default);

        Assert.Equal(["src/A.cs", "tests/A.Tests.cs"], loaded!.Current!.ChangedPaths);
        Assert.Equal(["src/A.cs", "tests/A.Tests.cs"], loaded.FactoryRunChangedPaths);
        Assert.Equal(2, loaded.Current.Changes!.Count);
        Assert.Equal(2, loaded.RunChanges!.Count);
    }
}
