using System.Text.Json;
using Idd.Factory.Agents;
using Idd.Factory.Domain;

namespace Idd.Factory.Tests;

public sealed class AgentProtocolTests
{
    [Theory]
    [InlineData("implementer", "completed")]
    [InlineData("implementer", "additional-work-required")]
    [InlineData("implementer", "global-replan-required")]
    [InlineData("researcher", "completed")]
    [InlineData("researcher", "additional-work-required")]
    [InlineData("final-reviewer", "approved")]
    [InlineData("final-reviewer", "correction-required")]
    [InlineData("final-reviewer", "needs-fix")]
    [InlineData("final-reviewer", "additional-work-required")]
    [InlineData("final-reviewer", "global-replan-required")]
    [InlineData("task-decomposer", "ready")]
    [InlineData("factory-replanner", "replan-proposed")]
    public void CapabilityProtocolAcceptsTypedOutcomes(string role, string outcome)
    {
        var invocation = Invocation(role);
        var result = Envelope(invocation, outcome);

        Assert.Same(result, new FactoryAgentResultValidator().Validate(invocation, result));
    }

    [Fact]
    public void InvalidRoleOutcomeCombinationIsRejected()
    {
        var invocation = Invocation("researcher");
        var exception = Assert.Throws<AgentProtocolException>(() =>
            new FactoryAgentResultValidator().Validate(invocation, Envelope(invocation, "approved")));

        Assert.Equal("UNSUPPORTED_AGENT_OUTCOME", exception.Code);
    }

    [Theory]
    [InlineData("implementer")]
    [InlineData("researcher")]
    [InlineData("final-reviewer")]
    public void WorkWorkersDoNotOwnUserClarificationOutcome(string role)
    {
        var invocation = Invocation(role);

        var exception = Assert.Throws<AgentProtocolException>(() =>
            new FactoryAgentResultValidator().Validate(invocation, Envelope(invocation, "needs-clarification")));

        Assert.Equal("UNSUPPORTED_AGENT_OUTCOME", exception.Code);
    }

    [Theory]
    [InlineData("implementation", "implementer", "idd-factory-execute-subtask", AgentExecutionProfile.WorkspaceWrite)]
    [InlineData("research", "researcher", "idd-factory-research", AgentExecutionProfile.ReadOnly)]
    [InlineData("semantic-review", "final-reviewer", "idd-factory-review-task", AgentExecutionProfile.ReadOnly)]
    [InlineData("documentation", "implementer", "idd-factory-execute-subtask", AgentExecutionProfile.WorkspaceWrite)]
    public void WorkCapabilityMapsDeterministically(string capability, string role, string skill, AgentExecutionProfile profile)
    {
        var contract = FactoryCapabilityCatalog.ResolveWorkItem(capability);

        Assert.Equal(role, contract.Agent.Role);
        Assert.Equal(skill, contract.Agent.SkillName);
        Assert.Equal(profile, contract.Agent.ExecutionProfile);
    }

    [Fact]
    public void UnknownCapabilityIsRejected()
    {
        Assert.Equal("UNKNOWN_CAPABILITY", Assert.Throws<AgentProtocolException>(() => FactoryCapabilityCatalog.ResolveWorkItem("mystery")).Code);
    }

    [Fact]
    public void ThereIsOnlyOneAuthoritativeAgentProtocolExceptionType()
    {
        var types = typeof(AgentProtocolException).Assembly.GetTypes().Where(type => type.Name == nameof(AgentProtocolException)).ToArray();
        Assert.Single(types);
        Assert.Equal(typeof(AgentProtocolException), types[0]);
    }

