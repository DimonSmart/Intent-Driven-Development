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
        fixture.AssertFile(Path.Combine(
            intentRoot, "skills", "idd-project-init", "assets", "bootstrap", ".idd", "intent", "README.md"));

        fixture.AssertFile(Path.Combine(factoryRoot, "runtime", "idd-factory.dll"));
        fixture.AssertFile(Path.Combine(factoryRoot, "runtime", "factory.yaml"));
        fixture.AssertMissing(Path.Combine(factoryRoot, "runtime", "factory-workflow.yaml"));
        fixture.AssertDirectory(Path.Combine(factoryRoot, "assets", "bootstrap", ".idd", "factory"));
        fixture.AssertFile(Path.Combine(factoryRoot, "assets", "bootstrap", ".idd", "factory", ".gitignore"));

        using (var methodology = JsonDocument.Parse(fixture.ReadText(
                   Path.Combine(factoryRoot, "skills", "idd-factory-run", "references", "methodology-version.json"))))
        {
            Assert.Equal(1, methodology.RootElement.GetProperty("schemaVersion").GetInt32());
            AssertString(methodology.RootElement, "methodologyVersion", fixture.Version);
        }

        var runFrontMatter = GenerationFixture.ReadFrontMatter(fixture.ReadText(
            Path.Combine(factoryRoot, "skills", "idd-factory-run", "SKILL.md")));
        var workerSkills = new[] { "idd-factory-decompose-task", "idd-factory-execute-subtask" };

        if (platform == "claude")
        {
            Assert.DoesNotContain("context: fork", runFrontMatter, StringComparison.Ordinal);
            foreach (var skill in workerSkills)
            {
                var frontMatter = GenerationFixture.ReadFrontMatter(fixture.ReadText(
                    Path.Combine(factoryRoot, "skills", skill, "SKILL.md")));
                Assert.Contains("context: fork", frontMatter, StringComparison.Ordinal);
            }
        }
        else
        {
            foreach (var skill in workerSkills.Prepend("idd-factory-run"))
            {
                var frontMatter = GenerationFixture.ReadFrontMatter(fixture.ReadText(
                    Path.Combine(factoryRoot, "skills", skill, "SKILL.md")));
                foreach (var claudeField in new[] { "context:", "agent:", "allowed-tools:", "argument-hint:" })
                    Assert.DoesNotContain(claudeField, frontMatter, StringComparison.Ordinal);
            }
        }

        AssertIddMetadata(intentRoot, [], ".idd/intent");
        AssertIddMetadata(factoryRoot, ["idd-intent"], ".idd/factory");
    }

    [Fact]
    public void FactoryMetadata_HasNoGeneratedRoleSurface()
    {
        foreach (var platform in new[] { "claude", "codex" })
        {
            var root = Path.Combine(fixture.MarketplaceRoot, "plugins", platform, "idd-factory");
            using var metadata = JsonDocument.Parse(fixture.ReadText(Path.Combine(root, "idd-plugin.json")));
            if (metadata.RootElement.TryGetProperty("roleDefinitions", out var roleDefinitions))
                Assert.Equal(0, roleDefinitions.GetArrayLength());
            if (metadata.RootElement.TryGetProperty("skillRoleBindings", out var roleBindings))
                Assert.Equal(0, roleBindings.GetArrayLength());
        }

        var executorClaudeFrontMatter = GenerationFixture.ReadFrontMatter(fixture.ReadText(Path.Combine(
            fixture.MarketplaceRoot, "plugins", "claude", "idd-factory", "skills", "idd-factory-execute-subtask", "SKILL.md")));
        Assert.Contains("allowed-tools: [Read, Glob, Grep, Edit, Write, Bash]", executorClaudeFrontMatter, StringComparison.Ordinal);

        fixture.AssertMissing(Path.Combine(fixture.MarketplaceRoot, "plugins", "codex", "idd-factory", "agents"));
        fixture.AssertMissing(Path.Combine(fixture.MarketplaceRoot, "plugins", "codex", "idd-intent", "agents"));
    }

    [Fact]
    public void FactoryTransport_HasStructuredPlatformContracts()
    {
        var codexIntent = Path.Combine(fixture.MarketplaceRoot, "plugins", "codex", "idd-intent");
        var codexFactory = Path.Combine(fixture.MarketplaceRoot, "plugins", "codex", "idd-factory");
        var claudeFactory = Path.Combine(fixture.MarketplaceRoot, "plugins", "claude", "idd-factory");

        using (var manifest = JsonDocument.Parse(fixture.ReadText(Path.Combine(codexFactory, ".codex-plugin", "plugin.json"))))
            AssertString(manifest.RootElement, "mcpServers", "./.mcp.json");

        using (var intentManifest = JsonDocument.Parse(fixture.ReadText(Path.Combine(codexIntent, ".codex-plugin", "plugin.json"))))
            Assert.False(intentManifest.RootElement.TryGetProperty("mcpServers", out _));

        fixture.AssertMissing(Path.Combine(codexIntent, ".mcp.json"));
        fixture.AssertMissing(Path.Combine(claudeFactory, ".mcp.json"));

        using (var mcp = JsonDocument.Parse(fixture.ReadText(Path.Combine(codexFactory, ".mcp.json"))))
        {
            var factory = mcp.RootElement.GetProperty("mcpServers").GetProperty("factory");
            AssertString(factory, "command", "dotnet");
            AssertString(factory, "cwd", ".");
            Assert.Equal(10800, factory.GetProperty("tool_timeout_sec").GetInt32());
            Assert.Equal(["runtime/idd-factory.dll", "mcp"],
                factory.GetProperty("args").EnumerateArray().Select(value => value.GetString()).ToArray());
            Assert.Equal(["deferred", "code_mode"],
                factory.GetProperty("omit_tools_from").EnumerateArray().Select(value => value.GetString()).ToArray());
            Assert.Equal(
                [
                    "IDD_FACTORY_CODEX_EXECUTABLE",
                    "IDD_FACTORY_MODEL",
                    "IDD_FACTORY_REASONING_EFFORT",
                    "IDD_FACTORY_INHERIT_USER_SKILLS",
                    "IDD_FACTORY_CAPABILITY_PROFILE"
                ],
                factory.GetProperty("env_vars").EnumerateArray().Select(value => value.GetString()).ToArray());
        }

        var claudeSkill = fixture.ReadText(Path.Combine(claudeFactory, "skills", "idd-factory-run", "SKILL.md"));
        Assert.Contains("runtime/idd-factory.dll", claudeSkill, StringComparison.Ordinal);
        Assert.Contains("--request-stdin true", claudeSkill, StringComparison.Ordinal);
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
        var skillRoot = Path.Combine(pluginRoot, "skills");
        var actual = Directory.GetDirectories(skillRoot)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
        foreach (var skill in expected)
            fixture.AssertFile(Path.Combine(skillRoot, skill, "SKILL.md"));
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

    private void AssertIddMetadata(string pluginRoot, string[] expectedDependencies, string expectedAssetDestination)
    {
        using var document = JsonDocument.Parse(fixture.ReadText(Path.Combine(pluginRoot, "idd-plugin.json")));
        var root = document.RootElement;
        AssertString(root, "version", fixture.Version);
        Assert.False(root.TryGetProperty("skillReferences", out _));
        Assert.Equal(expectedDependencies,
            root.GetProperty("dependencies").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal([expectedAssetDestination],
            root.GetProperty("assets").EnumerateArray().Select(asset => asset.GetProperty("destination").GetString()).ToArray());
    }

    private static void AssertString(JsonElement element, string propertyName, string expected)
    {
        Assert.True(element.TryGetProperty(propertyName, out var property), $"Missing '{propertyName}'.");
        Assert.Equal(JsonValueKind.String, property.ValueKind);
        Assert.Equal(expected, property.GetString());
    }
}
