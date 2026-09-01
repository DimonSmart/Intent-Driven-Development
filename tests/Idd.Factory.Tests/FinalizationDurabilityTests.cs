using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Finalization;
using Idd.Factory.Persistence;
using Idd.Factory.State;

namespace Idd.Factory.Tests;

public sealed class FinalizationDurabilityTests
{
    [Fact]
    public async Task FinalizationMovesWholeRunAndPreservesTokenAnalysisInputs()
    {
        using var temp = new TestWorkspace();
        var (state, current) = await PrepareFinalizableRunAsync(temp);
        temp.Write(".idd/factory/current/run-context.md", "runtime context");
        temp.Write(".idd/factory/current/clarifications/Q00001.md", "answer");

        var result = await new FinalizeHandler(temp.Path).FinalizeAsync(state, default);

        Assert.False(Directory.Exists(current));
        Assert.True(File.Exists(Path.Combine(result, "state.json")));
        Assert.True(File.Exists(Path.Combine(result, "request.md")));
        Assert.True(File.Exists(Path.Combine(result, "run-context.md")));
        Assert.True(File.Exists(Path.Combine(result, "events.jsonl")));
        Assert.True(Directory.Exists(Path.Combine(result, "attempts")));
        Assert.True(File.Exists(Path.Combine(result, "factory-result.json")));
        Assert.True(File.Exists(Path.Combine(result, "completed-work.json")));
        Assert.True(File.Exists(Path.Combine(result, "commit-message.md")));
        Assert.Equal("answer", File.ReadAllText(Path.Combine(result, "clarifications", "Q00001.md")));

        // factory-token-analysis requires exactly these two top-level inputs before it inspects attempts.
        Assert.True(Directory.Exists(Path.Combine(result, "attempts")));
        Assert.True(File.Exists(Path.Combine(result, "events.jsonl")));
    }

    [Fact]
    public async Task CrashBeforeDirectoryHandoffLeavesRunResumableAndRetryUsesPinnedDestination()
    {
        using var temp = new TestWorkspace();
        var (state, current) = await PrepareFinalizableRunAsync(temp);
        var handler = new FinalizeHandler(temp.Path, stage =>
        {
            if (stage == FinalizationStage.Prepared) throw new SimulatedCrashException();
        });

        await Assert.ThrowsAsync<SimulatedCrashException>(() => handler.FinalizeAsync(state, default));

        Assert.True(Directory.Exists(current));
        var store = new FileFactoryStateStore(current, new FactoryStateValidator());
        var recovered = await store.LoadAsync(default);
        Assert.Equal(state.RunId, recovered!.RunId);
        Assert.True(File.Exists(Path.Combine(current, "events.jsonl")));
        Assert.Empty(Directory.GetDirectories(Path.Combine(temp.Path, ".idd", "factory", "results")));

        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(current, "finalization.json")));
        var pinnedName = manifest.RootElement.GetProperty("resultDirectoryName").GetString();
        var result = await new FinalizeHandler(temp.Path).FinalizeAsync(recovered, default);

        Assert.Equal(pinnedName, Path.GetFileName(result));
        Assert.False(Directory.Exists(current));
        Assert.True(File.Exists(Path.Combine(result, "factory-result.json")));
        Assert.True(File.Exists(Path.Combine(result, "events.jsonl")));
    }

    [Fact]
    public async Task CrashAfterDirectoryHandoffLeavesCompleteResultAndNoPartialCurrent()
    {
        using var temp = new TestWorkspace();
        var (state, current) = await PrepareFinalizableRunAsync(temp);
        var handler = new FinalizeHandler(temp.Path, stage =>
        {
            if (stage == FinalizationStage.Committed) throw new SimulatedCrashException();
        });

        await Assert.ThrowsAsync<SimulatedCrashException>(() => handler.FinalizeAsync(state, default));

        Assert.False(Directory.Exists(current));
        var results = Directory.GetDirectories(Path.Combine(temp.Path, ".idd", "factory", "results"));
        var result = Assert.Single(results);
        Assert.True(File.Exists(Path.Combine(result, "state.json")));
        Assert.True(File.Exists(Path.Combine(result, "factory-result.json")));
        Assert.True(File.Exists(Path.Combine(result, "events.jsonl")));
        Assert.True(Directory.Exists(Path.Combine(result, "attempts")));
    }

    private static async Task<(FactoryState State, string Current)> PrepareFinalizableRunAsync(TestWorkspace temp)
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
            InitialPlanningCompleted = true,
            FinalVerificationPassed = true,
            FinalVerificationPlanRevision = 3,
            FinalReview = new FinalReviewState("approved", "attempts/A000001/result.json", 1, 3)
        };
        await new FileFactoryStateStore(current, new FactoryStateValidator()).CreateAsync(state, default);
        return (state, current);
    }

    private sealed class SimulatedCrashException : Exception;
}