    [Theory]
    [InlineData(".idd/factory.yaml", "WORKER_CHANGED_FACTORY_POLICY")]
    [InlineData(".idd/factory/current/state.json", "WORKER_CHANGED_RUNNER_STATE")]
    [InlineData(".idd/factory/current/request.md", "WORKER_CHANGED_RUNNER_STATE")]
    [InlineData(".idd/factory/current/run-context.md", "WORKER_CHANGED_RUNNER_STATE")]
    [InlineData(".idd/factory/current/work-items/item/contracts/000001.md", "WORKER_CHANGED_RUNNER_STATE")]
    [InlineData(".idd/factory/current/graph/mutations/G000001.json", "WORKER_CHANGED_RUNNER_STATE")]
    [InlineData(".idd/factory/current/clarifications/C000001.md", "WORKER_CHANGED_RUNNER_STATE")]
    [InlineData(".idd/intent/current.md", "WORKER_CHANGED_PRODUCT_INTENT")]
    [InlineData(".idd/verification.yaml", "WORKER_CHANGED_PRODUCT_INTENT")]
    public async Task WorkerCannotMutateProtectedArtifacts(string path, string expectedCode)
    {
        using var temp = new TestWorkspace();
        PrepareProtectedArtifacts(temp);
        var invocation = PreparedInvocation(temp);
        var backend = new MutatingBackend(invocation, Path.Combine(temp.Path, path));

        var exception = await Assert.ThrowsAsync<AgentProtocolException>(() =>
            new FactoryAgentExecutor(backend, new FactoryAgentResultValidator()).ExecuteAsync(invocation, default));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Theory]
    [InlineData(".idd/factory.yaml", "WORKER_CHANGED_FACTORY_POLICY")]
    [InlineData(".idd/factory/current/state.json", "WORKER_CHANGED_RUNNER_STATE")]
    [InlineData(".idd/factory/current/request.md", "WORKER_CHANGED_RUNNER_STATE")]
    [InlineData(".idd/factory/current/run-context.md", "WORKER_CHANGED_RUNNER_STATE")]
    [InlineData(".idd/factory/current/work-items/item/contracts/000001.md", "WORKER_CHANGED_RUNNER_STATE")]
    [InlineData(".idd/factory/current/graph/mutations/G000000.json", "WORKER_CHANGED_RUNNER_STATE")]
    [InlineData(".idd/factory/current/clarifications/C000000.md", "WORKER_CHANGED_RUNNER_STATE")]
    [InlineData(".idd/intent/current.md", "WORKER_CHANGED_PRODUCT_INTENT")]
    [InlineData(".idd/verification.yaml", "WORKER_CHANGED_PRODUCT_INTENT")]
    public async Task WorkerCannotDeleteProtectedArtifacts(string path, string expectedCode)
    {
        using var temp = new TestWorkspace();
        PrepareProtectedArtifacts(temp);
        var invocation = PreparedInvocation(temp);
        var backend = new DeletingBackend(invocation, Path.Combine(temp.Path, path));

        var exception = await Assert.ThrowsAsync<AgentProtocolException>(() =>
            new FactoryAgentExecutor(backend, new FactoryAgentResultValidator()).ExecuteAsync(invocation, default));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public void InvocationContainsFreshBackendNeutralSemanticContract()
    {
        var invocation = Invocation("researcher");
        var json = JsonSerializer.Serialize(invocation, FactoryJson.Options);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("researcher", document.RootElement.GetProperty("role").GetString());
        Assert.Equal("idd-factory-research", document.RootElement.GetProperty("skillName").GetString());
        Assert.Equal("read-only", document.RootElement.GetProperty("executionProfile").GetString());
        Assert.False(document.RootElement.TryGetProperty("conversationHistory", out _));
        Assert.False(document.RootElement.TryGetProperty("nextWorkflowStep", out _));
    }

    private static AgentInvocation Invocation(string role)
    {
        var contract = role switch
        {
            "researcher" => FactoryCapabilityCatalog.ResolveWorkItem("research").Agent,
            "final-reviewer" => FactoryCapabilityCatalog.ResolveWorkItem("semantic-review").Agent,
            "task-decomposer" => FactoryCapabilityCatalog.Resolve("initial-decomposition").Agent,
            "factory-replanner" => FactoryCapabilityCatalog.Resolve("global-replan").Agent,
            _ => FactoryCapabilityCatalog.ResolveWorkItem("implementation").Agent
        };
        return new AgentInvocation
        {
            RunId = "run",
            AttemptId = "A000001",
            Role = role,
            Workspace = "workspace",
            ResultPath = "result.json",
            SkillName = contract.SkillName,
            ExecutionProfile = contract.ExecutionProfile,
            Input = "focused input",
            StartedAt = DateTimeOffset.UnixEpoch
        };
    }

    private static AgentInvocation PreparedInvocation(TestWorkspace temp)
    {
        var placeholder = temp.Write(".idd/factory/current/attempts/A000001/placeholder", "x");
        var source = Invocation("implementer");
        return source with
        {
            Workspace = temp.Path,
            ResultPath = Path.Combine(Path.GetDirectoryName(placeholder)!, "result.json")
        };
    }

    private static void PrepareProtectedArtifacts(TestWorkspace temp)
    {
        temp.Write(".idd/factory/current/state.json", "state");
        temp.Write(".idd/factory/current/request.md", "request");
        temp.Write(".idd/factory/current/run-context.md", "context");
        temp.Write(".idd/factory/current/work-items/item/contracts/000001.md", "contract");
        temp.Write(".idd/factory/current/graph/mutations/G000000.json", "history");
        temp.Write(".idd/factory/current/clarifications/C000000.md", "clarification");
        temp.Write(".idd/factory.yaml", "schemaVersion: 1");
        temp.Write(".idd/intent/current.md", "intent");
        temp.Write(".idd/verification.yaml", "version: 1");
    }

    private static AgentResultEnvelope Envelope(AgentInvocation invocation, string outcome) => new()
    {
        ProtocolVersion = AgentInvocation.CurrentProtocolVersion,
        RunId = invocation.RunId,
        AttemptId = invocation.AttemptId,
        Role = invocation.Role,
        Outcome = outcome
    };

    private sealed class MutatingBackend(AgentInvocation invocation, string path) : IAgentBackend
    {
        public Task<AgentRunHandle> StartAsync(AgentInvocation _, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "changed");
            File.WriteAllText(invocation.ResultPath, JsonSerializer.Serialize(Envelope(invocation, "completed"), FactoryJson.Options));
            return Task.FromResult(new AgentRunHandle(invocation.AttemptId, 1, invocation.AttemptId));
        }

        public Task<AgentProcessResult> WaitAsync(AgentRunHandle handle, CancellationToken cancellationToken) =>
            Task.FromResult(new AgentProcessResult(0, "", "", true, false, AgentTerminationKind.CleanExit));

        public Task CancelAsync(AgentRunHandle handle, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class DeletingBackend(AgentInvocation invocation, string path) : IAgentBackend
    {
        public Task<AgentRunHandle> StartAsync(AgentInvocation _, CancellationToken cancellationToken)
        {
            File.Delete(path);
            File.WriteAllText(invocation.ResultPath, JsonSerializer.Serialize(Envelope(invocation, "completed"), FactoryJson.Options));
            return Task.FromResult(new AgentRunHandle(invocation.AttemptId, 1, invocation.AttemptId));
        }

        public Task<AgentProcessResult> WaitAsync(AgentRunHandle handle, CancellationToken cancellationToken) =>
            Task.FromResult(new AgentProcessResult(0, "", "", true, false, AgentTerminationKind.CleanExit));

        public Task CancelAsync(AgentRunHandle handle, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
