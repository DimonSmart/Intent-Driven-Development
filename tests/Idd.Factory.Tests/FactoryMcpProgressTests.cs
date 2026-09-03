using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Runtime;
using ModelContextProtocol;

namespace Idd.Factory.Tests;

public sealed class FactoryMcpProgressTests
{
    [Fact]
    public async Task ProgressSequenceIsMonotonicAndDoesNotSetTotal()
    {
        using var temp = new TestWorkspace();
        var statusReader = new FactoryStatusReader();
        var tools = new FactoryMcpTools(
            new FactoryRuntimeProcessRunner(new ImmediateInvoker("COMPLETED")),
            statusReader,
            new FactoryMcpProgressMonitor(statusReader));
        var progress = new RecordingProgress();

        var result = await tools.FactoryRunAsync(temp.Path, "test request", progress, CancellationToken.None);

        Assert.Equal("COMPLETED", result.FactoryOutcome);
        Assert.True(progress.Values.Count >= 2);
        for (var index = 0; index < progress.Values.Count; index++)
        {
            Assert.Equal(index + 1d, Convert.ToDouble(progress.Values[index].Progress));
            Assert.Null(progress.Values[index].Total);
        }
        Assert.Equal("Factory completed", progress.Values[^1].Message);
    }

    [Fact]
    public async Task ExistingEventsAreNotReplayed()
    {
        using var temp = new TestWorkspace();
        var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        Directory.CreateDirectory(current);
        var events = Path.Combine(current, "events.jsonl");
        await File.WriteAllLinesAsync(events, [Event("run-created", new { }), Event("agent-completed", new { })]);
        var monitor = new FactoryMcpProgressMonitor(new FactoryStatusReader());
        var baseline = monitor.CaptureExistingEventCount(temp.Path);
        await File.AppendAllTextAsync(events, Event("scheduler-decision", new { Kind = FactoryCommandKind.RunFinalVerification }) + Environment.NewLine);

        var batch = await FactoryMcpProgressMonitor.ReadNewEventLinesAsync(temp.Path, baseline, CancellationToken.None);

        Assert.Single(batch.Lines);
        Assert.Equal(3, batch.NextIndex);
        Assert.Contains("scheduler-decision", batch.Lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentDispatchIncludesBoundedSingleLineSubject()
    {
        using var temp = new TestWorkspace();
        var attemptDirectory = Path.Combine(temp.Path, ".idd", "factory", "current", "attempts", "A000007");
        Directory.CreateDirectory(attemptDirectory);
        var input = "Work item contract:\nImplement locked-file diagnostics\nwith   multiple\tspaces " + new string('x', 300) + "\n\nRelevant completed work:";
        await File.WriteAllTextAsync(
            Path.Combine(attemptDirectory, "invocation.json"),
            JsonSerializer.Serialize(new { input, workItemId = "W000003" }));
        var line = Event("agent-dispatching", new
        {
            attemptId = "A000007",
            capability = "implementation",
            workItemId = "W000003"
        });

        var message = await FactoryMcpProgressMonitor.ProjectEventAsync(temp.Path, line);

        Assert.NotNull(message);
        Assert.StartsWith("W000003 implementation A000007: \"Implement locked-file diagnostics", message, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', message!);
        Assert.DoesNotContain('\r', message!);
        Assert.DoesNotContain('\t', message!);
        Assert.True(message!.Length <= 160);
        Assert.Contains('…', message);
    }

    [Fact]
    public async Task PlanningAndReplanningAreDistinct()
    {
        using var temp = new TestWorkspace();
        var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        Directory.CreateDirectory(current);
        var line = Event("scheduler-decision", new { Kind = FactoryCommandKind.Plan });

        Assert.Equal("Planning", await FactoryMcpProgressMonitor.ProjectEventAsync(temp.Path, line));

        await File.WriteAllTextAsync(Path.Combine(current, "state.json"), "{\"planningCycleCount\":1}");
        Assert.Equal("Replanning", await FactoryMcpProgressMonitor.ProjectEventAsync(temp.Path, line));
    }

    [Theory]
    [InlineData(FactoryCommandKind.RunVerification, "W000003", "Verifying W000003")]
    [InlineData(FactoryCommandKind.RunFinalVerification, null, "Final verification")]
    [InlineData(FactoryCommandKind.Finalize, null, "Finalizing")]
    public async Task SchedulerEventsProjectHighLevelActivity(FactoryCommandKind kind, string? workItemId, string expected)
    {
        using var temp = new TestWorkspace();
        var line = Event("scheduler-decision", new { Kind = kind, WorkItemId = workItemId });

        Assert.Equal(expected, await FactoryMcpProgressMonitor.ProjectEventAsync(temp.Path, line));
    }

    [Theory]
    [InlineData(VerificationDecision.Ok, "W000003 verification passed")]
    [InlineData(VerificationDecision.ExpectedFailure, "W000003 verification completed with expected failure")]
    [InlineData(VerificationDecision.UnexpectedFailure, "W000003 verification failed; retrying")]
    public async Task VerificationDecisionIsShortAndDoesNotExposeOutput(VerificationDecision decision, string expected)
    {
        using var temp = new TestWorkspace();
        var line = Event("verification-decision", new
        {
            context = "subtask",
            workItemId = "W000003",
            decision,
            stdout = new string('z', 1000)
        });

        var message = await FactoryMcpProgressMonitor.ProjectEventAsync(temp.Path, line);

        Assert.Equal(expected, message);
        Assert.DoesNotContain("zzz", message!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentCompletionUsesInvocationWithoutSemanticResultPayload()
    {
        using var temp = new TestWorkspace();
        var attemptDirectory = Path.Combine(temp.Path, ".idd", "factory", "current", "attempts", "A000008");
        Directory.CreateDirectory(attemptDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(attemptDirectory, "invocation.json"),
            JsonSerializer.Serialize(new { input = "Work item contract:\nFix verification", workItemId = "W000003" }));
        var line = Event("agent-completed", new
        {
            attemptId = "A000008",
            capability = "implementation",
            semanticResult = new string('s', 1000)
        });

        var message = await FactoryMcpProgressMonitor.ProjectEventAsync(temp.Path, line);

        Assert.Equal("W000003 implementation completed", message);
        Assert.DoesNotContain("sss", message!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedEventsAndMissingCurrentAreBestEffort()
    {
        using var temp = new TestWorkspace();

        Assert.Null(await FactoryMcpProgressMonitor.ProjectEventAsync(temp.Path, "{not-json"));
        var batch = await FactoryMcpProgressMonitor.ReadNewEventLinesAsync(temp.Path, 10, CancellationToken.None);
        Assert.Empty(batch.Lines);
        Assert.Equal(10, batch.NextIndex);
    }

    [Fact]
    public void HeartbeatUsesActiveWorkAndElapsedTime()
    {
        var startedAt = new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);
        var status = new FactoryStatusResult
        {
            Status = "ACTIVE",
            RunId = "run-1",
            CurrentWorkItemId = "W000003",
            CurrentAttemptId = "A000007",
            CurrentPhase = "Running",
            RuntimeStartedAt = startedAt
        };

        var message = FactoryMcpProgressMonitor.FormatHeartbeat(status, startedAt.AddMinutes(1).AddSeconds(15));

        Assert.Equal("W000003 A000007 running; active 1:15", message);
    }

    private static string Event(string type, object data) =>
        JsonSerializer.Serialize(new { schemaVersion = 1, timestamp = DateTimeOffset.UtcNow, runId = "run-1", type, data });

    private sealed class RecordingProgress : IProgress<ProgressNotificationValue>
    {
        public List<ProgressNotificationValue> Values { get; } = [];
        public void Report(ProgressNotificationValue value) => Values.Add(value);
    }

    private sealed class ImmediateInvoker(string outcome) : IFactoryProcessInvoker
    {
        public Task<FactoryProcessResult> RunAsync(FactoryProcessInvocation invocation, CancellationToken cancellationToken) =>
            Task.FromResult(new FactoryProcessResult(
                0,
                JsonSerializer.Serialize(new FactoryCliOutcome(outcome, "run-1", null, null, null), FactoryJson.Options),
                string.Empty));
    }
}
