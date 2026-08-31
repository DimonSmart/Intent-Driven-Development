using System.Text.Json;
using Idd.Factory.Agents;
using Idd.Factory.Configuration;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.Runtime;
using Idd.Factory.State;
using Idd.Factory.Telemetry;
using Idd.Factory.Verification;

namespace Idd.Factory.Tests;

public sealed class DynamicTaskGraphRuntimeTests
{
    [Fact]
    public async Task PartialDecompositionRefinesOutlineOnlyAfterDependencyCompletes()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(invocation => Envelope(invocation, "ready", new
        {
            workItems = new object[]
            {
                Work("A", "implementation"),
                new { id = "B", sequence = 2, kind = "subtask", definitionState = "outline", contractMarkdown = "# B outline", dependencies = new[] { "A" } }
            }
        }));
        backend.Enqueue(invocation => Envelope(invocation, "completed"));
        backend.Enqueue(invocation => Envelope(invocation, "ready", new
        {
            workItems = new[] { Work("B", "research", 2, new[] { "A" }, "# B executable") }
        }));
        backend.Enqueue(invocation => Envelope(invocation, "completed"));
        backend.Enqueue(invocation => Envelope(invocation, "approved"));

        var outcome = await CreateRuntime(temp.Path, backend).RunRequestAsync("Implement partial task", "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal(new[] { "task-decomposer", "implementer", "task-decomposer", "researcher", "final-reviewer" }, backend.Invocations.Select(x => x.Role));
        Assert.Equal("A", backend.Invocations[1].WorkItemId);
        Assert.Equal("B", backend.Invocations[2].WorkItemId);
        Assert.Contains("Completed prerequisite results", backend.Invocations[2].Input);
        Assert.Contains("A", backend.Invocations[2].Input);
    }

    [Fact]
    public async Task AdditionalResearchBecomesLocalDependencyAndDoesNotInvokeGlobalReplanner()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(invocation => Envelope(invocation, "ready", new { workItems = new[] { Work("A", "implementation") } }));
        backend.Enqueue(invocation => Envelope(invocation, "additional-work-required", new
        {
            requirement = new
            {
                capability = "research",
                goal = "Determine the repository constraint",
                reason = "Implementation depends on repository evidence",
                context = "Inspect the existing integration",
                expectedOutput = "A concrete decision"
            }
        }));
        backend.Enqueue(invocation => Envelope(invocation, "completed", new { finding = "use existing contract" }));
        backend.Enqueue(invocation => Envelope(invocation, "completed"));
        backend.Enqueue(invocation => Envelope(invocation, "approved"));

        var outcome = await CreateRuntime(temp.Path, backend).RunRequestAsync("Implement after focused research", "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal(2, backend.Invocations.Count(x => x.Role == "implementer"));
        Assert.Single(backend.Invocations, x => x.Role == "researcher");
        Assert.DoesNotContain(backend.Invocations, x => x.Role == "factory-replanner");
        var secondImplementation = backend.Invocations.Where(x => x.Role == "implementer").Last();
        Assert.Contains("R-001", secondImplementation.Input);
        Assert.Contains("use existing contract", secondImplementation.Input);
    }

    [Fact]
    public async Task UnknownDynamicCapabilityIsRejectedWithoutGraphMutation()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(invocation => Envelope(invocation, "ready", new { workItems = new[] { Work("A", "implementation") } }));
        backend.Enqueue(invocation => Envelope(invocation, "additional-work-required", new
        {
            requirement = new { capability = "mystery", goal = "Unknown", reason = "Should reject" }
        }));
        var runtime = CreateRuntime(temp.Path, backend);

        var outcome = await runtime.RunRequestAsync("Reject unknown capability", "test", default);

