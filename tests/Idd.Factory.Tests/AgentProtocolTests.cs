using System.Text.Json;
using Idd.Factory.Agents;
using Idd.Factory.Domain;

namespace Idd.Factory.Tests;

public sealed class AgentProtocolTests
{
    [Fact]
    public void PlanningResultIsCompleteWithOnlyOutcomeAndFlatTasksAsSemanticBody()
    {
        var invocation = Invocation("task-decomposer");
        var result = FactoryRuntimeTestHarness.Envelope(invocation, "ready", new
        {
            tasks = new object[]
            {
                new { capability = "implementation", task = "Do A" },
                new { capability = "research", task = "Investigate B" }
            }
        });
        var json = JsonSerializer.SerializeToElement(result, FactoryJson.Options);

        Assert.Equal("ready", json.GetProperty("outcome").GetString());
        Assert.Equal(2, json.GetProperty("tasks").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("payload").ValueKind);
        Assert.False(json.GetProperty("tasks")[0].TryGetProperty("id", out _));
        Assert.False(json.GetProperty("tasks")[0].TryGetProperty("dependencies", out _));
    }

    [Fact]
    public void ImplementationCompletedAcceptsDocumentedSemanticFields()
    {
        var invocation = Invocation("implementer");
        const string json = """
        {
          "outcome": "completed",
          "summary": "Implemented requested feature.",
          "declaredChanges": ["Added implementation", "Added tests"],
          "concerns": [],
          "verificationClaims": ["Tests were added"]
        }
        """;

        var result = new FactoryAgentResultValidator().ParseAndValidate(invocation, json);

        Assert.Equal("completed", result.Outcome);
        Assert.Equal("Implemented requested feature.", result.Summary);
        Assert.Equal(2, result.DeclaredChanges?.Count);
        Assert.Empty(result.Concerns!);
        Assert.Single(result.VerificationClaims!);
    }

    [Fact]
    public void RuntimeOwnedSemanticFieldIsRejected()
    {
        var invocation = Invocation("implementer");
        const string json = """
        {
          "outcome": "completed",
          "summary": "Done",
          "attemptId": "A000123"
        }
        """;

        var exception = Assert.Throws<AgentProtocolException>(() =>
            new FactoryAgentResultValidator().ParseAndValidate(invocation, json));

        Assert.Equal("MALFORMED_AGENT_RESULT", exception.Code);
        Assert.Contains("attemptId", exception.Message);
        Assert.Contains("implementation-v1", exception.Message);
    }

    [Fact]
    public void ImplementationCompletedRequiresSummary()
    {
        var invocation = Invocation("implementer");

        var exception = Assert.Throws<AgentProtocolException>(() =>
            new FactoryAgentResultValidator().ParseAndValidate(invocation, "{\"outcome\":\"completed\"}"));

        Assert.Equal("MALFORMED_AGENT_RESULT", exception.Code);
        Assert.Contains("summary", exception.Message);
    }

    [Fact]
    public void OutcomeSpecificFieldsDoNotLeakAcrossOutcomes()
    {
        var invocation = Invocation("implementer");
        const string json = """
        {
          "outcome": "additional-work-required",
          "summary": "Not valid for this outcome",
          "payload": {
            "capability": "research",
            "task": "Investigate",
            "reason": "Need evidence"
          }
        }
        """;

        var exception = Assert.Throws<AgentProtocolException>(() =>
            new FactoryAgentResultValidator().ParseAndValidate(invocation, json));

        Assert.Equal("MALFORMED_AGENT_RESULT", exception.Code);
        Assert.Contains("summary", exception.Message);
    }

    [Fact]
    public void UnknownSemanticResultSchemaIsRejected()
    {
        var invocation = Invocation("implementer") with { SemanticResultSchema = "implementation-v99" };

        var exception = Assert.Throws<AgentProtocolException>(() =>
            new FactoryAgentResultValidator().ParseAndValidate(invocation, "{\"outcome\":\"completed\",\"summary\":\"Done\"}"));

        Assert.Equal("UNSUPPORTED_SEMANTIC_RESULT_SCHEMA", exception.Code);
    }

    [Fact]
    public void SemanticResultSchemaCannotBeBorrowedFromAnotherCapability()
    {
        var invocation = Invocation("implementer") with { SemanticResultSchema = "research-v1" };

        var exception = Assert.Throws<AgentProtocolException>(() =>
            new FactoryAgentResultValidator().ParseAndValidate(invocation, "{\"outcome\":\"completed\",\"summary\":\"Done\"}"));

        Assert.Equal("UNSUPPORTED_SEMANTIC_RESULT_SCHEMA", exception.Code);
    }

