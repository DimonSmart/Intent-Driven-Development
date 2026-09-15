using Idd.Factory.Agents;
using Idd.Factory.Domain;

namespace Idd.Factory.Tests;

public sealed class AgentExecutionTests
{
    [Fact]
    public void SequentialShellCommandsLeaveNoActiveCommands()
    {
        var tracker = new CommandExecutionTracker();
        var started = DateTimeOffset.Parse("2026-09-08T12:00:00Z");

        tracker.Observe(Start("A", "dotnet build"), started);
        Assert.Equal("A", Assert.Single(tracker.GetActive()).Id);
        tracker.Observe(Complete("A"), started.AddSeconds(1));
        Assert.Empty(tracker.GetActive());

        tracker.Observe(Start("B", "dotnet test"), started.AddSeconds(2));
        Assert.Equal("B", Assert.Single(tracker.GetActive()).Id);
        tracker.Observe(Complete("B"), started.AddSeconds(3));
        Assert.Empty(tracker.GetActive());
    }

    [Fact]
    public void OverlappingShellCommandsAreTrackedIndependently()
    {
        var tracker = new CommandExecutionTracker();
        var started = DateTimeOffset.Parse("2026-09-08T12:00:00Z");

        tracker.Observe(Start("A", "long command"), started);
        tracker.Observe(Start("B", "short command"), started.AddSeconds(1));

        Assert.Equal(new[] { "A", "B" }, tracker.GetActive().Select(command => command.Id));

        tracker.Observe(Complete("B"), started.AddSeconds(2));
        Assert.Equal("A", Assert.Single(tracker.GetActive()).Id);

        tracker.Observe(Complete("A"), started.AddSeconds(3));
        Assert.Empty(tracker.GetActive());
    }

    [Fact]
    public void ThreeParallelShellCommandsKeepStableActiveState()
    {
        var tracker = new CommandExecutionTracker();
        var started = DateTimeOffset.Parse("2026-09-08T12:00:00Z");

        tracker.Observe(Start("A", "a"), started);
        tracker.Observe(Start("B", "b"), started.AddSeconds(1));
        tracker.Observe(Start("C", "c"), started.AddSeconds(2));
        Assert.Equal(new[] { "A", "B", "C" }, tracker.GetActive().Select(command => command.Id));

        tracker.Observe(Complete("B"), started.AddSeconds(3));
        Assert.Equal(new[] { "A", "C" }, tracker.GetActive().Select(command => command.Id));
        tracker.Observe(Complete("A"), started.AddSeconds(4));
        Assert.Equal("C", Assert.Single(tracker.GetActive()).Id);
        tracker.Observe(Complete("C"), started.AddSeconds(5));
        Assert.Empty(tracker.GetActive());
    }

    [Fact]
    public void IncompleteShellCommandDiagnosticIdentifiesFinalActiveCommands()
    {
        var tracker = new CommandExecutionTracker();
        var started = DateTimeOffset.Parse("2026-09-08T12:00:00Z");
        tracker.Observe(
            Start("item_7", "dotnet test tests/Desktop.Tests.csproj"),
            started);

        var diagnostic = CodexCommandProtocol.BuildIncompleteCommandDiagnostic(tracker.GetActive());

        Assert.Contains("[item_7] dotnet test tests/Desktop.Tests.csproj", diagnostic, StringComparison.Ordinal);
        Assert.Contains("Inspect stdout.log for the complete event stream", diagnostic, StringComparison.Ordinal);
        Assert.Contains("Temporary directory cleanup was skipped", diagnostic, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AgentExecutionProfile.ReadOnly, "read-only")]
    [InlineData(AgentExecutionProfile.WorkspaceWrite, "workspace-write")]
    public void ExecutionProfilesMapDirectlyToCodexSandboxCapabilities(AgentExecutionProfile profile, string expectedSandbox)
    {
        Assert.Equal(expectedSandbox, CodexCommandProtocol.Sandbox(profile));
    }

