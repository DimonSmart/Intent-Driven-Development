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

    [Fact]
    public async Task PendingVerificationReloadHydratesPathOutsidePreview()
    {
        using var temp = new TestWorkspace();
        var store = new FileFactoryStateStore(temp.Path, new FactoryStateValidator());
        var paths = Enumerable.Range(0, 5_000)
            .Select(index => $"src/File{index:D4}.cs")
            .ToList();
        var state = StateStoreTests.State();
        state.Current = StateStoreTests.Planned("W000001");
        state.CurrentPhase = CurrentWorkPhase.AwaitingVerification;
        state.Current.ChangedPaths.AddRange(paths);
        state.FactoryRunChangedPaths.AddRange(paths);
        state.PendingVerificationSession = new PendingVerificationSession(
            "subtask",
            "W000001",
            ["late-check"],
            paths.ToList(),
            0,
            [],
            [],
            [],
            null,
            null,
            "policy-hash",
            VerificationContinuationStage.ExecuteCheck);

        await store.CreateAsync(state, default);

        using (var persisted = JsonDocument.Parse(
                   await File.ReadAllTextAsync(Path.Combine(temp.Path, "state.json"))))
        {
            var session = persisted.RootElement.GetProperty("pendingVerificationSession");
            Assert.False(session.TryGetProperty("changedPaths", out _));
            Assert.Equal(5_000, session.GetProperty("changes").GetProperty("count").GetInt32());
            Assert.Equal(50, session.GetProperty("changes").GetProperty("preview").GetArrayLength());
        }

        var loaded = await store.LoadAsync(default);

        Assert.NotNull(loaded!.PendingVerificationSession);
        Assert.Equal(5_000, loaded.PendingVerificationSession!.ChangedPaths.Count);
        Assert.Contains("src/File4000.cs", loaded.PendingVerificationSession.ChangedPaths);
    }

    [Fact]
    public async Task MissingPendingVerificationArtifactDoesNotFallBackToPreview()
    {
        using var temp = new TestWorkspace();
        var store = new FileFactoryStateStore(temp.Path, new FactoryStateValidator());
        var paths = Enumerable.Range(0, 100)
            .Select(index => $"src/File{index:D4}.cs")
            .ToList();
        var state = StateStoreTests.State();
        state.Current = StateStoreTests.Planned("W000001");
        state.CurrentPhase = CurrentWorkPhase.AwaitingVerification;
        state.Current.ChangedPaths.AddRange(paths);
        state.FactoryRunChangedPaths.AddRange(paths);
        state.PendingVerificationSession = new PendingVerificationSession(
            "subtask",
            "W000001",
            ["check"],
            paths.ToList(),
            0,
            [],
            [],
            [],
            null,
            null,
            "policy-hash",
            VerificationContinuationStage.ExecuteCheck);

        await store.CreateAsync(state, default);
        File.Delete(Path.Combine(temp.Path, "changes", "W000001.json"));

        var error = await Assert.ThrowsAsync<FactoryStateException>(() => store.LoadAsync(default));

        Assert.Equal("CORRUPT_FACTORY_STATE", error.Code);
    }

    [Fact]
    public async Task MultiAttemptAggregateRebuildIsDistinctSortedAndIdempotent()
    {
        using var temp = new TestWorkspace();
        await WriteAttemptAsync(temp.Path, "A000002", ["src/B.cs", "src/C.cs"]);
        await WriteAttemptAsync(temp.Path, "A000001", ["src/A.cs", "src/B.cs"]);

        var state = StateStoreTests.State();
        state.Current = StateStoreTests.Planned("W000001");
        state.CurrentPhase = CurrentWorkPhase.Ready;
        state.RunChanges = EmptySummary("changes/run.json");
        state.Current.Changes = EmptySummary("changes/W000001.json");
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "state.json"),
            JsonSerializer.Serialize(state, FactoryJson.Options));

        var store = new FileFactoryStateStore(temp.Path, new FactoryStateValidator());
        var first = await store.LoadAsync(default);
        var workItemArtifact = Path.Combine(temp.Path, "changes", "W000001.json");
        var firstArtifact = await File.ReadAllTextAsync(workItemArtifact);

        var second = await store.LoadAsync(default);
        var secondArtifact = await File.ReadAllTextAsync(workItemArtifact);

        Assert.Equal(["src/A.cs", "src/B.cs", "src/C.cs"], first!.Current!.ChangedPaths);
        Assert.Equal(["src/A.cs", "src/B.cs", "src/C.cs"], first.FactoryRunChangedPaths);
        Assert.Equal(first.Current.ChangedPaths, second!.Current!.ChangedPaths);
        Assert.Equal(firstArtifact, secondArtifact);
    }

    [Fact]
    public async Task CorruptImmutableAttemptArtifactBlocksAggregateRebuild()
    {
        using var temp = new TestWorkspace();
        await WriteAttemptAsync(temp.Path, "A000001", ["src/A.cs"]);
        await WriteAttemptAsync(temp.Path, "A000002", ["src/B.cs"]);
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "attempts", "A000002", "workspace-changes.json"),
            "{ broken");

        var state = StateStoreTests.State();
        state.Current = StateStoreTests.Planned("W000001");
        state.CurrentPhase = CurrentWorkPhase.Ready;
        state.RunChanges = EmptySummary("changes/run.json");
        state.Current.Changes = EmptySummary("changes/W000001.json");
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "state.json"),
            JsonSerializer.Serialize(state, FactoryJson.Options));

        var error = await Assert.ThrowsAsync<FactoryStateException>(() =>
            new FileFactoryStateStore(temp.Path, new FactoryStateValidator()).LoadAsync(default));

        Assert.Equal("CORRUPT_FACTORY_STATE", error.Code);
    }

    private static ChangeSetSummary EmptySummary(string reference) => new()
    {
        Reference = reference,
        Count = 0,
        Preview = []
    };

    private static async Task WriteAttemptAsync(
        string runDirectory,
        string attemptId,
        IReadOnlyList<string> changedPaths)
    {
        var directory = Path.Combine(runDirectory, "attempts", attemptId);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "invocation.json"),
            JsonSerializer.Serialize(new AgentInvocation
            {
                RunId = "run",
                AttemptId = attemptId,
                Capability = "implementation",
                Role = "executor",
                WorkItemId = "W000001",
                Workspace = runDirectory,
                SemanticOutputPath = Path.Combine(directory, "semantic-result.md"),
                SkillName = "idd-factory-execute-subtask",
                ExecutionProfile = AgentExecutionProfile.WorkspaceWrite,
                Input = "test",
                StartedAt = DateTimeOffset.UtcNow
            }, FactoryJson.Options));
        await File.WriteAllTextAsync(
            Path.Combine(directory, "workspace-changes.json"),
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                changedPaths = changedPaths.OrderBy(path => path, StringComparer.Ordinal).ToArray()
            }, FactoryJson.Options));
    }

}
