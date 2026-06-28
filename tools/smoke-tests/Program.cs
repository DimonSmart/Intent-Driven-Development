using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

var repoRoot = FindRepoRoot();
var failures = new List<string>();
var generatorDll = Path.Combine(repoRoot, "tools", "generate", "bin", "Debug", "net10.0", "Generate.dll");
var toolDll = Path.Combine(repoRoot, "tools", "idd-tool", "bin", "Debug", "net10.0", "IntentDrivenDevelopment.Tool.dll");

RunGenerator();

ExpectManifestShape();
ExpectFile("generated/codex/AGENTS.md");

ExpectFile("generated/claude/CLAUDE.md");

ExpectFile("generated/gemini/GEMINI.md");
ExpectNoDirectory("generated/gemini/.agents");
ExpectNoDirectory("generated/gemini/.claude");
ExpectNoDirectory("generated/gemini/.github/skills");

ExpectFile("generated/copilot/.github/copilot-instructions.md");

ExpectNoGeneratedHeaderComments();
ExpectNoEntryIncludes("generated/claude/CLAUDE.md", "AGENTS.md");
ExpectNoEntryIncludes("generated/gemini/GEMINI.md", "AGENTS.md");
ExpectEntryPointLineLimits();
ExpectAllSkillsGenerated();
ExpectClaudeSkillMetadata();
ExpectPackManifestShape();
ExpectFactoryGeneratedShape();
ExpectFactoryRolePromptReferences();
ExpectListPacks();
ExpectListCodingAgents();
ExpectDefaultInstallCoreOnly();
ExpectFactoryInstall();
ExpectFactoryUnsupportedTargetRejected();
ExpectInstallEntryNone();
ExpectInstallGeminiEntryNoneRejected();
ExpectInstallAllAfterInit();
ExpectNpmListTargets();
ExpectNpmListCodingAgents();
ExpectNpmListPacks();
ExpectNpmInstallDefaultMinimal();
ExpectNpmInstallDefaultCoreOnly();
ExpectNpmInstallFactory();
ExpectNpmInstallEntryNone();
ExpectNpmInstallEntryFull();
ExpectNpmRejectsGeminiEntryNone();
ExpectNpmRejectsFactoryForGemini();
ExpectNpmRejectsUnknownPack();
ExpectNpmRejectsUnknownEntryMode();
ExpectGeneratorCheckPasses();
ExpectSecondRunStable();

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

void ExpectFile(string relativePath)
{
    if (!File.Exists(Path.Combine(repoRoot, relativePath)))
    {
        failures.Add($"Missing file: {relativePath}");
    }
}

void ExpectNoDirectory(string relativePath)
{
    if (Directory.Exists(Path.Combine(repoRoot, relativePath)))
    {
        failures.Add($"Unexpected directory: {relativePath}");
    }
}

void ExpectNoGeneratedHeaderComments()
{
    foreach (var path in GeneratedFiles())
    {
        var text = File.ReadAllText(path);
        if (text.Contains("Generated from Intent-Driven-Development canonical sources.", StringComparison.Ordinal))
        {
            failures.Add($"Generated header comment is present: {Relative(path)}");
        }
    }
}

