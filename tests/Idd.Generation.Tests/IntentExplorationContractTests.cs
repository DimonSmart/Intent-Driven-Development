using System.Text.Json;
using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class IntentExplorationContractTests(GenerationFixture fixture)
{
    [Fact]
    public void ExplorationSkills_AreRegisteredWithCorrectMetadataAndReferences()
    {
        using var manifest = JsonDocument.Parse(fixture.ReadText(Path.Combine(
            fixture.RepoRoot, "src", "canonical", "plugins", "plugin-manifest.json")));
        var plugins = manifest.RootElement.GetProperty("plugins");
        var intent = plugins.GetProperty("idd-intent");
        var names = intent.GetProperty("skills").EnumerateArray()
            .Select(item => item.GetString()).ToArray();
        Assert.Contains("idd-intent-clarify", names);
        Assert.Contains("idd-intent-brainstorm", names);
        Assert.Contains("idd-intent-visual-brainstorm", names);
        Assert.DoesNotContain(plugins.GetProperty("idd-factory").GetProperty("skills")
            .EnumerateArray().Select(item => item.GetString()), name =>
            name is "idd-intent-clarify" or "idd-intent-brainstorm");

        using var descriptions = JsonDocument.Parse(fixture.ReadText(Path.Combine(
            fixture.RepoRoot, "src", "canonical", "skills", "skill-descriptions.json")));
        foreach (var name in new[] { "idd-intent-clarify", "idd-intent-brainstorm" })
        {
            var metadata = descriptions.RootElement.GetProperty(name);
            Assert.Equal("workflow", metadata.GetProperty("exposure").GetString());
            Assert.False(metadata.TryGetProperty("invocation", out _));
        }

        Assert.Contains(intent.GetProperty("skillReferences").EnumerateArray(), reference =>
            reference.GetProperty("skill").GetString() == "idd-intent-brainstorm" &&
            reference.GetProperty("source").GetString() == "src/canonical/methodology/brainstorm-method.md" &&
            reference.GetProperty("destination").GetString() == "brainstorm-method.md");
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void ExplorationSkillsAndReferences_AreGeneratedFromCanonicalSources(string platform)
    {
        var source = Path.Combine(fixture.RepoRoot, "src", "canonical", "skills");
        var plugin = Path.Combine(fixture.MarketplaceRoot, "plugins", platform, "idd-intent", "skills");
        foreach (var name in new[] { "idd-intent-clarify", "idd-intent-brainstorm" })
        {
            var canonical = fixture.ReadText(Path.Combine(source, name + ".md"));
            var generated = fixture.ReadText(Path.Combine(plugin, name, "SKILL.md"));
            Assert.Contains(canonical.ReplaceLineEndings("\n").Trim(),
                generated.ReplaceLineEndings("\n"));
        }
        fixture.AssertFile(Path.Combine(plugin, "idd-intent-brainstorm",
            "references", "brainstorm-method.md"));
        fixture.AssertMissing(Path.Combine(plugin, "idd-intent-clarify",
            "references", "brainstorm-method.md"));
        fixture.AssertFile(Path.Combine(plugin, "idd-intent-visual-brainstorm", "SKILL.md"));
    }

    [Fact]
    public void DiscoveryScenarioCorpus_HasValidStructureAndUniqueIds()
    {
        foreach (var file in new[] { "route-cases.json", "discovery-cases.json" })
        {
            using var corpus = JsonDocument.Parse(fixture.ReadText(Path.Combine(
                fixture.RepoRoot, "tests", "Idd.Intent.LiveTests", "Scenarios", file)));
            var scenarios = corpus.RootElement.GetProperty("scenarios").EnumerateArray().ToArray();
            Assert.NotEmpty(scenarios);
            var ids = scenarios.Select(x => x.GetProperty("id").GetString()).ToArray();
            Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Theory]
    [InlineData("idd-intent.workflow.json")]
    [InlineData("idd-routing.workflow.json")]
    [InlineData("idd-overview.workflow.json")]
    public void DiagramEdges_ReferenceExistingNodeIds(string file)
    {
        using var diagram = JsonDocument.Parse(fixture.ReadText(Path.Combine(
            fixture.RepoRoot, "docs", "diagram", file)));
        var nodes = diagram.RootElement.GetProperty("nodes").EnumerateArray().ToArray();
        var ids = nodes.Select(node => node.GetProperty("id").GetString()).ToHashSet();
        foreach (var edge in diagram.RootElement.GetProperty("edges").EnumerateArray())
        {
            Assert.Contains(edge.GetProperty("from").GetString(), ids);
            Assert.Contains(edge.GetProperty("to").GetString(), ids);
        }
    }
}
