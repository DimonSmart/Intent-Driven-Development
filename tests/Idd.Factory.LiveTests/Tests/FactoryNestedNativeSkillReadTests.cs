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
}