void ExpectNoEntryIncludes(string relativePath, string forbidden)
{
    var content = File.ReadAllText(Path.Combine(repoRoot, relativePath));
    if (content.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
    {
        failures.Add($"{relativePath} includes {forbidden}");
    }
}

void ExpectContains(string content, string expected, string relativePath, string context)
{
    if (!content.Contains(expected, StringComparison.Ordinal))
    {
        failures.Add($"{context} is missing '{expected}': {relativePath}");
    }
}

void ExpectDoesNotContain(string content, string forbidden, string relativePath, string context)
{
    if (content.Contains(forbidden, StringComparison.Ordinal))
    {
        failures.Add($"{context} contains obsolete text '{forbidden}': {relativePath}");
    }
}

string[] GeneratedSkillPaths(string skillName) =>
[
    $"generated/codex/.agents/skills/{skillName}/SKILL.md",
    $"generated/claude/.claude/skills/{skillName}/SKILL.md",
    $"generated/copilot/.github/skills/{skillName}/SKILL.md"
];

void ExpectEntryPointLineLimits()
{
    foreach (var relativePath in EntryPoints())
    {
        var content = File.ReadAllText(Path.Combine(repoRoot, relativePath));
        var lineCount = content.ReplaceLineEndings("\n").Split('\n').Length;
        if (lineCount > 80)
        {
            failures.Add($"Entry point exceeds 80 lines: {relativePath} has {lineCount} lines.");
        }
    }
}

void ExpectAllSkillsGenerated()
{
    var canonicalSkills = Directory
        .GetFiles(Path.Combine(repoRoot, "src", "canonical", "skills"), "*.md")
        .Select(Path.GetFileNameWithoutExtension)
        .OrderBy(name => name)
        .ToArray();

    var generatedSkillRoots = new[]
    {
        "generated/codex/.agents/skills",
        "generated/claude/.claude/skills",
        "generated/copilot/.github/skills"
    };

    foreach (var root in generatedSkillRoots)
    {
        var fullRoot = Path.Combine(repoRoot, root);
        if (!Directory.Exists(fullRoot))
        {
            failures.Add($"Generated skills root is missing: {root}");
            continue;
        }

        var generatedSkills = Directory
            .GetDirectories(fullRoot)
            .Select(Path.GetFileName)
            .OrderBy(name => name)
            .ToArray();

        if (!canonicalSkills.SequenceEqual(generatedSkills))
        {
            failures.Add($"Generated skills do not match canonical skills: {root}");
        }
    }
}

void ExpectPackManifestShape()
{
    const string manifestPath = "src/canonical/packs/pack-manifest.json";
    var fullManifestPath = Path.Combine(repoRoot, manifestPath);
    var content = File.ReadAllText(fullManifestPath);
    ExpectContains(content, "\"core\"", manifestPath, "pack manifest");
    ExpectContains(content, "\"factory\"", manifestPath, "pack manifest");
    ExpectContains(content, "\"requires\"", manifestPath, "pack manifest");
    ExpectContains(content, "\"projectFiles\"", manifestPath, "pack manifest");
    ExpectContains(content, "\"rolePrompts\"", manifestPath, "pack manifest");
    ExpectContains(content, "\"skillRoleReferences\"", manifestPath, "pack manifest");
    ExpectDoesNotContain(content, "\"agents\"", manifestPath, "pack manifest");

    var manifest = ReadPackManifest();
    if (manifest?.Packs is null || manifest.Packs.Count == 0)
    {
        failures.Add("Pack manifest could not be parsed.");
        return;
    }

    var canonicalSkills = Directory.GetFiles(Path.Combine(repoRoot, "src", "canonical", "skills"), "*.md")
        .Select(path => Path.GetFileNameWithoutExtension(path)!)
        .ToHashSet(StringComparer.Ordinal);
    var skillOwners = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var skillPath in Directory.GetFiles(Path.Combine(repoRoot, "src", "canonical", "skills"), "*.md"))
    {
        var skillName = Path.GetFileNameWithoutExtension(skillPath);
        var owners = manifest.Packs
            .Where(item => item.Value.Skills.Contains(skillName, StringComparer.Ordinal))
            .Select(item => item.Key)
            .ToArray();
        if (owners.Length != 1)
        {
            failures.Add($"Canonical skill is not owned by exactly one pack: {skillName}");
        }
        else
        {
            skillOwners[skillName!] = owners[0];
        }
    }

    foreach (var (packName, pack) in manifest.Packs)
    {
        foreach (var skill in pack.Skills)
        {
            if (!canonicalSkills.Contains(skill))
            {
                failures.Add($"Pack '{packName}' lists missing canonical skill: {skill}");
            }
        }

        foreach (var (skill, rolePrompts) in pack.SkillRoleReferences)
        {
            if (!pack.Skills.Contains(skill, StringComparer.Ordinal))
            {
                failures.Add($"Pack '{packName}' has role references for non-owned skill: {skill}");
            }

            foreach (var rolePrompt in rolePrompts)
            {
                if (!pack.RolePrompts.Contains(rolePrompt, StringComparer.Ordinal))
                {
                    failures.Add($"Pack '{packName}' skill '{skill}' references undeclared role prompt: {rolePrompt}");
                }
            }
        }
    }

    foreach (var skill in canonicalSkills)
    {
        if (!skillOwners.ContainsKey(skill))
        {
            failures.Add($"Canonical skill file is not listed in exactly one pack: {skill}");
        }
    }

    foreach (var rolePrompt in manifest.Packs.Values.SelectMany(pack => pack.RolePrompts).Distinct(StringComparer.Ordinal))
    {
        ExpectFile($"src/canonical/factory/roles/{rolePrompt}.md");
        ExpectContains(content, $"\"{rolePrompt}\"", manifestPath, "pack manifest role prompt");
    }

    ExpectNoDirectory("src/canonical/agents");
}

