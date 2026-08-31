using Idd.Factory.Domain;
using static Idd.Factory.Tests.FactoryRuntimeTestHarness;

namespace Idd.Factory.Tests;

public sealed class DynamicGraphExecutionTests
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
        backend.Enqueue(invocation => Envelope(invocation, "ready", new { workItems = new[] { Work("B", "research", 2, new[] { "A" }, "# B executable") } }));
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

        var outcome = await CreateRuntime(temp.Path, backend).RunRequestAsync("Reject unknown capability", "test", default);

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

        var outcome = await CreateRuntime(temp.Path, backend, ["implementation", "semantic-review"])
            .RunRequestAsync("Reject disallowed capability", "test", default);

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
}