    [Fact]
    public void BootstrapPromptRequiresShellCommandsToFinish()
    {
        var invocation = new AgentInvocation
        {
            RunId = "run",
            AttemptId = "attempt",
            Capability = "implementation",
            Role = "executor",
            Workspace = "workspace",
            SemanticOutputPath = "result.md",
            SkillName = "idd-factory-execute-subtask",
            ExecutionProfile = AgentExecutionProfile.WorkspaceWrite,
            Input = "work",
            StartedAt = DateTimeOffset.UtcNow
        };

        var prompt = CodexCommandProtocol.BuildBootstrapPrompt(invocation, "instructions");

        Assert.Contains("Do not return while a shell command is still running", prompt, StringComparison.Ordinal);
        Assert.Contains("--disable-build-servers -m:1", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandTrackerReportsOnlyCommandsThatExceedTheirOwnTimeout()
    {
        var tracker = new CommandExecutionTracker();
        var started = DateTimeOffset.Parse("2026-09-08T12:00:00Z");
        tracker.Observe(Start("A", "long"), started);
        tracker.Observe(Start("B", "later"), started.AddMinutes(3));

        Assert.Null(tracker.FindTimedOut(started.AddMinutes(9), TimeSpan.FromMinutes(10)));
        var timedOut = Assert.IsType<IncompleteCommandExecution>(
            tracker.FindTimedOut(started.AddMinutes(10), TimeSpan.FromMinutes(10)));
        Assert.Equal("A", timedOut.Id);

        tracker.Observe(Complete("A"), started.AddMinutes(10));
        Assert.Null(tracker.FindTimedOut(started.AddMinutes(12), TimeSpan.FromMinutes(10)));
        Assert.Equal(
            "B",
            Assert.IsType<IncompleteCommandExecution>(
                tracker.FindTimedOut(started.AddMinutes(13), TimeSpan.FromMinutes(10))).Id);
    }

    [Fact]
    public void DuplicateStartedDoesNotExtendTimeoutAndCanFillMissingCommandText()
    {
        var tracker = new CommandExecutionTracker();
        var started = DateTimeOffset.Parse("2026-09-08T12:00:00Z");

        tracker.Observe(Start("A", null), started);
        tracker.Observe(Start("A", "dotnet test"), started.AddMinutes(5));

        var active = Assert.Single(tracker.GetActive());
        Assert.Equal(started, active.StartedAt);
        Assert.Equal("dotnet test", active.Command);
        Assert.Null(tracker.FindTimedOut(started.AddMinutes(9), TimeSpan.FromMinutes(10)));
        Assert.Equal(
            "A",
            Assert.IsType<IncompleteCommandExecution>(
                tracker.FindTimedOut(started.AddMinutes(10), TimeSpan.FromMinutes(10))).Id);
    }

    [Fact]
    public void CompletedThenStartedAgainUsesNewStartTime()
    {
        var tracker = new CommandExecutionTracker();
        var first = DateTimeOffset.Parse("2026-09-08T12:00:00Z");
        var second = first.AddMinutes(2);

        tracker.Observe(Start("A", "first"), first);
        tracker.Observe(Complete("A"), first.AddMinutes(1));
        tracker.Observe(Start("A", "second"), second);

        var active = Assert.Single(tracker.GetActive());
        Assert.Equal(second, active.StartedAt);
        Assert.Equal("second", active.Command);
    }

    [Fact]
    public void UnknownAndDuplicateCompletedEventsAreTolerated()
    {
        var tracker = new CommandExecutionTracker();
        var started = DateTimeOffset.Parse("2026-09-08T12:00:00Z");

        tracker.Observe(Complete("unknown"), started);
        tracker.Observe(Start("A", "command"), started.AddSeconds(1));
        tracker.Observe(Complete("A"), started.AddSeconds(2));
        tracker.Observe(Complete("A"), started.AddSeconds(3));

        Assert.Empty(tracker.GetActive());
    }

    [Fact]
    public void ActiveSnapshotIsDeterministicAndIndependentFromTrackerStorage()
    {
        var tracker = new CommandExecutionTracker();
        var started = DateTimeOffset.Parse("2026-09-08T12:00:00Z");
        tracker.Observe(Start("B", "b"), started);
        tracker.Observe(Start("A", "a"), started);

        var snapshot = tracker.GetActive();
        tracker.Observe(Complete("A"), started.AddSeconds(1));
        tracker.Observe(Complete("B"), started.AddSeconds(1));

        Assert.Equal(new[] { "A", "B" }, snapshot.Select(command => command.Id));
        Assert.Empty(tracker.GetActive());
    }

    [Fact]
    public void CommandTimeoutDiagnosticRejectsPartialResultsAndNamesTheCommand()
    {
        var diagnostic = CodexCommandProtocol.BuildCommandTimeoutDiagnostic(
            new IncompleteCommandExecution("item_3", "dotnet test tests/Desktop.Tests.csproj"),
            TimeSpan.FromMinutes(10));

        Assert.Contains("[item_3]", diagnostic, StringComparison.Ordinal);
        Assert.Contains("dotnet test tests/Desktop.Tests.csproj", diagnostic, StringComparison.Ordinal);
        Assert.Contains("partial results are not trusted", diagnostic, StringComparison.Ordinal);
        Assert.Contains("process tree was terminated", diagnostic, StringComparison.Ordinal);
    }

    private static string Start(string id, string? command)
    {
        var commandJson = command is null ? string.Empty : $",\"command\":\"{command}\"";
        return $"{{\"type\":\"item.started\",\"item\":{{\"id\":\"{id}\",\"type\":\"command_execution\"{commandJson},\"status\":\"in_progress\"}}}}";
    }

    private static string Complete(string id) =>
        $"{{\"type\":\"item.completed\",\"item\":{{\"id\":\"{id}\",\"type\":\"command_execution\",\"status\":\"completed\"}}}}";
}
