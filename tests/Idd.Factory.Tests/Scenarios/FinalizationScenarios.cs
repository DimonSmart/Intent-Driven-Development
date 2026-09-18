using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Finalization;
using Idd.Factory.Persistence;
using Idd.Factory.State;

namespace Idd.Factory.Tests;

public sealed class FinalizationScenarios
{
    [Fact]
    public async Task FinalizationMovesTheRunAndPreservesDurableAnalysisInputs()
    {
        using var temp = new TestWorkspace();
        var (state, current) = await PrepareAsync(temp);
        temp.Write(".idd/factory/current/run-context.md", "runtime context");
        temp.Write(".idd/factory/current/clarifications/Q00001.md", "answer");

        var result = await new FinalizeHandler(temp.Path).FinalizeAsync(state, default);

        Assert.Matches(@"^\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}Z_durable-finalization$", Path.GetFileName(result));
        Assert.False(Directory.Exists(current));
        AssertArtifacts(result, "state.json", "request.md", "run-context.md", "events.jsonl", "factory-result.json", "completed-work.json", "commit-message.md");
        Assert.True(Directory.Exists(Path.Combine(result, "attempts")));
        Assert.Equal("answer", File.ReadAllText(Path.Combine(result, "clarifications", "Q00001.md")));
    }

