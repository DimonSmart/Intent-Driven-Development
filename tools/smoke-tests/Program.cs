using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var repoRoot = FindRepoRoot();
var marketplaceRoot = Path.Combine(repoRoot, "artifacts", "marketplace");
var generatorDll = Path.Combine(repoRoot, "tools", "generate", "bin", "Debug", "net10.0", "Generate.dll");
var version = ParseVersion(args);
var failures = new List<string>();

RunGenerator();
CheckSkillReferenceDestinationValidation();
CheckClaudeMarketplace();
CheckCodexMarketplace();
CheckPlatformPlugins("claude", ".claude-plugin");
CheckPlatformPlugins("codex", ".codex-plugin");
CheckCanonicalRoleReader();
CheckFactoryRoleGeneration();
CheckCodexFactoryMetadata();
CheckFactoryTransportGeneration();
CheckCanonicalSkillReferences();
CheckCanonicalFactoryNeutrality();
CheckVerificationPolicyContract();
CheckPublishedLayout();
CheckGeneratorCheckMode();
CheckSecondRunIsStable();

if (failures.Count > 0)
{
    foreach (var failure in failures)
    {
        Console.Error.WriteLine(failure);
    }

    return 1;
}

Console.WriteLine("Smoke tests passed.");
return 0;

void RunGenerator()
{
    if (RunProcess("dotnet", $"exec \"{generatorDll}\" --version {version}") != 0)
    {
        failures.Add("Generator failed.");
    }
}

void CheckClaudeMarketplace()
{
    var path = Path.Combine(marketplaceRoot, ".claude-plugin", "marketplace.json");
    using var document = ReadJson(path);
    if (document is null)
    {
        return;
    }

    var root = document.RootElement;
    ExpectString(root, "name", "intent-driven-development", "Claude marketplace name");
    ExpectString(root, "version", version, "Claude marketplace version");

    if (!root.TryGetProperty("renames", out var renames))
    {
        failures.Add("Claude marketplace is missing rename metadata.");
    }
    else
    {
        ExpectString(renames, "idd", "idd-intent", "Claude unified-plugin migration");
        ExpectString(renames, "idd-core", "idd-intent", "Claude core-plugin migration");
    }

    var plugins = root.GetProperty("plugins").EnumerateArray().ToArray();
    ExpectPluginNames(plugins, "Claude marketplace");

    foreach (var plugin in plugins)
    {
        var name = plugin.GetProperty("name").GetString() ?? "";
        ExpectString(plugin, "version", version, $"Claude marketplace {name} version");
        if (plugin.TryGetProperty("policy", out _))
        {
            failures.Add($"Claude marketplace plugin {name} contains Codex-only policy.");
        }

        var source = plugin.GetProperty("source").GetString();
        ExpectMarketplacePath(source, $"Claude marketplace {name} source");
    }
}

void CheckCodexMarketplace()
{
    var path = Path.Combine(marketplaceRoot, ".agents", "plugins", "marketplace.json");
    using var document = ReadJson(path);
    if (document is null)
    {
        return;
    }

    var root = document.RootElement;
    ExpectString(root, "name", "intent-driven-development", "Codex marketplace name");
    ExpectString(root.GetProperty("interface"), "displayName", "Intent-Driven Development", "Codex marketplace display name");

    var plugins = root.GetProperty("plugins").EnumerateArray().ToArray();
    ExpectPluginNames(plugins, "Codex marketplace");

    foreach (var plugin in plugins)
    {
        var name = plugin.GetProperty("name").GetString() ?? "";
        var source = plugin.GetProperty("source");
        ExpectString(source, "source", "local", $"Codex marketplace {name} source type");
        ExpectMarketplacePath(source.GetProperty("path").GetString(), $"Codex marketplace {name} source path");

        if (!plugin.TryGetProperty("policy", out var policy))
        {
            failures.Add($"Codex marketplace plugin {name} is missing policy.");
            continue;
        }

        ExpectString(policy, "installation", "AVAILABLE", $"Codex marketplace {name} installation policy");
        ExpectString(policy, "authentication", "ON_INSTALL", $"Codex marketplace {name} authentication policy");
    }
}

