using System.Text.Json;
using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class NativeFactoryAgentTests(GenerationFixture fixture)
{
    [Theory]
    [InlineData("codex", ".toml")]
    [InlineData("claude", ".md")]
    public void OrdinaryGeneration_EmbedsCanonicalInstructionsInRegisteredDefinitions(string platform, string extension)
    {
        var root = Path.Combine(fixture.MarketplaceRoot, "plugins", platform, "idd-factory");
        var run = fixture.ReadText(Path.Combine(root, "skills", "idd-factory-run", "SKILL.md"));
        Assert.DoesNotContain("references/factory-planner.md", run);
        Assert.DoesNotContain("references/factory-worker.md", run);
        Assert.DoesNotContain("Load and follow Skill idd-internal", run);
        Assert.DoesNotContain("<complete contents of references", run);
        Assert.Contains("registered", run, StringComparison.OrdinalIgnoreCase);

        var guardrails = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "src", "canonical", "methodology", "engineering-guardrails.md")).ReplaceLineEndings("\n").Trim();
        foreach (var role in new[] { "planner", "worker" })
        {
            var name = "idd-internal-factory-" + role;
            var generated = fixture.ReadText(Path.Combine(root, "agents", name + extension));
            var body = platform == "codex"
                ? JsonSerializer.Deserialize<string>(generated.Split((char)10)
                    .Single(line => line.StartsWith("developer_instructions = ", StringComparison.Ordinal))
                    ["developer_instructions = ".Length..])!
                : generated;

            Assert.Contains(guardrails, body);
            Assert.Contains("## Native Factory assignment guard", body);
            Assert.Contains("BLOCKED", body);
            Assert.Contains("# IDD Factory " + (role == "planner" ? "Planner" : "Worker") + " Protocol", body);
            Assert.False(Directory.Exists(Path.Combine(root, "skills", name)));
        }
    }

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
