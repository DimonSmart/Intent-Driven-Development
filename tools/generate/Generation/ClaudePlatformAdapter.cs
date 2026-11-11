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
                    ## Claude generic Factory subagents

                    Use a native generic Agent/subagent or fork when it guarantees
                    a fresh bounded context without parent transcript, repository
                    and installed Skill access, terminal wait, final result and
                    stop/interrupt lifecycle. Do not depend on registered custom
                    Factory subagent_type identifiers. Use a built-in general-purpose
                    agent if the host requires a type. Give the child the unambiguous
                    absolute path to the installed internal Planner or Worker SKILL.md
                    relative to THIS Factory Run Skill; it must fully load the Skill
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
