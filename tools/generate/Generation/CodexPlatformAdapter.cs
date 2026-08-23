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
        if (StringComparer.Ordinal.Equals(pluginName, "idd-factory"))
        {
            pluginJson["mcpServers"] = "./.mcp.json";
        }

        return pluginJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    protected override IReadOnlyList<GeneratedFile> BuildAdditionalPluginFiles(string pluginName, string version)
    {
        var files = base.BuildAdditionalPluginFiles(pluginName, version).ToList();
        if (!StringComparer.Ordinal.Equals(pluginName, "idd-factory"))
        {
            return files;
        }

        var mcp = new JsonObject
        {
            ["mcpServers"] = new JsonObject
            {
                ["factory"] = new JsonObject
                {
                    ["command"] = "dotnet",
                    ["args"] = new JsonArray("runtime/idd-factory.dll", "mcp"),
                    ["cwd"] = ".",
                    ["tool_timeout_sec"] = 1800,
                    ["env_vars"] = new JsonArray(
                        "IDD_FACTORY_CODEX_EXECUTABLE",
                        "IDD_FACTORY_MODEL",
                        "IDD_FACTORY_REASONING_EFFORT",
                        "IDD_FACTORY_INHERIT_USER_SKILLS",
                        "IDD_FACTORY_CAPABILITY_PROFILE"),
                    ["omit_tools_from"] = new JsonArray("deferred", "code_mode")
                }
            }
        };
        files.Add(new GeneratedFile(".mcp.json", mcp.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n"));
        return files;
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
                    ## Codex launcher

                    Use the bundled direct `mcp__factory` tools:

                    - new run: `factory_run`
                    - continue without or with a clarification answer: `factory_continue`
                    - explicit cancellation: `factory_cancel`

                    Do not start `idd-factory.dll` through a shell. Do not use a
                    command execution, wait, write-stdin, or status-polling loop as
                    the Factory launcher. Do not use tool search for Factory tools
                    and do not enable Code Mode.

                    If the bundled Factory tools are unavailable, report that the
                    installed Codex host does not expose the bundled IDD Factory MCP
                    transport and that a supported Codex version is required. Do not
                    fall back to the shell launcher.
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
            {
                bindings.Add(new JsonObject { ["skill"] = skill, ["role"] = role, ["dispatchMode"] = "generic-subagent" });
            }
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

            Do not treat a semantic capability as unavailable merely because no
            runtime tool has the same name. A capability is unavailable only when
            its mapped Codex tool or operation is actually unavailable. In
            particular, use `spawn_agent` for `agent.spawn` and use `wait_agent`
            for `agent.wait`.
            Codex Multi-Agent V2 `wait_agent` is an event-driven wait for mailbox
            activity from any live agent; it does not take a child agent id. When a
            child result is on the critical path, prefer one long wait allowed by
            the host instead of repeated short waits or another status-polling loop.
            Before returning a dispatch-related `BLOCKED`, call `spawn_agent` or
            `wait_agent`, as applicable, and preserve the observed runtime error
            if it fails.
            """,
            """
            ## Codex role delivery

            Codex Factory roles are delivered to generic child agents through the
            dispatch message. A role is not a native custom agent type.
            """);
    }

    private static string DescribeCodexCapability(RoleTool tool) => tool switch
    {
        RoleTool.FileRead =>
            "Read files using the available shell or file-reading operations.",
        RoleTool.FileWrite =>
            "Create, modify, rename, or remove files using the available file-editing or shell operations.",
        RoleTool.CommandExecute =>
            "Execute repository commands using the available command-execution operation.",
        RoleTool.AgentSpawn =>
            "Call the Codex `spawn_agent` collaboration tool.",
        RoleTool.AgentWait =>
            "Call the Codex `wait_agent` collaboration tool. In Multi-Agent V2 it waits for mailbox activity from any live agent and does not take an agent id; prefer a long event-driven wait over short polling loops.",
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "Unknown role capability.")
    };

    protected override string BuildSkillFrontMatter(
        string skillName,
        SkillDescription skillDescription,
        AdapterConfig adapter,
        IReadOnlyList<RoleDefinition> roles) =>
        YamlFrontMatterWriter.BuildCodexSkillFrontMatter(skillName, skillDescription);
}