        Assert.Equal("UNKNOWN_CAPABILITY", outcome.FactoryOutcome);
        var state = await LoadState(temp.Path);
        Assert.Equal(1, state.GraphRevision);
        Assert.Single(state.WorkItems);
        Assert.Equal("A", state.WorkItems[0].Id);
    }

    [Fact]
    public async Task DisallowedDynamicCapabilityIsRejectedWithoutGraphMutation()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(invocation => Envelope(invocation, "ready", new { workItems = new[] { Work("A", "implementation") } }));
        backend.Enqueue(invocation => Envelope(invocation, "additional-work-required", new
        {
            requirement = new { capability = "research", goal = "Research", reason = "Should be blocked by policy" }
        }));
        var runtime = CreateRuntime(temp.Path, backend, ["implementation", "semantic-review"]);

        var outcome = await runtime.RunRequestAsync("Reject disallowed capability", "test", default);

        Assert.Equal("CAPABILITY_NOT_ALLOWED", outcome.FactoryOutcome);
        var state = await LoadState(temp.Path);
        Assert.Equal(1, state.GraphRevision);
        Assert.Single(state.WorkItems);
    }

    [Fact]
    public async Task InvalidGlobalReplanCycleIsAtomic()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(invocation => Envelope(invocation, "ready", new { workItems = new[] { Work("A", "implementation") } }));
        backend.Enqueue(invocation => Envelope(invocation, "global-replan-required", new { reason = "global strategy changed" }));
        backend.Enqueue(invocation => Envelope(invocation, "replan-proposed", new
        {
            operations = new object[]
            {
                new { kind = "add-work", workItem = Work("B", "implementation", 2, new[] { "A" }) },
                new { kind = "change-dependencies", id = "A", dependencies = new[] { "B" } }
            }
        }));

        var outcome = await CreateRuntime(temp.Path, backend).RunRequestAsync("Reject cyclic replan", "test", default);

        Assert.Equal("INVALID_GRAPH_MUTATION", outcome.FactoryOutcome);
        var state = await LoadState(temp.Path);
        Assert.Equal(1, state.GraphRevision);
        Assert.Single(state.WorkItems);
        var contracts = Directory.GetFiles(Path.Combine(temp.Path, ".idd", "factory", "current", "work-items"), "*.md", SearchOption.AllDirectories);
        Assert.Single(contracts);
        var mutations = Directory.GetFiles(Path.Combine(temp.Path, ".idd", "factory", "current", "graph", "mutations"), "*.json");
        Assert.Single(mutations);
    }

    [Fact]
    public async Task ExpectedRedCompletesWorkWithoutSemanticClassifier()
    {
        using var temp = new TestWorkspace();
        temp.Write(".idd/verification.yaml", """
            version: 1
            checks:
              expected-red:
                run: dotnet build definitely-missing.csproj --nologo
            default:
              use: []
            final:
              use: []
            """);
        var backend = new FakeAgentBackend();
        backend.Enqueue(invocation => Envelope(invocation, "ready", new
        {
            workItems = new[]
            {
                new
                {
                    id = "A", sequence = 1, kind = "subtask", definitionState = "executable", capability = "implementation",
                    contractMarkdown = "# A", dependencies = Array.Empty<string>(), verificationCheckIds = new[] { "expected-red" },
                    verificationExpectations = new Dictionary<string, string> { ["expected-red"] = "may-fail" }
                }
            }
        }));
        backend.Enqueue(invocation => Envelope(invocation, "completed"));
        backend.Enqueue(invocation =>
        {
            var state = JsonSerializer.Deserialize<FactoryState>(File.ReadAllText(Path.Combine(temp.Path, ".idd", "factory", "current", "state.json")), FactoryJson.Options)!;
            var work = state.WorkItems.Single(x => x.Id == "A");
            Assert.Equal(WorkItemStatus.Completed, work.Status);
            Assert.Equal(VerificationDecision.ExpectedFailure, work.LastVerificationDecision);
            return Envelope(invocation, "approved");
        });

        var outcome = await CreateRuntime(temp.Path, backend).RunRequestAsync("Expected red is intentional", "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal(3, backend.Invocations.Count);
    }

    [Fact]
    public async Task UnexpectedRegressionBlocksIntermediateWork()
    {
        using var temp = new TestWorkspace();
        temp.Write(".idd/verification.yaml", """
            version: 1
            checks:
              expected-red:
                run: dotnet build definitely-missing-a.csproj --nologo
              regression:
                run: dotnet build definitely-missing-b.csproj --nologo
            default:
              use: []
            final:
              use: []
            """);
        var backend = new FakeAgentBackend();
        backend.Enqueue(invocation => Envelope(invocation, "ready", new
        {
            workItems = new[]
            {
                new
                {
                    id = "A", sequence = 1, kind = "subtask", definitionState = "executable", capability = "implementation",
                    contractMarkdown = "# A", dependencies = Array.Empty<string>(), verificationCheckIds = new[] { "expected-red", "regression" },
                    verificationExpectations = new Dictionary<string, string> { ["expected-red"] = "may-fail" }
                }
            }
        }));
        backend.Enqueue(invocation => Envelope(invocation, "completed"));

        var outcome = await CreateRuntime(temp.Path, backend).RunRequestAsync("Unexpected red must block", "test", default);

        Assert.Equal("UNEXPECTED_VERIFICATION_FAILURE", outcome.FactoryOutcome);
        var state = await LoadState(temp.Path);
        Assert.Equal(VerificationDecision.UnexpectedFailure, state.WorkItems.Single().LastVerificationDecision);
        Assert.Equal(WorkItemStatus.Blocked, state.WorkItems.Single().Status);
    }

    [Fact]
    public async Task MayFailExpectationNeverWeakensFinalVerification()
    {
        using var temp = new TestWorkspace();
        temp.Write(".idd/verification.yaml", """
            version: 1
            checks:
              red:
                run: dotnet build definitely-missing.csproj --nologo
            default:
              use: []
            final:
              use:
                - red
            """);
        var backend = new FakeAgentBackend();
        backend.Enqueue(invocation => Envelope(invocation, "ready", new
        {
            workItems = new[]
            {
                new
                {
                    id = "A", sequence = 1, kind = "subtask", definitionState = "executable", capability = "implementation",
                    contractMarkdown = "# A", dependencies = Array.Empty<string>(), verificationCheckIds = new[] { "red" },
                    verificationExpectations = new Dictionary<string, string> { ["red"] = "may-fail" }
                }
            }
        }));
        backend.Enqueue(invocation => Envelope(invocation, "completed"));

        var outcome = await CreateRuntime(temp.Path, backend).RunRequestAsync("Final verification stays strict", "test", default);

        Assert.Equal("UNEXPECTED_VERIFICATION_FAILURE", outcome.FactoryOutcome);
        Assert.DoesNotContain(backend.Invocations, x => x.Role == "final-reviewer");
        var state = await LoadState(temp.Path);
        Assert.False(state.FinalVerificationPassed);
    }

    [Fact]
    public async Task FinalReviewCorrectionCreatesNewWorkAndNewReviewWithoutMutatingOldReview()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(invocation => Envelope(invocation, "ready", new { workItems = new[] { Work("A", "implementation") } }));
        backend.Enqueue(invocation => Envelope(invocation, "completed"));
        backend.Enqueue(invocation => Envelope(invocation, "correction-required", new
        {
            correction = new { capability = "implementation", contractMarkdown = "# Correct the integrated defect" }
        }, "Integrated defect remains."));
        backend.Enqueue(invocation => Envelope(invocation, "completed"));
        backend.Enqueue(invocation => Envelope(invocation, "approved"));

        var outcome = await CreateRuntime(temp.Path, backend).RunRequestAsync("Review and correct", "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        var reviews = backend.Invocations.Where(x => x.Role == "final-reviewer").ToArray();
        Assert.Equal(2, reviews.Length);
        Assert.NotEqual(reviews[0].WorkItemId, reviews[1].WorkItemId);
        var graph = JsonDocument.Parse(File.ReadAllText(Path.Combine(outcome.ResultDirectory!, "decomposition", "decomposition.json")));
        var items = graph.RootElement.GetProperty("workItems").EnumerateArray().ToArray();
        Assert.Contains(items, x => x.GetProperty("id").GetString() == reviews[0].WorkItemId && x.GetProperty("status").GetString() == "completed");
        Assert.Contains(items, x => x.GetProperty("kind").GetString() == "corrective-subtask");
        Assert.Contains(items, x => x.GetProperty("id").GetString() == reviews[1].WorkItemId && x.GetProperty("status").GetString() == "completed");
    }

    [Fact]
    public async Task OrphanGraphMutationHistoryDoesNotInfluenceRecovery()
    {
        using var temp = new TestWorkspace();
        var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        Directory.CreateDirectory(Path.Combine(current, "graph", "mutations"));
        temp.Write(".idd/factory/current/request.md", "Resume from authoritative state");
        temp.Write(".idd/factory/current/work-items/A/contracts/000001.md", "# A");
        temp.Write(".idd/factory/current/graph/mutations/orphan.json", "{\"fromGraphRevision\":1,\"toGraphRevision\":99}");
        var state = StateStoreTests.State() with { RunId = "resume", GraphRevision = 1, FactoryConfigurationHash = Configuration().Hash };
        state.WorkItems.Add(StateStoreTests.Executable("A", WorkItemStatus.Ready));
        await new FileFactoryStateStore(current, new FactoryStateValidator()).CreateAsync(state, default);
        var backend = new FakeAgentBackend();
        backend.Enqueue(invocation => Envelope(invocation, "completed"));
        backend.Enqueue(invocation => Envelope(invocation, "approved"));

        var outcome = await CreateRuntime(temp.Path, backend).ContinueAsync(default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal("A", backend.Invocations.First().WorkItemId);
    }

    private static FactoryRuntime CreateRuntime(string workspace, FakeAgentBackend backend, IEnumerable<string>? allowed = null)
    {
        var current = Path.Combine(workspace, ".idd", "factory", "current");
        var clock = new FakeClock();
        var configuration = Configuration(allowed);
        return new FactoryRuntime(
            workspace,
            configuration,
            new FileFactoryStateStore(current, new FactoryStateValidator()),
            new FactoryAgentExecutor(backend, new FactoryAgentResultValidator()),
            new VerificationEngine(workspace, current),
            new FactoryEventWriter(current, clock),
            clock);
    }

    private static FactoryConfiguration Configuration(IEnumerable<string>? allowed = null) => new(
        1,
        new FactoryLimits(4, 3, 5, 64),
        new FinalReviewPolicy(true),
        (allowed ?? new[] { "implementation", "research", "semantic-review", "documentation" }).ToHashSet(StringComparer.Ordinal),
        "test-factory.yaml",
        "test-config-hash");

    private static async Task<FactoryState> LoadState(string workspace) =>
        (await new FileFactoryStateStore(Path.Combine(workspace, ".idd", "factory", "current"), new FactoryStateValidator()).LoadAsync(default))!;

    private static object Work(string id, string capability, int sequence = 1, string[]? dependencies = null, string? contract = null) => new
    {
        id,
        sequence,
        kind = "subtask",
        definitionState = "executable",
        capability,
        contractMarkdown = contract ?? $"# {id}",
        dependencies = dependencies ?? Array.Empty<string>(),
        verificationCheckIds = Array.Empty<string>()
    };

    private static AgentResultEnvelope Envelope(AgentInvocation invocation, string outcome, object? payload = null, string? reason = null) => new()
    {
        ProtocolVersion = AgentInvocation.CurrentProtocolVersion,
        RunId = invocation.RunId,
        AttemptId = invocation.AttemptId,
        Role = invocation.Role,
        Outcome = outcome,
        Reason = reason,
        Payload = payload is null ? null : JsonSerializer.SerializeToElement(payload, FactoryJson.Options)
    };

    private sealed class FakeAgentBackend : IAgentBackend
    {
        private readonly Queue<Func<AgentInvocation, AgentResultEnvelope>> results = new();
        private readonly Dictionary<string, AgentInvocation> active = new(StringComparer.Ordinal);
        public List<AgentInvocation> Invocations { get; } = [];

        public void Enqueue(Func<AgentInvocation, AgentResultEnvelope> result) => results.Enqueue(result);

        public Task<AgentRunHandle> StartAsync(AgentInvocation invocation, CancellationToken cancellationToken)
        {
            if (results.Count == 0) throw new InvalidOperationException($"No fake result queued for {invocation.Role}/{invocation.WorkItemId}.");
            Invocations.Add(invocation);
            active.Add(invocation.AttemptId, invocation);
            Directory.CreateDirectory(Path.GetDirectoryName(invocation.ResultPath)!);
            var result = results.Dequeue()(invocation);
            File.WriteAllText(invocation.ResultPath, JsonSerializer.Serialize(result, FactoryJson.Options));
            return Task.FromResult(new AgentRunHandle(invocation.AttemptId, 1, invocation.AttemptId));
        }

        public Task<AgentProcessResult> WaitAsync(AgentRunHandle handle, CancellationToken cancellationToken)
        {
            active.Remove(handle.BackendHandle);
            return Task.FromResult(new AgentProcessResult(0, "", "", true, false, AgentTerminationKind.CleanExit));
        }

        public Task CancelAsync(AgentRunHandle handle, CancellationToken cancellationToken)
        {
            active.Remove(handle.BackendHandle);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeClock : IClock
    {
        private DateTimeOffset now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        public DateTimeOffset UtcNow => now = now.AddMilliseconds(1);
    }
}
