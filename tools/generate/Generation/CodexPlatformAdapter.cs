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
            ["author"] = new JsonObject { ["name"] = AuthorName },
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

        if (StringComparer.Ordinal.Equals(skillName, "idd-factory-run"))
        {
            var skill = files.Single(file => StringComparer.Ordinal.Equals(
                file.RelativePath,
                Path.Combine("skills", skillName, "SKILL.md")));
            files[files.IndexOf(skill)] = skill with
            {
                Content = ContentNormalizer.NormalizeContent(ContentNormalizer.JoinBlocks(
                    skill.Content!,
                    """
                    ## Codex native-agent orchestration

                    Use native Codex child-agent delegation for Factory semantic work.

                    - Spawn every planner and worker in a fresh semantic context with
                      parent-history inheritance explicitly disabled. Use
                      `fork_turns = "none"` or the host-equivalent setting; never rely
                      on an inheritance default.
                    - Prefer a read-only planner and a workspace-writing worker when the
                      host exposes those sandbox choices.
                    - Use the native `spawn_agent` operation (or its current native
                      equivalent) and a blocking/event-driven `wait_agent` operation.
                      Prefer one long wait on the critical path. Do not build a
                      short-wait/status-polling loop that consumes parent model turns.
                    - If a wait fails or the host times out, do not start a replacement
                      worker while the previous child may still write to the workspace.
                      Stop or close the child natively when possible, leave the task in
                      `plan.md`, and end the current Factory invocation.
                    - If Codex cannot provide fresh/no-parent-history children, shared
                      repository access, terminal waiting without model polling, final
                      child results, and stop/close lifecycle control, Factory is
                      unsupported on that host. Do not emulate the missing capability
                      with a packaged runtime, MCP transport, shell process supervisor,
                      or polling protocol.
                    """))
            };
        }

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
            definitions.Add(new JsonObject
            {
                ["name"] = role.Name,
                ["tools"] = JsonStringArray(role.Tools.Select(RoleToolNames.GetName)),
                ["dispatchMode"] = "generic-subagent",
                ["roleDelivery"] = "prompt-reference",
                ["toolsEnforcement"] = "prompt-only"
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
                bindings.Add(new JsonObject { ["skill"] = skill, ["role"] = role, ["dispatchMode"] = "generic-subagent" });
        }

        return bindings;
    }

    protected override string BuildRole(RoleDefinition role)
    {
        var mappings = string.Join(
            "\n",
            role.Tools.Select(tool => $"- `{RoleToolNames.GetName(tool)}`: {DescribeCodexCapability(tool)}"));

        return ContentNormalizer.JoinBlocks(
            base.BuildRole(role),
            $"""
            ## Codex capability mapping

            The names in `Available tools` describe technical permissions, not
            literal Codex tool names. Use these runtime operations:

            {mappings}

            A capability is unavailable only when its mapped Codex operation is
            actually unavailable.
            """);
    }

    private static string DescribeCodexCapability(RoleTool tool) => tool switch
    {
        RoleTool.FileRead => "Read files using available file or shell operations.",
        RoleTool.FileWrite => "Create or modify files using available file-editing or shell operations.",
        RoleTool.CommandExecute => "Execute repository commands using the available command operation.",
        RoleTool.AgentSpawn => "Call the native Codex `spawn_agent` collaboration operation.",
        RoleTool.AgentWait => "Call the native Codex `wait_agent` collaboration operation; prefer one long event-driven wait over polling.",
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "Unknown role capability.")
    };

    protected override string BuildSkillFrontMatter(
        string skillName,
        SkillDescription skillDescription,
        AdapterConfig adapter,
        IReadOnlyList<RoleDefinition> roles) =>
        YamlFrontMatterWriter.BuildCodexSkillFrontMatter(skillName, skillDescription);
}
