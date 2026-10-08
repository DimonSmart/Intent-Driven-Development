using System.Text.Json;
using Idd.Generation.Tests.Infrastructure;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class SkillExposureTests(GenerationFixture fixture)
{
    private static readonly HashSet<string> Commands = new(StringComparer.Ordinal)
    {
        "idd-help", "idd-route", "idd-project-init", "idd-verification-configure",
        "idd-glossary-build", "idd-skip", "idd-factory-run", "idd-factory-configure"
    };

    private static readonly HashSet<string> Manual = new(StringComparer.Ordinal)
    {
        "idd-help", "idd-project-init", "idd-verification-configure",
        "idd-glossary-build", "idd-skip", "idd-factory-configure"
    };

    [Fact]
    public void AllRegisteredSkills_KeepTheirInvocationAndPlatformAvailability()
    {
        var inventory = new SkillDescriptionReader().Read(
            Path.Combine(fixture.RepoRoot, "src", "canonical", "skills", "skill-descriptions.json"),
            new HashSet<string>(StringComparer.Ordinal) { "claude", "codex" });
        Assert.Equal(19, inventory.Count);
        Assert.Equal(8, inventory.Count(skill => skill.Value.Exposure == SkillExposure.Command));
        Assert.Equal(11, inventory.Count(skill => skill.Value.Exposure == SkillExposure.Workflow));
        Assert.Equal(6, inventory.Count(skill => skill.Value.Invocation == SkillInvocation.Manual));
        Assert.Equal(13, inventory.Count(skill => skill.Value.Invocation == SkillInvocation.Auto));

        foreach (var (name, description) in inventory)
        {
            Assert.Equal(Commands.Contains(name) ? SkillExposure.Command : SkillExposure.Workflow, description.Exposure);
            Assert.Equal(Manual.Contains(name) ? SkillInvocation.Manual : SkillInvocation.Auto, description.Invocation);
            foreach (var platform in new[] { "claude", "codex" })
            {
                var plugin = name.StartsWith("idd-factory-", StringComparison.Ordinal) ? "idd-factory" : "idd-intent";
                var folder = Path.Combine(fixture.MarketplaceRoot, "plugins", platform, plugin, "skills", name);
                var skill = fixture.ReadText(Path.Combine(folder, "SKILL.md"));
                Assert.Contains("#", skill);

                if (platform == "claude")
                {
                    var yaml = ParseYaml(GenerationFixture.ReadFrontMatter(skill));
                    AssertScalar(yaml, "user-invocable", Commands.Contains(name) ? "true" : "false");
                    AssertScalar(yaml, "disable-model-invocation", Manual.Contains(name) ? "true" : "false");
                    Assert.Equal(1, yaml.Children.Keys.Count(key => ((YamlScalarNode)key).Value == "user-invocable"));
                    Assert.Equal(1, yaml.Children.Keys.Count(key => ((YamlScalarNode)key).Value == "disable-model-invocation"));
                }
                else
                {
                    var metadata = Path.Combine(folder, "agents", "openai.yaml");
                    if (Manual.Contains(name))
                    {
                        var text = fixture.ReadText(metadata);
                        var yaml = ParseYaml(text);
                        var ui = Assert.IsType<YamlMappingNode>(yaml.Children[new YamlScalarNode("interface")]);
                        Assert.False(string.IsNullOrWhiteSpace(GetScalar(ui, "display_name")));
                        Assert.False(string.IsNullOrWhiteSpace(GetScalar(ui, "short_description")));
                        var policy = Assert.IsType<YamlMappingNode>(yaml.Children[new YamlScalarNode("policy")]);
                        AssertScalar(policy, "allow_implicit_invocation", "false");
                    }
                    else
                    {
                        fixture.AssertMissing(metadata);
                    }
                }
            }
        }

        foreach (var platform in new[] { "claude", "codex" })
        {
            var root = Path.Combine(fixture.MarketplaceRoot, "plugins", platform);
            Assert.Equal(17, Directory.GetDirectories(Path.Combine(root, "idd-intent", "skills")).Length);
            Assert.Equal(2, Directory.GetDirectories(Path.Combine(root, "idd-factory", "skills")).Length);
        }
    }

    [Theory]
    [InlineData("\"Example\"", "Auto", "Command")]
    [InlineData("{\"description\":\"Example\"}", "Auto", "Command")]
    [InlineData("{\"description\":\"Example\",\"invocation\":\"manual\",\"exposure\":\"command\"}", "Manual", "Command")]
    [InlineData("{\"description\":\"Example\",\"exposure\":\"workflow\"}", "Auto", "Workflow")]
    public void Reader_AcceptsCompatibleDescriptions(string description, string expectedInvocation, string expectedExposure)
    {
        var value = ReadDescription(description);
        Assert.Equal(expectedInvocation, value.Invocation.ToString());
        Assert.Equal(expectedExposure, value.Exposure.ToString());
    }

    [Theory]
    [InlineData("{\"description\":\"Example\",\"invocation\":\"manual\",\"exposure\":\"workflow\"}")]
    [InlineData("{\"description\":\"Example\",\"exposure\":null}")]
    [InlineData("{\"description\":\"Example\",\"exposure\":\"\"}")]
    [InlineData("{\"description\":\"Example\",\"exposure\":\"unsupported\"}")]
    [InlineData("{\"description\":\"Example\",\"exposure\":5}")]
    [InlineData("{\"description\":\"Example\",\"exposure\":false}")]
    [InlineData("{\"description\":\"Example\",\"exposure\":[]}")]
    [InlineData("{\"description\":\"Example\",\"exposure\":{}}")]
    [InlineData("{\"description\":\"Example\",\"exposure\":\"command\",\"exposure\":\"workflow\"}")]
    [InlineData("{\"description\":\"Example\",\"interface\":null}")]
    [InlineData("{\"description\":\"Example\",\"interface\":{\"displayName\":\"Demo\"}}")]
    [InlineData("{\"description\":\"Example\",\"interface\":{\"displayName\":\"Demo\",\"shortDescription\":\"\"}}")]
    [InlineData("{\"description\":\"Example\",\"interface\":{\"displayName\":\"Demo\",\"shortDescription\":\"Short\",\"extra\":\"Bad\"}}")]
    [InlineData("{\"description\":\"Example\",\"interface\":{\"displayName\":\"Demo\",\"displayName\":\"Other\",\"shortDescription\":\"Short\"}}")]
    public void Reader_RejectsInvalidPolicyAndUiMetadata(string description)
    {
        var error = Assert.Throws<InvalidOperationException>(() => ReadDescription(description));
        Assert.Contains("idd-intent-example", error.Message);
    }

    [Fact]
    public void Reader_AcceptsExplicitInterfaceAndRejectsDuplicatedAdapterMetadata()
    {
        var valid = ReadDescription("""
            {"description":"Example","interface":{"displayName":"A: B","shortDescription":"Quoted \"text\""}}
            """);
        Assert.Equal("A: B", valid.Interface?.DisplayName);
        Assert.Equal("Quoted \"text\"", valid.Interface?.ShortDescription);

        var invalid = """
            {"description":"Example","adapters":{"claude":{"frontmatter":{"context":"fork","context":"other"}}}}
            """;
        Assert.Contains("Duplicate JSON property", Assert.Throws<InvalidOperationException>(
            () => ReadDescription(invalid)).Message);
    }

    [Theory]
    [InlineData("user-invocable", "false", "true")]
    [InlineData("disable-model-invocation", "true", "false")]
    [InlineData("user-invocable", "\"false\"", "true")]
    public void ClaudeWriter_RejectsConflictingOrMismatchedTypes(string field, string actual, string expected)
    {
        using var value = JsonDocument.Parse(actual);
        var adapterMetadata = new AdapterSkillMetadata(
            new Dictionary<string, JsonElement> { [field] = value.RootElement.Clone() });
        var description = new SkillDescription("Example", SkillInvocation.Auto, SkillExposure.Command, null,
            new Dictionary<string, AdapterSkillMetadata> { ["claude"] = adapterMetadata });
        var adapter = new AdapterConfig("claude", "", null, true, true);

        var exception = Assert.Throws<InvalidOperationException>(
            () => YamlFrontMatterWriter.BuildClaudeSkillFrontMatter("idd-intent-example", description, adapter, []));
        Assert.Contains(field, exception.Message);
        Assert.Contains("idd-intent-example", exception.Message);
        Assert.Contains("claude", exception.Message);
        Assert.Contains(expected, exception.Message);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("null")]
    [InlineData("yes")]
    [InlineData("42")]
    [InlineData("- item")]
    public void ClaudeWriter_QuotesAmbiguousAdapterStrings(string value)
    {
        using var element = JsonDocument.Parse(JsonSerializer.Serialize(value));
        var metadata = new AdapterSkillMetadata(
            new Dictionary<string, JsonElement> { ["argument-hint"] = element.RootElement.Clone() });
        var description = new SkillDescription("Example", SkillInvocation.Auto, SkillExposure.Command, null,
            new Dictionary<string, AdapterSkillMetadata> { ["claude"] = metadata });
        var yaml = YamlFrontMatterWriter.BuildClaudeSkillFrontMatter("idd-intent-example", description,
            new AdapterConfig("claude", "", null, true, true), []);
        var scalar = Assert.IsType<YamlScalarNode>(
            ParseYaml(yaml).Children[new YamlScalarNode("argument-hint")]);

        Assert.Equal(value, scalar.Value);
        Assert.Equal(YamlDotNet.Core.ScalarStyle.DoubleQuoted, scalar.Style);
    }

    [Fact]
    public void ClaudeWriter_AcceptsMatchingAdapterFieldsWithoutDuplicates()
    {
        using var element = JsonDocument.Parse("true");
        var metadata = new AdapterSkillMetadata(
            new Dictionary<string, JsonElement> { ["user-invocable"] = element.RootElement.Clone() });
        var description = new SkillDescription("Example", SkillInvocation.Auto, SkillExposure.Command, null,
            new Dictionary<string, AdapterSkillMetadata> { ["claude"] = metadata });
        var yaml = YamlFrontMatterWriter.BuildClaudeSkillFrontMatter("idd-intent-example", description,
            new AdapterConfig("claude", "", null, true, true), []);

        Assert.Contains("user-invocable: true", yaml);
        Assert.Equal(1, yaml.Split('\n').Count(line => line.StartsWith("user-invocable:", StringComparison.Ordinal)));
        AssertScalar(ParseYaml(yaml), "disable-model-invocation", "false");
    }

    private static SkillDescription ReadDescription(string description)
    {
        var filename = Path.GetTempFileName();
        try
        {
            File.WriteAllText(filename, "{\"idd-intent-example\":" + description + "}");
            return new SkillDescriptionReader().Read(filename, new HashSet<string> { "claude", "codex" })["idd-intent-example"];
        }
        finally
        {
            File.Delete(filename);
        }
    }

    private static YamlMappingNode ParseYaml(string text)
    {
        var yamlText = text.Trim();
        if (yamlText.StartsWith("---\n", StringComparison.Ordinal))
            yamlText = yamlText[4..];
        if (yamlText.EndsWith("\n---", StringComparison.Ordinal))
            yamlText = yamlText[..^4];

        var stream = new YamlStream();
        stream.Load(new StringReader(yamlText));
        var document = Assert.Single(stream.Documents);
        return Assert.IsType<YamlMappingNode>(document.RootNode);
    }

    private static string? GetScalar(YamlMappingNode node, string key) =>
        Assert.IsType<YamlScalarNode>(node.Children[new YamlScalarNode(key)]).Value;

    private static void AssertScalar(YamlMappingNode node, string key, string expected) =>
        Assert.Equal(expected, GetScalar(node, key));
}
