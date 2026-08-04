using System.Text.Json;
using System.Text.Json.Nodes;

internal sealed class CodexPlatformAdapter : PlatformPluginBuilder
{
    public override string Platform => "codex";
    protected override string ManifestDirectory => ".codex-plugin";
    protected override string ManifestFileName => "plugin.json";

    public override GeneratedFile BuildMarketplaceFile(PluginManifest manifest, string version) =>
        CodexMarketplaceBuilder.Build(manifest);

    public override IReadOnlyList<GeneratedFile> BuildPluginFiles(
        AdapterDefinition adapterDefinition,
        PluginManifest manifest,
        string pluginName,
        PluginDefinition plugin,
        IReadOnlyDictionary<string, RoleDefinition> roleDefinitions,
        IReadOnlyDictionary<string, SkillDescription> skillDescriptions,
        string version)
    {
        var files = base.BuildPluginFiles(
            adapterDefinition, manifest, pluginName, plugin, roleDefinitions, skillDescriptions, version).ToList();

        foreach (var roleName in plugin.Roles.OrderBy(name => name, StringComparer.Ordinal))
        {
            var role = roleDefinitions[roleName];
            files.Add(new GeneratedFile(Path.Combine("agents", role.Name + ".toml"), BuildAgentDefinition(role)));
        }

        return files;
    }

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

    protected override string BuildIddPluginMetadata(
        AdapterConfig adapter,
        PluginDefinition plugin,
        IReadOnlyDictionary<string, RoleDefinition> roleDefinitions,
        string version)
    {
        var metadata = new JsonObject
        {
            ["version"] = version,
            ["platform"] = adapter.CodingAgent,
            ["dependencies"] = JsonStringArray(plugin.Dependencies),
            ["roles"] = JsonStringArray(plugin.Roles),
            ["roleDefinitions"] = BuildRoleDefinitions(plugin, roleDefinitions),
            ["skillRoleBindings"] = BuildSkillRoleBindings(plugin),
            ["assets"] = BuildAssets(plugin),
            ["canonicalSource"] = "src/canonical"
        };

        return metadata.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    protected override JsonArray BuildRoleDefinitions(
        PluginDefinition plugin,
        IReadOnlyDictionary<string, RoleDefinition> roleDefinitions)
    {
        var definitions = new JsonArray();
        foreach (var roleName in plugin.Roles)
        {
            var role = roleDefinitions[roleName];
            var tools = new JsonArray();
            foreach (var mapping in CodexRoleToolMapper.Map(role.Tools))
            {
                tools.Add(new JsonObject
                {
                    ["name"] = RoleToolNames.GetName(mapping.Tool),
                    ["enforcement"] = mapping.PromptOnly ? "prompt-only" : "native"
                });
            }

            definitions.Add(new JsonObject
            {
                ["name"] = role.Name,
                ["agentType"] = role.Name,
                // Keep the existing string array for metadata consumers.
                ["tools"] = JsonStringArray(role.Tools.Select(RoleToolNames.GetName)),
                ["toolDefinitions"] = tools
            });
        }

        return definitions;
    }

    private static JsonArray BuildSkillRoleBindings(PluginDefinition plugin)
    {
        var bindings = new JsonArray();
        foreach (var (skill, roles) in plugin.SkillRoleReferences.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            foreach (var role in roles)
            {
                bindings.Add(new JsonObject { ["skill"] = skill, ["role"] = role, ["agentType"] = role });
            }
        }

        return bindings;
    }

    private string BuildAgentDefinition(RoleDefinition role)
    {
        var prompt = ContentNormalizer.NormalizeContent(BuildCodexRolePrompt(role)).TrimEnd('\n');
        return $"""
            name = {JsonSerializer.Serialize(role.Name)}
            description = {JsonSerializer.Serialize($"IDD Factory role: {role.Name}.")}
            developer_instructions = {JsonSerializer.Serialize(prompt)}
            """.ReplaceLineEndings("\n") + "\n";
    }

    private string BuildCodexRolePrompt(RoleDefinition role)
    {
        var prompt = BuildRole(role);
        if (!role.Tools.Contains(RoleTool.AgentSpawn))
        {
            return prompt;
        }

        return ContentNormalizer.JoinBlocks(
            prompt,
            """
            ## Codex dispatch

            In Codex, dispatch means calling `spawn_agent` with the required
            registered `agent_type`, then waiting for that child agent's result.
            """);
    }

    protected override string BuildSkillFrontMatter(
        string skillName,
        SkillDescription skillDescription,
        AdapterConfig adapter,
        IReadOnlyList<RoleDefinition> roles) =>
        YamlFrontMatterWriter.BuildCodexSkillFrontMatter(skillName, skillDescription);
}
