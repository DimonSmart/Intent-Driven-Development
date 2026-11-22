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
    public void FactoryNativeWaits_UseLongIntervalsWithoutTreatingTimeoutAsTermination(string platform)
    {
        var factorySkills = Path.Combine(fixture.MarketplaceRoot,
            "plugins", platform, "idd-factory", "skills");
        var supervisor = fixture.ReadText(Path.Combine(factorySkills,
            "idd-factory-run", "SKILL.md"));
        var root = fixture.ReadText(Path.Combine(factorySkills,
            "idd-internal-factory-root", "SKILL.md"));

        Assert.Contains("timeout_ms: 3600000", supervisor);
        Assert.Contains("timed_out: true", supervisor);
        Assert.Contains("A wait timeout alone must", supervisor);
        Assert.Contains("NEVER cause `interrupt_agent`", supervisor);
        Assert.Contains("### User-facing recovery guidance", supervisor);
        Assert.Contains("Cancel temporary Factory state", supervisor);

        Assert.Contains("wait_agent(timeout_ms: 600000)", root);
        Assert.Contains("A timeout by itself", root);
        Assert.Contains("must NEVER call `interrupt_agent`", root);
        Assert.Contains("re-arm its", root);
        Assert.DoesNotContain(
            "On timeout, failed wait or emergency interrupt:", root);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void FactoryContinueAfterQuota_SeparatesWriterLivenessFromTaskCompletion(string platform)
    {
        var skills = Path.Combine(fixture.MarketplaceRoot,
            "plugins", platform, "idd-factory", "skills");
        var supervisor = fixture.ReadText(Path.Combine(skills,
            "idd-factory-run", "SKILL.md"));
        var root = fixture.ReadText(Path.Combine(skills,
            "idd-internal-factory-root", "SKILL.md"));
        var preflight = fixture.ReadText(Path.Combine(skills,
            "idd-internal-factory-root", "references", "intent-preflight.md"));

        Assert.Contains("quota/usage exhaustion", supervisor);
        Assert.Contains("pending checkpoint, missing trusted terminal", supervisor);
        Assert.Contains("without a trusted completed checkpoint", supervisor);
        Assert.Contains("If the host reports a still-active prior Writer", supervisor);

        Assert.Contains("pending Task is not proof of a live process", root);
        Assert.Contains("unconfirmed", root);
        Assert.Contains("replan or finish only the remaining work", root);

        Assert.Contains("a previous top-level host turn ended", preflight);
        Assert.Contains("An aborted child transcript", preflight);
        Assert.Contains("Writer absence establishes ONLY re-entry safety", preflight);
        Assert.Contains("Never mark an interrupted Worker successful", preflight,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("block before a new Writer", preflight);
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
