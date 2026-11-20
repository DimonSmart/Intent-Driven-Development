using System.Text.Json;
using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class CosmicFunctionalSizeContractTests(GenerationFixture fixture)
{
    [Fact]
    public void CosmicSkill_IsRegisteredWithExpectedPackageReferences()
    {
        using var manifest = JsonDocument.Parse(fixture.ReadText(Path.Combine(
            fixture.RepoRoot, "src", "canonical", "plugins", "plugin-manifest.json")));
        var plugins = manifest.RootElement.GetProperty("plugins");
        var intent = plugins.GetProperty("idd-intent");
        var skills = intent.GetProperty("skills").EnumerateArray()
            .Select(x => x.GetString()).ToArray();
        Assert.Equal(1, skills.Count(x => x == "idd-intent-cosmic"));
        Assert.DoesNotContain("idd-intent-cosmic", plugins.GetProperty("idd-factory")
            .GetProperty("skills").EnumerateArray().Select(x => x.GetString()));

        var references = intent.GetProperty("skillReferences").EnumerateArray()
            .Where(x => x.GetProperty("skill").GetString() == "idd-intent-cosmic")
            .Select(x => x.GetProperty("destination").GetString()).ToArray();
        Assert.Equal(new[] { "cosmic-measurement-guide.md", "cosmic-delegation-protocol.md",
            "cosmic-execution-state.md" }, references);

        using var descriptions = JsonDocument.Parse(fixture.ReadText(Path.Combine(
            fixture.RepoRoot, "src", "canonical", "skills", "skill-descriptions.json")));
        var cosmic = descriptions.RootElement.GetProperty("idd-intent-cosmic");
        Assert.Equal("auto", cosmic.GetProperty("invocation").GetString());
        Assert.Equal("command", cosmic.GetProperty("exposure").GetString());
        var claude = cosmic.GetProperty("adapters").GetProperty("claude")
            .GetProperty("frontmatter");
        Assert.Equal("fork", claude.GetProperty("context").GetString());
        Assert.Equal("general-purpose", claude.GetProperty("agent").GetString());
        Assert.False(claude.GetProperty("background").GetBoolean());
        Assert.Equal("Read Glob Grep Agent Write", claude.GetProperty("allowed-tools").GetString());
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void CosmicPackage_ContainsExactCanonicalReferences(string platform)
    {
        var canonical = Path.Combine(fixture.RepoRoot, "src", "canonical");
        var skill = Path.Combine(fixture.MarketplaceRoot, "plugins", platform,
            "idd-intent", "skills", "idd-intent-cosmic");
        var generated = fixture.ReadText(Path.Combine(skill, "SKILL.md"));
        var sourceSkill = fixture.ReadText(Path.Combine(canonical, "skills", "idd-intent-cosmic.md"));
        Assert.Contains(sourceSkill.ReplaceLineEndings("\n").Trim(),
            generated.ReplaceLineEndings("\n"));

        foreach (var filename in new[]
        {
            "cosmic-measurement-guide.md",
            "cosmic-delegation-protocol.md",
            "cosmic-execution-state.md"
        })
        {
            var source = fixture.ReadText(Path.Combine(canonical, "methodology", filename));
            var packaged = fixture.ReadText(Path.Combine(skill, "references", filename));
            Assert.Equal(GenerationFixture.NormalizeText(source),
                GenerationFixture.NormalizeText(packaged));
        }

        fixture.AssertMissing(Path.Combine(skill, "references", "cosmic-validator.mjs"));
        fixture.AssertMissing(Path.Combine(skill, "assets", "cosmic-state", "cosmic_state.py"));

        if (platform == "codex")
            fixture.AssertFile(Path.Combine(skill, "agents", "openai.yaml"));
        else
        {
            var frontmatter = GenerationFixture.ReadFrontMatter(generated);
            Assert.Contains("user-invocable: true", frontmatter);
            Assert.Contains("disable-model-invocation: false", frontmatter);
        }
    }

    [Fact]
    public void CosmicLiveCorpus_IsValidJsonWithUniqueScenarioIds()
    {
        using var corpus = JsonDocument.Parse(fixture.ReadText(Path.Combine(
            fixture.RepoRoot, "tests", "Idd.Intent.LiveTests", "Scenarios", "cosmic-cases.json")));
        var scenarios = corpus.RootElement.GetProperty("scenarios").EnumerateArray().ToArray();
        Assert.NotEmpty(scenarios);
        var ids = scenarios.Select(x => x.GetProperty("id").GetString()).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }
}