void CheckPlatformPlugins(string platform, string manifestDirectory)
{
    var intentRoot = Path.Combine(marketplaceRoot, "plugins", platform, "idd-intent");
    var factoryRoot = Path.Combine(marketplaceRoot, "plugins", platform, "idd-factory");

    CheckPluginManifest(intentRoot, manifestDirectory, "idd-intent", platform);
    CheckPluginManifest(factoryRoot, manifestDirectory, "idd-factory", platform);

    foreach (var skill in new[]
    {
        "idd-project-init",
        "idd-verification-configure",
        "idd-intent-change",
        "idd-code-implement",
        "idd-code-check-implementation"
    })
    {
        ExpectFile(Path.Combine(intentRoot, "skills", skill, "SKILL.md"));
    }

    ExpectMissing(Path.Combine(intentRoot, "skills", "idd-factory-create-work-plan"));
    ExpectMissing(Path.Combine(intentRoot, "skills", "idd-factory-execute-work-plan"));
    ExpectFile(Path.Combine(intentRoot, "assets", "bootstrap", ".idd", "intent", "README.md"));
    foreach (var skill in new[]
    {
        "idd-project-init",
        "idd-verification-configure",
        "idd-code-implement",
        "idd-code-check-implementation"
    })
    {
        ExpectFile(Path.Combine(intentRoot, "skills", skill, "references", "project-verification.md"));
    }
    ExpectFile(Path.Combine(
        intentRoot,
        "skills",
        "idd-project-init",
        "assets",
        "bootstrap",
        ".idd",
        "intent",
        "README.md"));

    foreach (var skill in new[]
    {
        "idd-factory-run",
        "idd-factory-decompose-task",
        "idd-factory-execute-subtask",
        "idd-factory-review-checkpoint",
        "idd-factory-review-task",
        "idd-factory-replan"
    })
    {
        ExpectFile(Path.Combine(factoryRoot, "skills", skill, "SKILL.md"));
        if (skill == "idd-factory-run") ExpectFile(Path.Combine(factoryRoot, "skills", skill, "references", "project-verification.md"));
        else ExpectMissing(Path.Combine(factoryRoot, "skills", skill, "references", "project-verification.md"));
    }

    ExpectMissing(Path.Combine(factoryRoot, "skills", "idd-factory-create-work-plan"));
    ExpectMissing(Path.Combine(factoryRoot, "skills", "idd-factory-execute-work-plan"));
    ExpectMissing(Path.Combine(factoryRoot, "skills", "idd-factory-decompose-work"));
    ExpectMissing(Path.Combine(factoryRoot, "skills", "idd-factory-execute-task"));
    ExpectMissing(Path.Combine(factoryRoot, "skills", "idd-factory-review-work-result"));
    ExpectMissing(Path.Combine(factoryRoot, "skills", "idd-factory-finish-work"));
    ExpectMissing(Path.Combine(factoryRoot, "skills", "idd-project-init"));
    ExpectMissing(Path.Combine(factoryRoot, "skills", "idd-intent-change"));
    ExpectMissing(Path.Combine(factoryRoot, "skills", "idd-factory-coordinate-step"));
    ExpectMissing(Path.Combine(factoryRoot, "skills", "idd-factory-finalize-run"));
    ExpectFile(Path.Combine(factoryRoot, "runtime", "idd-factory.dll"));
    ExpectFile(Path.Combine(factoryRoot, "runtime", "factory-workflow.yaml"));
    ExpectDirectory(Path.Combine(factoryRoot, "assets", "bootstrap", ".idd", "factory"));
    ExpectFile(Path.Combine(factoryRoot, "assets", "bootstrap", ".idd", "factory", ".gitignore"));
    using (var methodology = ReadJson(Path.Combine(factoryRoot, "skills", "idd-factory-run", "references", "methodology-version.json")))
    {
        if (methodology is not null)
        {
            if (!methodology.RootElement.TryGetProperty("schemaVersion", out var schemaVersion) || schemaVersion.GetInt32() != 1)
            {
                failures.Add($"{platform} Factory methodology reference has an invalid schemaVersion.");
            }
            ExpectString(methodology.RootElement, "methodologyVersion", version, $"{platform} Factory methodology version");
        }
    }

    foreach (var reference in new[]
    {
        (Skill: "idd-factory-decompose-task", Role: "task-decomposer"),
        (Skill: "idd-factory-execute-subtask", Role: "implementer"),
        (Skill: "idd-factory-review-checkpoint", Role: "checkpoint-reviewer"),
        (Skill: "idd-factory-review-task", Role: "final-reviewer"),
        (Skill: "idd-factory-replan", Role: "factory-replanner")
    })
    {
        ExpectMissing(Path.Combine(
            factoryRoot,
            "skills",
            reference.Skill,
            "references",
            "roles",
            $"{reference.Role}.md"));
    }

    var runFrontMatter = ReadFrontMatter(ReadText(Path.Combine(factoryRoot, "skills", "idd-factory-run", "SKILL.md")));
    var workerSkills = new[]
    {
        "idd-factory-decompose-task",
        "idd-factory-execute-subtask",
        "idd-factory-review-checkpoint",
        "idd-factory-review-task",
        "idd-factory-replan"
    };

    if (platform == "claude")
    {
        if (runFrontMatter.Contains("context: fork", StringComparison.Ordinal))
        {
            failures.Add("Claude idd-factory-run must remain in the coordinator context.");
        }

        foreach (var skill in workerSkills)
        {
            var workerFrontMatter = ReadFrontMatter(ReadText(Path.Combine(factoryRoot, "skills", skill, "SKILL.md")));
            ExpectContains(workerFrontMatter, "context: fork", $"Claude {skill} isolation metadata");
        }

    }
    else
    {
        foreach (var skill in workerSkills.Prepend("idd-factory-run"))
        {
            var skillFrontMatter = ReadFrontMatter(ReadText(Path.Combine(factoryRoot, "skills", skill, "SKILL.md")));
            foreach (var claudeField in new[] { "context:", "agent:", "allowed-tools:", "argument-hint:" })
            {
                if (skillFrontMatter.Contains(claudeField, StringComparison.Ordinal))
                {
                    failures.Add($"Codex {skill} contains Claude-specific frontmatter '{claudeField}'.");
                }
            }
        }

        var runSkill = ReadText(Path.Combine(factoryRoot, "skills", "idd-factory-run", "SKILL.md"));
        ExpectContains(runSkill, "mcp__factory", "Codex direct MCP launcher");
        ExpectContains(runSkill, "factory_run", "Codex Factory run tool");
        ExpectContains(runSkill, "factory_continue", "Codex Factory continue tool");
        ExpectContains(runSkill, "factory_cancel", "Codex Factory cancel tool");
        if (runSkill.Contains("runtime/idd-factory.dll", StringComparison.Ordinal)) failures.Add("Codex run skill still instructs shell-launching idd-factory.dll.");
        if (runSkill.Contains("write_stdin", StringComparison.Ordinal)) failures.Add("Codex run skill still instructs launcher write_stdin polling.");
        ExpectContains(runSkill, "Do not spawn semantic or coordinator agents", "Codex coordinator removal invariant");
    }

    CheckIddMetadata(intentRoot, [], ".idd/intent", platform, "idd-intent");
    CheckIddMetadata(factoryRoot, ["idd-intent"], ".idd/factory", platform, "idd-factory");
}

