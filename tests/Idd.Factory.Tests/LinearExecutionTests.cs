using Idd.Factory.Domain;
using static Idd.Factory.Tests.FactoryRuntimeTestHarness;

namespace Idd.Factory.Tests;

public sealed class LinearExecutionTests
{
    [Fact]
    public async Task InitialPlanExecutesStrictlyInReturnedOrder()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = new[] { Work("A", "research"), Work("B", "research"), Work("C", "research") } }));
        backend.Enqueue(x => Envelope(x, "completed", new { finding = "A" }));
        backend.Enqueue(x => Envelope(x, "completed", new { finding = "B" }));
        backend.Enqueue(x => Envelope(x, "completed", new { finding = "C" }));
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = Array.Empty<object>() }));
        backend.Enqueue(x => Envelope(x, "approved"));

        var outcome = await CreateRuntime(temp.Path, backend).RunRequestAsync("Execute A, B, C", "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal(new[] { "W000001", "W000002", "W000003" }, backend.Invocations.Where(x => x.Role == "researcher").Select(x => x.WorkItemId));
        using var completed = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(outcome.ResultDirectory!, "completed-work.json")));
        Assert.Equal(new[] { "W000001", "W000002", "W000003" }, completed.RootElement.GetProperty("completed").EnumerateArray().Select(x => x.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task AdditionalWorkIsPrependedBeforeInterruptedTaskAndOldTail()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = new[] { Work("B", "research"), Work("C", "research"), Work("D", "research") } }));
        backend.Enqueue(x => Envelope(x, "additional-work-required", new { capability = "research", task = "X", reason = "X is required first" }));
        backend.Enqueue(x => Envelope(x, "completed", new { finding = "X" }));
        backend.Enqueue(x => Envelope(x, "completed", new { finding = "B" }));
        backend.Enqueue(x => Envelope(x, "completed", new { finding = "C" }));
        backend.Enqueue(x => Envelope(x, "completed", new { finding = "D" }));
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = Array.Empty<object>() }));
        backend.Enqueue(x => Envelope(x, "approved"));

        var outcome = await CreateRuntime(temp.Path, backend).RunRequestAsync("Dynamic prerequisite", "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal(new[] { "W000001", "W000004", "W000001", "W000002", "W000003" }, backend.Invocations.Where(x => x.Role == "researcher").Select(x => x.WorkItemId));
        Assert.True(Directory.GetFiles(Path.Combine(outcome.ResultDirectory!, "plan-revisions"), "*.json").Length >= 2);
    }

    [Fact]
    public async Task ReplanReplacesOnlyFutureSuffixAndKeepsCompletedHistory()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = new[] { Work("A", "research"), Work("B", "research"), Work("C", "research") } }));
        backend.Enqueue(x => Envelope(x, "completed"));
        backend.Enqueue(x => Envelope(x, "completed"));
        backend.Enqueue(x => Envelope(x, "global-replan-required", new { reason = "strategy changed" }));
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = new[] { Work("X", "research"), Work("Y", "research") } }));
        backend.Enqueue(x => Envelope(x, "completed"));
        backend.Enqueue(x => Envelope(x, "completed"));
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = Array.Empty<object>() }));
        backend.Enqueue(x => Envelope(x, "approved"));

        var outcome = await CreateRuntime(temp.Path, backend).RunRequestAsync("Replace future strategy", "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        using var completed = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(outcome.ResultDirectory!, "completed-work.json")));
        Assert.Equal(new[] { "W000001", "W000002", "W000004", "W000005" }, completed.RootElement.GetProperty("completed").EnumerateArray().Select(x => x.GetProperty("id").GetString()));
        Assert.Equal(3, backend.Invocations.Count(x => x.Role == "task-decomposer"));
    }

    [Fact]
    public async Task UnknownAdditionalCapabilityLeavesCurrentAndRemainingAuthoritative()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = new[] { Work("A", "research"), Work("B", "research") } }));
        backend.Enqueue(x => Envelope(x, "additional-work-required", new { capability = "mystery", task = "X", reason = "X is required first" }));

        var outcome = await CreateRuntime(temp.Path, backend).RunRequestAsync("Reject malformed expansion", "test", default);

        Assert.Equal("UNKNOWN_CAPABILITY", outcome.FactoryOutcome);
        var state = await LoadState(temp.Path);
        Assert.Equal("W000001", state.Current!.Id);
        Assert.Equal(new[] { "W000002" }, state.Remaining.Select(x => x.Id));
        Assert.Empty(state.Completed);
    }
}
