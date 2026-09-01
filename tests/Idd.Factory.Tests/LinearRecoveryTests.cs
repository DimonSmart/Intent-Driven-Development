using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.State;
using static Idd.Factory.Tests.FactoryRuntimeTestHarness;

namespace Idd.Factory.Tests;

public sealed class LinearRecoveryTests
{
    [Fact]
    public async Task SelectedCurrentBeforeWorkerStartContinuesExactTask()
    {
        using var temp = new TestWorkspace();
        var state = PrepareCurrentState(temp, CurrentWorkPhase.Ready);
        await Store(temp).CreateAsync(state, default);
        var backend = new FakeAgentBackend();
        backend.Enqueue(x => Envelope(x, "completed", new { finding = "done" }));
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = Array.Empty<object>() }));
        backend.Enqueue(x => Envelope(x, "approved"));

        var outcome = await CreateRuntime(temp.Path, backend).ContinueAsync(default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal("W000001", backend.Invocations[0].WorkItemId);
        Assert.Equal("researcher", backend.Invocations[0].Role);
    }

    [Fact]
    public async Task PersistedCompleteWorkerResultIsConsumedWithoutRedispatch()
    {
        using var temp = new TestWorkspace();
        var state = PrepareCurrentState(temp, CurrentWorkPhase.Running);
        state.AttemptSequence = 1;
        state.CurrentAttemptId = "A000001";
        state.Current!.CurrentAttemptId = "A000001";
        state.Current.AttemptCount = 1;
        state.PendingContinuation = new(ContinuationKind.SemanticInvocation, state.Current.Id, null, "WORKITEMEXECUTION", true, SemanticOperationKind.WorkItemExecution);
        var attemptDirectory = Path.Combine(temp.Path, ".idd", "factory", "current", "attempts", "A000001");
        Directory.CreateDirectory(attemptDirectory);
        var invocation = new AgentInvocation
        {
            RunId = state.RunId, AttemptId = "A000001", Capability = "research", Role = "researcher", WorkItemId = state.Current.Id,
            Workspace = temp.Path, RawResultPath = Path.Combine(attemptDirectory, "raw-result.json"), SkillName = "idd-factory-research",
            ExecutionProfile = AgentExecutionProfile.ReadOnly, SemanticResultSchema = "research-v1", Input = "persisted input", StartedAt = DateTimeOffset.UnixEpoch
        };
        await File.WriteAllTextAsync(Path.Combine(attemptDirectory, "invocation.json"), JsonSerializer.Serialize(invocation, FactoryJson.Options));
        var persisted = new PersistedAttemptResult
        {
            Invocation = AttemptIdentity.From(invocation),
            SemanticResult = Envelope(invocation, "completed", new { finding = "persisted" }),
            ReceivedAt = DateTimeOffset.UnixEpoch,
            TerminationKind = AgentTerminationKind.CleanExit
        };
        await File.WriteAllTextAsync(Path.Combine(attemptDirectory, "result.json"), JsonSerializer.Serialize(persisted, FactoryJson.Options));
        await Store(temp).CreateAsync(state, default);
        var backend = new FakeAgentBackend();
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = Array.Empty<object>() }));
        backend.Enqueue(x => Envelope(x, "approved"));

        var outcome = await CreateRuntime(temp.Path, backend).ContinueAsync(default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.DoesNotContain(backend.Invocations, x => x.Role == "researcher");
        Assert.Equal(new[] { "task-decomposer", "final-reviewer" }, backend.Invocations.Select(x => x.Role));
    }

    [Fact]
    public async Task AwaitingVerificationResumesBeforeAnyLaterWork()
    {
        using var temp = new TestWorkspace();
        var state = PrepareCurrentState(temp, CurrentWorkPhase.AwaitingVerification, "implementation");
        state.Remaining.Add(StateStoreTests.Planned("W000002", "research"));
        temp.Write(".idd/factory/current/work-items/W000002/contract.md", "Do later work");
        temp.Write(".idd/verification.yaml", "version: 1\nchecks: {}\ndefault:\n  use: []\nfinal:\n  use: []\n");
        await Store(temp).CreateAsync(state, default);
        var backend = new FakeAgentBackend();
        backend.Enqueue(x => Envelope(x, "completed"));
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = Array.Empty<object>() }));
        backend.Enqueue(x => Envelope(x, "approved"));

        var outcome = await CreateRuntime(temp.Path, backend).ContinueAsync(default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal("W000002", backend.Invocations[0].WorkItemId);
        using var completed = JsonDocument.Parse(File.ReadAllText(Path.Combine(outcome.ResultDirectory!, "completed-work.json")));
        Assert.Equal(new[] { "W000001", "W000002" }, completed.RootElement.GetProperty("completed").EnumerateArray().Select(x => x.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task OrphanPlanArtifactsNeverReplaceAuthoritativeRemaining()
    {
        using var temp = new TestWorkspace();
        var state = StateStoreTests.State();
        state.InitialPlanningCompleted = true;
        state.PlanRevision = 1;
        state.Remaining.Add(StateStoreTests.Planned("W000001", "research"));
        await Store(temp).CreateAsync(state, default);
        temp.Write(".idd/factory/current/plan-revisions/P999999.json", "{\"newRemainingIds\":[\"W999999\"]}");
        temp.Write(".idd/factory/current/work-items/W999999/contract.md", "orphan");

        var loaded = await Store(temp).LoadAsync(default);

        Assert.Equal(new[] { "W000001" }, loaded!.Remaining.Select(x => x.Id));
        Assert.Equal(1, loaded.PlanRevision);
    }

    private static FactoryState PrepareCurrentState(TestWorkspace temp, CurrentWorkPhase phase, string capability = "research")
    {
        temp.Write(".idd/factory/current/request.md", "Recover linear work");
        temp.Write(".idd/factory/current/work-items/W000001/contract.md", "Do current work");
        var state = StateStoreTests.State() with { FactoryConfigurationHash = "test-config-hash" };
        state.InitialPlanningCompleted = true;
        state.PlanRevision = 1;
        state.NextWorkItemNumber = 2;
        state.Current = StateStoreTests.Planned("W000001", capability);
        state.CurrentPhase = phase;
        return state;
    }

    private static FileFactoryStateStore Store(TestWorkspace temp) => new(Path.Combine(temp.Path, ".idd", "factory", "current"), new FactoryStateValidator());
}