void CheckPluginManifest(string pluginRoot, string manifestDirectory, string pluginName, string platform)
{
    var path = Path.Combine(pluginRoot, manifestDirectory, "plugin.json");
    using var document = ReadJson(path);
    if (document is null)
    {
        return;
    }

    ExpectString(document.RootElement, "name", pluginName, $"{platform} {pluginName} manifest name");
    ExpectString(document.RootElement, "version", version, $"{platform} {pluginName} manifest version");

    if (platform == "codex")
    {
        var expectedDisplayName = pluginName == "idd-intent" ? "IDD Intent" : "IDD Factory";
        ExpectString(
            document.RootElement.GetProperty("interface"),
            "displayName",
            expectedDisplayName,
            $"Codex {pluginName} display name");
        ExpectString(document.RootElement, "skills", "./skills/", $"Codex {pluginName} skills path");
    }
}

void CheckIddMetadata(
    string pluginRoot,
    string[] expectedDependencies,
    string expectedAssetDestination,
    string platform,
    string pluginName)
{
    using var document = ReadJson(Path.Combine(pluginRoot, "idd-plugin.json"));
    if (document is null)
    {
        return;
    }

    var root = document.RootElement;
    ExpectString(root, "version", version, $"{platform} {pluginName} metadata version");
    if (root.TryGetProperty("skillReferences", out _))
    {
        failures.Add($"{platform} {pluginName} metadata exposes skillReferences.");
    }

    var dependencies = root.GetProperty("dependencies")
        .EnumerateArray()
        .Select(value => value.GetString() ?? "")
        .ToArray();
    if (!dependencies.SequenceEqual(expectedDependencies))
    {
        failures.Add($"{platform} {pluginName} dependencies are [{string.Join(", ", dependencies)}], expected [{string.Join(", ", expectedDependencies)}].");
    }

    var destinations = root.GetProperty("assets")
        .EnumerateArray()
        .Select(asset => asset.GetProperty("destination").GetString() ?? "")
        .ToArray();
    if (!destinations.SequenceEqual([expectedAssetDestination]))
    {
        failures.Add($"{platform} {pluginName} asset destinations are [{string.Join(", ", destinations)}], expected {expectedAssetDestination}.");
    }
}

