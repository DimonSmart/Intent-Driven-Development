using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class InternalAgentExperimentTests(GenerationFixture fixture)
{
    [Fact]
    public void Experiment_PreservesCanonicalTaskRulesAndDefaultMarketplace()
    {
        var before = fixture.SnapshotMarketplace();
        var output = Path.Combine(Path.GetTempPath(), "idd-internal-agent-generation", Guid.NewGuid().ToString("N"));
        try
        {
            var arguments = new[] { "exec", fixture.GeneratorDll, "--version", fixture.Version,
                "--output", output, "--internal-agent-experiment" };
            var result = fixture.RunProcess("dotnet", arguments);
            Assert.True(result.ExitCode == 0, result.Stderr);
            foreach (var platform in new[] { "codex", "claude" })
            {
                var plugin = Path.Combine(output, "plugins", platform, "idd-factory", "skills");
                var root = File.ReadAllText(Path.Combine(plugin, "idd-factory-run", "SKILL.md"));
                Assert.DoesNotContain("references/factory-planner.md", root);
                Assert.DoesNotContain("references/factory-worker.md", root);
                Assert.DoesNotContain("<complete contents of references/engineering-guardrails.md", root);
                foreach (var role in new[] { "planner", "worker" })
                {
                    var name = "idd-internal-factory-" + role;
                    var skill = Path.Combine(plugin, name);
                    var text = File.ReadAllText(Path.Combine(skill, "SKILL.md"));
                    var canonical = File.ReadAllText(Path.Combine(fixture.RepoRoot, "src", "canonical", "factory", role + ".md"))
                        .ReplaceLineEndings("\n");
                    Assert.Equal(canonical[canonical.IndexOf("## Inputs", StringComparison.Ordinal)..].TrimEnd(),
                        text[text.IndexOf("## Inputs", StringComparison.Ordinal)..].TrimEnd());
                    Assert.Contains("name: " + name, text);
                    Assert.Contains("return BLOCKED without modifying files", text);
                    Assert.Contains("Load and follow Skill " + name, root);
                    Assert.False(File.Exists(Path.Combine(plugin, "idd-factory-run", "references", "factory-" + role + ".md")));
                    Assert.Equal(File.ReadAllText(Path.Combine(plugin, "idd-factory-run", "references", "engineering-guardrails.md")),
                        File.ReadAllText(Path.Combine(skill, "references", "engineering-guardrails.md")));
                    if (platform == "claude")
                    {
                        Assert.Contains("user-invocable: false", text);
                        Assert.Contains("disable-model-invocation: false", text);
                    }
                }
            }
            var check = fixture.RunProcess("dotnet", arguments.Append("--check"));
            Assert.True(check.ExitCode == 0, check.Stderr);
            Assert.Equal(before, fixture.SnapshotMarketplace());
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        }
    }
}
