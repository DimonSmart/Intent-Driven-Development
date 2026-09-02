using System.Text.Json.Nodes;

namespace Idd.Generate.Generation;

internal sealed class CodexPlatformAdapter(TemplateLoader templates) : PlatformAdapter(templates)
{
    public override string Platform => "codex";

    protected override string BuildSkill(
        AdapterConfig adapter,
        PluginDefinition plugin,
        string skillName,
        string canonical,
        IReadOnlyDictionary<string, string> methodologyFiles,
        IReadOnlyDictionary<string, RoleDefinition> roleDefinitions,
        IReadOnlyDictionary<string, SkillDescription> skillDescriptions)
    {
        var files = base.BuildSkillFiles(adapter, plugin, skillName, canonical, methodologyFiles, roleDefinitions, skillDescriptions).ToList();
        if (skillName == "idd-factory-run")
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
                    - read-only recovery/status after a lost or timed-out blocking response: `factory_status`

                    A host/tool timeout is transport loss, not a Factory outcome, and
                    it does not prove that the runtime stopped. If `factory_run` or
                    `factory_continue` times out or loses its response, call
                    `factory_status` once:

                    - `ACTIVE`: the original runtime still owns the workspace; the run
                      has not finished and no Factory outcome is available yet. Report
                      this as `Factory status: ACTIVE`, never `Factory outcome: ACTIVE`.
                      Include current work item, attempt, phase, completed/remaining
                      counts, operation, and start time when returned. Do not call
                      `factory_run` or `factory_continue`.
                    - `READY_TO_CONTINUE`: no runtime owns the workspace; resume the
                      persisted run once with `factory_continue`.
                    - `WAITING_FOR_CONTINUATION`: report the persisted Factory outcome,
                      reason, resume condition, and payload; continue only when that
                      outcome's normal contract permits it.
                    - `COMPLETED`: report the persisted completed result.
                    - any other status: report it as returned rather than guessing.

                    Do not use `factory_status` as a polling loop. An `ACTIVE` status
                    ends the current launcher attempt; a later explicit invocation can
                    check status again or continue after the runtime releases the
                    workspace.

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
            "Write or patch workspace files using the available shell or file-writing operations.",
        RoleTool.Search =>
            "Search workspace text using the available shell search operations.",
        RoleTool.ShellExec =>
            "Execute shell commands using the available command-execution operation.",
        RoleTool.AgentSpawn =>
            "Spawn generic child agents with `spawn_agent`; deliver the required Factory role and skill contract in the child message.",
        RoleTool.AgentWait =>
            "Wait for child-agent mailbox activity with `wait_agent`; Codex Multi-Agent V2 wait is event-driven and does not take a child id.",
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, null)
    };
}