void CheckCanonicalFactoryNeutrality()
{
    var forbidden = new[] { "Codex", "Claude", "spawn_agent", "wait_agent", "fork_context", "codex-dispatch", ".agents/skills/", "mcp__factory", "runtime/idd-factory.dll", "PowerShell", "`items`", "`message`" };
    var canonicalFiles = Directory.GetFiles(Path.Combine(repoRoot, "src", "canonical", "skills"), "idd-factory-*.md")
        .Concat(Directory.GetFiles(Path.Combine(repoRoot, "src", "canonical", "factory"), "*.md", SearchOption.AllDirectories));
    foreach (var file in canonicalFiles)
    {
        var content = File.ReadAllText(file);
        foreach (var literal in forbidden.Where(content.Contains))
        {
            failures.Add($"Canonical Factory file {Relative(file)} contains platform-specific literal '{literal}'.");
        }
    }

    var decomposition = ReadText(Path.Combine(repoRoot, "src", "canonical", "skills", "idd-factory-decompose-task.md"));
    ExpectContains(decomposition, "self-contained contract", "Canonical self-contained decomposition contract");
}

void CheckFactoryRoleGeneration()
{
    foreach (var platform in new[] { "claude", "codex" })
    {
        var root = Path.Combine(marketplaceRoot, "plugins", platform, "idd-factory");
        using var metadata = ReadJson(Path.Combine(root, "idd-plugin.json"));
        if (metadata is null)
        {
            continue;
        }

        if (metadata.RootElement.TryGetProperty("roleDefinitions", out var roleDefinitions) && roleDefinitions.GetArrayLength() != 0)
            failures.Add($"{platform} Factory metadata still contains obsolete roleDefinitions.");
    }

    foreach (var (skill, role) in new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["idd-factory-decompose-task"] = "task-decomposer",
        ["idd-factory-execute-subtask"] = "implementer",
        ["idd-factory-review-checkpoint"] = "checkpoint-reviewer",
        ["idd-factory-review-task"] = "final-reviewer",
        ["idd-factory-replan"] = "factory-replanner"
    })
    {
        foreach (var platform in new[] { "claude", "codex" })
        {
            var rolePath = Path.Combine(marketplaceRoot, "plugins", platform, "idd-factory", "skills", skill, "references", "roles", role + ".md");
            ExpectMissing(rolePath);
        }
    }

    var implementerClaudeSkill = ReadFrontMatter(ReadText(Path.Combine(
        marketplaceRoot, "plugins", "claude", "idd-factory", "skills", "idd-factory-execute-subtask", "SKILL.md")));
    ExpectContains(implementerClaudeSkill, "allowed-tools: [Read, Glob, Grep, Edit, Write, Bash]", "Claude implementer native tools");
    var reviewerClaudeSkill = ReadFrontMatter(ReadText(Path.Combine(
        marketplaceRoot, "plugins", "claude", "idd-factory", "skills", "idd-factory-review-task", "SKILL.md")));
    ExpectContains(reviewerClaudeSkill, "allowed-tools: [Read, Glob, Grep, Bash]", "Claude reviewer native tools");
}

void CheckCodexFactoryMetadata()
{
    var root = Path.Combine(marketplaceRoot, "plugins", "codex", "idd-factory");
    using var metadata = ReadJson(Path.Combine(root, "idd-plugin.json"));
    if (metadata is null)
    {
        return;
    }

    if (metadata.RootElement.GetProperty("roleDefinitions").GetArrayLength() != 0)
        failures.Add("Codex Factory metadata still contains obsolete role definitions.");

    ExpectMissing(Path.Combine(root, "agents"));
    ExpectMissing(Path.Combine(marketplaceRoot, "plugins", "codex", "idd-intent", "agents"));

    var bindings = metadata.RootElement.GetProperty("skillRoleBindings").EnumerateArray().ToArray();
    if (bindings.Length != 0) failures.Add("Codex Factory metadata still contains obsolete skill-role bindings.");

}

