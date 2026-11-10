using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class NativeFactoryAgentTests(GenerationFixture fixture)
{
    [Fact]
    public void ObsoleteExperimentFlag_IsRejected()
    {
        var result = fixture.RunProcess("dotnet",
            ["exec", fixture.GeneratorDll, "--version", fixture.Version, "--internal-agent-experiment"]);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Unknown option: --internal-agent-experiment", result.Stderr);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void NativeDefinitions_DoNotHardCodeModelsOrExcessPermissions(string platform)
    {
        var root = Path.Combine(fixture.MarketplaceRoot, "plugins", platform, "idd-factory", "agents");
        var suffix = platform == "codex" ? ".toml" : ".md";
        var planner = fixture.ReadText(Path.Combine(root, "idd-internal-factory-planner" + suffix));
        var worker = fixture.ReadText(Path.Combine(root, "idd-internal-factory-worker" + suffix));
        if (platform == "codex")
        {
            Assert.Contains("developer_instructions = ", planner);
            Assert.Contains("developer_instructions = ", worker);
            Assert.DoesNotContain(planner.Split((char)10), line => line.StartsWith("model = ", StringComparison.Ordinal));
            Assert.DoesNotContain(worker.Split((char)10), line => line.StartsWith("model = ", StringComparison.Ordinal));
            Assert.DoesNotContain(worker.Split((char)10), line => line.StartsWith("model_reasoning_effort = ", StringComparison.Ordinal));
        }
        else
        {
            Assert.Contains("tools: Read, Glob, Grep", planner);
            Assert.Contains("disallowedTools:", planner);
            Assert.DoesNotContain("tools: Read, Glob, Grep, Edit", planner);
            Assert.Contains("tools: Read, Glob, Grep, Edit, Write", worker);
            Assert.DoesNotContain("permissionMode:", planner);
            Assert.DoesNotContain("mcpServers:", worker);
        }
    }
}
