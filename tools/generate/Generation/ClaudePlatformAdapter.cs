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
                ## Claude launcher

                Resolve the installed plugin root as two parent directories above
                this `SKILL.md`. Invoke the packaged runtime with the platform shell:

                ```text
                dotnet <plugin-root>/runtime/idd-factory.dll run
                  --workspace <absolute-workspace>
                  --request-stdin true
                  --plugin-root <plugin-root>
                ```

                Pipe the exact request as UTF-8 standard input, wait for process
                exit, and parse the single structured outcome. Use `continue` with
                the same workspace and plugin root; when supplying an answer, write
                it to a temporary UTF-8 file and pass `--answer-file`. Use `cancel`
                for explicit cancellation. Always remove launcher-owned temporary
                files. Do not search for a repository-local runtime.
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
