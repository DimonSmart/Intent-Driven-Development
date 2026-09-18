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
        if (!StringComparer.Ordinal.Equals(skillName, "idd-factory-run")) return files;

        var skill = files.Single(file => StringComparer.Ordinal.Equals(
            file.RelativePath,
            Path.Combine("skills", skillName, "SKILL.md")));
        files[files.IndexOf(skill)] = skill with
        {
            Content = ContentNormalizer.NormalizeContent(ContentNormalizer.JoinBlocks(
                skill.Content!,
                """
                ## Claude native-agent capability

                Use a native Claude fork/subagent mechanism only when the host can
                provide every Factory capability required by the canonical skill:
                a fresh semantic context with controlled input and no substantial
                inherited parent transcript, shared repository access, terminal
                waiting without model-driven polling, a final child result, and
                stop/close lifecycle control.

                When those properties cannot be guaranteed, report Factory as
                unsupported on the current Claude host. Do not restore or emulate
                Factory through the removed .NET runtime, a packaged CLI launcher,
                MCP transport, process supervision, or a status-polling loop.
                """))
        };
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
            ["author"] = new JsonObject { ["name"] = AuthorName },
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
