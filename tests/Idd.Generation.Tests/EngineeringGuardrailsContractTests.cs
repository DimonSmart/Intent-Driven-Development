using System.Text.Json;
using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class EngineeringGuardrailsContractTests(GenerationFixture fixture)
{
    private string Canonical(params string[] parts) =>
        fixture.ReadText(Path.Combine([fixture.RepoRoot, "src", "canonical", .. parts]));

    [Fact]
    public void GeneratedClaudeAndCodexArtifacts_PreserveCanonicalEngineeringReference()
    {
        var source = GenerationFixture.NormalizeText(Canonical("methodology", "engineering-guardrails.md"));
        var skills = new[]
        {
            ("idd-intent", "idd-code-implement"),
            ("idd-intent", "idd-code-check-implementation"),
            ("idd-intent", "idd-intent-lint"),
            ("idd-intent", "idd-engineering-change"),
            ("idd-factory", "idd-factory-run"),
            ("idd-factory", "idd-factory-decompose-task"),
            ("idd-factory", "idd-factory-execute-subtask")
        };

        foreach (var platform in new[] { "claude", "codex" })
        foreach (var (plugin, skill) in skills)
        {
            var generated = fixture.ReadText(Path.Combine(
                fixture.MarketplaceRoot, "plugins", platform, plugin, "skills", skill,
                "references", "engineering-guardrails.md"));
            Assert.Equal(source, GenerationFixture.NormalizeText(generated));
        }
    }

    [Fact]
    public void EngineeringManagement_IsOwnedByIntentPlugin()
    {
        using var manifest = JsonDocument.Parse(Canonical("plugins", "plugin-manifest.json"));
        var plugins = manifest.RootElement.GetProperty("plugins");
        var intentSkills = plugins.GetProperty("idd-intent").GetProperty("skills")
            .EnumerateArray().Select(value => value.GetString()).ToArray();

        Assert.Contains("idd-engineering-change", intentSkills);
        Assert.False(plugins.TryGetProperty("idd-engineering", out _));
        Assert.Equal(2, plugins.EnumerateObject().Count());
    }

    [Fact]
    public void PublicSkillName_AllowsOnlyExplicitEngineeringChangeSpecialCase()
    {
        SkillDescriptionValidator.GuardPublicSkillName("test", "idd-engineering-change");

        Assert.Throws<InvalidOperationException>(() =>
            SkillDescriptionValidator.GuardPublicSkillName("test", "idd-engineering-add"));
        Assert.Throws<InvalidOperationException>(() =>
            SkillDescriptionValidator.GuardPublicSkillName("test", "idd-engineering-remove"));
    }

    [Fact]
    public void FactoryPackaging_DoesNotReintroduceRuntimeMcpOrEngineeringPlugin()
    {
        using var manifest = JsonDocument.Parse(Canonical("plugins", "plugin-manifest.json"));
        var plugins = manifest.RootElement.GetProperty("plugins");

        Assert.True(plugins.TryGetProperty("idd-intent", out _));
        Assert.True(plugins.TryGetProperty("idd-factory", out _));
        Assert.False(plugins.TryGetProperty("idd-engineering", out _));
        Assert.Equal(2, plugins.EnumerateObject().Count());

        foreach (var platform in new[] { "claude", "codex" })
        {
            var factory = Path.Combine(fixture.MarketplaceRoot, "plugins", platform, "idd-factory");
            fixture.AssertMissing(Path.Combine(factory, "runtime"));
            fixture.AssertMissing(Path.Combine(factory, ".mcp.json"));
        }
    }
}
