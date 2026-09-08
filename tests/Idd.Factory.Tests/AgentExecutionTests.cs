using Idd.Factory.Agents;
using Idd.Factory.Domain;

namespace Idd.Factory.Tests;

public sealed class AgentExecutionTests
{
    [Fact]
    public void CompletedShellCommandIsNotReportedAsIncomplete()
    {
        var stdout = """
            {"type":"item.started","item":{"id":"command-1","type":"command_execution","status":"in_progress"}}
            {"type":"item.completed","item":{"id":"command-1","type":"command_execution","status":"completed"}}
            """;

        Assert.Empty(CodexCommandProtocol.FindIncompleteCommandExecutions(stdout));
    }

    [Fact]
    public void StartedShellCommandWithoutCompletionIsReportedAsIncomplete()
    {
        var stdout = """
            {"type":"item.started","item":{"id":"command-1","type":"command_execution","status":"in_progress"}}
            {"type":"item.completed","item":{"id":"message-1","type":"agent_message"}}
            """;

        var incomplete = Assert.Single(CodexCommandProtocol.FindIncompleteCommandExecutions(stdout));
        Assert.Equal("command-1", incomplete.Id);
        Assert.Null(incomplete.Command);
    }

    [Fact]
    public void IncompleteShellCommandDiagnosticIdentifiesCommandAndLocalEvidence()
    {
        var stdout = """
            {"type":"item.started","item":{"id":"item_7","type":"command_execution","command":"dotnet test tests/Desktop.Tests.csproj","status":"in_progress"}}
            {"type":"item.completed","item":{"id":"message-1","type":"agent_message"}}
            """;

        var incomplete = CodexCommandProtocol.FindIncompleteCommandExecutions(stdout);
        var diagnostic = CodexCommandProtocol.BuildIncompleteCommandDiagnostic(incomplete);

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
        tracker.Observe("""{"type":"item.started","item":{"id":"item_3","type":"command_execution","command":"dotnet test","status":"in_progress"}}""", started);

        Assert.Null(tracker.FindTimedOut(started.AddMinutes(9), TimeSpan.FromMinutes(10)));
        var timedOut = Assert.IsType<IncompleteCommandExecution>(tracker.FindTimedOut(started.AddMinutes(10), TimeSpan.FromMinutes(10)));
        Assert.Equal("item_3", timedOut.Id);
        Assert.Equal("dotnet test", timedOut.Command);

        tracker.Observe("""{"type":"item.completed","item":{"id":"item_3","type":"command_execution","status":"completed"}}""", started.AddMinutes(10));
        Assert.Null(tracker.FindTimedOut(started.AddHours(1), TimeSpan.FromMinutes(10)));
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

    [Fact]
    public void CommandTrackerReportsStartingAnotherCommandBeforeTheFirstCompletes()
    {
        var tracker = new CommandExecutionTracker();
        var started = DateTimeOffset.Parse("2026-09-08T12:00:00Z");
        tracker.Observe("""{"type":"item.started","item":{"id":"item_3","type":"command_execution","command":"dotnet test","status":"in_progress"}}""", started);
        tracker.Observe("""{"type":"item.started","item":{"id":"item_5","type":"command_execution","command":"git diff --check","status":"in_progress"}}""", started.AddSeconds(30));

        var overlap = Assert.IsType<CommandExecutionOverlap>(tracker.FindOverlap());
        Assert.Equal("item_3", overlap.Active.Id);
        Assert.Equal("item_5", overlap.Started.Id);
        var diagnostic = CodexCommandProtocol.BuildCommandOverlapDiagnostic(overlap);
        Assert.Contains("item_5", diagnostic, StringComparison.Ordinal);
        Assert.Contains("before [item_3] completed", diagnostic, StringComparison.Ordinal);
        Assert.Contains("partial results are not trusted", diagnostic, StringComparison.Ordinal);
    }
}
