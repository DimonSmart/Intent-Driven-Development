using System.Text.Json;
using Idd.Factory.Agents;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.State;
using static Idd.Factory.Tests.FactoryRuntimeTestHarness;

namespace Idd.Factory.Tests;

public sealed class CrashRecoveryTests
{
    [Fact]
    public async Task PersistedAttemptBeforeInvocationRetriesDeterministically()
    {
        using var temp = new TestWorkspace();
        await SeedRunningResearchAttemptAsync(temp, writeInvocation: false, writeResult: false);
        var backend = HappyRetryBackend();

        var outcome = await CreateRuntime(temp.Path, backend).ContinueAsync(default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        var retry = Assert.Single(backend.Invocations, x => x.Role == "researcher");
        Assert.Equal("A000002", retry.AttemptId);
        Assert.Equal("A", retry.WorkItemId);
    }

    [Fact]
    public async Task InvocationWithoutResultReturnsWorkToRetryState()
    {
        using var temp = new TestWorkspace();
        await SeedRunningResearchAttemptAsync(temp, writeInvocation: true, writeResult: false);
        var backend = HappyRetryBackend();

        var outcome = await CreateRuntime(temp.Path, backend).ContinueAsync(default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        var retry = Assert.Single(backend.Invocations, x => x.Role == "researcher");
        Assert.Equal("A000002", retry.AttemptId);
    }

    [Fact]
    public async Task PersistedResultIsReusedWithoutDuplicateSemanticDispatch()
    {
        using var temp = new TestWorkspace();
        await SeedRunningResearchAttemptAsync(temp, writeInvocation: true, writeResult: true);
        var backend = new FakeAgentBackend();
        backend.Enqueue(invocation => Envelope(invocation, "approved"));

        var outcome = await CreateRuntime(temp.Path, backend).ContinueAsync(default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.DoesNotContain(backend.Invocations, x => x.Role == "researcher");
        Assert.Single(backend.Invocations, x => x.Role == "final-reviewer");
        using var graph = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outcome.ResultDirectory!, "decomposition", "decomposition.json")));
        var work = graph.RootElement.GetProperty("workItems").EnumerateArray().Single(x => x.GetProperty("id").GetString() == "A");
        Assert.Equal("Completed", work.GetProperty("status").GetString());
        Assert.Equal("attempts/A000001/result.json", work.GetProperty("lastResultRef").GetString());
    }

    private static FakeAgentBackend HappyRetryBackend()
    {
        var backend = new FakeAgentBackend();
        backend.Enqueue(invocation => Envelope(invocation, "completed", new { finding = "recovered retry" }));
        backend.Enqueue(invocation => Envelope(invocation, "approved"));
        return backend;
    }

    private static async Task SeedRunningResearchAttemptAsync(TestWorkspace temp, bool writeInvocation, bool writeResult)
    {
        var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        temp.Write(".idd/factory/current/request.md", "Recover semantic work");
        temp.Write(".idd/factory/current/work-items/A/contracts/000001.md", "# Research A");
        var state = StateStoreTests.State() with
        {
            RunId = "recovery-run",
            GraphRevision = 1,
            FactoryConfigurationHash = CreateConfiguration().Hash,
            CurrentAttemptId = "A000001",
            AttemptSequence = 1,
            PendingContinuation = new(
                ContinuationKind.SemanticInvocation,
                "A",
                null,
                "WORK_ITEM_EXECUTION",
                true,
                SemanticOperationKind.WorkItemExecution,
                "focused persisted operation")
        };
        state.WorkItems.Add(new WorkItemState
        {
            Id = "A",
            Sequence = 1,
            Kind = WorkItemKind.Subtask,
            Capability = "research",
            DefinitionState = WorkDefinitionState.Executable,
            Status = WorkItemStatus.Running,
            ContractPath = "work-items/A/contracts/000001.md",
            ContractRevision = 1,
            CurrentAttemptId = "A000001",
            AttemptCount = 1
        });
        await new FileFactoryStateStore(current, new FactoryStateValidator()).CreateAsync(state, default);

        if (!writeInvocation) return;
        var attemptDirectory = Path.Combine(current, "attempts", "A000001");
        Directory.CreateDirectory(attemptDirectory);
        var invocation = new AgentInvocation
        {
            RunId = state.RunId,
            AttemptId = "A000001",
            Role = "researcher",
            WorkItemId = "A",
            Workspace = temp.Path,
            ResultPath = Path.Combine(attemptDirectory, "result.json"),
            SkillName = "idd-factory-research",
            ExecutionProfile = AgentExecutionProfile.ReadOnly,
            Input = "focused persisted operation",
            StartedAt = DateTimeOffset.UnixEpoch
        };
        await File.WriteAllTextAsync(Path.Combine(attemptDirectory, "invocation.json"), JsonSerializer.Serialize(invocation, FactoryJson.Options));
        if (writeResult)
            await File.WriteAllTextAsync(invocation.ResultPath, JsonSerializer.Serialize(Envelope(invocation, "completed", new { finding = "persisted result" }), FactoryJson.Options));
    }
}
