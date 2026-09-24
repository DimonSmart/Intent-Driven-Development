using System.Text.Json;
using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class PlatformGenerationTests(GenerationFixture fixture)
{
    [Theory]
    [InlineData("claude", ".claude-plugin")]
    [InlineData("codex", ".codex-plugin")]
    public void PlatformPlugins_MatchCanonicalStructureAndMachineMetadata(string platform, string manifestDirectory)
    {
        var intentRoot = Path.Combine(fixture.MarketplaceRoot, "plugins", platform, "idd-intent");
        var factoryRoot = Path.Combine(fixture.MarketplaceRoot, "plugins", platform, "idd-factory");

        AssertPluginManifest(intentRoot, manifestDirectory, "idd-intent", platform);
        AssertPluginManifest(factoryRoot, manifestDirectory, "idd-factory", platform);
        AssertGeneratedSkillLayout(intentRoot, "idd-intent");
        AssertGeneratedSkillLayout(factoryRoot, "idd-factory");

        fixture.AssertFile(Path.Combine(intentRoot, "assets", "bootstrap", ".idd", "intent", "README.md"));
        fixture.AssertFile(Path.Combine(intentRoot, "assets", "bootstrap", ".idd", "engineering", "README.md"));
        fixture.AssertFile(Path.Combine(intentRoot, "assets", "bootstrap", ".idd", "engineering", "INDEX.md"));
        fixture.AssertFile(Path.Combine(
            intentRoot, "skills", "idd-project-init", "assets", "bootstrap", ".idd", "intent", "README.md"));
        fixture.AssertMissing(Path.Combine(
            intentRoot, "skills", "idd-project-init", "assets", "bootstrap", ".idd", "engineering"));
        fixture.AssertFile(Path.Combine(
            intentRoot, "skills", "idd-engineering-change", "assets", "bootstrap", ".idd", "engineering", "README.md"));
        fixture.AssertFile(Path.Combine(
            intentRoot, "skills", "idd-engineering-change", "assets", "bootstrap", ".idd", "engineering", "INDEX.md"));
        fixture.AssertMissing(Path.Combine(
            intentRoot, "skills", "idd-engineering-change", "assets", "bootstrap", ".idd", "intent"));

        fixture.AssertMissing(Path.Combine(factoryRoot, "runtime"));
        fixture.AssertMissing(Path.Combine(factoryRoot, ".mcp.json"));
        fixture.AssertDirectory(Path.Combine(factoryRoot, "assets", "bootstrap", ".idd", "factory"));
        fixture.AssertFile(Path.Combine(factoryRoot, "assets", "bootstrap", ".idd", "factory", ".gitignore"));

        using (var methodology = JsonDocument.Parse(fixture.ReadText(
                   Path.Combine(factoryRoot, "skills", "idd-factory-run", "references", "methodology-version.json"))))
        {
            Assert.Equal(1, methodology.RootElement.GetProperty("schemaVersion").GetInt32());
            AssertString(methodology.RootElement, "methodologyVersion", fixture.Version);
        }

        AssertIddMetadata(intentRoot, [], [".idd/intent", ".idd/engineering"]);
        AssertIddMetadata(factoryRoot, ["idd-intent"], [".idd/factory"]);
    }

    [Fact]
    public void FactoryPlugin_HasNoRuntimeOrMcpTransport()
    {
        var codexFactory = Path.Combine(fixture.MarketplaceRoot, "plugins", "codex", "idd-factory");
        var claudeFactory = Path.Combine(fixture.MarketplaceRoot, "plugins", "claude", "idd-factory");

        using (var manifest = JsonDocument.Parse(fixture.ReadText(
                   Path.Combine(codexFactory, ".codex-plugin", "plugin.json"))))
        {
            Assert.False(manifest.RootElement.TryGetProperty("mcpServers", out _));
        }

        fixture.AssertMissing(Path.Combine(codexFactory, ".mcp.json"));
        fixture.AssertMissing(Path.Combine(claudeFactory, ".mcp.json"));
        fixture.AssertMissing(Path.Combine(codexFactory, "runtime"));
        fixture.AssertMissing(Path.Combine(claudeFactory, "runtime"));
    }

    [Fact]
    public void FactoryMetadata_HasOnlyCanonicalFourSkillsAndNoGeneratedRoleSurface()
    {
        foreach (var platform in new[] { "claude", "codex" })
        {
            var root = Path.Combine(fixture.MarketplaceRoot, "plugins", platform, "idd-factory");
            var skills = Directory.GetDirectories(Path.Combine(root, "skills"))
                .Select(Path.GetFileName)
                .Where(name => name is not null)
                .Select(name => name!)
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(
                new[]
                {
                    "idd-factory-configure",
                    "idd-factory-decompose-task",
                    "idd-factory-execute-subtask",
                    "idd-factory-run"
                },
                skills);

            using var metadata = JsonDocument.Parse(fixture.ReadText(Path.Combine(root, "idd-plugin.json")));
            if (metadata.RootElement.TryGetProperty("roleDefinitions", out var roleDefinitions))
                Assert.Equal(0, roleDefinitions.GetArrayLength());
            if (metadata.RootElement.TryGetProperty("skillRoleBindings", out var roleBindings))
                Assert.Equal(0, roleBindings.GetArrayLength());
        }
    }

    private void AssertGeneratedSkillLayout(string pluginRoot, string pluginName)
    {
        var manifestPath = Path.Combine(fixture.RepoRoot, "src", "canonical", "plugins", "plugin-manifest.json");
        using var manifest = JsonDocument.Parse(fixture.ReadText(manifestPath));
        var expected = manifest.RootElement
            .GetProperty("plugins")
            .GetProperty(pluginName)
            .GetProperty("skills")
            .EnumerateArray()
            .Select(skill => skill.GetString()!)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var actual = Directory.GetDirectories(Path.Combine(pluginRoot, "skills"))
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
        foreach (var skill in expected)
            fixture.AssertFile(Path.Combine(pluginRoot, "skills", skill, "SKILL.md"));
    }

    private void AssertPluginManifest(string pluginRoot, string manifestDirectory, string pluginName, string platform)
    {
        using var document = JsonDocument.Parse(fixture.ReadText(Path.Combine(pluginRoot, manifestDirectory, "plugin.json")));
        AssertString(document.RootElement, "name", pluginName);
        AssertString(document.RootElement, "version", fixture.Version);

        if (platform == "codex")
        {
            AssertString(document.RootElement.GetProperty("interface"), "displayName",
                pluginName == "idd-intent" ? "IDD Intent" : "IDD Factory");
            AssertString(document.RootElement, "skills", "./skills/");
        }
    }

    private void AssertIddMetadata(string pluginRoot, string[] expectedDependencies, string[] expectedAssetDestinations)
    {
        using var document = JsonDocument.Parse(fixture.ReadText(Path.Combine(pluginRoot, "idd-plugin.json")));
        var root = document.RootElement;
        AssertString(root, "version", fixture.Version);
        Assert.Equal(expectedDependencies,
            root.GetProperty("dependencies").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(expectedAssetDestinations,
            root.GetProperty("assets").EnumerateArray().Select(asset => asset.GetProperty("destination").GetString()).ToArray());
    }

    private static void AssertString(JsonElement element, string propertyName, string expected)
    {
        Assert.True(element.TryGetProperty(propertyName, out var property), $"Missing '{propertyName}'.");
        Assert.Equal(JsonValueKind.String, property.ValueKind);
        Assert.Equal(expected, property.GetString());
    }
}
