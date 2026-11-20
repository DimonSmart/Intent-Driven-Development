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

        if (StringComparer.Ordinal.Equals(skillName, "idd-internal-factory-root"))
        {
            var skill = files.Single(file => StringComparer.Ordinal.Equals(
                file.RelativePath,
                Path.Combine("skills", skillName, "SKILL.md")));
            files[files.IndexOf(skill)] = skill with
            {
                Content = ContentNormalizer.NormalizeContent(ContentNormalizer.JoinBlocks(
                    skill.Content!,
                    """
                    ## Codex generic Planner/Worker children from internal Root

                    This is the INSTALLED internal Factory Root Skill, not the public
                    Supervisor. A Factory-specific agent_type is not required. Resolve
                    Planner and Worker SKILL.md files from THIS internal Root's installed
                    plugin version; pass their exact absolute paths and bounded
                    assignments. Children read full Skills before other work.
                    Use fork_turns: "none" for every Planner and Worker spawn.
                    Never send protocol bodies or previous child transcripts.

                    Inspect the direct host-visible tool schema, including tools not
                    exposed inside functions.exec/ALL_TOOLS. An absent agent_type is
                    NOT a capability failure. If the host requires a built-in generic
                    agent type, use that built-in type, never a registered Factory role.
                    Check fresh context, shared workspace/installed files, terminal
                    results, native wait and stop/interrupt. Prefer read-only Planner
                    permissions if available but do not confuse a text guard with a
                    technical sandbox. Diagnose missing mandatory capabilities precisely.

                    Reserve unique batch-scoped task_name arguments such as
                    idd_factory_planner_b0001_0001 and idd_factory_worker_b0001_0001.
                    The batch ordinal is supplied by Supervisor; the local suffix
                    counts ATTEMPTS and rejected attempts consume names.
                    Example (replace placeholders with exact real values):
                    {"task_name":"idd_factory_worker_b0001_0001","fork_turns":"none","message":"Read the entire installed Worker SKILL.md at <absolute path>, then perform exactly one bounded Task: <one Task packet>"}.
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
                    ## Codex native Supervisor → Factory Root

                    Use MultiAgentV2 native generic spawn_agent. The absence of an
                    agent_type parameter is NOT a capability failure. The public
                    Supervisor must never spawn Planner or Worker directly.

                    Each distinct batch uses a unique native task_name of the form
                    idd_factory_root_b0001, idd_factory_root_b0002, etc., and
                    explicitly passes fork_turns: "none". The message includes the
                    exact absolute path to the installed idd-internal-factory-root
                    SKILL.md, shared cwd, entry mode and full New/Restart request or
                    exact active request file for Continue, the batch ordinal and
                    effective positive max_workers_per_batch (default 8).
                    Never copy the internal Root Skill body or previous transcripts.

                    Require a real nested spawn capability through depth 2, native
                    wait_agent terminal results with parent-thread correlation,
                    reliable stop AND child-descendant quiescence, and exact model/
                    reasoning overrides to Planner/Worker grandchildren when policy
                    requires them. MultiAgentV2 flags alone do not prove capability;
                    unsupported hosts BLOCK, never fall back to flat execution or
                    invoke separate Codex CLI processes.

                    Wait natively for the Root's genuine terminal child result.
                    CONTINUE requires a confirmed full Planner-plan checkpoint,
                    idle descendants, BATCH_LIMIT and
                    CompletedWorkers >= max_workers_per_batch > 0. The target is
                    not a hard cap: finish the entire current plan before rotation,
                    even when its confirmed Worker count exceeds the target.
                    Only then spawn ONE new fresh Root with mode Continue. A new
                    Root starts from existing .idd/factory state, not Supervisor's
                    summary. Keep all Preflight, verification and implementation
                    responsibilities in the internal Root.
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
