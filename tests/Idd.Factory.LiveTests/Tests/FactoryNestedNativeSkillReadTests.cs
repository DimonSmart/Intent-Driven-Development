using System.Text.Json;
using Idd.Factory.Report;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class FactoryNestedNativeSkillReadTests
{
    [Theory]
    [InlineData(@"C:\plugins\idd-factory\skills\idd-internal-factory-planner\SKILL.md", "planner")]
    [InlineData(@"C:\plugins\idd-factory\skills\idd-internal-factory-worker\SKILL.md", "worker")]
    [InlineData("/home/user/plugins/idd-factory/skills/idd-internal-factory-planner/SKILL.md", "planner")]
    public void ReadsJsonEscapedOrUnixSkillPaths(string path, string role)
    {
        // Native Codex tool arguments can be a JSON object with doubled Windows
        // separators, not a plain command line.
        var args = JsonSerializer.Serialize(new { cmd = $"Get-Content -Raw '{path}'" });
        var evidence = new CodexEvent
        {
            ToolName = "command_execution",
            ToolArguments = args
        };

        Assert.True(NativeFactoryEndToEndLiveTests.IsNativeSkillRead(evidence, role));
    }

    [Fact]
    public void DoesNotConfuseFileExistenceWithContentRead()
    {
        var path = @"C:\plugins\skills\idd-internal-factory-planner\SKILL.md";
        var check = new CodexEvent
        {
            ToolName = "command_execution",
            ToolArguments = JsonSerializer.Serialize(new { cmd = $"Test-Path '{path}'" })
        };

        Assert.False(NativeFactoryEndToEndLiveTests.IsNativeSkillRead(check, "planner"));
    }

    [Fact]
    public void DoesNotTrustSpawnMessageOrWrongRole()
    {
        const string path = "/plugins/skills/idd-internal-factory-planner/SKILL.md";
        var spawn = new CodexEvent
        {
            ToolName = "spawn_agent",
            ToolArguments = JsonSerializer.Serialize(new { message = $"Get-Content '{path}'" })
        };
        var wrongRole = new CodexEvent
        {
            ToolName = "command_execution",
            ToolArguments = JsonSerializer.Serialize(new { cmd = $"cat '{path}'" })
        };

        Assert.False(NativeFactoryEndToEndLiveTests.IsNativeSkillRead(spawn, "planner"));
        Assert.False(NativeFactoryEndToEndLiveTests.IsNativeSkillRead(wrongRole, "worker"));
    }
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void NativeSkillEvidenceRequiresCompleteSuccessfulToolOutput(
        bool fullOutput, bool expected)
    {
        const string skillPath = @"C:\codex\plugins\skills\idd-internal-factory-planner\SKILL.md";
        const string skillContent = "# IDD Factory Planner Protocol\nYou are the Factory planner.\n";
        var response = fullOutput
            ? "Script completed\nOutput:\n" + skillContent
            : "Script completed\nOutput:\n# IDD Factory Planner Protocol\n";

        var result = InspectSyntheticNativeTrace(skillPath, skillContent,
            $"Get-Content -Raw '{skillPath}'", response);
        Assert.Equal(expected, result.Confirmed);
        Assert.Equal(1, result.ReadAttempts);
        Assert.Equal(1, result.ToolResponses);
    }

    [Fact]
    public void RejectedNativeSkillReadCannotCountAsEvidence()
    {
        const string skillPath = @"C:\codex\plugins\skills\idd-internal-factory-worker\SKILL.md";
        const string skillContent = "# IDD Factory Worker Protocol\nYou are the Factory worker.\n";
        var result = InspectSyntheticNativeTrace(skillPath, skillContent,
            $"Get-Content -Raw '{skillPath}'",
            "Script failed\nexec_command failed: Rejected: blocked by policy");
        Assert.False(result.Confirmed);
        Assert.Equal(1, result.RejectedResponses);
    }

    [Fact]
    public void NativeSkillEvidenceRejectsUnrelatedCommand()
    {
        const string skillPath = @"C:\codex\plugins\skills\idd-internal-factory-planner\SKILL.md";
        const string skillContent = "# IDD Factory Planner Protocol";
        var result = InspectSyntheticNativeTrace(skillPath, skillContent,
            $"Test-Path '{skillPath}'", "Script completed\nOutput:\n" + skillContent);
        Assert.False(result.Confirmed);
        Assert.Equal(0, result.ReadAttempts);
    }

    private static NativeSkillReadEvidence.Result InspectSyntheticNativeTrace(
        string skillPath, string content, string command, string response)
    {
        var rollout = Path.GetTempFileName();
        try
        {
            var invocation = JsonSerializer.Serialize(new
            {
                type = "response_item",
                payload = new
                {
                    type = "custom_tool_call",
                    call_id = "call-stage0",
                    name = "exec",
                    input = $"const r = await tools.exec_command({{cmd: {JsonSerializer.Serialize(command)}}});"
                }
            });
            var completion = JsonSerializer.Serialize(new
            {
                type = "response_item",
                payload = new
                {
                    type = "custom_tool_call_output",
                    call_id = "call-stage0",
                    output = new[] { new { type = "input_text", text = response } }
                }
            });
            File.WriteAllText(rollout, invocation + "\n" + completion + "\n");
            return NativeSkillReadEvidence.Inspect(rollout, skillPath, content);
        }
        finally
        {
            File.Delete(rollout);
        }
    }

}