void ExpectManifestShape()
{
    const string manifestPath = "manifest.json";
    var fullManifestPath = Path.Combine(repoRoot, manifestPath);
    if (!File.Exists(fullManifestPath))
    {
        failures.Add("Generator did not create manifest.json.");
        return;
    }

    using var document = JsonDocument.Parse(File.ReadAllText(fullManifestPath));
    var root = document.RootElement;
    ExpectJsonProperty(root, "codingAgents", manifestPath);
    ExpectJsonProperty(root, "targets", manifestPath);
    ExpectJsonProperty(root, "codingAgentCapabilities", manifestPath);
    ExpectJsonProperty(root, "targetCapabilities", manifestPath);
    ExpectJsonProperty(root, "entryPoints", manifestPath);
    ExpectJsonProperty(root, "packs", manifestPath);

    var codingAgents = JsonStringArray(root, "codingAgents");
    var targets = JsonStringArray(root, "targets");
    if (!codingAgents.SequenceEqual(targets, StringComparer.Ordinal))
    {
        failures.Add("manifest.json codingAgents and targets differ.");
    }

    var codingAgentCapabilityKeys = JsonObjectKeys(root, "codingAgentCapabilities");
    var targetCapabilityKeys = JsonObjectKeys(root, "targetCapabilities");
    if (!codingAgentCapabilityKeys.SequenceEqual(targetCapabilityKeys, StringComparer.Ordinal))
    {
        failures.Add("manifest.json codingAgentCapabilities and targetCapabilities keys differ.");
    }

    if (!root.TryGetProperty("packs", out var packs) || packs.ValueKind != JsonValueKind.Object)
    {
        failures.Add("manifest.json packs must be an object.");
        return;
    }

    if (!packs.TryGetProperty("core", out _))
    {
        failures.Add("manifest.json is missing packs.core.");
    }

    if (!packs.TryGetProperty("factory", out var factoryPack))
    {
        failures.Add("manifest.json is missing packs.factory.");
    }
    else
    {
        ExpectJsonProperty(factoryPack, "rolePrompts", manifestPath);
        ExpectJsonProperty(factoryPack, "skillRoleReferences", manifestPath);
    }
}

void ExpectJsonProperty(JsonElement element, string propertyName, string relativePath)
{
    if (!element.TryGetProperty(propertyName, out _))
    {
        failures.Add($"{relativePath} is missing '{propertyName}'.");
    }
}

string[] JsonStringArray(JsonElement root, string propertyName) =>
    root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Array
        ? property.EnumerateArray().Select(item => item.GetString() ?? "").ToArray()
        : [];