void CheckFactoryTransportGeneration()
{
    var codexIntent = Path.Combine(marketplaceRoot, "plugins", "codex", "idd-intent");
    var codexFactory = Path.Combine(marketplaceRoot, "plugins", "codex", "idd-factory");
    var claudeFactory = Path.Combine(marketplaceRoot, "plugins", "claude", "idd-factory");

    using (var manifest = ReadJson(Path.Combine(codexFactory, ".codex-plugin", "plugin.json")))
    {
        if (manifest is not null) ExpectString(manifest.RootElement, "mcpServers", "./.mcp.json", "Codex Factory bundled MCP manifest binding");
    }
    using (var intentManifest = ReadJson(Path.Combine(codexIntent, ".codex-plugin", "plugin.json")))
    {
        if (intentManifest is not null && intentManifest.RootElement.TryGetProperty("mcpServers", out _))
            failures.Add("Codex idd-intent must not bind the Factory MCP server.");
    }
    ExpectMissing(Path.Combine(codexIntent, ".mcp.json"));
    ExpectMissing(Path.Combine(claudeFactory, ".mcp.json"));

    using (var mcp = ReadJson(Path.Combine(codexFactory, ".mcp.json")))
    {
        if (mcp is not null)
        {
            var factory = mcp.RootElement.GetProperty("mcpServers").GetProperty("factory");
            ExpectString(factory, "command", "dotnet", "Codex Factory MCP command");
            ExpectString(factory, "cwd", ".", "Codex Factory MCP cwd");
            if (factory.GetProperty("tool_timeout_sec").GetInt32() != 1800) failures.Add("Codex Factory MCP timeout must equal 1800 seconds.");
            var arguments = factory.GetProperty("args").EnumerateArray().Select(value => value.GetString()).ToArray();
            if (!arguments.SequenceEqual(new[] { "runtime/idd-factory.dll", "mcp" })) failures.Add("Codex Factory MCP args are not exact.");
            var omitted = factory.GetProperty("omit_tools_from").EnumerateArray().Select(value => value.GetString()).ToArray();
            if (!omitted.SequenceEqual(new[] { "deferred", "code_mode" })) failures.Add("Codex Factory MCP omit_tools_from is not exact.");
            var inheritedEnvironment = factory.GetProperty("env_vars").EnumerateArray().Select(value => value.GetString()).ToArray();
            if (!inheritedEnvironment.SequenceEqual(new[] { "IDD_FACTORY_CODEX_EXECUTABLE", "IDD_FACTORY_MODEL", "IDD_FACTORY_REASONING_EFFORT", "IDD_FACTORY_INHERIT_USER_SKILLS", "IDD_FACTORY_CAPABILITY_PROFILE" }))
                failures.Add("Codex Factory MCP env_vars allowlist is not exact.");
        }
    }

    var claudeSkill = ReadText(Path.Combine(claudeFactory, "skills", "idd-factory-run", "SKILL.md"));
    ExpectContains(claudeSkill, "runtime/idd-factory.dll", "Claude packaged CLI launcher");
    if (claudeSkill.Contains("mcp__factory", StringComparison.Ordinal)) failures.Add("Claude run skill contains Codex MCP mechanics.");

    var canonical = ReadText(Path.Combine(repoRoot, "src", "canonical", "skills", "idd-factory-run.md"));
    foreach (var literal in new[] { "mcp__factory", "factory_run", "runtime/idd-factory.dll", "PowerShell", "write_stdin" })
        if (canonical.Contains(literal, StringComparison.Ordinal)) failures.Add($"Canonical Factory run skill contains transport mechanic '{literal}'.");
}