    [Fact]
    public async Task CrashBeforeDirectoryHandoffLeavesRunResumableAtPinnedDestination()
    {
        using var temp = new TestWorkspace();
        var (state, current) = await PrepareAsync(temp);
        var crashing = new FinalizeHandler(temp.Path, stage =>
        {
            if (stage == FinalizationStage.Prepared) throw new SimulatedCrashException();
        });

        await Assert.ThrowsAsync<SimulatedCrashException>(() => crashing.FinalizeAsync(state, default));

        var recovered = await new FileFactoryStateStore(current, new FactoryStateValidator()).LoadAsync(default);
        Assert.Equal(state.RunId, recovered!.RunId);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(current, "finalization.json")));
        var pinnedName = manifest.RootElement.GetProperty("resultDirectoryName").GetString();

        var result = await new FinalizeHandler(temp.Path).FinalizeAsync(recovered, default);

        Assert.Equal(pinnedName, Path.GetFileName(result));
        Assert.False(Directory.Exists(current));
        AssertArtifacts(result, "factory-result.json", "events.jsonl");
    }

    [Fact]
    public async Task CrashAfterDirectoryHandoffLeavesCompleteResultAndNoPartialCurrent()
    {
        using var temp = new TestWorkspace();
        var (state, current) = await PrepareAsync(temp);
        var crashing = new FinalizeHandler(temp.Path, stage =>
        {
            if (stage == FinalizationStage.Committed) throw new SimulatedCrashException();
        });

        await Assert.ThrowsAsync<SimulatedCrashException>(() => crashing.FinalizeAsync(state, default));

        Assert.False(Directory.Exists(current));
        var result = Assert.Single(Directory.GetDirectories(Path.Combine(temp.Path, ".idd", "factory", "results")));
        AssertArtifacts(result, "state.json", "factory-result.json", "events.jsonl");
        Assert.True(Directory.Exists(Path.Combine(result, "attempts")));
    }

    [Fact]
    public async Task FinalizationRetriesTransientWindowsDirectoryLock()
    {
        using var temp = new TestWorkspace();
        var (state, current) = await PrepareAsync(temp);
        using var blockingRead = new FileStream(Path.Combine(current, "events.jsonl"), FileMode.Open, FileAccess.Read, FileShare.Read);
        Task release = Task.CompletedTask;
        var handler = new FinalizeHandler(temp.Path, stage =>
        {
            if (stage != FinalizationStage.Prepared) return;
            release = Task.Run(async () =>
            {
                await Task.Delay(100);
                blockingRead.Dispose();
            });
        });

        var result = await handler.FinalizeAsync(state, default);
        if (OperatingSystem.IsWindows()) Assert.True(release.IsCompleted);
        await release;

        Assert.False(Directory.Exists(current));
        Assert.True(File.Exists(Path.Combine(result, "factory-result.json")));
    }


    [Fact]
    public async Task FinalizationKeepsCompletedWorkBoundedForFiveThousandPaths()
    {
        using var temp = new TestWorkspace();
        var paths = Enumerable.Range(0, 5_000)
            .Select(index => $"src/File{index:D4}.cs")
            .ToList();
        var (state, current) = await PrepareCompletedAsync(temp, paths);

        var result = await new FinalizeHandler(temp.Path).FinalizeAsync(state, default);

        Assert.False(Directory.Exists(current));
        using var completed = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(result, "completed-work.json")));
        var work = Assert.Single(completed.RootElement.GetProperty("completed").EnumerateArray());
        Assert.False(work.TryGetProperty("changedPaths", out _));
        var changes = work.GetProperty("changes");
        Assert.Equal("changes/W000001.json", changes.GetProperty("reference").GetString());
        Assert.Equal(5_000, changes.GetProperty("count").GetInt32());
        Assert.Equal(50, changes.GetProperty("preview").GetArrayLength());
        Assert.True(File.Exists(Path.Combine(result, "changes", "W000001.json")));
        Assert.True(File.Exists(Path.Combine(result, "changes", "run.json")));
    }

    [Fact]
    public async Task MissingAuthoritativeArtifactBlocksFinalizationBeforeMove()
    {
        using var temp = new TestWorkspace();
        var (state, current) = await PrepareCompletedAsync(temp, ["src/A.cs"]);
        File.Delete(Path.Combine(current, "changes", "W000001.json"));

        var error = await Assert.ThrowsAsync<FactoryStateException>(() =>
            new FinalizeHandler(temp.Path).FinalizeAsync(state, default));

        Assert.Equal("CORRUPT_FACTORY_STATE", error.Code);
        Assert.True(Directory.Exists(current));
        AssertNoCommittedResults(temp.Path);
    }

    [Fact]
    public async Task CorruptAuthoritativeArtifactBlocksFinalizationBeforeMove()
    {
        using var temp = new TestWorkspace();
        var (state, current) = await PrepareCompletedAsync(temp, ["src/A.cs"]);
        await File.WriteAllTextAsync(
            Path.Combine(current, "changes", "W000001.json"),
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                changedPaths = new[] { "src/Other.cs" }
            }, FactoryJson.Options));

        var error = await Assert.ThrowsAsync<FactoryStateException>(() =>
            new FinalizeHandler(temp.Path).FinalizeAsync(state, default));

        Assert.Equal("CORRUPT_FACTORY_STATE", error.Code);
        Assert.True(Directory.Exists(current));
        AssertNoCommittedResults(temp.Path);
    }

    private static async Task<(FactoryState State, string Current)> PrepareAsync(TestWorkspace temp)
    {
        var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        Directory.CreateDirectory(Path.Combine(current, "attempts"));
        Directory.CreateDirectory(Path.Combine(current, "plan-revisions"));
        File.WriteAllText(Path.Combine(current, "request.md"), "# Durable finalization\n");
        File.WriteAllText(Path.Combine(current, "events.jsonl"), "{\"event\":\"scheduler-decision\"}\n");
        var state = new FactoryState
        {
            MethodologyVersion = "test-methodology",
            RuntimeVersion = "test-runtime",
            RunId = "durable-finalization-run",
            FactoryConfigurationHash = "test-config",
            RequestPath = "request.md",
            PlanRevision = 3,
            PlanningCycleCount = 2,
            FinalVerificationPassed = true,
            FinalVerificationPlanRevision = 3
        };
        await new FileFactoryStateStore(current, new FactoryStateValidator()).CreateAsync(state, default);
        return (state, current);
    }


    private static async Task<(FactoryState State, string Current)> PrepareCompletedAsync(
        TestWorkspace temp,
        IReadOnlyList<string> changedPaths)
    {
        var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        Directory.CreateDirectory(Path.Combine(current, "attempts"));
        Directory.CreateDirectory(Path.Combine(current, "plan-revisions"));
        File.WriteAllText(Path.Combine(current, "request.md"), "# Bounded finalization\n");
        File.WriteAllText(Path.Combine(current, "events.jsonl"), "{\"event\":\"scheduler-decision\"}\n");

        var completed = StateStoreTests.Completed("W000001") with
        {
            VerificationDecision = VerificationDecision.Ok
        };
        completed.ChangedPaths.AddRange(changedPaths);

        var state = new FactoryState
        {
            MethodologyVersion = "test-methodology",
            RuntimeVersion = "test-runtime",
            RunId = "bounded-finalization-run",
            FactoryConfigurationHash = "test-config",
            RequestPath = "request.md",
            PlanRevision = 3,
            PlanningCycleCount = 2,
            FinalVerificationPassed = true,
            FinalVerificationPlanRevision = 3
        };
        state.Completed.Add(completed);
        state.FactoryRunChangedPaths.AddRange(changedPaths);

        await new FileFactoryStateStore(current, new FactoryStateValidator()).CreateAsync(state, default);
        return (state, current);
    }

    private static void AssertNoCommittedResults(string workspace)
    {
        var results = Path.Combine(workspace, ".idd", "factory", "results");
        Assert.True(!Directory.Exists(results) || Directory.GetDirectories(results).Length == 0);
    }

    private static void AssertArtifacts(string directory, params string[] files) =>
        Assert.All(files, file => Assert.True(File.Exists(Path.Combine(directory, file)), file));

    private sealed class SimulatedCrashException : Exception { }
}
