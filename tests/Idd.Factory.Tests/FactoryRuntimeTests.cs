using System.Text.Json;
using Idd.Factory.Agents;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.Runtime;
using Idd.Factory.State;
using Idd.Factory.Telemetry;
using Idd.Factory.Verification;
using Idd.Factory.Workflow;

namespace Idd.Factory.Tests;

public sealed class FactoryRuntimeTests
{
    [Fact] public async Task OneSubtaskHappyPathUsesNoCoordinatorAndFinalizes()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Implement the specified behavior."); var workflowPath = temp.Write("workflow.yaml", WorkflowTests.ValidText);
        var workflow = new WorkflowDefinitionLoader().Load(temp.Path, workflowPath); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"); var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", new { workItems = new[] { new { id = "one", sequence = 1, kind = "subtask", contractMarkdown = "# One\n\nImplement one.", dependencies = Array.Empty<string>(), coveredWorkItems = Array.Empty<string>(), verificationCheckIds = Array.Empty<string>() } } }));
        backend.Results.Enqueue(invocation => Envelope(invocation, "completed")); backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));
        var runtime = Create(temp.Path, workflow, current, backend); var outcome = await runtime.RunAsync(request, "test", default);
        Assert.Equal("COMPLETED", outcome.FactoryOutcome); Assert.Equal(new[] { "task-decomposer", "implementer", "final-reviewer" }, backend.Roles); Assert.DoesNotContain("factory-step-coordinator", backend.Roles);
        Assert.Empty(Directory.EnumerateFileSystemEntries(current)); Assert.True(File.Exists(System.IO.Path.Combine(outcome.ResultDirectory!, "factory-result.json")));
    }

    [Fact] public async Task WorkflowChangeDuringRunIsDetected()
    {
        using var temp = new TestWorkspace(); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"); Directory.CreateDirectory(current);
        var state = StateStoreTests.State() with { WorkflowHash = "old" }; await new FileFactoryStateStore(current, new FactoryStateValidator()).CreateAsync(state, default);
        var workflow = new WorkflowDefinitionLoader().Load(temp.Path, temp.Write("workflow.yaml", WorkflowTests.ValidText)); var outcome = await Create(temp.Path, workflow, current, new FakeAgentBackend()).ContinueAsync(default);
        Assert.Equal("WORKFLOW_CHANGED", outcome.FactoryOutcome);
    }

    [Fact] public async Task LegacyStateIsNotMigrated()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/factory/current/001-old.ready.md", "old"); var workflow = new WorkflowDefinitionLoader().Load(temp.Path, temp.Write("workflow.yaml", WorkflowTests.ValidText));
        var runtime = Create(temp.Path, workflow, System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"), new FakeAgentBackend());
        Assert.Equal("LEGACY_FACTORY_STATE", (await Assert.ThrowsAsync<FactoryStateException>(() => runtime.RunAsync(temp.Write("request.md", "x"), "test", default))).Code);
    }

    [Fact] public async Task ContinueReusesPersistedValidWorkerResult()
    {
        using var temp = new TestWorkspace(); var workflow = new WorkflowDefinitionLoader().Load(temp.Path, temp.Write("workflow.yaml", WorkflowTests.ValidText));
        var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"); temp.Write(".idd/factory/current/request.md", "Resume task"); temp.Write(".idd/factory/current/work-items/001-one.md", "# One");
        var state = StateStoreTests.State() with { WorkflowHash = workflow.Hash, CurrentWorkflowStep = "execute", CurrentAttemptId = "A000001", AttemptSequence = 1 };
        state.WorkItems.Add(new WorkItemState { Id = "one", Sequence = 1, Kind = WorkItemKind.Subtask, Status = WorkItemStatus.Running, ContractPath = "work-items/001-one.md", CurrentAttemptId = "A000001", AttemptCount = 1 });
        await new FileFactoryStateStore(current, new FactoryStateValidator()).CreateAsync(state, default);
        var invocation = new AgentInvocation { RunId = state.RunId, AttemptId = "A000001", Role = "implementer", WorkItemId = "one", Workspace = temp.Path, ResultPath = System.IO.Path.Combine(current, "attempts", "A000001", "result.json"), Prompt = "p", StartedAt = DateTimeOffset.UnixEpoch, WorkspaceFingerprint = "f" };
        temp.Write(".idd/factory/current/attempts/A000001/invocation.json", JsonSerializer.Serialize(invocation, FactoryJson.Options)); temp.Write(".idd/factory/current/attempts/A000001/result.json", JsonSerializer.Serialize(Envelope(invocation, "completed"), FactoryJson.Options));
        var backend = new FakeAgentBackend(); backend.Results.Enqueue(next => Envelope(next, "approved"));
        var outcome = await Create(temp.Path, workflow, current, backend).ContinueAsync(default);
        Assert.Equal("COMPLETED", outcome.FactoryOutcome); Assert.Equal(["final-reviewer"], backend.Roles);
    }

    [Fact] public async Task UnknownPersistedAttemptIsRejected()
    {
        using var temp = new TestWorkspace(); var workflow = new WorkflowDefinitionLoader().Load(temp.Path, temp.Write("workflow.yaml", WorkflowTests.ValidText)); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current");
        var state = StateStoreTests.State() with { WorkflowHash = workflow.Hash, CurrentAttemptId = "A000001" }; await new FileFactoryStateStore(current, new FactoryStateValidator()).CreateAsync(state, default);
        Assert.Equal("UNKNOWN_ATTEMPT", (await Assert.ThrowsAsync<AgentProtocolException>(() => Create(temp.Path, workflow, current, new FakeAgentBackend()).ContinueAsync(default))).Code);
    }

    [Fact] public async Task IntentGateResumesAfterDurableIntentChanges()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/intent/spec.md", "before"); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"); var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "intent-required"));
        Assert.Equal("INTENT_REQUIRED", (await Create(temp.Path, workflow, current, backend).RunAsync(request, "test", default)).FactoryOutcome);
        temp.Write(".idd/intent/spec.md", "after"); EnqueueHappyPath(backend);
        Assert.Equal("COMPLETED", (await Create(temp.Path, workflow, current, backend).ContinueAsync(default)).FactoryOutcome);
    }

    [Fact] public async Task ClarificationRequiresAndPersistsAnswer()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"); var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "needs-clarification")); var runtime = Create(temp.Path, workflow, current, backend);
        Assert.Equal("NEEDS_CLARIFICATION", (await runtime.RunAsync(request, "test", default)).FactoryOutcome); var count = backend.Roles.Count;
        Assert.Equal("NEEDS_CLARIFICATION", (await runtime.ContinueAsync(default)).FactoryOutcome); Assert.Equal(count, backend.Roles.Count);
        EnqueueHappyPath(backend); var answer = temp.Write("answer.md", "Use option A.");
        Assert.Equal("COMPLETED", (await runtime.ContinueAsync(default, answer)).FactoryOutcome);
    }

    private static FactoryRuntime Create(string workspace, WorkflowDefinition workflow, string current, IAgentBackend backend)
    { var validator = new FactoryStateValidator(); var fingerprint = new WorkspaceFingerprinter(); var clock = new FakeClock(); return new(workspace, workspace, workflow, new FileFactoryStateStore(current, validator), new AgentExecutor(backend, new AgentResultValidator()), new VerificationEngine(workspace, current, fingerprint), fingerprint, new FactoryEventWriter(current, clock), clock); }
    private static AgentResultEnvelope Envelope(AgentInvocation invocation, string outcome, object? payload = null)
    { JsonElement? element = payload is null ? null : JsonSerializer.SerializeToElement(payload, FactoryJson.Options); return new() { ProtocolVersion = 1, RunId = invocation.RunId, AttemptId = invocation.AttemptId, Role = invocation.Role, Outcome = outcome, Payload = element }; }
    private static WorkflowDefinition DefaultWorkflow(TestWorkspace temp)
    { var source = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "runtime", "Idd.Factory", "factory-workflow.yaml")); return new WorkflowDefinitionLoader().Load(temp.Path, source); }
    private static void EnqueueHappyPath(FakeAgentBackend backend)
    {
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", new { workItems = new[] { new { id = "one", sequence = 1, kind = "subtask", contractMarkdown = "# One", dependencies = Array.Empty<string>(), coveredWorkItems = Array.Empty<string>(), verificationCheckIds = Array.Empty<string>() } } }));
        backend.Results.Enqueue(invocation => Envelope(invocation, "completed")); backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));
    }

    private sealed class FakeAgentBackend : IAgentBackend
    {
        public Queue<Func<AgentInvocation, AgentResultEnvelope>> Results { get; } = new(); public List<string> Roles { get; } = [];
        public Task<AgentRunHandle> StartAsync(AgentInvocation invocation, CancellationToken cancellationToken) { Roles.Add(invocation.Role); var result = Results.Dequeue()(invocation); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(invocation.ResultPath)!); File.WriteAllText(invocation.ResultPath, JsonSerializer.Serialize(result, FactoryJson.Options)); return Task.FromResult(new AgentRunHandle(invocation.AttemptId, 1, invocation.AttemptId)); }
        public Task<AgentProcessResult> WaitAsync(AgentRunHandle handle, CancellationToken cancellationToken) => Task.FromResult(new AgentProcessResult(0, "", "", false));
        public Task CancelAsync(AgentRunHandle handle, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class FakeClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-01-01T00:00:00Z"); }
}
