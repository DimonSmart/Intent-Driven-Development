using System.Text.Json;
using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class RoutingCoreGenerationTests(GenerationFixture fixture)
{
    [Fact]
    public void RoutingCore_HasUniqueManifestReferencesAndKeepsExistingConsumers()
    {
        var root = fixture.RepoRoot;
        var core = Path.Combine(root, "src", "canonical", "methodology", "routing-core.md");
        fixture.AssertFile(core);

        using var manifest = JsonDocument.Parse(fixture.ReadText(Path.Combine(
            root, "src", "canonical", "plugins", "plugin-manifest.json")));
        var plugin = manifest.RootElement.GetProperty("plugins").GetProperty("idd-intent");
        var references = plugin.GetProperty("skillReferences").EnumerateArray()
            .Select(e => new
            {
                Skill = e.GetProperty("skill").GetString()!,
                Source = e.GetProperty("source").GetString()!,
                Destination = e.GetProperty("destination").GetString()!
            }).ToArray();

        Assert.Equal(references.Length, references
            .Select(r => (r.Skill, r.Destination)).Distinct().Count());

        Assert.Single(references, r => r.Skill == "idd-route" &&
            r.Source == "src/canonical/skills/skill-descriptions.json" &&
            r.Destination == "skill-descriptions.json");

        foreach (var skill in new[] { "idd-route", "idd-help" })
        {
            Assert.Single(references, r => r.Skill == skill &&
                r.Source == "src/canonical/methodology/routing-core.md" &&
                r.Destination == "routing-core.md");
            Assert.Single(references, r => r.Skill == skill &&
                r.Source == "src/canonical/methodology/common-workflows.md" &&
                r.Destination == "common-workflows.md");
        }

        Assert.Single(references, r => r.Skill == "idd-engineering-change" &&
            r.Source == "src/canonical/methodology/common-workflows.md" &&
            r.Destination == "common-workflows.md");

        foreach (var reference in references)
        {
            Assert.True(!Path.IsPathRooted(reference.Source));
            Assert.True(!Path.IsPathRooted(reference.Destination));
            Assert.DoesNotContain("..", reference.Destination.Split('/'));
            fixture.AssertFile(Path.Combine(root,
                reference.Source.Replace('/', Path.DirectorySeparatorChar)));
        }
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void RoutingCore_IsGeneratedForBothConsumersWithoutLosingDetailedWorkflows(string platform)
    {
        var sourceRoot = Path.Combine(fixture.RepoRoot,
            "src", "canonical", "methodology");
        var pluginRoot = Path.Combine(fixture.MarketplaceRoot,
            "plugins", platform, "idd-intent", "skills");

        foreach (var skill in new[] { "idd-route", "idd-help" })
        {
            foreach (var name in new[] { "routing-core.md", "common-workflows.md" })
            {
                var canonical = GenerationFixture.NormalizeText(fixture.ReadText(
                    Path.Combine(sourceRoot, name)));
                var generated = GenerationFixture.NormalizeText(fixture.ReadText(
                    Path.Combine(pluginRoot, skill, "references", name)));
                Assert.Equal(canonical, generated);
            }
        }

        var metadataSource = GenerationFixture.NormalizeText(fixture.ReadText(
            Path.Combine(fixture.RepoRoot, "src", "canonical", "skills",
                "skill-descriptions.json")));
        var routeMetadata = GenerationFixture.NormalizeText(fixture.ReadText(
            Path.Combine(pluginRoot, "idd-route", "references",
                "skill-descriptions.json")));
        Assert.Equal(metadataSource, routeMetadata);

        fixture.AssertFile(Path.Combine(pluginRoot, "idd-engineering-change",
            "references", "common-workflows.md"));
        fixture.AssertMissing(Path.Combine(pluginRoot, "idd-engineering-change",
            "references", "routing-core.md"));

        var route = Path.Combine(pluginRoot, "idd-route");
        fixture.AssertFile(Path.Combine(route, "SKILL.md"));
        fixture.AssertMissing(Path.Combine(route, "agents", "openai.yaml"));
    }

    [Fact]
    public void RouteMetadata_RemainsAutomaticWorkflowAndHelpRemainsManualCommand()
    {
        using var descriptions = JsonDocument.Parse(fixture.ReadText(Path.Combine(
            fixture.RepoRoot, "src", "canonical", "skills",
            "skill-descriptions.json")));
        var route = descriptions.RootElement.GetProperty("idd-route");
        Assert.Equal("workflow", route.GetProperty("exposure").GetString());
        Assert.False(route.TryGetProperty("invocation", out _));
        var claude = route.GetProperty("adapters").GetProperty("claude")
            .GetProperty("frontmatter");
        Assert.Equal("fork", claude.GetProperty("context").GetString());
        Assert.Equal("Explore", claude.GetProperty("agent").GetString());
        Assert.False(claude.GetProperty("background").GetBoolean());

        var help = descriptions.RootElement.GetProperty("idd-help");
        Assert.Equal("command", help.GetProperty("exposure").GetString());
        Assert.Equal("manual", help.GetProperty("invocation").GetString());
    }

    [Fact]
    public void OrdinaryGenerationAndCheckMode_AreIdempotentAfterCoreExtraction()
    {
        var before = fixture.SnapshotMarketplace();
        var generated = fixture.RunGenerator();
        Assert.Equal(0, generated.ExitCode);
        Assert.Equal(before, fixture.SnapshotMarketplace());

        var check = fixture.RunGenerator(checkOnly: true);
        Assert.True(check.ExitCode == 0,
            $"Generator --check failed: {check.Stdout}{Environment.NewLine}{check.Stderr}");
    }
}