void CheckCanonicalRoleReader()
{
    var root = Path.Combine(Path.GetTempPath(), "idd-role-reader-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var path = Path.Combine(root, "sample.md");
    var reader = new CanonicalRoleReader();

    try
    {
        File.WriteAllText(path, "---\ntools:\n  - file.read\n  - file.write\n  - command.execute\n---\n\n# Sample\n\nInstructions.\n");
        var role = reader.Read("sample", path);
        if (!role.Tools.SequenceEqual([RoleTool.FileRead, RoleTool.FileWrite, RoleTool.CommandExecute]) ||
            !StringComparer.Ordinal.Equals(role.Instructions, "# Sample\n\nInstructions."))
        {
            failures.Add("Canonical role reader did not preserve tools or Markdown instructions.");
        }

        foreach (var (name, content) in new Dictionary<string, string>
        {
            ["missing-front-matter"] = "# Sample\n",
            ["missing-tools"] = "---\nname: sample\n---\n# Sample\n",
            ["empty-tools"] = "---\ntools:\n---\n# Sample\n",
            ["unknown-tool"] = "---\ntools:\n  - workspace.write\n---\n# Sample\n",
            ["removed-repository-tool"] = "---\ntools:\n  - repository.read\n---\n# Sample\n",
            ["removed-factory-state-tool"] = "---\ntools:\n  - factory-state.write\n---\n# Sample\n",
            ["duplicate-tool"] = "---\ntools:\n  - file.read\n  - file.read\n---\n# Sample\n",
            ["invalid-yaml"] = "---\ntools: file.read\n---\n# Sample\n",
            ["empty-instructions"] = "---\ntools:\n  - file.read\n---\n"
        })
        {
            File.WriteAllText(path, content);
            try
            {
                _ = reader.Read(name, path);
                failures.Add($"Canonical role reader accepted {name}.");
            }
            catch (InvalidOperationException exception) when (
                exception.Message.Contains($"Role '{name}'", StringComparison.Ordinal) &&
                exception.Message.Contains(path, StringComparison.Ordinal))
            {
            }
            catch (InvalidOperationException exception)
            {
                failures.Add($"Canonical role reader diagnostic for {name} omits its name or path: {exception.Message}");
            }
        }
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

void CheckCanonicalSkillReferences()
{
    var path = Path.Combine(repoRoot, "src", "canonical", "plugins", "plugin-manifest.json");
    using var document = ReadJson(path);
    if (document is null)
    {
        return;
    }

    var plugins = document.RootElement.GetProperty("plugins");
    foreach (var pluginProperty in plugins.EnumerateObject())
    {
        var pluginName = pluginProperty.Name;
        var plugin = pluginProperty.Value;
        var skills = plugin.GetProperty("skills").EnumerateArray()
            .Select(skill => skill.GetString() ?? "")
            .ToHashSet(StringComparer.Ordinal);
        var destinationsBySkill = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        if (!plugin.TryGetProperty("skillReferences", out var references))
        {
            continue;
        }

        foreach (var reference in references.EnumerateArray())
        {
            var skill = reference.GetProperty("skill").GetString() ?? "";
            var source = reference.GetProperty("source").GetString() ?? "";
            var destination = reference.GetProperty("destination").GetString() ?? "";

            if (!skills.Contains(skill))
            {
                failures.Add($"Plugin '{pluginName}' assigns a reference to unowned skill '{skill}'.");
            }

            var sourcePath = Path.GetFullPath(Path.Combine(repoRoot, source.Replace('/', Path.DirectorySeparatorChar)));
            var rootWithSeparator = repoRoot.EndsWith(Path.DirectorySeparatorChar) ? repoRoot : repoRoot + Path.DirectorySeparatorChar;
            if (!sourcePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) || !File.Exists(sourcePath))
            {
                failures.Add($"Plugin '{pluginName}' reference source '{source}' is not a repository text file.");
                continue;
            }

            try
            {
                var content = File.ReadAllText(sourcePath, new UTF8Encoding(false, true));
                if (content.Contains('\0'))
                {
                    failures.Add($"Plugin '{pluginName}' reference source '{source}' is not valid UTF-8 text.");
                }
            }
            catch (DecoderFallbackException)
            {
                failures.Add($"Plugin '{pluginName}' reference source '{source}' is not valid UTF-8 text.");
            }

            string normalizedDestination;
            try
            {
                normalizedDestination = SkillReferencePathValidator.NormalizeDestination(destination);
            }
            catch (ArgumentException exception)
            {
                failures.Add($"Plugin '{pluginName}' reference destination '{destination}' is invalid: {exception.Message}");
                continue;
            }

            if (!destinationsBySkill.TryGetValue(skill, out var destinations))
            {
                destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                destinationsBySkill.Add(skill, destinations);
            }

            if (!destinations.Add(normalizedDestination))
            {
                failures.Add($"Plugin '{pluginName}' skill '{skill}' has a case-insensitive destination conflict at '{normalizedDestination}'.");
            }

            foreach (var platform in new[] { "claude", "codex" })
            {
                var generatedPath = Path.Combine(marketplaceRoot, "plugins", platform, pluginName, "skills", skill, "references", normalizedDestination.Replace('/', Path.DirectorySeparatorChar));
                ExpectFile(generatedPath);
                if (File.Exists(generatedPath) && !StringComparer.Ordinal.Equals(NormalizeText(File.ReadAllText(generatedPath)), NormalizeText(File.ReadAllText(sourcePath))))
                {
                    failures.Add($"{platform} generated reference for '{pluginName}/{skill}/{normalizedDestination}' does not match its canonical source.");
                }
            }
        }
    }

    foreach (var platform in new[] { "claude", "codex" })
    {
        ExpectMissing(Path.Combine(marketplaceRoot, "plugins", platform, "idd-factory", "skills", "idd-route", "references"));
    }
}

void CheckVerificationPolicyContract()
{
    var legacyPolicyPath = ".idd/" + "verification" + ".md";

    foreach (var relativePath in GetTrackedTextFiles())
    {
        var file = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.ReadAllText(file, new UTF8Encoding(false, true)).Contains(legacyPolicyPath, StringComparison.Ordinal))
        {
            failures.Add($"Active repository content still references unsupported verification policy '{legacyPolicyPath}': {relativePath}.");
        }
    }
}

IEnumerable<string> GetTrackedTextFiles()
{
    using var process = Process.Start(new ProcessStartInfo
    {
        FileName = "git",
        Arguments = "ls-files -z",
        WorkingDirectory = repoRoot,
        UseShellExecute = false,
        RedirectStandardOutput = true
    });

    if (process is null)
    {
        failures.Add("Could not list tracked files for verification policy validation.");
        return [];
    }

    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        failures.Add("git ls-files failed during verification policy validation.");
        return [];
    }

    return output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
        .Where(path => !IsExcludedVerificationPolicyPath(path))
        .Where(path => IsUtf8TextFile(Path.Combine(repoRoot, path.Replace('/', Path.DirectorySeparatorChar))))
        .ToArray();
}

