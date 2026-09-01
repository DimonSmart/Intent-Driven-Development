using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.State;
using static Idd.Factory.Tests.FactoryRuntimeTestHarness;

namespace Idd.Factory.Tests;

public sealed class PlanMutationProvenanceTests
{
    [Fact]
    public async Task ReplanRevisionUsesPersistedTriggerWorkItemProvenance()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        var configuration = CreateConfiguration();

        backend.Enqueue(x => Envelope(x, "ready", new { tasks = Array.Empty<object>() }));
        backend.Enqueue(x => Envelope(x, "approved"));

        var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        temp.Write(".idd/factory/current/request.md", "Replan");
        temp.Write(".idd/factory/current/work-items/W000002/contract.md", "# Current");

        var state = new FactoryState
        {
            MethodologyVersion = "test",
            RuntimeVersion = "test",
            RunId = "run",
            FactoryConfigurationHash = configuration.Hash,
            RequestPath = "request.md",
            InitialPlanningCompleted = true,
            PlanRevision = 5,
            NextWorkItemNumber = 3,
            Current = StateStoreTests.Planned("W000002", "research"),
            CurrentPhase = CurrentWorkPhase.Ready,
            PendingReplanTrigger = new(
                "research",
                "W000001",
                "attempts/A000010/result.json",
                "Source work requested a global replan.",
                null,
                [])
        };
        await new FileFactoryStateStore(current, new FactoryStateValidator()).CreateAsync(state, default);

        var outcome = await CreateRuntime(temp.Path, backend, configuration: configuration).ContinueAsync(default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        var revisions = Directory.GetFiles(Path.Combine(outcome.ResultDirectory!, "plan-revisions"), "*.json")
            .Select(path => JsonSerializer.Deserialize<PlanRevisionArtifact>(
                File.ReadAllText(path),
                FactoryJson.Options)!)
            .ToArray();
        var replan = Assert.Single(revisions.Where(x => x.Reason == "semantic-replan"));

        Assert.Equal("W000001", replan.SourceWorkItemId);
        Assert.Equal(
            backend.Invocations.Single(x => x.Role == "task-decomposer").AttemptId,
            replan.SourceAttemptId);
    }
}
