using System.Text.Json;
using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class MarketplaceGenerationTests(GenerationFixture fixture)
{
    [Fact]
    public void ClaudeMarketplace_HasCanonicalPluginsAndMigrationMetadata()
    {
        var path = Path.Combine(fixture.MarketplaceRoot, ".claude-plugin", "marketplace.json");
        using var document = JsonDocument.Parse(fixture.ReadText(path));
        var root = document.RootElement;

        AssertString(root, "name", "intent-driven-development");
        AssertString(root, "version", fixture.Version);
        var renames = root.GetProperty("renames");
        AssertString(renames, "idd", "idd-intent");
        AssertString(renames, "idd-core", "idd-intent");

        var plugins = root.GetProperty("plugins").EnumerateArray().ToArray();
        Assert.Equal(["idd-intent", "idd-factory"], plugins.Select(plugin => plugin.GetProperty("name").GetString()).ToArray());
        foreach (var plugin in plugins)
        {
            AssertString(plugin, "version", fixture.Version);
            Assert.False(plugin.TryGetProperty("policy", out _), $"Claude plugin {plugin.GetProperty("name").GetString()} contains Codex-only policy.");
            fixture.AssertMarketplacePath(plugin.GetProperty("source").GetString());
        }
    }

    [Fact]
    public void CodexMarketplace_HasCanonicalPluginsAndPolicies()
    {
        var path = Path.Combine(fixture.MarketplaceRoot, ".agents", "plugins", "marketplace.json");
        using var document = JsonDocument.Parse(fixture.ReadText(path));
        var root = document.RootElement;

        AssertString(root, "name", "intent-driven-development");
        AssertString(root.GetProperty("interface"), "displayName", "Intent-Driven Development");

        var plugins = root.GetProperty("plugins").EnumerateArray().ToArray();
        Assert.Equal(["idd-intent", "idd-factory"], plugins.Select(plugin => plugin.GetProperty("name").GetString()).ToArray());
        foreach (var plugin in plugins)
        {
            var source = plugin.GetProperty("source");
            AssertString(source, "source", "local");
            fixture.AssertMarketplacePath(source.GetProperty("path").GetString());

            var policy = plugin.GetProperty("policy");
            AssertString(policy, "installation", "AVAILABLE");
            AssertString(policy, "authentication", "ON_INSTALL");
        }
    }

    [Fact]
    public void PublishedLayout_ContainsOnlyCurrentPluginRoots()
    {
        foreach (var relativePath in new[]
        {
            ".claude-plugin/marketplace.json",
            ".agents/plugins/marketplace.json",
            "plugins/claude/idd-intent",
            "plugins/claude/idd-factory",
            "plugins/codex/idd-intent",
            "plugins/codex/idd-factory"
        })
        {
            var fullPath = Path.Combine(fixture.MarketplaceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (Path.HasExtension(fullPath)) fixture.AssertFile(fullPath);
            else fixture.AssertDirectory(fullPath);
        }

        foreach (var relativePath in new[]
        {
            "plugins/claude/idd",
            "plugins/codex/idd",
            "plugins/claude/idd-core",
            "plugins/codex/idd-core"
        })
            fixture.AssertMissing(Path.Combine(fixture.MarketplaceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static void AssertString(JsonElement element, string propertyName, string expected)
    {
        Assert.True(element.TryGetProperty(propertyName, out var property), $"Missing '{propertyName}'.");
        Assert.Equal(JsonValueKind.String, property.ValueKind);
        Assert.Equal(expected, property.GetString());
    }
}