static bool IsExcludedVerificationPolicyPath(string path) => path.Split('/').Any(segment =>
    segment.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
    segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
    segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
    segment.Equals("artifacts", StringComparison.OrdinalIgnoreCase));

static bool IsUtf8TextFile(string path)
{
    try
    {
        var bytes = File.ReadAllBytes(path);
        return !bytes.Contains((byte)0) && TryDecodeUtf8(bytes);
    }
    catch (IOException)
    {
        return false;
    }
}

static bool TryDecodeUtf8(byte[] bytes)
{
    try
    {
        _ = new UTF8Encoding(false, true).GetString(bytes);
        return true;
    }
    catch (DecoderFallbackException)
    {
        return false;
    }
}

void CheckSkillReferenceDestinationValidation()
{
    foreach (var destination in new[] { "common-workflows.md", "docs/common-workflows.md" })
    {
        try
        {
            _ = SkillReferencePathValidator.NormalizeDestination(destination);
        }
        catch (ArgumentException exception)
        {
            failures.Add($"Valid skill reference destination '{destination}' was rejected: {exception.Message}");
        }
    }

    foreach (var destination in new[]
    {
        "C:",
        "C:file.md",
        "C:/file.md",
        "C:\\file.md",
        "/file.md",
        "\\file.md",
        "//server/share/file.md",
        "\\\\server\\share\\file.md",
        "../file.md",
        "folder//file.md",
        "./file.md",
        "roles/file.md",
        "Roles/file.md"
    })
    {
        try
        {
            _ = SkillReferencePathValidator.NormalizeDestination(destination);
            failures.Add($"Invalid skill reference destination '{destination}' was accepted.");
        }
        catch (ArgumentException)
        {
        }
    }
}

