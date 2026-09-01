using Idd.Factory.Agents;
using Idd.Factory.Domain;
using Idd.Factory.Runtime;

namespace Idd.Factory.Tests;

public sealed class CapabilityDrivenRecoveryTests
{
    [Theory]
    [InlineData("planning", "final-reviewer", SemanticOperationKind.Planning)]
    [InlineData("final-review", "task-decomposer", SemanticOperationKind.FinalReview)]
    [InlineData("research", "task-decomposer", SemanticOperationKind.WorkItemExecution)]
    public void RecoveryOperationComesFromCapabilityNotRole(
        string capability,
        string unrelatedRole,
        SemanticOperationKind expected)
    {
        var invocation = Invocation(capability, unrelatedRole);

        Assert.Equal(expected, SemanticAttemptReconciler.ResolveOperation(invocation));
    }

    [Fact]
    public async Task PersistedPlanningResultCreatesPlanningContinuationFromCapabilityMetadata()
    {
        using var temp = new TestWorkspace();
        var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        var attemptDirectory = Path.Combine(current, "attempts", "A000001");
        Directory.CreateDirectory(attemptDirectory);
        var state = StateStoreTests.State();
        state.CurrentAttemptId = "A000001";
        var invocation = Invocation("planning", "task-decomposer", state.RunId, attemptDirectory);
        await File.WriteAllTextAsync(Path.Combine(attemptDirectory, "invocation.json"), System.Text.Json.JsonSerializer.Serialize(invocation, FactoryJson.Options));
        await File.WriteAllTextAsync(Path.Combine(attemptDirectory, "result.json"), "{}");
        var saves = 0;
        var reconciler = new SemanticAttemptReconciler(
            current,
            (_, _, _, _) => Task.CompletedTask,
            (_, _) => { saves++; return Task.CompletedTask; });

        await reconciler.ReconcileAsync(state, default);

        Assert.Equal(1, saves);
        Assert.Equal(SemanticOperationKind.Planning, state.PendingContinuation?.Operation);
        Assert.Equal(ContinuationKind.SemanticInvocation, state.PendingContinuation?.Kind);
    }

    [Fact]
    public void RoleRemainsTransportIdentityEvenThoughItDoesNotSelectOperation()
    {
        var state = StateStoreTests.State();
        state.CurrentAttemptId = "A000001";
        var invocation = Invocation("planning", "renamed-planner", state.RunId);

        var exception = Assert.Throws<AgentProtocolException>(() =>
            SemanticAttemptReconciler.ValidateIdentity(state, "A000001", invocation));

        Assert.Equal("UNKNOWN_ATTEMPT", exception.Code);
        Assert.Equal(SemanticOperationKind.Planning, SemanticAttemptReconciler.ResolveOperation(invocation));
    }

    private static AgentInvocation Invocation(
        string capability,
        string role,
        string runId = "test-run",
        string? attemptDirectory = null)
    {
        attemptDirectory ??= Path.Combine(Path.GetTempPath(), "idd-capability-recovery", Guid.NewGuid().ToString("N"));
        return new AgentInvocation
        {
            RunId = runId,
            AttemptId = "A000001",
            Capability = capability,
            Role = role,
            WorkItemId = null,
            Workspace = Path.GetTempPath(),
            RawResultPath = Path.Combine(attemptDirectory, "raw-result.json"),
            SkillName = "test-skill",
            ExecutionProfile = AgentExecutionProfile.ReadOnly,
            SemanticResultSchema = capability + "-v1",
            Input = "test",
            StartedAt = DateTimeOffset.UnixEpoch
        };
    }
}
