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

        Assert.Empty(CodexCliBackend.FindIncompleteCommandExecutions(stdout));
    }

    [Fact]
    public void StartedShellCommandWithoutCompletionIsReportedAsIncomplete()
    {
        var stdout = """
            {"type":"item.started","item":{"id":"command-1","type":"command_execution","status":"in_progress"}}
            {"type":"item.completed","item":{"id":"message-1","type":"agent_message"}}
            """;

        var incomplete = Assert.Single(CodexCliBackend.FindIncompleteCommandExecutions(stdout));
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

        var incomplete = CodexCliBackend.FindIncompleteCommandExecutions(stdout);
        var diagnostic = CodexCliBackend.BuildIncompleteCommandDiagnostic(incomplete);

        Assert.Contains("[item_7] dotnet test tests/Desktop.Tests.csproj", diagnostic, StringComparison.Ordinal);
        Assert.Contains("Inspect stdout.log for the complete event stream", diagnostic, StringComparison.Ordinal);
        Assert.Contains("Temporary directory cleanup was skipped", diagnostic, StringComparison.Ordinal);
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

        var prompt = CodexCliBackend.BuildBootstrapPrompt(invocation, "instructions");

        Assert.Contains("Do not return while a shell command is still running", prompt, StringComparison.Ordinal);
        Assert.Contains("--disable-build-servers -m:1", prompt, StringComparison.Ordinal);
    }
}