void CheckPublishedLayout()
{
    foreach (var path in new[]
    {
        ".claude-plugin/marketplace.json",
        ".agents/plugins/marketplace.json",
        "plugins/claude/idd-intent",
        "plugins/claude/idd-factory",
        "plugins/codex/idd-intent",
        "plugins/codex/idd-factory"
    })
    {
        var fullPath = Path.Combine(marketplaceRoot, path.Replace('/', Path.DirectorySeparatorChar));
        if (Path.HasExtension(fullPath))
        {
            ExpectFile(fullPath);
        }
        else
        {
            ExpectDirectory(fullPath);
        }
    }

    foreach (var obsoletePath in new[]
    {
        "plugins/claude/idd",
        "plugins/codex/idd",
        "plugins/claude/idd-core",
        "plugins/codex/idd-core"
    })
    {
        ExpectMissing(Path.Combine(marketplaceRoot, obsoletePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}

void CheckGeneratorCheckMode()
{
    if (RunProcess("dotnet", $"exec \"{generatorDll}\" --check --version {version}") != 0)
    {
        failures.Add("Generator --check failed.");
    }
}

void CheckSecondRunIsStable()
{
    var before = SnapshotMarketplace();
    RunGenerator();
    var after = SnapshotMarketplace();
    if (!before.SequenceEqual(after))
    {
        failures.Add("Running the generator twice changed marketplace output.");
    }
}

void ExpectPluginNames(JsonElement[] plugins, string context)
{
    var names = plugins.Select(plugin => plugin.GetProperty("name").GetString()).ToArray();
    if (!names.SequenceEqual(["idd-intent", "idd-factory"]))
    {
        failures.Add($"{context} plugins are [{string.Join(", ", names)}], expected idd-intent and idd-factory.");
    }
}

void ExpectMarketplacePath(string? relativePath, string context)
{
    if (string.IsNullOrWhiteSpace(relativePath))
    {
        failures.Add($"{context} is empty.");
        return;
    }

    var fullPath = Path.GetFullPath(Path.Combine(
        marketplaceRoot,
        relativePath.Replace('/', Path.DirectorySeparatorChar)));
    if (!fullPath.StartsWith(Path.GetFullPath(marketplaceRoot), StringComparison.OrdinalIgnoreCase))
    {
        failures.Add($"{context} resolves outside the marketplace root.");
        return;
    }

    if (!Directory.Exists(fullPath) && !File.Exists(fullPath))
    {
        failures.Add($"{context} does not exist: {relativePath}");
    }
}

JsonDocument? ReadJson(string path)
{
    ExpectFile(path);
    if (!File.Exists(path))
    {
        return null;
    }

    try
    {
        return JsonDocument.Parse(File.ReadAllText(path));
    }
    catch (Exception exception)
    {
        failures.Add($"Invalid JSON in {Relative(path)}: {exception.Message}");
        return null;
    }
}

void ExpectString(JsonElement element, string propertyName, string expected, string context)
{
    if (!element.TryGetProperty(propertyName, out var property) ||
        property.ValueKind != JsonValueKind.String ||
        !StringComparer.Ordinal.Equals(property.GetString(), expected))
    {
        failures.Add($"{context} is not '{expected}'.");
    }
}

void ExpectContains(string text, string expected, string context)
{
    if (!text.Contains(expected, StringComparison.Ordinal))
    {
        failures.Add($"{context} does not contain '{expected}'.");
    }
}

string ReadText(string path)
{
    ExpectFile(path);
    return File.Exists(path) ? File.ReadAllText(path) : "";
}

static string ReadFrontMatter(string text)
{
    var lines = text.ReplaceLineEndings("\n").Split('\n');
    if (lines.Length == 0 || !StringComparer.Ordinal.Equals(lines[0], "---"))
    {
        return "";
    }

    var end = Array.FindIndex(lines, 1, line => StringComparer.Ordinal.Equals(line, "---"));
    return end < 0 ? "" : string.Join("\n", lines.Take(end + 1));
}

void ExpectFile(string path)
{
    if (!File.Exists(path))
    {
        failures.Add($"Missing file: {Relative(path)}");
    }
}

void ExpectDirectory(string path)
{
    if (!Directory.Exists(path))
    {
        failures.Add($"Missing directory: {Relative(path)}");
    }
}

void ExpectMissing(string path)
{
    if (File.Exists(path) || Directory.Exists(path))
    {
        failures.Add($"Obsolete path exists: {Relative(path)}");
    }
}

string[] SnapshotMarketplace() => Directory.Exists(marketplaceRoot)
    ? Directory.GetFiles(marketplaceRoot, "*", SearchOption.AllDirectories)
        .OrderBy(path => path, StringComparer.Ordinal)
        .Select(path => $"{Relative(path)}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}")
        .ToArray()
    : [];

string Relative(string path) => Path.GetRelativePath(repoRoot, path).Replace('\\', '/');

static string NormalizeText(string text) => text.ReplaceLineEndings("\n").TrimEnd() + "\n";

int RunProcess(string fileName, string arguments)
{
    using var process = Process.Start(new ProcessStartInfo
    {
        FileName = fileName,
        Arguments = arguments,
        WorkingDirectory = repoRoot,
        UseShellExecute = false
    });

    if (process is null)
    {
        return -1;
    }

    process.WaitForExit();
    return process.ExitCode;
}

static string FindRepoRoot()
{
    var current = new DirectoryInfo(Environment.CurrentDirectory);
    while (current is not null)
    {
        if (Directory.Exists(Path.Combine(current.FullName, "src", "canonical")) &&
            Directory.Exists(Path.Combine(current.FullName, "tools", "generate")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new InvalidOperationException("Could not locate repository root.");
}

static string ParseVersion(string[] arguments)
{
    for (var index = 0; index < arguments.Length; index++)
    {
        if (!StringComparer.Ordinal.Equals(arguments[index], "--version"))
        {
            continue;
        }

        if (index + 1 >= arguments.Length)
        {
            throw new InvalidOperationException("Missing value for --version.");
        }

        return arguments[index + 1];
    }

    throw new InvalidOperationException("Missing required --version MAJOR.MINOR.PATCH option.");
}
