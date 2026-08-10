using System.Text.Json;
using System.Text.Json.Nodes;

internal sealed class ClaudePlatformAdapter : PlatformPluginBuilder
{
    public override string Platform => "claude";
    protected override string ManifestDirectory => ".claude-plugin";
    protected override string ManifestFileName => "plugin.json";

    public override GeneratedFile BuildMarketplaceFile(PluginManifest manifest, string version) =>
        ClaudeMarketplaceBuilder.Build(manifest, version);

    protected override IReadOnlyList<GeneratedFile> BuildSkillFiles(
        AdapterConfig adapter,
        PluginDefinition plugin,
        IReadOnlyDictionary<string, RoleDefinition> roleDefinitions,
        string skillName,
        IReadOnlyDictionary<string, SkillDescription> skillDescriptions)
    {
        var files = base.BuildSkillFiles(adapter, plugin, roleDefinitions, skillName, skillDescriptions).ToList();
        if (StringComparer.Ordinal.Equals(skillName, "idd-factory-run") ||
            StringComparer.Ordinal.Equals(skillName, "idd-factory-coordinate-step"))
        {
            files.Add(new GeneratedFile(
                Path.Combine("skills", skillName, "references", "platform-dispatch.md"),
                ContentNormalizer.NormalizeContent(RequiredFileReader.Read(
                    "src/adapters/claude/factory-dispatch.md"))));
        }

        return files;
    }

    protected override string BuildPluginManifest(
        AdapterConfig adapter,
        string pluginName,
        PluginDefinition plugin,
        string version)
    {
        var pluginJson = new JsonObject
        {
            ["name"] = pluginName,
            ["description"] = plugin.Description,
            ["version"] = version,
            ["author"] = new JsonObject
            {
                ["name"] = AuthorName
            },
            ["repository"] = RepositoryUrl,
            ["license"] = "MIT"
        };

        return pluginJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    protected override string BuildSkillFrontMatter(
        string skillName,
        SkillDescription skillDescription,
        AdapterConfig adapter,
        IReadOnlyList<RoleDefinition> roles) =>
        YamlFrontMatterWriter.BuildClaudeSkillFrontMatter(
            skillName,
            skillDescription,
            adapter,
            ClaudeRoleToolMapper.Map(roles));
}