    [Theory]
    [InlineData("implementer", "completed")]
    [InlineData("implementer", "additional-work-required")]
    [InlineData("implementer", "global-replan-required")]
    [InlineData("researcher", "completed")]
    [InlineData("researcher", "additional-work-required")]
    [InlineData("final-reviewer", "approved")]
    [InlineData("final-reviewer", "correction-required")]
    [InlineData("final-reviewer", "additional-work-required")]
    [InlineData("final-reviewer", "global-replan-required")]
    [InlineData("task-decomposer", "ready")]
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
    [InlineData("semantic-review", "checkpoint-reviewer", "idd-factory-review-checkpoint", AgentExecutionProfile.ReadOnly)]
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
    [InlineData(".idd/factory/current/work-items/W000001/contract.md", "WORKER_CHANGED_RUNNER_STATE")]
    [InlineData(".idd/factory/current/plan-revisions/P000001.json", "WORKER_CHANGED_RUNNER_STATE")]
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
    [InlineData(".idd/factory/current/work-items/W000001/contract.md", "WORKER_CHANGED_RUNNER_STATE")]
    [InlineData(".idd/factory/current/plan-revisions/P000001.json", "WORKER_CHANGED_RUNNER_STATE")]
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
        Assert.Equal("research-v1", document.RootElement.GetProperty("semanticResultSchema").GetString());
        Assert.False(document.RootElement.TryGetProperty("conversationHistory", out _));
        Assert.False(document.RootElement.TryGetProperty("nextWorkflowStep", out _));
    }

    private static AgentInvocation Invocation(string role)
    {
        var contract = role switch
        {
            "researcher" => FactoryCapabilityCatalog.ResolveWorkItem("research").Agent,
            "final-reviewer" => FactoryCapabilityCatalog.Resolve("final-review").Agent,
            "task-decomposer" => FactoryCapabilityCatalog.Resolve("planning").Agent,
            _ => FactoryCapabilityCatalog.ResolveWorkItem("implementation").Agent
        };
        var capability = role switch { "researcher" => "research", "final-reviewer" => "final-review", "task-decomposer" => "planning", _ => "implementation" };
        return new AgentInvocation
        {
            RunId = "run",
            AttemptId = "A000001",
            Capability = capability,
            Role = role,
            Workspace = "workspace",
            RawResultPath = "raw-result.json",
            SkillName = contract.SkillName,
            ExecutionProfile = contract.ExecutionProfile,
            SemanticResultSchema = SemanticResultContracts.SchemaForCapability(capability),
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
            RawResultPath = Path.Combine(Path.GetDirectoryName(placeholder)!, "raw-result.json")
        };
    }

    private static void PrepareProtectedArtifacts(TestWorkspace temp)
    {
        temp.Write(".idd/factory/current/state.json", "state");
        temp.Write(".idd/factory/current/request.md", "request");
        temp.Write(".idd/factory/current/run-context.md", "context");
        temp.Write(".idd/factory/current/work-items/W000001/contract.md", "contract");
        temp.Write(".idd/factory/current/plan-revisions/P000001.json", "history");
        temp.Write(".idd/factory/current/clarifications/C000000.md", "clarification");
        temp.Write(".idd/factory.yaml", "schemaVersion: 1");
        temp.Write(".idd/intent/current.md", "intent");
        temp.Write(".idd/verification.yaml", "version: 1");
    }

    private static SemanticAgentResult Envelope(AgentInvocation invocation, string outcome)
    {
        var futureTask = JsonSerializer.SerializeToElement(new { capability = "research", task = "Investigate", reason = "Evidence is required." }, FactoryJson.Options);
        return new SemanticAgentResult
        {
            Outcome = outcome,
            Summary = outcome == "completed" ? $"Completed {invocation.Capability} work." : null,
            Tasks = outcome == "ready" ? JsonSerializer.SerializeToElement(new[] { new { capability = "research", task = "Investigate" } }, FactoryJson.Options) : null,
            Payload = outcome is "additional-work-required" or "correction-required" ? futureTask : null
        };
    }

    private sealed class MutatingBackend(AgentInvocation invocation, string path) : IAgentBackend
    {
        public Task<AgentRunHandle> StartAsync(AgentInvocation _, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "changed");
            File.WriteAllText(invocation.RawResultPath, JsonSerializer.Serialize(Envelope(invocation, "completed"), FactoryJson.Options));
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
            File.WriteAllText(invocation.RawResultPath, JsonSerializer.Serialize(Envelope(invocation, "completed"), FactoryJson.Options));
            return Task.FromResult(new AgentRunHandle(invocation.AttemptId, 1, invocation.AttemptId));
        }

        public Task<AgentProcessResult> WaitAsync(AgentRunHandle handle, CancellationToken cancellationToken) =>
            Task.FromResult(new AgentProcessResult(0, "", "", true, false, AgentTerminationKind.CleanExit));

        public Task CancelAsync(AgentRunHandle handle, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
