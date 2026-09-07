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

        Assert.Equal(0, CodexCliBackend.CountIncompleteCommandExecutions(stdout));
    }

    [Fact]
    public void StartedShellCommandWithoutCompletionIsReportedAsIncomplete()
    {
        var stdout = """
            {"type":"item.started","item":{"id":"command-1","type":"command_execution","status":"in_progress"}}
            {"type":"item.completed","item":{"id":"message-1","type":"agent_message"}}
            """;

        Assert.Equal(1, CodexCliBackend.CountIncompleteCommandExecutions(stdout));
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
    }
}
