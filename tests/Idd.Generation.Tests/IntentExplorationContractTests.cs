using System.Text.Json;
using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class IntentExplorationContractTests(GenerationFixture fixture)
{
    [Fact]
    public void BothReadOnlySkills_AreRegisteredOnlyInIntentWithDistinctRoles()
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
        var clarifyDescription = descriptions.RootElement.GetProperty("idd-intent-clarify")
            .GetProperty("description").GetString()!;
        var brainstormDescription = descriptions.RootElement.GetProperty("idd-intent-brainstorm")
            .GetProperty("description").GetString()!;
        Assert.NotEqual(clarifyDescription, brainstormDescription);
        Assert.Contains("ambiguity", clarifyDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("alternative", brainstormDescription, StringComparison.OrdinalIgnoreCase);

        Assert.Contains(intent.GetProperty("skillReferences").EnumerateArray(), reference =>
            reference.GetProperty("skill").GetString() == "idd-intent-brainstorm" &&
            reference.GetProperty("source").GetString() == "src/canonical/methodology/brainstorm-method.md" &&
            reference.GetProperty("destination").GetString() == "brainstorm-method.md");
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void CanonicalSkillsAndOptionalMethods_GenerateIndependently(string platform)
    {
        var source = Path.Combine(fixture.RepoRoot, "src", "canonical", "skills");
        var package = Path.Combine(fixture.MarketplaceRoot, "plugins", platform, "idd-intent", "skills");
        foreach (var name in new[] { "idd-intent-clarify", "idd-intent-brainstorm" })
        {
            var content = fixture.ReadText(Path.Combine(package, name, "SKILL.md"));
            var canonical = fixture.ReadText(Path.Combine(source, name + ".md")).Trim();
            Assert.Contains(canonical, content);
            Assert.Contains("read-only", content, StringComparison.OrdinalIgnoreCase);
        }
        fixture.AssertFile(Path.Combine(package, "idd-intent-brainstorm",
            "references", "brainstorm-method.md"));
        fixture.AssertMissing(Path.Combine(package, "idd-intent-clarify",
            "references", "brainstorm-method.md"));
        fixture.AssertFile(Path.Combine(package, "idd-intent-visual-brainstorm", "SKILL.md"));
    }

    [Fact]
    public void RoutingAndDiscoveryScenarios_PreserveExplicitBoundaries()
    {
        var core = fixture.ReadText(Path.Combine(fixture.RepoRoot, "src",
            "canonical", "methodology", "routing-core.md"));
        Assert.Contains("idd-intent-clarify", core);
        Assert.Contains("idd-intent-brainstorm", core);
        Assert.Contains("idd-intent-visual-brainstorm", core);
        Assert.Contains("route-only", core);
        Assert.Contains("intent-only", core);

        using var routes = JsonDocument.Parse(fixture.ReadText(Path.Combine(
            fixture.RepoRoot, "tests", "Idd.Intent.LiveTests",
            "Scenarios", "route-cases.json")));
        var cases = routes.RootElement.GetProperty("scenarios").EnumerateArray().ToArray();
        Assert.Equal(cases.Length, cases.Select(c => c.GetProperty("id").GetString())
            .Distinct(StringComparer.Ordinal).Count());
        foreach (var id in new[] { "mixed-product-engineering-code", "clarify-generic-permissions", "clarify-uploads", "clarify-mixed" })
            Assert.Equal("idd-intent-clarify", cases.Single(c =>
                c.GetProperty("id").GetString() == id).GetProperty("expected")
                .GetProperty("firstSkill").GetString());
        foreach (var id in new[] { "brainstorm-access-options", "brainstorm-file-manager-opportunities", "brainstorm-named" })
            Assert.Equal("idd-intent-brainstorm", cases.Single(c =>
                c.GetProperty("id").GetString() == id).GetProperty("expected")
                .GetProperty("firstSkill").GetString());

        using var discovery = JsonDocument.Parse(fixture.ReadText(Path.Combine(
            fixture.RepoRoot, "tests", "Idd.Intent.LiveTests", "Scenarios", "discovery-cases.json")));
        var scenarios = discovery.RootElement.GetProperty("scenarios").EnumerateArray().ToArray();
        Assert.Equal(13, scenarios.Length);
        Assert.Equal(13, scenarios.Select(c => c.GetProperty("id").GetString())
            .Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(5, scenarios.Count(c => c.GetProperty("skill").GetString() == "idd-intent-clarify"));
        Assert.Equal(8, scenarios.Count(c => c.GetProperty("skill").GetString() == "idd-intent-brainstorm"));
        Assert.All(scenarios, scenario =>
        {
            Assert.False(string.IsNullOrWhiteSpace(scenario.GetProperty("request").GetString()));
            Assert.NotEmpty(scenario.GetProperty("expect").EnumerateArray());
        });
    }

    [Theory]
    [InlineData("idd-intent.workflow.json")]
    [InlineData("idd-routing.workflow.json")]
    [InlineData("idd-overview.workflow.json")]
    public void Diagrams_DistinguishClarifyFromBrainstormWithoutDanglingEdges(string file)
    {
        using var diagram = JsonDocument.Parse(fixture.ReadText(Path.Combine(
            fixture.RepoRoot, "docs", "diagram", file)));
        var nodes = diagram.RootElement.GetProperty("nodes").EnumerateArray().ToArray();
        var labels = nodes.Select(node => node.GetProperty("label").GetString() ?? "").ToArray();
        Assert.Contains(labels, label => label.Contains("Clarify", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(labels, label => label.Contains("Brainstorm", StringComparison.OrdinalIgnoreCase));
        var ids = nodes.Select(node => node.GetProperty("id").GetString()).ToHashSet();
        foreach (var edge in diagram.RootElement.GetProperty("edges").EnumerateArray())
        {
            Assert.Contains(edge.GetProperty("from").GetString(), ids);
            Assert.Contains(edge.GetProperty("to").GetString(), ids);
        }
    }
}
