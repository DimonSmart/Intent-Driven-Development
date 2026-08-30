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

public sealed class CheckpointCorrectionTests
{
    [Fact]
    public async Task NeedsFixRunsCorrectionBeforeRepeatingCheckpoint()
    {
        using var temp = new TestWorkspace();
        var request = temp.Write("task.md", "Task");
        var workflow = DefaultWorkflow(temp);
        var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        var backend = new FakeAgentBackend();

        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", Decomposition()));
        backend.Results.Enqueue(invocation => Envelope(invocation, "completed"));
        backend.Results.Enqueue(invocation => Envelope(invocation, "needs-fix", Correction()));
        backend.Results.Enqueue(invocation =>
        {
            Assert.Equal("implementer", invocation.Role);
            var state = ReadState(current);
            var review = state.WorkItems.Single(x => x.Id == "review");
            Assert.Equal(WorkItemStatus.Planned, review.Status);
            Assert.Null(review.CurrentAttemptId);
            Assert.Equal("attempts/A000003/result.json", review.LastResultRef);
            Assert.Contains("fix-review", review.Dependencies);
            Assert.Contains("fix-review", review.CoveredWorkItems);
            Assert.Equal(WorkItemStatus.Running, state.WorkItems.Single(x => x.Id == "fix-review").Status);
            return Envelope(invocation, "completed");
        });
        backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));
        backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));

        var outcome = await Create(temp.Path, workflow, current, backend).RunAsync(request, "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal(
            ["task-decomposer", "implementer", "checkpoint-reviewer", "implementer", "checkpoint-reviewer", "final-reviewer"],
            backend.Roles);
    }

    [Fact]
    public async Task ContinueRecoversCompletedNeedsFixFromWorkItemAttempt()
    {
        using var temp = new TestWorkspace();
        var workflow = DefaultWorkflow(temp);
        var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        temp.Write(".idd/factory/current/request.md", "Resume task");
        temp.Write(".idd/factory/current/work-items/001-one.md", "# One");
        temp.Write(".idd/factory/current/work-items/002-review.md", "# Review");
        temp.Write(".idd/factory/current/work-items/003-fix-review.md", "# Orphaned correction artifact");

        var state = StateStoreTests.State() with
        {
            WorkflowHash = workflow.Hash,
            CurrentWorkflowStep = "execute",
            AttemptSequence = 9
        };
        state.WorkItems.Add(new WorkItemState
        {
            Id = "one",
            Sequence = 1,
            Kind = WorkItemKind.Subtask,
            Status = WorkItemStatus.Completed,
            ContractPath = "work-items/001-one.md",
            LastResultRef = "attempts/A000001/result.json"
        });
        state.WorkItems.Add(new WorkItemState
        {
            Id = "review",
            Sequence = 2,
            Kind = WorkItemKind.ReviewCheckpoint,
            Status = WorkItemStatus.Running,
            ContractPath = "work-items/002-review.md",
            Dependencies = ["one"],
            CoveredWorkItems = ["one"],
            CurrentAttemptId = "A000009",
            AttemptCount = 1
        });
        await new FileFactoryStateStore(current, new FactoryStateValidator()).CreateAsync(state, default);

        var invocation = new AgentInvocation
        {
            RunId = state.RunId,
            AttemptId = "A000009",
            Role = "checkpoint-reviewer",
            WorkItemId = "review",
            Workspace = temp.Path,
            ResultPath = Path.Combine(current, "attempts", "A000009", "result.json"),
            SkillName = "idd-factory-review-checkpoint",
            ExecutionProfile = AgentExecutionProfile.ReadOnly,
            Input = "input",
            StartedAt = DateTimeOffset.UnixEpoch
        };
        temp.Write(".idd/factory/current/attempts/A000009/invocation.json", JsonSerializer.Serialize(invocation, FactoryJson.Options));
        temp.Write(".idd/factory/current/attempts/A000009/result.json", JsonSerializer.Serialize(Envelope(invocation, "needs-fix", Correction()), FactoryJson.Options));

        var backend = new FakeAgentBackend();
        backend.Results.Enqueue(next =>
        {
            Assert.Equal("implementer", next.Role);
            var recovered = ReadState(current);
            var review = recovered.WorkItems.Single(x => x.Id == "review");
            Assert.Equal(WorkItemStatus.Planned, review.Status);
            Assert.Null(review.CurrentAttemptId);
            Assert.Equal("attempts/A000009/result.json", review.LastResultRef);
            Assert.Equal(1, recovered.CorrectiveCycleCount);
            var correction = Assert.Single(recovered.WorkItems.Where(x => x.Kind == WorkItemKind.CorrectiveSubtask));
            Assert.Equal("fix-review", correction.Id);
            Assert.Equal("# Fix review", File.ReadAllText(Path.Combine(current, correction.ContractPath)));
            return Envelope(next, "completed");
        });
        backend.Results.Enqueue(next => Envelope(next, "approved"));
        backend.Results.Enqueue(next => Envelope(next, "approved"));

        var outcome = await Create(temp.Path, workflow, current, backend).ContinueAsync(default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal(["implementer", "checkpoint-reviewer", "final-reviewer"], backend.Roles);
    }

    [Fact]
    public void RunningToPlannedIsRestrictedToCompletedCheckpointReview()
    {
        var validator = new FactoryStateValidator();
        var previous = StateStoreTests.State();
        previous.WorkItems.Add(new WorkItemState
        {
            Id = "review",
            Sequence = 1,
            Kind = WorkItemKind.ReviewCheckpoint,
            Status = WorkItemStatus.Running,
            ContractPath = "review.md",
            CurrentAttemptId = "A000001"
        });
        var next = Clone(previous);
        next.WorkItems[0].Status = WorkItemStatus.Planned;
        next.WorkItems[0].CurrentAttemptId = null;
        next.WorkItems[0].LastResultRef = "attempts/A000001/result.json";

        validator.ValidateMutation(previous, next);

        previous.WorkItems[0] = previous.WorkItems[0] with { Kind = WorkItemKind.Subtask };
        next = Clone(previous);
        next.WorkItems[0].Status = WorkItemStatus.Planned;
        next.WorkItems[0].CurrentAttemptId = null;
        next.WorkItems[0].LastResultRef = "attempts/A000001/result.json";
        Assert.Equal("INVALID_STATE_TRANSITION", Assert.Throws<FactoryStateException>(() => validator.ValidateMutation(previous, next)).Code);
    }

    private static object Decomposition() => new
    {
        workItems = new object[]
        {
            new
            {
                id = "one",
                sequence = 1,
                kind = "subtask",
                contractMarkdown = "# One",
                dependencies = Array.Empty<string>(),
                coveredWorkItems = Array.Empty<string>(),
                verificationCheckIds = Array.Empty<string>()
            },
            new
            {
                id = "review",
                sequence = 2,
                kind = "review-checkpoint",
                contractMarkdown = "# Review",
                dependencies = new[] { "one" },
                coveredWorkItems = new[] { "one" },
                verificationCheckIds = Array.Empty<string>()
            }
        }
    };

    private static object Correction() => new
    {
        correctiveSubtask = new
        {
            id = "fix-review",
            contractMarkdown = "# Fix review",
            verificationCheckIds = Array.Empty<string>()
        }
    };

    private static FactoryRuntime Create(string workspace, WorkflowDefinition workflow, string current, IAgentBackend backend)
    {
        var validator = new FactoryStateValidator();
        var clock = new FakeClock();
        return new FactoryRuntime(
            workspace,
            workflow,
            new FileFactoryStateStore(current, validator),
            new AgentExecutor(backend, new AgentResultValidator()),
            new VerificationEngine(workspace, current),
            new FactoryEventWriter(current, clock),
            clock);
    }

    private static WorkflowDefinition DefaultWorkflow(TestWorkspace temp)
    {
        var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "runtime", "Idd.Factory", "factory-workflow.yaml"));
        return new WorkflowDefinitionLoader().Load(temp.Path, source);
    }

    private static AgentResultEnvelope Envelope(AgentInvocation invocation, string outcome, object? payload = null)
    {
        JsonElement? element = payload is null ? null : JsonSerializer.SerializeToElement(payload, FactoryJson.Options);
        return new AgentResultEnvelope
        {
            ProtocolVersion = AgentInvocation.CurrentProtocolVersion,
            RunId = invocation.RunId,
            AttemptId = invocation.AttemptId,
            Role = invocation.Role,
            Outcome = outcome,
            Payload = element
        };
    }

    private static FactoryState ReadState(string current) =>
        JsonSerializer.Deserialize<FactoryState>(File.ReadAllText(Path.Combine(current, "state.json")), FactoryJson.Options)!;

    private static FactoryState Clone(FactoryState state) =>
        JsonSerializer.Deserialize<FactoryState>(JsonSerializer.Serialize(state, FactoryJson.Options), FactoryJson.Options)!;

    private sealed class FakeAgentBackend : IAgentBackend
    {
        public Queue<Func<AgentInvocation, AgentResultEnvelope>> Results { get; } = new();
        public List<string> Roles { get; } = [];

        public Task<AgentRunHandle> StartAsync(AgentInvocation invocation, CancellationToken cancellationToken)
        {
            Roles.Add(invocation.Role);
            var result = Results.Dequeue()(invocation);
            Directory.CreateDirectory(Path.GetDirectoryName(invocation.ResultPath)!);
            File.WriteAllText(invocation.ResultPath, JsonSerializer.Serialize(result, FactoryJson.Options));
            return Task.FromResult(new AgentRunHandle(invocation.AttemptId, 1, invocation.AttemptId));
        }

        public Task<AgentProcessResult> WaitAsync(AgentRunHandle handle, CancellationToken cancellationToken) =>
            Task.FromResult(new AgentProcessResult(0, "", "", true, false, AgentTerminationKind.CleanExit));

        public Task CancelAsync(AgentRunHandle handle, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    }
}
