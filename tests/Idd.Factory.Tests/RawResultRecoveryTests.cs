using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.State;
using Idd.Factory.Verification;
using static Idd.Factory.Tests.FactoryRuntimeTestHarness;

namespace Idd.Factory.Tests;

public sealed class RawResultRecoveryTests
{
    [Fact]
    public async Task ImplementationRawResultRecoveryContinuesThroughVerificationWithoutRedispatch()
    {
        using var temp = new TestWorkspace();
        var (state, invocation, attemptDirectory) = PrepareInterruptedAttempt(temp, "implementation");
        await File.WriteAllTextAsync(
            invocation.RawResultPath,
            """
            {
              "outcome": "completed",
              "summary": "Recovered implementation result.",
              "declaredChanges": ["Added implementation", "Added tests"],
              "concerns": ["Keep recovery bounded"],
              "verificationClaims": ["Worker says tests pass"]
            }
            """);
        await WriteCompleteTelemetryAsync(attemptDirectory);
        await Store(temp).CreateAsync(state, default);
        var backend = new FakeAgentBackend();
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = Array.Empty<object>() }));
        backend.Enqueue(x => Envelope(x, "approved"));
        var verification = new SequencedVerificationEngine(
            temp.Path,
            (VerificationStatus.Passed, "subtask", 0, "passed"),
            (VerificationStatus.Passed, "final", 0, "passed"));

        var outcome = await CreateRuntime(temp.Path, backend, verification: verification).ContinueAsync(default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.DoesNotContain(backend.Invocations, x => x.Capability == "implementation");
        var planner = Assert.Single(backend.Invocations, x => x.Capability == "planning");
        Assert.Contains("Recovered implementation result.", planner.Input);
        Assert.Contains("Added implementation", planner.Input);
        Assert.Contains("Keep recovery bounded", planner.Input);
        Assert.DoesNotContain("Worker says tests pass", planner.Input);
        var persisted = JsonSerializer.Deserialize<PersistedAttemptResult>(
            File.ReadAllText(Path.Combine(outcome.ResultDirectory!, "attempts", "A000001", "result.json")), FactoryJson.Options)!;
        Assert.Equal("Recovered implementation result.", persisted.SemanticResult.Summary);
        Assert.Equal(2, persisted.SemanticResult.DeclaredChanges?.Count);
        Assert.Equal(AttemptIdentity.From(invocation), persisted.Invocation);
    }

    [Fact]
    public async Task MalformedRawResultRemainsResumableAndNeverBecomesAuthoritative()
    {
        using var temp = new TestWorkspace();
        var (state, invocation, attemptDirectory) = PrepareInterruptedAttempt(temp);
        await File.WriteAllTextAsync(
            invocation.RawResultPath,
            "{\"outcome\":\"completed\",\"attemptId\":\"A000001\"}");
        await WriteCompleteTelemetryAsync(attemptDirectory);
        await Store(temp).CreateAsync(state, default);
        var backend = new FakeAgentBackend();

        var outcome = await CreateRuntime(temp.Path, backend).ContinueAsync(default);
        var persistedState = await LoadState(temp.Path);

        Assert.Equal("MALFORMED_AGENT_RESULT", outcome.FactoryOutcome);
        Assert.Contains("research-v1", outcome.Reason);
        Assert.Contains("attemptId", outcome.Reason);
        Assert.Contains("A000001", outcome.Reason);
        Assert.Contains("continue the exact operation", outcome.ResumeWhen, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(attemptDirectory, "result.json")));
        Assert.Equal(FactoryRunStatus.Blocked, persistedState.RunStatus);
        Assert.Equal("A000001", persistedState.CurrentAttemptId);
        Assert.True(persistedState.PendingContinuation?.IsResumable);
        Assert.Empty(backend.Invocations);
    }

    [Fact]
    public async Task RawResultCannotBeRecoveredForAnotherAttemptIdentity()
    {
        using var temp = new TestWorkspace();
        var (state, invocation, attemptDirectory) = PrepareInterruptedAttempt(temp);
        var foreignInvocation = invocation with { AttemptId = "A000002" };
        await File.WriteAllTextAsync(
            Path.Combine(attemptDirectory, "invocation.json"),
            JsonSerializer.Serialize(foreignInvocation, FactoryJson.Options));
        await File.WriteAllTextAsync(invocation.RawResultPath, "{\"outcome\":\"completed\",\"summary\":\"foreign\"}");
        await WriteCompleteTelemetryAsync(attemptDirectory);
        await Store(temp).CreateAsync(state, default);
        var backend = new FakeAgentBackend();

        var outcome = await CreateRuntime(temp.Path, backend).ContinueAsync(default);

        Assert.Equal("UNKNOWN_ATTEMPT", outcome.FactoryOutcome);
        Assert.False(File.Exists(Path.Combine(attemptDirectory, "result.json")));
        Assert.Empty(backend.Invocations);
    }

    [Fact]
    public async Task RawResultWithoutCompletionTelemetryIsNotPromoted()
    {
        using var temp = new TestWorkspace();
        var (state, invocation, attemptDirectory) = PrepareInterruptedAttempt(temp);
        await File.WriteAllTextAsync(invocation.RawResultPath, "{\"outcome\":\"completed\",\"summary\":\"not proven complete\"}");
        await Store(temp).CreateAsync(state, default);
        var backend = new FakeAgentBackend();

        var outcome = await CreateRuntime(temp.Path, backend).ContinueAsync(default);

        Assert.Equal("ATTEMPT_RECOVERY_UNSAFE", outcome.FactoryOutcome);
        Assert.Contains("process-telemetry.json", outcome.Reason);
        Assert.False(File.Exists(Path.Combine(attemptDirectory, "result.json")));
        Assert.Empty(backend.Invocations);
    }

    private static (FactoryState State, AgentInvocation Invocation, string AttemptDirectory) PrepareInterruptedAttempt(
        TestWorkspace temp,
        string capability = "research")
    {
        temp.Write(".idd/factory/current/request.md", "Recover raw result");
        temp.Write(".idd/factory/current/work-items/W000001/contract.md", $"Do current {capability} work");
        var state = StateStoreTests.State() with { FactoryConfigurationHash = "test-config-hash" };
        state.InitialPlanningCompleted = true;
        state.PlanRevision = 1;
        state.NextWorkItemNumber = 2;
        state.Current = StateStoreTests.Planned("W000001", capability);
        state.CurrentPhase = CurrentWorkPhase.Running;
        state.AttemptSequence = 1;
        state.CurrentAttemptId = "A000001";
        state.Current.CurrentAttemptId = "A000001";
        state.Current.AttemptCount = 1;
        state.PendingContinuation = new(
            ContinuationKind.SemanticInvocation,
            state.Current.Id,
            null,
            "WORKITEMEXECUTION",
            true,
            SemanticOperationKind.WorkItemExecution,
            "persisted input");

        var attemptDirectory = Path.Combine(temp.Path, ".idd", "factory", "current", "attempts", "A000001");
        Directory.CreateDirectory(attemptDirectory);
        var agent = FactoryCapabilityCatalog.ResolveWorkItem(capability).Agent;
        var invocation = new AgentInvocation
        {
            RunId = state.RunId,
            AttemptId = "A000001",
            Capability = capability,
            Role = agent.Role,
            WorkItemId = state.Current.Id,
            Workspace = temp.Path,
            RawResultPath = Path.Combine(attemptDirectory, "raw-result.json"),
            SkillName = agent.SkillName,
            ExecutionProfile = agent.ExecutionProfile,
            SemanticResultSchema = SemanticResultContracts.SchemaForCapability(capability),
            Input = "persisted input",
            StartedAt = DateTimeOffset.UnixEpoch
        };
        File.WriteAllText(Path.Combine(attemptDirectory, "invocation.json"), JsonSerializer.Serialize(invocation, FactoryJson.Options));
        return (state, invocation, attemptDirectory);
    }

    private static Task WriteCompleteTelemetryAsync(string attemptDirectory) =>
        File.WriteAllTextAsync(
            Path.Combine(attemptDirectory, "process-telemetry.json"),
            JsonSerializer.Serialize(
                new AgentProcessResult(0, "", "", true, false, AgentTerminationKind.CleanExit),
                FactoryJson.Options));

    private static FileFactoryStateStore Store(TestWorkspace temp) =>
        new(Path.Combine(temp.Path, ".idd", "factory", "current"), new FactoryStateValidator());
}
