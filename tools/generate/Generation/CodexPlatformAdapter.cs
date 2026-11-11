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
            ["agents"] = "./agents/",
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

    protected override IReadOnlyList<GeneratedFile> BuildNativeAgentFiles(PluginDefinition plugin) =>
        plugin.NativeAgentsOrEmpty.OrderBy(agent => agent.Name, StringComparer.Ordinal)
            .Select(agent => new GeneratedFile(
                Path.Combine("agents", agent.Name + ".toml"),
                NativeFactoryAgentRenderer.CodexToml(agent)))
            .ToArray();

    protected override IReadOnlyList<GeneratedFile> BuildAdditionalPluginFiles(string pluginName, string version)
    {
        var files = base.BuildAdditionalPluginFiles(pluginName, version).ToList();
        if (StringComparer.Ordinal.Equals(pluginName, "idd-factory"))
        {
            files.Add(new GeneratedFile(
                Path.Combine("skills", "idd-factory-configure", "scripts", "register-codex-agents.py"),
                RequiredFileReader.Read("src/canonical/factory/register-codex-agents.py")));
        }
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
                    ## Codex native-agent orchestration

                    Use native Codex child-agent delegation for Factory semantic work.

                    - Spawn every planner and worker in a fresh semantic context with
                      parent-history inheritance explicitly disabled. Pass
                      `fork_turns: "none"` (or the host-equivalent setting); never
                      rely on an inheritance default.
                    - Spawn only the verified registered native `agent_type` (never a
                      generic agent or prompt-based Skill discovery). Verify both
                      internal roles before durable preflight and the specific
                      role again before each spawn. On missing/conflicting role,
                      block without fallback.
                    - Prefer a read-only planner and a workspace-writing worker when the
                      host exposes those sandbox choices.
                    - Sandbox selection is an optional hardening feature, not a Factory
                      capability requirement. When Codex exposes no per-child sandbox
                      choice, continue with fresh no-parent-history children that share
                      repository access; do not report Factory as unsupported solely for
                      that reason.
                    - Use the native `spawn_agent` operation (or its current native
                      equivalent) and a blocking/event-driven `wait_agent` operation.
                    - Before **every** Factory-owned planner or worker spawn attempt,
                      choose an unused structured `task_name`: `idd_factory_planner_01`,
                      `idd_factory_planner_02`, ... for planners, or
                      `idd_factory_worker_01`, `idd_factory_worker_02`, ... for workers
                      (format `^idd_factory_(planner|worker)_[0-9]+$`).
                      Names must be unique within this Factory invocation and the
                      visible shared host namespace. Reserve the name even if the host
                      rejects the attempt; use a new suffix for any retry.
                    - Pass `task_name` in native `spawn_agent` arguments, never only
                      inside `message`, and pass `fork_turns: "none"` for **both**
                      planners and workers. Example without optional model overrides:
                      `{"agent_type":"idd-internal-factory-worker","task_name":"idd_factory_worker_01","fork_turns":"none","message":"<bounded one-task Factory assignment>"}`.
                      The `agent_type` selects the registered native role; `task_name`
                      is only a trace identity, never an agent type.
                      Include `model` and `reasoning_effort` only when required by the
                      validated execution profile mapping; never substitute models.
                    - Prefer one long wait on the critical path. Do not build a
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
                    - Do not return a completed Factory result until the root has run
                      every configured final verification check after the last planner
                      returns `# Done`. On success, remove `.idd/factory/current/` or
                      move it under `.idd/factory/results/`; an active request must not
                      remain after a completed result.

                    ## Codex native-agent capability diagnostics

                    Inspect the direct host-visible native tool schema, not an inventory
                    exposed by a nested helper such as functions.exec/ALL_TOOLS.
                    A nested tool inventory may omit the top-level agents namespace.
                    Never conclude that native tools are absent from that inventory alone.
                    Require direct evidence of agent_type selection for both registered
                    Factory roles, fresh fork_turns, native wait and interrupt/stop,
                    repository access and the actual role instructions.
                    If no agent_type selector is exposed by this host (including some
                    Codex Desktop collaboration sessions), stop before any durable write.
                    If agent_type is exposed but the roles are unknown, tell the user to
                    use idd-factory-configure for explicit role registration and restart
                    Codex; do not silently substitute generic children or change trust.

                    ## Codex Factory execution policy

                    Immediately before each worker spawn, map the planner's
                    `ExecutionProfile` through the validated project-owned
                    `.idd/execution.yaml` policy.

                    - `inherit` means omit Factory-specific model and reasoning
                      overrides so the normal host/session/default model applies.
                    - For an explicit active-platform mapping, pass
                      `codex.model` as the native spawn `model` argument and optional
                      `codex.reasoningEffort` as `reasoning_effort`, together with
                      `fork_turns: "none"`. Do not place a model-switching instruction
                      in the worker prompt.
                    - Do not reconsider task complexity or substitute a different
                      model. The configured mapping is authoritative.
                    - If the host rejects the configured model/settings or cannot
                      honor an explicit per-child override, stop with a diagnostic
                      that names the profile, requested settings, and rejection reason;
                      suggest `idd-factory-configure` and never pick a fallback model.
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
                    ## Codex native-agent installation and registration

                    Codex CLI 0.167.1 does not register native roles merely because
                    the installed Marketplace manifest advertises agents/*.toml.
                    An explicit registration operation is required in that host.
                    For a user request to install/check/update Factory native roles,
                    use the bundled script at
                    scripts/register-codex-agents.py relative to THIS SKILL.md.
                    Resolve this installed file by its real absolute path, not by
                    guessing a plugin cache version or reading a different checkout.

                    - Determine whether the request authorizes project-local or user-scope
                      changes. Ask for the target scope if ambiguous. Never silently
                      modify CODEX_HOME, global agents, project trust or config.toml.
                    - First run the script with --scope project --project <repo> --check,
                      or --scope user --check, using python3 (or a verified Python 3
                      interpreter). It is read-only and reports missing/stale/conflicting
                      definitions. The script obtains canonical TOML from the SAME installed
                      plugin version and does not create Product Intent, Factory state,
                      or execution.yaml.
                    - For explicitly authorized registration, repeat with --apply.
                      Existing differing roles block without edits. --replace is permitted
                      only after explicit approval to replace those exact role paths;
                      content-addressed backups are retained.
                    - Read-only review must never invoke --apply; an automatic routing
                      recommendation must never authorize role installation.
                    - When installation succeeds, independently check the destination
                      bytes, advise user to trust the project through supported Codex UI
                      when project-scoped, and start a NEW Codex session. Do not report a
                      ready Factory until real native agent_type selection, host-loaded
                      instructions and terminal wait are verified.
                    - A Desktop host that hides agent_type remains unsupported even
                      after registration; use a compatible Codex CLI session.
                    Registration is orthogonal to Factory model policy. Never create or
                    modify .idd/execution.yaml as part of native role registration.

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
