using System.Text.Json;
using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class BusinessComplexityAssessmentContractTests(GenerationFixture fixture)
{
    [Fact]
    public void Estimator_HasExpectedPluginRegistrationAndHostMetadata()
    {
        var source = Path.Combine(fixture.RepoRoot, "src", "canonical");
        fixture.AssertFile(Path.Combine(source, "skills", "idd-intent-estimate.md"));

        using var manifest = JsonDocument.Parse(fixture.ReadText(
            Path.Combine(source, "plugins", "plugin-manifest.json")));
        var plugins = manifest.RootElement.GetProperty("plugins");
        var intent = plugins.GetProperty("idd-intent").GetProperty("skills")
            .EnumerateArray().Select(x => x.GetString()).ToArray();
        var factory = plugins.GetProperty("idd-factory").GetProperty("skills")
            .EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Contains("idd-intent-estimate", intent);
        Assert.DoesNotContain("idd-intent-estimate", factory);
        Assert.Equal(1, intent.Count(x => x == "idd-intent-estimate"));

        using var descriptions = JsonDocument.Parse(fixture.ReadText(
            Path.Combine(source, "skills", "skill-descriptions.json")));
        var skill = descriptions.RootElement.GetProperty("idd-intent-estimate");
        Assert.Equal("auto", skill.GetProperty("invocation").GetString());
        Assert.Equal("command", skill.GetProperty("exposure").GetString());
        var claude = skill.GetProperty("adapters").GetProperty("claude")
            .GetProperty("frontmatter");
        Assert.Equal("fork", claude.GetProperty("context").GetString());
        Assert.Equal("Explore", claude.GetProperty("agent").GetString());
        Assert.False(claude.GetProperty("background").GetBoolean());
        Assert.Equal("Read Glob Grep", claude.GetProperty("allowed-tools").GetString());
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void BothAdapters_PackageCanonicalEstimatorSkill(string platform)
    {
        var skillRoot = Path.Combine(fixture.MarketplaceRoot, "plugins", platform,
            "idd-intent", "skills", "idd-intent-estimate");
        var generated = fixture.ReadText(Path.Combine(skillRoot, "SKILL.md"));
        var canonical = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "src", "canonical", "skills", "idd-intent-estimate.md"));
        Assert.Contains(canonical.ReplaceLineEndings("\n").Trim(),
            generated.ReplaceLineEndings("\n"));

        if (platform == "codex")
            fixture.AssertFile(Path.Combine(skillRoot, "agents", "openai.yaml"));
        else
        {
            var frontmatter = GenerationFixture.ReadFrontMatter(generated);
            Assert.Contains("user-invocable: true", frontmatter);
            Assert.Contains("disable-model-invocation: false", frontmatter);
        }
    }

    [Fact]
    public void EstimationScenarioCorpus_IsWellFormed()
    {
        using var suite = JsonDocument.Parse(fixture.ReadText(Path.Combine(
            fixture.RepoRoot, "tests", "Idd.Intent.LiveTests", "Scenarios", "estimate-cases.json")));
        Assert.Equal(1, suite.RootElement.GetProperty("schemaVersion").GetInt32());
        var scenarios = suite.RootElement.GetProperty("scenarios").EnumerateArray().ToArray();
        Assert.NotEmpty(scenarios);
        var ids = scenarios.Select(x => x.GetProperty("id").GetString()).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        foreach (var scenario in scenarios)
        {
            Assert.False(string.IsNullOrWhiteSpace(scenario.GetProperty("request").GetString()));
            Assert.NotEmpty(scenario.GetProperty("expected").EnumerateArray());
            Assert.Equal(JsonValueKind.Object, scenario.GetProperty("documents").ValueKind);
        }
    }
}
