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

    private static void AssertArtifacts(string directory, params string[] files) =>
        Assert.All(files, file => Assert.True(File.Exists(Path.Combine(directory, file)), file));

    private sealed class SimulatedCrashException : Exception { }
}
