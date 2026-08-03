using System.Text.Json;
using System.Text.Json.Nodes;

internal sealed class CodexPlatformAdapter : PlatformPluginBuilder
{
    public override string Platform => "codex";
    protected override string ManifestDirectory => ".codex-plugin";
    protected override string ManifestFileName => "plugin.json";

    public override GeneratedFile BuildMarketplaceFile(PluginManifest manifest, string version) =>
        CodexMarketplaceBuilder.Build(manifest);

    protected override string BuildPluginManifest(
        AdapterConfig adapter,
        string pluginName,
        PluginDefinition plugin,
        string version)
    {
        var displayName = pluginName switch
        {
            "idd-intent" => "IDD Intent",
            "idd-factory" => "IDD Factory",
            _ => DisplayName(pluginName)
        };

        var pluginJson = new JsonObject
        {
            ["name"] = pluginName,
            ["version"] = version,
            ["description"] = plugin.Description,
            ["skills"] = "./skills/",
            ["author"] = new JsonObject
            {
                ["name"] = AuthorName
            },
            ["repository"] = RepositoryUrl,
            ["license"] = "MIT",
            ["interface"] = new JsonObject
            {
                ["displayName"] = displayName,
                ["shortDescription"] = plugin.Description
            }
        };

        return pluginJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    protected override IReadOnlyList<GeneratedFile> BuildSkillFiles(
        AdapterConfig adapter,
        PluginDefinition plugin,
        IReadOnlyDictionary<string, RoleDefinition> roleDefinitions,
        string skillName,
        IReadOnlyDictionary<string, SkillDescription> skillDescriptions)
    {
        var files = base.BuildSkillFiles(adapter, plugin, roleDefinitions, skillName, skillDescriptions).ToList();
        if (skillDescriptions.TryGetValue(skillName, out var skillDescription) &&
            skillDescription.Invocation == SkillInvocation.Manual)
        {
            files.Add(new GeneratedFile(
                Path.Combine("skills", skillName, "agents", "openai.yaml"),
                """
                policy:
                  allow_implicit_invocation: false
                """.ReplaceLineEndings("\n") + "\n"));
        }

        return files;
    }

    protected override IReadOnlyList<GeneratedFile> BuildAdditionalPluginFiles(string pluginName, string version)
    {
        if (!StringComparer.Ordinal.Equals(pluginName, "idd-factory"))
        {
            return [];
        }

        var methodologyVersion = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["methodologyVersion"] = version
        };
        return [new GeneratedFile(
            Path.Combine("skills", "idd-factory-run", "references", "methodology-version.json"),
            methodologyVersion.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n")];
    }

    protected override string BuildSkillFrontMatter(
        string skillName,
        SkillDescription skillDescription,
        AdapterConfig adapter,
        IReadOnlyList<RoleDefinition> roles) =>
        YamlFrontMatterWriter.BuildCodexSkillFrontMatter(skillName, skillDescription);
}
