using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class InternalFactorySkillTests(GenerationFixture fixture)
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
    public void FactoryInternalSkills_ArePackagedWithoutCustomAgentRegistration(string platform)
    {
        var plugin = Path.Combine(fixture.MarketplaceRoot, "plugins", platform, "idd-factory");
        fixture.AssertMissing(Path.Combine(plugin, "agents"));
        fixture.AssertMissing(Path.Combine(plugin, "skills", "idd-factory-configure",
            "scripts", "register-codex-agents.py"));

        foreach (var role in new[] { "root", "planner", "worker" })
        {
            fixture.AssertFile(Path.Combine(plugin, "skills",
                "idd-internal-factory-" + role, "SKILL.md"));
            fixture.AssertMissing(Path.Combine(plugin, "skills",
                "idd-internal-factory-" + role, "agents", "openai.yaml"));
        }
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void FactoryRoot_HasRequiredPackagedReferences(string platform)
    {
        var references = Path.Combine(fixture.MarketplaceRoot, "plugins", platform,
            "idd-factory", "skills", "idd-internal-factory-root", "references");
        foreach (var name in new[]
        {
            "intent-preflight.md",
            "project-verification.md",
            "factory-execution-policy.md",
            "engineering-guardrails.md",
            "methodology-version.json"
        })
            fixture.AssertFile(Path.Combine(references, name));
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void FactorySkillBodies_MatchCanonicalSourcesExactly(string platform)
    {
        var cases = new (string Skill, string Source)[]
        {
            ("idd-factory-run", "src/canonical/skills/idd-factory-run.md"),
            ("idd-factory-configure", "src/canonical/skills/idd-factory-configure.md"),
            ("idd-internal-factory-root", "src/canonical/factory/root.md"),
            ("idd-internal-factory-planner", "src/canonical/factory/planner.md"),
            ("idd-internal-factory-worker", "src/canonical/factory/worker.md")
        };

        foreach (var (skill, source) in cases)
        {
            var canonical = fixture.ReadText(Path.Combine(fixture.RepoRoot,
                source.Replace('/', Path.DirectorySeparatorChar)));
            var generated = fixture.ReadText(Path.Combine(fixture.MarketplaceRoot,
                "plugins", platform, "idd-factory", "skills", skill, "SKILL.md"))
                .ReplaceLineEndings("\n");

            Assert.StartsWith("---\n", generated);
            var separator = generated.IndexOf("\n---\n", 4, StringComparison.Ordinal);
            Assert.True(separator >= 0, $"Missing YAML frontmatter terminator in {platform}/{skill}.");
            var body = generated[(separator + "\n---\n".Length)..].Trim();
            Assert.Equal(canonical.ReplaceLineEndings("\n").Trim(), body);
        }
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void RootReferences_MatchCanonicalSourceFilesExactly(string platform)
    {
        var sources = new (string Source, string Destination)[]
        {
            ("intent-preflight.md", "intent-preflight.md"),
            ("project-verification.md", "project-verification.md"),
            ("factory-execution-policy.md", "factory-execution-policy.md"),
            ("engineering-guardrails.md", "engineering-guardrails.md")
        };

        foreach (var (source, destination) in sources)
        {
            var canonical = fixture.ReadText(Path.Combine(fixture.RepoRoot,
                "src", "canonical", "methodology", source));
            var packaged = fixture.ReadText(Path.Combine(fixture.MarketplaceRoot,
                "plugins", platform, "idd-factory", "skills",
                "idd-internal-factory-root", "references", destination));
            Assert.Equal(GenerationFixture.NormalizeText(canonical),
                GenerationFixture.NormalizeText(packaged));
        }
    }
}
