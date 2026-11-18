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
                    ## Claude internal Root → Planner/Worker subagents

                    Use a native generic Agent/subagent or fork when it guarantees
                    a fresh bounded context without parent transcript, repository
                    and installed Skill access, terminal wait, final result and
                    stop/interrupt lifecycle. Do not depend on registered custom
                    Factory subagent_type identifiers. Use a built-in general-purpose
                    agent if the host requires a type. Give the child the unambiguous
                    absolute path to the installed internal Planner or Worker SKILL.md
                    relative to THIS installed internal Factory Root Skill; it must fully load the Skill
                    before reading task/project data. Root must not copy its contents.
                    A Skill is instruction material, not a process or sandbox.
                    The Planner must act read-only; use host permissions when provided.
                    Do not insert context: fork metadata into the internal Skills:
                    Root has already created the fresh child. Preserve bounded
                    assignments and native waiting without model-based polling.

                    ## Claude Factory execution policy

                    Immediately before each worker spawn, map the planner's
                    `ExecutionProfile` through the validated project-owned
                    `.idd/execution.yaml` policy.

                    - `inherit` means omit Factory-specific model and effort overrides.
                    - For an explicit active-platform mapping, pass the configured
                      `claude.model` and optional `claude.effort` through the native
                      Agent/subagent invocation. If the host has no corresponding
                      explicit per-child parameter, block the worker instead of
                      embedding an override request in its prompt.
                    - Do not reconsider task complexity or substitute a different
                      model. The configured mapping is authoritative.
                    - If the host rejects the configured settings or cannot honor an
                      explicit per-child override, stop with a diagnostic that names
                      the profile, requested settings, and rejection reason; suggest
                      `idd-factory-configure` and never pick a fallback model.
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
                    ## Claude native two-level Supervisor

                    Use one fresh native generic Agent/subagent per Factory Root
                    batch. Each must receive the absolute installed internal Root
                    SKILL.md path, effective batch limit (default 8), explicit
                    entry mode and exact complete original request or active
                    saved request path. The Root reads its own entire Skill before
                    project work. Parent transcript/history is never inherited.

                    Require the host to genuinely support nested generic agents:
                    Supervisor → Factory Root → Planner/Worker, shared workspace,
                    terminal native waits, writer/descendant quiescence and exact
                    explicit child model/effort overrides. A builtin general-purpose
                    agent is permitted if the host requires a subtype. Do not
                    require a registered custom Factory role or silently use a
                    child process as a fallback. No unsupported host may claim
                    two-level compatibility: return BLOCKED before mutation.

                    Never implement Product tasks, read full normative Factory
                    references, plan Tasks, execute verification or edit the
                    Factory state in the public Supervisor. Native terminal
                    CONTINUE is only valid after full batch checkpoint, with
                    BATCH_LIMIT and CompletedWorkers equal to the effective
                    limit. Confirm every writing descendant is stopped before
                    starting the next fresh Root. No auto retry after any other
                    terminal status or an ambiguous host result.
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
                    ## Claude Factory model configuration

                    Use native `AskUserQuestion` for blocking configuration choices
                    when it is available; do not reproduce its tool schema. Prefer a
                    native host model/capability catalog when available, then current
                    platform-provided model information or current documentation/
                    knowledge, and finally concrete model IDs supplied by the user.
                    Do not claim account-specific availability unless the host can
                    verify it.

                    Persist Claude mappings under the `claude` platform key with a
                    concrete `model` and optional `effort`. Never embed a recommended
                    concrete model ID in generated guidance, and never save an
                    automatically proposed mapping before user confirmation.
                    """))
            };
        }

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
