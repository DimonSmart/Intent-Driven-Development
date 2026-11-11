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
                    ## Codex generic Factory subagents

                    Use the host's native generic child-agent spawn. A Factory-specific
                    agent_type is not required. Resolve the internal Planner and Worker
                    SKILL.md files relative to THIS installed Factory Run Skill; pass
                    absolute paths and a bounded assignment to each new child. Children
                    read the complete installed Skill themselves before other work.
                    Use fork_turns: "none" for every spawn, including Planner.
                    Never send protocol bodies or previous child transcripts.

                    Inspect the direct host-visible tool schema, including tools not
                    exposed inside functions.exec/ALL_TOOLS. An absent agent_type is
                    NOT a capability failure. If the host requires a built-in generic
                    agent type, use that built-in type, never a registered Factory role.
                    Check fresh context, shared workspace/installed files, terminal
                    results, native wait and stop/interrupt. Prefer read-only Planner
                    permissions if available but do not confuse a text guard with a
                    technical sandbox. Diagnose missing mandatory capabilities precisely.

                    Reserve unique task_name arguments idd_factory_planner_01,
                    idd_factory_worker_01, etc. Rejected attempts consume their names.
                    Example without model overrides:
                    {"task_name":"idd_factory_worker_01","fork_turns":"none","message":"Read the entire installed Worker SKILL.md at <absolute path>, then perform exactly this bounded Factory assignment: <one Task packet>"}.
                    Replace placeholders with real paths and task data.
                    Never add an unsupported spawn argument.

                    If an explicit Codex policy mapping is active, pass the exact
                    codex.model as model and codex.reasoningEffort as reasoning_effort;
                    inherit omits both overrides. No prompt-based switching or fallback.
                    Wait natively without short model-driven polling. On timeout or
                    failure, stop/interrupt the writer and confirm termination before
                    starting another; preserve the pending Task. A final Planner Done
                    always requires the configured final verification checks.
                    """))
            };
        }

        if (StringComparer.Ordinal.Equals(skillName, "idd-factory-configure"))
        {
            var skill = files.Single(file => StringComparer.Ordinal.Equals(
                file.RelativePath,
                Path.Combine("skills", skillName, "SKILL.md")));
            files[files.IndexOf(skill)] = skill with
            {
                Content = ContentNormalizer.NormalizeContent(ContentNormalizer.JoinBlocks(
                    skill.Content!,
                    """
                    ## Codex Factory installation compatibility

                    Factory Planner and Worker are shipped as internal Skills in the
                    same installed idd-factory package. They run in fresh generic
                    child agents, without installing roles or choosing a registry
                    scope. Older project/user custom Factory agent files are ignored
                    by this workflow and must not be modified automatically. If asked
                    to register an old Factory role, explain the changed mechanism;
                    do not create .idd/execution.yaml as a side effect.

                    ## Codex Factory model configuration

                    Use native `request_user_input` for blocking configuration
                    choices when it is available; do not reproduce its tool schema.
                    Prefer a native host model/capability catalog when Codex exposes
                    one, then current platform-provided model information or current
                    documentation/knowledge, and finally concrete model IDs supplied
                    by the user. Do not claim account-specific availability unless
                    the host can verify it.

                    Persist Codex mappings under the `codex` platform key with a
                    concrete `model` and optional `reasoningEffort`. Never embed a
                    recommended concrete model ID in generated guidance, and never
                    save an automatically proposed mapping before user confirmation.
                    """))
            };
        }

        if (skillDescriptions.TryGetValue(skillName, out var skillDescription) &&
            skillDescription.Exposure == SkillExposure.Command)
        {
            files.Add(new GeneratedFile(
                Path.Combine("skills", skillName, "agents", "openai.yaml"),
                BuildAgentMetadata(skillName, skillDescription)));
        }

        return files;
    }

    private static string BuildAgentMetadata(string skillName, SkillDescription description)
    {
        var displayName = description.Interface?.DisplayName ?? BuildFallbackDisplayName(skillName);
        var shortDescription = description.Interface?.ShortDescription ??
            BuildFallbackShortDescription(description.Description);

        return string.Join("\n",
            "interface:",
            $"  display_name: {YamlFrontMatterWriter.QuoteYamlString(displayName)}",
            $"  short_description: {YamlFrontMatterWriter.QuoteYamlString(shortDescription)}",
            "",
            "policy:",
            $"  allow_implicit_invocation: {(description.Invocation == SkillInvocation.Auto ? "true" : "false")}",
            "");
    }

    private static string BuildFallbackDisplayName(string skillName) =>
        string.Join(' ', skillName.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part == "idd"
                ? "IDD"
                : char.ToUpperInvariant(part[0]) + part[1..]));

    private static string BuildFallbackShortDescription(string description)
    {
        var sentence = description.Trim().Split(['\r', '\n'], 2)[0].Trim();
        var sentenceEnd = sentence.IndexOf(". ", StringComparison.Ordinal);
        if (sentenceEnd >= 0)
            sentence = sentence[..(sentenceEnd + 1)];

        if (sentence.Length <= 120)
            return sentence;

        var lastWordBoundary = sentence.LastIndexOf(' ', 116);
        return lastWordBoundary > 0
            ? sentence[..lastWordBoundary].TrimEnd() + "..."
            : "Run an IDD workflow";
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
            ["nativeAgents"] = new JsonArray(plugin.NativeAgentsOrEmpty.Select(agent => (JsonNode?)new JsonObject
            {
                ["name"] = agent.Name, ["source"] = agent.Source
            }).ToArray()),
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