string[] JsonObjectKeys(JsonElement root, string propertyName) =>
    root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Object
        ? property.EnumerateObject().Select(item => item.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray()
        : [];

SmokePackManifest? ReadPackManifest()
{
    var path = Path.Combine(repoRoot, "src", "canonical", "packs", "pack-manifest.json");
    if (!File.Exists(path))
    {
        failures.Add("Missing pack manifest: src/canonical/packs/pack-manifest.json");
        return null;
    }

    try
    {
        return JsonSerializer.Deserialize<SmokePackManifest>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }
    catch (JsonException exception)
    {
        failures.Add($"Pack manifest could not be parsed: {exception.Message}");
        return null;
    }
}

void ExpectFactoryGeneratedShape()
{
    var manifest = ReadPackManifest();
    if (manifest?.Packs is null || !manifest.Packs.TryGetValue("factory", out var factoryPack))
    {
        failures.Add("Pack manifest is missing factory pack.");
        return;
    }

    foreach (var relativePath in factoryPack.Skills.SelectMany(GeneratedSkillPaths))
    {
        ExpectFile(relativePath);
    }
}

void ExpectFactoryRolePromptReferences()
{
    var manifest = ReadPackManifest();
    if (manifest?.Packs is null || !manifest.Packs.TryGetValue("factory", out var factoryPack))
    {
        failures.Add("Pack manifest is missing factory pack.");
        return;
    }

    var roots = new[]
    {
        "generated/codex/.agents/skills",
        "generated/claude/.claude/skills",
        "generated/copilot/.github/skills"
    };

    foreach (var root in roots)
    {
        foreach (var skill in factoryPack.Skills)
        {
            var expectedRolePrompts = factoryPack.SkillRoleReferences.GetValueOrDefault(skill) ?? [];
            foreach (var rolePrompt in expectedRolePrompts)
            {
                ExpectFile($"{root}/{skill}/references/roles/{rolePrompt}.md");
            }

            var roleRoot = Path.Combine(repoRoot, $"{root}/{skill}/references/roles".Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(roleRoot))
            {
                continue;
            }

            var actualRolePrompts = Directory
                .GetFiles(roleRoot, "*.md")
                .Select(path => Path.GetFileNameWithoutExtension(path)!)
                .ToArray();
            foreach (var rolePrompt in actualRolePrompts.Except(expectedRolePrompts, StringComparer.Ordinal))
            {
                failures.Add($"Factory skill has unexpected role prompt reference: {root}/{skill}/references/roles/{rolePrompt}.md");
            }
        }

        var specImplementRoles = Path.Combine(repoRoot, $"{root}/spec-implement/references/roles".Replace('/', Path.DirectorySeparatorChar));
        if (Directory.Exists(specImplementRoles))
        {
            failures.Add($"{root}/spec-implement must not contain factory role prompt references.");
        }
    }
}

void ExpectClaudeSkillMetadata()
{
    var specAuditPath = Path.Combine(repoRoot, "generated/claude/.claude/skills/spec-audit/SKILL.md".Replace('/', Path.DirectorySeparatorChar));
    if (!File.Exists(specAuditPath))
    {
        failures.Add("Missing Claude spec-audit skill for frontmatter check.");
    }
    else
    {
        var content = File.ReadAllText(specAuditPath);
        foreach (var text in new[]
        {
            "context: fork",
            "agent: Explore",
            "argument-hint: \"[scope or audit focus]\"",
            "allowed-tools: Read Glob Grep"
        })
        {
            if (!content.Contains(text, StringComparison.Ordinal))
            {
                failures.Add($"Claude spec-audit skill is missing frontmatter '{text}'.");
            }
        }
    }

    var specChangePath = Path.Combine(repoRoot, "generated/claude/.claude/skills/spec-change/SKILL.md".Replace('/', Path.DirectorySeparatorChar));
    if (!File.Exists(specChangePath))
    {
        failures.Add("Missing Claude spec-change skill for frontmatter check.");
    }
    else
    {
        var content = File.ReadAllText(specChangePath);
        if (content.Contains("context: fork", StringComparison.Ordinal))
        {
            failures.Add("Claude spec-change skill unexpectedly has context: fork.");
        }
    }
}

void ExpectGeneratorCheckPasses()
{
    var exitCode = RunProcess("dotnet", $"exec \"{generatorDll}\" --check");
    if (exitCode != 0)
    {
        failures.Add("Generator check failed.");
    }
}

void ExpectListPacks()
{
    var result = RunProcessResult("dotnet", $"exec \"{toolDll}\" list-packs");
    var expected = string.Join(Environment.NewLine, new[] { "core", "factory" });
    var actual = result.StandardOutput.Trim().ReplaceLineEndings(Environment.NewLine);

    if (result.ExitCode != 0)
    {
        failures.Add("list-packs failed.");
    }

    if (!StringComparer.Ordinal.Equals(actual, expected))
    {
        failures.Add($"list-packs returned unexpected output: {actual}");
    }
}

void ExpectListCodingAgents()
{
    var result = RunProcessResult("dotnet", $"exec \"{toolDll}\" list-coding-agents");
    var expected = string.Join(Environment.NewLine, new[] { "claude", "codex", "copilot", "gemini" });
    var actual = result.StandardOutput.Trim().ReplaceLineEndings(Environment.NewLine);

    if (result.ExitCode != 0)
    {
        failures.Add("list-coding-agents failed.");
    }

    if (!StringComparer.Ordinal.Equals(actual, expected))
    {
        failures.Add($"list-coding-agents returned unexpected output: {actual}");
    }
}

void ExpectDefaultInstallCoreOnly()
{
    WithToolInstall("install --target claude", installRoot =>
    {
        ExpectTempFile(installRoot, "CLAUDE.md", "default install did not create CLAUDE.md.");
        ExpectTempFile(installRoot, ".claude/skills/spec-new-document/SKILL.md", "default install did not install core skill.");
        ExpectTempFile(installRoot, ".specs/README.md", "default install did not install .specs.");

        if (File.Exists(Path.Combine(installRoot, ".claude/skills/factory-create-work-plan/SKILL.md".Replace('/', Path.DirectorySeparatorChar))))
        {
            failures.Add("default install installed factory skills.");
        }
    });

    WithToolInstall("install --coding-agent claude", installRoot =>
    {
        ExpectTempFile(installRoot, "CLAUDE.md", "install --coding-agent did not create CLAUDE.md.");
        ExpectTempFile(installRoot, ".claude/skills/spec-new-document/SKILL.md", "install --coding-agent did not install core skill.");
    });
}

void ExpectFactoryInstall()
{
    foreach (var target in new[] { "claude", "codex" })
    {
        WithToolInstall($"install --target {target} --pack factory", installRoot =>
        {
            var skillRoot = target == "claude" ? ".claude/skills" : ".agents/skills";
            var entry = target == "claude" ? "CLAUDE.md" : "AGENTS.md";
            ExpectTempFile(installRoot, entry, $"factory install for {target} did not create {entry}.");
            ExpectTempFile(installRoot, $"{skillRoot}/spec-new-document/SKILL.md", $"factory install for {target} did not install core skill.");
            ExpectTempFile(installRoot, $"{skillRoot}/factory-create-work-plan/SKILL.md", $"factory install for {target} did not install factory skill.");
            ExpectTempFile(installRoot, ".idd/factory/.gitignore", $"factory install for {target} did not install factory .gitignore.");

            if (Directory.Exists(Path.Combine(installRoot, ".idd/factory/work".Replace('/', Path.DirectorySeparatorChar))))
            {
                failures.Add($"factory install for {target} created work directory.");
            }
        });
    }
}

void ExpectFactoryUnsupportedTargetRejected()
{
    var tempRoot = Path.Combine(Path.GetTempPath(), "idd-smoke-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempRoot);

    try
    {
        var result = RunProcessResult("dotnet", $"exec \"{toolDll}\" install --target gemini --pack factory", tempRoot);
        if (result.ExitCode == 0)
        {
            failures.Add("Factory install for Gemini succeeded unexpectedly.");
        }

        if (!result.StandardError.Contains("Factory pack requires generated skills", StringComparison.Ordinal))
        {
            failures.Add("Factory install for Gemini did not report unsupported generated skills.");
        }
    }
    finally
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}

void WithToolInstall(string arguments, Action<string> assertions)
{
    var installRoot = Path.Combine(Path.GetTempPath(), "idd-smoke-install-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(installRoot);

    try
    {
        var result = RunProcessResult("dotnet", $"exec \"{toolDll}\" {arguments}", installRoot);
        if (result.ExitCode != 0)
        {
            failures.Add($"{arguments} failed.");
            return;
        }

        assertions(installRoot);
    }
    finally
    {
        if (Directory.Exists(installRoot))
        {
            Directory.Delete(installRoot, recursive: true);
        }
    }
}

void ExpectInstallEntryNone()
{
    var tempRoot = Path.Combine(Path.GetTempPath(), "idd-smoke-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempRoot);

    try
    {
        var exitCode = RunProcess("dotnet", $"exec \"{toolDll}\" install --target claude --entry none", tempRoot);
        if (exitCode != 0)
        {
            failures.Add("Install with --entry none failed.");
            return;
        }

        if (File.Exists(Path.Combine(tempRoot, "CLAUDE.md")))
        {
            failures.Add("Install with --entry none created CLAUDE.md.");
        }

        if (!File.Exists(Path.Combine(tempRoot, ".claude", "skills", "spec-new-document", "SKILL.md")))
        {
            failures.Add("Install with --entry none did not install skills.");
        }

        if (!File.Exists(Path.Combine(tempRoot, ".specs", "README.md")))
        {
            failures.Add("Install with --entry none did not install .specs.");
        }
    }
    finally
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}

void ExpectInstallGeminiEntryNoneRejected()
{
    var tempRoot = Path.Combine(Path.GetTempPath(), "idd-smoke-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempRoot);

    try
    {
        var result = RunProcessResult("dotnet", $"exec \"{toolDll}\" install --target gemini --entry none", tempRoot);
        if (result.ExitCode == 0)
        {
            failures.Add("Gemini install with --entry none succeeded unexpectedly.");
        }

        if (!result.StandardError.Contains("CodingAgent gemini does not support generated skills", StringComparison.Ordinal))
        {
            failures.Add("Gemini install with --entry none did not report unsupported generated skills.");
        }
    }
    finally
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}

void ExpectInstallAllAfterInit()
{
    var tempRoot = Path.Combine(Path.GetTempPath(), "idd-smoke-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempRoot);

    try
    {
        var initExitCode = RunProcess("dotnet", $"exec \"{toolDll}\" init", tempRoot);
        if (initExitCode != 0)
        {
            failures.Add("Init failed before install --all.");
            return;
        }

        var installExitCode = RunProcess("dotnet", $"exec \"{toolDll}\" install --all", tempRoot);
        if (installExitCode != 0)
        {
            failures.Add("Install --all failed after init.");
            return;
        }

        foreach (var relativePath in new[]
        {
            "CLAUDE.md",
            "AGENTS.md",
            "GEMINI.md",
            ".github/copilot-instructions.md"
        })
        {
            if (!File.Exists(Path.Combine(tempRoot, relativePath)))
            {
                failures.Add($"Install --all after init did not create {relativePath}.");
            }
        }
    }
    finally
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}

void ExpectNpmListTargets()
{
    WithNpmFixture(fixtureRoot =>
    {
        var script = Path.Combine(fixtureRoot, "bin", "intent-driven-development.js");
        var result = RunProcessResult("node", $"\"{script}\" list-targets", fixtureRoot);
        var expected = string.Join(Environment.NewLine, new[] { "claude", "codex", "copilot", "gemini" });
        var actual = result.StandardOutput.Trim().ReplaceLineEndings(Environment.NewLine);

        if (result.ExitCode != 0)
        {
            failures.Add("npm list-targets failed.");
        }

        if (!StringComparer.Ordinal.Equals(actual, expected))
        {
            failures.Add($"npm list-targets returned unexpected output: {actual}");
        }
    });
}

void ExpectNpmListCodingAgents()
{
    WithNpmFixture(fixtureRoot =>
    {
        var script = Path.Combine(fixtureRoot, "bin", "intent-driven-development.js");
        var result = RunProcessResult("node", $"\"{script}\" list-coding-agents", fixtureRoot);
        var expected = string.Join(Environment.NewLine, new[] { "claude", "codex", "copilot", "gemini" });
        var actual = result.StandardOutput.Trim().ReplaceLineEndings(Environment.NewLine);

        if (result.ExitCode != 0)
        {
            failures.Add("npm list-coding-agents failed.");
        }

        if (!StringComparer.Ordinal.Equals(actual, expected))
        {
            failures.Add($"npm list-coding-agents returned unexpected output: {actual}");
        }
    });
}

void ExpectNpmListPacks()
{
    WithNpmFixture(fixtureRoot =>
    {
        var script = Path.Combine(fixtureRoot, "bin", "intent-driven-development.js");
        var result = RunProcessResult("node", $"\"{script}\" list-packs", fixtureRoot);
        var expected = string.Join(Environment.NewLine, new[] { "core", "factory" });
        var actual = result.StandardOutput.Trim().ReplaceLineEndings(Environment.NewLine);

        if (result.ExitCode != 0)
        {
            failures.Add("npm list-packs failed.");
        }

        if (!StringComparer.Ordinal.Equals(actual, expected))
        {
            failures.Add($"npm list-packs returned unexpected output: {actual}");
        }
    });
}

void ExpectNpmInstallDefaultMinimal()
{
    WithNpmInstall("install --target claude", installRoot =>
    {
        ExpectTempFile(installRoot, "CLAUDE.md", "npm default minimal install did not create CLAUDE.md.");
        ExpectTempFile(installRoot, ".claude/skills/spec-new-document/SKILL.md", "npm default minimal install did not install skills.");
        ExpectTempFile(installRoot, ".specs/README.md", "npm default minimal install did not install .specs.");
    });

    WithNpmInstall("install --coding-agent claude", installRoot =>
    {
        ExpectTempFile(installRoot, "CLAUDE.md", "npm install --coding-agent did not create CLAUDE.md.");
        ExpectTempFile(installRoot, ".claude/skills/spec-new-document/SKILL.md", "npm install --coding-agent did not install skills.");
    });
}

void ExpectNpmInstallDefaultCoreOnly()
{
    WithNpmInstall("install --target claude", installRoot =>
    {
        if (File.Exists(Path.Combine(installRoot, ".claude/skills/factory-create-work-plan/SKILL.md".Replace('/', Path.DirectorySeparatorChar))))
        {
            failures.Add("npm default install installed factory skills.");
        }
    });
}

void ExpectNpmInstallFactory()
{
    WithNpmInstall("install --target codex --pack factory", installRoot =>
    {
        ExpectTempFile(installRoot, "AGENTS.md", "npm factory install did not create AGENTS.md.");
        ExpectTempFile(installRoot, ".agents/skills/spec-new-document/SKILL.md", "npm factory install did not install core skill.");
        ExpectTempFile(installRoot, ".agents/skills/factory-create-work-plan/SKILL.md", "npm factory install did not install factory skill.");
        ExpectTempFile(installRoot, ".idd/factory/.gitignore", "npm factory install did not install factory .gitignore.");

        if (Directory.Exists(Path.Combine(installRoot, ".idd/factory/work".Replace('/', Path.DirectorySeparatorChar))))
        {
            failures.Add("npm factory install created work directory.");
        }
    });
}

void ExpectNpmInstallEntryNone()
{
    WithNpmInstall("install --target claude --entry none", installRoot =>
    {
        if (File.Exists(Path.Combine(installRoot, "CLAUDE.md")))
        {
            failures.Add("npm install with --entry none created CLAUDE.md.");
        }

        ExpectTempFile(installRoot, ".claude/skills/spec-new-document/SKILL.md", "npm install with --entry none did not install skills.");
        ExpectTempFile(installRoot, ".specs/README.md", "npm install with --entry none did not install .specs.");
    });
}

void ExpectNpmInstallEntryFull()
{
    WithNpmInstall("install --target claude --entry full", installRoot =>
    {
        var entryPath = Path.Combine(installRoot, "CLAUDE.md");
        ExpectTempFile(installRoot, "CLAUDE.md", "npm install with --entry full did not create CLAUDE.md.");
        if (File.Exists(entryPath))
        {
            var lineCount = File.ReadAllText(entryPath).ReplaceLineEndings("\n").Split('\n').Length;
            if (lineCount <= 80)
            {
                failures.Add($"npm install with --entry full created an unexpectedly short entry point: {lineCount} lines.");
            }
        }

        ExpectTempFile(installRoot, ".claude/skills/spec-new-document/SKILL.md", "npm install with --entry full did not install skills.");
        ExpectTempFile(installRoot, ".specs/README.md", "npm install with --entry full did not install .specs.");
    });
}

void ExpectNpmRejectsGeminiEntryNone()
{
    WithNpmFixture(fixtureRoot =>
    {
        var script = Path.Combine(fixtureRoot, "bin", "intent-driven-development.js");
        var installRoot = Path.Combine(Path.GetTempPath(), "idd-smoke-npm-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(installRoot);

        try
        {
            var result = RunProcessResult("node", $"\"{script}\" install --target gemini --entry none", installRoot);
            if (result.ExitCode == 0)
            {
                failures.Add("npm Gemini install with --entry none succeeded unexpectedly.");
            }

            if (!result.StandardError.Contains("CodingAgent gemini does not support generated skills", StringComparison.Ordinal))
            {
                failures.Add("npm Gemini install with --entry none did not report unsupported generated skills.");
            }
        }
        finally
        {
            if (Directory.Exists(installRoot))
            {
                Directory.Delete(installRoot, recursive: true);
            }
        }
    });
}

void ExpectNpmRejectsFactoryForGemini()
{
    WithNpmFixture(fixtureRoot =>
    {
        var script = Path.Combine(fixtureRoot, "bin", "intent-driven-development.js");
        var installRoot = Path.Combine(Path.GetTempPath(), "idd-smoke-npm-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(installRoot);

        try
        {
            var result = RunProcessResult("node", $"\"{script}\" install --target gemini --pack factory", installRoot);
            if (result.ExitCode == 0)
            {
                failures.Add("npm factory install for Gemini succeeded unexpectedly.");
            }

            if (!result.StandardError.Contains("Factory pack requires generated skills", StringComparison.Ordinal))
            {
                failures.Add("npm factory install for Gemini did not report unsupported generated skills.");
            }
        }
        finally
        {
            if (Directory.Exists(installRoot))
            {
                Directory.Delete(installRoot, recursive: true);
            }
        }
    });
}

void ExpectNpmRejectsUnknownPack()
{
    WithNpmFixture(fixtureRoot =>
    {
        var script = Path.Combine(fixtureRoot, "bin", "intent-driven-development.js");
        var installRoot = Path.Combine(Path.GetTempPath(), "idd-smoke-npm-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(installRoot);

        try
        {
            var result = RunProcessResult("node", $"\"{script}\" install --target claude --pack bogus", installRoot);
            if (result.ExitCode == 0)
            {
                failures.Add("npm install with unknown pack succeeded unexpectedly.");
            }

            if (!result.StandardError.Contains("Unknown pack: bogus", StringComparison.Ordinal))
            {
                failures.Add("npm install with unknown pack did not report the invalid pack.");
            }
        }
        finally
        {
            if (Directory.Exists(installRoot))
            {
                Directory.Delete(installRoot, recursive: true);
            }
        }
    });
}

void ExpectNpmRejectsUnknownEntryMode()
{
    WithNpmFixture(fixtureRoot =>
    {
        var script = Path.Combine(fixtureRoot, "bin", "intent-driven-development.js");
        var installRoot = Path.Combine(Path.GetTempPath(), "idd-smoke-npm-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(installRoot);

        try
        {
            var result = RunProcessResult("node", $"\"{script}\" install --target claude --entry compact", installRoot);
            if (result.ExitCode == 0)
            {
                failures.Add("npm install with unknown entry mode succeeded unexpectedly.");
            }

            if (!result.StandardError.Contains("Unknown entry mode: compact", StringComparison.Ordinal))
            {
                failures.Add("npm install with unknown entry mode did not report the invalid mode.");
            }
        }
        finally
        {
            if (Directory.Exists(installRoot))
            {
                Directory.Delete(installRoot, recursive: true);
            }
        }
    });
}

void WithNpmInstall(string arguments, Action<string> assertions)
{
    WithNpmFixture(fixtureRoot =>
    {
        var script = Path.Combine(fixtureRoot, "bin", "intent-driven-development.js");
        var installRoot = Path.Combine(Path.GetTempPath(), "idd-smoke-npm-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(installRoot);

        try
        {
            var result = RunProcessResult("node", $"\"{script}\" {arguments}", installRoot);
            if (result.ExitCode != 0)
            {
                failures.Add($"npm {arguments} failed.");
                return;
            }

            assertions(installRoot);
        }
        finally
        {
            if (Directory.Exists(installRoot))
            {
                Directory.Delete(installRoot, recursive: true);
            }
        }
    });
}

void WithNpmFixture(Action<string> action)
{
    var fixtureRoot = Path.Combine(Path.GetTempPath(), "idd-smoke-npm-fixture-" + Guid.NewGuid().ToString("N"));

    try
    {
        Directory.CreateDirectory(fixtureRoot);
        Directory.CreateDirectory(Path.Combine(fixtureRoot, "package-content"));
        File.Copy(Path.Combine(repoRoot, "npm", "package.json"), Path.Combine(fixtureRoot, "package.json"));
        CopyDirectoryRecursive(Path.Combine(repoRoot, "npm", "bin"), Path.Combine(fixtureRoot, "bin"));
        if (!File.Exists(Path.Combine(repoRoot, "manifest.json")))
        {
            failures.Add("manifest.json is missing before npm fixture copy. RunGenerator must create it.");
            return;
        }

        File.Copy(Path.Combine(repoRoot, "manifest.json"), Path.Combine(fixtureRoot, "package-content", "manifest.json"));
        CopyDirectoryRecursive(Path.Combine(repoRoot, "generated"), Path.Combine(fixtureRoot, "package-content", "generated"));
        CopyDirectoryRecursive(Path.Combine(repoRoot, "src"), Path.Combine(fixtureRoot, "package-content", "src"));
        File.Copy(Path.Combine(repoRoot, "README.md"), Path.Combine(fixtureRoot, "package-content", "README.md"));
        File.Copy(Path.Combine(repoRoot, "LICENSE"), Path.Combine(fixtureRoot, "package-content", "LICENSE"));

        action(fixtureRoot);
    }
    finally
    {
        if (Directory.Exists(fixtureRoot))
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }
}

void ExpectTempFile(string root, string relativePath, string failure)
{
    if (!File.Exists(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar))))
    {
        failures.Add(failure);
    }
}

void CopyDirectoryRecursive(string source, string destination)
{
    Directory.CreateDirectory(destination);
    foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
    {
        Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
    }

    foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
    {
        var destinationPath = Path.Combine(destination, Path.GetRelativePath(source, file));
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(file, destinationPath, overwrite: true);
    }
}

void ExpectSecondRunStable()
{
    var before = SnapshotGeneratedFiles();
    RunGenerator();
    var after = SnapshotGeneratedFiles();
    if (!before.SequenceEqual(after))
    {
        failures.Add("Running generator twice changed generated output.");
    }
}

void RunGenerator()
{
    var exitCode = RunProcess("dotnet", $"exec \"{generatorDll}\"");
    if (exitCode != 0)
    {
        failures.Add("Generator failed.");
    }
}

IEnumerable<string> GeneratedFiles() =>
    Directory.Exists(Path.Combine(repoRoot, "generated"))
        ? Directory.GetFiles(Path.Combine(repoRoot, "generated"), "*", SearchOption.AllDirectories).OrderBy(path => path)
        : Array.Empty<string>();

string[] SnapshotGeneratedFiles() =>
    GeneratedFiles()
        .Select(path => $"{Relative(path)}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}")
        .OrderBy(value => value)
        .ToArray();

int RunProcess(string fileName, string arguments, string? workingDirectory = null)
{
    return RunProcessResult(fileName, arguments, workingDirectory).ExitCode;
}

ProcessResult RunProcessResult(string fileName, string arguments, string? workingDirectory = null)
{
    using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
    {
        WorkingDirectory = workingDirectory ?? repoRoot,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    });

    if (process is null)
    {
        failures.Add($"Could not start process: {fileName}");
        return new ProcessResult(1, "", $"Could not start process: {fileName}");
    }

    var standardOutput = process.StandardOutput.ReadToEnd();
    var standardError = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (!string.IsNullOrWhiteSpace(standardOutput))
    {
        Console.Write(standardOutput);
    }

    if (!string.IsNullOrWhiteSpace(standardError))
    {
        Console.Error.Write(standardError);
    }

    return new ProcessResult(process.ExitCode, standardOutput, standardError);
}

string Relative(string path) => Path.GetRelativePath(repoRoot, path).Replace('\\', '/');

string[] EntryPoints() =>
[
    "generated/claude/CLAUDE.md",
    "generated/codex/AGENTS.md",
    "generated/gemini/GEMINI.md",
    "generated/copilot/.github/copilot-instructions.md"
];

static string FindRepoRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
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

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal sealed record SmokePackManifest(Dictionary<string, SmokePackDefinition> Packs);

internal sealed record SmokePackDefinition(
    string Description,
    bool Default,
    string[] Requires,
    string[] Skills,
    string[] RolePrompts,
    Dictionary<string, string[]> SkillRoleReferences,
    SmokeProjectFileDefinition[] ProjectFiles);

internal sealed record SmokeProjectFileDefinition(string Source, string Destination);
