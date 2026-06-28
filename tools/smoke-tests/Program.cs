using System.Diagnostics;
using System.Security.Cryptography;

var repoRoot = FindRepoRoot();
var failures = new List<string>();
var generatorDll = Path.Combine(repoRoot, "tools", "generate", "bin", "Debug", "net10.0", "Generate.dll");
var toolDll = Path.Combine(repoRoot, "tools", "idd-tool", "bin", "Debug", "net10.0", "IntentDrivenDevelopment.Tool.dll");

RunGenerator();

ExpectFile("generated/codex/AGENTS.md");
ExpectSkillFiles("generated/codex/.agents/skills");

ExpectFile("generated/claude/CLAUDE.md");
ExpectSkillFiles("generated/claude/.claude/skills");

ExpectFile("generated/gemini/GEMINI.md");
ExpectNoDirectory("generated/gemini/.agents");
ExpectNoDirectory("generated/gemini/.claude");
ExpectNoDirectory("generated/gemini/.github/skills");

ExpectFile("generated/copilot/.github/copilot-instructions.md");
ExpectSkillFiles("generated/copilot/.github/skills");

ExpectNoGeneratedHeaderComments();
ExpectNoGeneratedText("Worklog-driven development");
ExpectNoGeneratedText("Generated files are not source of truth");
ExpectNoCanonicalAgentCoupling();
ExpectNoEntryIncludes("generated/claude/CLAUDE.md", "AGENTS.md");
ExpectNoEntryIncludes("generated/gemini/GEMINI.md", "AGENTS.md");
ExpectEntryPointLineLimits();
ExpectNoFullMethodologyInEntryPoints();
ExpectAllSkillsGenerated();
ExpectClaudeSkillMetadata();
ExpectSpecImportGeneratedShape();
ExpectNoLegacySpecImportReportGuidance();
ExpectSpecBrainstormGeneratedShape();
ExpectEntryPointSkillRoutingShape();
ExpectInstallEntryNone();
ExpectInstallGeminiEntryNoneRejected();
ExpectInstallAllAfterInit();
ExpectNpmListTargets();
ExpectNpmInstallDefaultMinimal();
ExpectNpmInstallEntryNone();
ExpectNpmInstallEntryFull();
ExpectNpmRejectsGeminiEntryNone();
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

void ExpectSkillFiles(string skillsRoot)
{
    var expected = new[]
    {
        "spec-audit",
        "spec-brainstorm",
        "spec-change",
        "spec-import",
        "spec-implement",
        "spec-lint",
        "spec-new-document",
        "spec-update-from-implementation",
        "spec-normalize-current",
        "spec-check-implementation"
    };

    foreach (var skill in expected)
    {
        ExpectFile($"{skillsRoot}/{skill}/SKILL.md");
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

void ExpectNoGeneratedText(string text)
{
    foreach (var path in GeneratedFiles())
    {
        var content = File.ReadAllText(path);
        if (content.Contains(text, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"Generated file contains forbidden text '{text}': {Relative(path)}");
        }
    }
}

void ExpectNoCanonicalAgentCoupling()
{
    var canonicalRoot = Path.Combine(repoRoot, "src", "canonical");
    foreach (var path in Directory.GetFiles(canonicalRoot, "*.md", SearchOption.AllDirectories))
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.Contains("/migration-from-copilotinstructions.md", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        var content = File.ReadAllText(path);
        if (content.Contains("Codex-specific", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("AGENTS.md", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"Canonical file contains agent-specific wording: {Relative(path)}");
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

string ReadRequiredGeneratedFile(string relativePath)
{
    var fullPath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    if (!File.Exists(fullPath))
    {
        failures.Add($"Missing generated file: {relativePath}");
        return "";
    }

    return File.ReadAllText(fullPath);
}

void ExpectSections(string content, string relativePath, params string[] headings)
{
    foreach (var heading in headings)
    {
        ExpectContains(content, heading, relativePath, "section");
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

void ExpectSkillReferences(string content, string relativePath, params string[] skillNames)
{
    foreach (var skillName in skillNames)
    {
        ExpectContains(content, skillName, relativePath, "generated skill reference");
    }
}

void ExpectFencedBlockBetween(string content, string relativePath, string startHeading, string endHeading)
{
    var start = content.IndexOf(startHeading, StringComparison.Ordinal);
    if (start < 0)
    {
        failures.Add($"section is missing '{startHeading}': {relativePath}");
        return;
    }

    var end = content.IndexOf(endHeading, start + startHeading.Length, StringComparison.Ordinal);
    if (end < 0)
    {
        failures.Add($"section is missing '{endHeading}' after '{startHeading}': {relativePath}");
        return;
    }

    var section = content[start..end];
    if (!section.Contains("```", StringComparison.Ordinal))
    {
        failures.Add($"section '{startHeading}' is missing a fenced output format block: {relativePath}");
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

void ExpectNoFullMethodologyInEntryPoints()
{
    var forbidden = new[]
    {
        "## Required Reading",
        "## Method Summary",
        "## Classification Rules",
        "## Output Format",
        "Specifications should be complete enough to rebuild the product from scratch"
    };

    foreach (var relativePath in EntryPoints())
    {
        var content = File.ReadAllText(Path.Combine(repoRoot, relativePath));
        foreach (var text in forbidden)
        {
            if (content.Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"Entry point contains full methodology text '{text}': {relativePath}");
            }
        }
    }
}

void ExpectAllSkillsGenerated()
{
    var canonicalSkills = Directory
        .GetFiles(Path.Combine(repoRoot, "src", "canonical", "skills"), "spec-*.md")
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

// These smoke tests intentionally verify generated file shape and stable mechanical anchors only.
// Semantic routing/behavior of skills belongs in separate prompt/LLM evaluation tests.
void ExpectSpecImportGeneratedShape()
{
    var sections = new[]
    {
        "## Default Modes",
        "## Current Spec Test",
        "## Structural Normalization",
        "## Required Behavior",
        "## Source Triage",
        "## Import Inventory",
        "## Fragment Classification",
        "## Source-to-target Remap",
        "## Conflict Handling",
        "## Normalized Writing Rules",
        "## Relation Normalization",
        "## Index Regeneration",
        "## Workflow",
        "## Post-import Cleanup",
        "## Import Report"
    };

    foreach (var relativePath in GeneratedSkillPaths("spec-import"))
    {
        var content = ReadRequiredGeneratedFile(relativePath);
        ExpectSections(content, relativePath, sections);
        ExpectContains(content, ".specs/INDEX.md", relativePath, "spec-import generated skill reference");
        ExpectContains(content, ".specs/README.md", relativePath, "spec-import generated skill reference");
        ExpectSkillReferences(content, relativePath, "spec-lint", "spec-normalize-current");
    }
}

void ExpectNoLegacySpecImportReportGuidance()
{
    var forbidden = new[]
    {
        "For non-trivial imports, create or update `.specs/import-report.md`",
        "Write an import report for non-trivial imports."
    };

    foreach (var relativePath in GeneratedSkillPaths("spec-import"))
    {
        var content = ReadRequiredGeneratedFile(relativePath);
        foreach (var text in forbidden)
        {
            ExpectDoesNotContain(content, text, relativePath, "legacy spec-import report guidance");
        }
    }
}

void ExpectSpecBrainstormGeneratedShape()
{
    var sections = new[]
    {
        "## Purpose",
        "## When to use this skill",
        "## When not to use this skill",
        "## Boundaries",
        "## Relationship to other skills",
        "## Customer discovery questions",
        "## Output formats",
        "## Rules",
        "## Examples",
        "## Non-goals"
    };

    var relatedSkills = new[]
    {
        "spec-change",
        "spec-new-document",
        "spec-implement",
        "spec-check-implementation",
        "spec-update-from-implementation",
        "spec-normalize-current",
        "spec-audit",
        "spec-lint",
        "spec-import"
    };

    foreach (var relativePath in GeneratedSkillPaths("spec-brainstorm"))
    {
        var content = ReadRequiredGeneratedFile(relativePath);
        ExpectSections(content, relativePath, sections);
        ExpectSkillReferences(content, relativePath, relatedSkills);
        ExpectFencedBlockBetween(content, relativePath, "## Output formats", "## Rules");
    }
}

void ExpectEntryPointSkillRoutingShape()
{
    foreach (var relativePath in EntryPoints().Where(path => !path.Contains("/gemini/", StringComparison.Ordinal)))
    {
        var content = ReadRequiredGeneratedFile(relativePath);
        ExpectSections(content, relativePath, "## IDD Workflow Routing");
        ExpectSkillReferences(content, relativePath, "spec-brainstorm", "spec-change", "spec-implement", "spec-check-implementation");
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

        if (!result.StandardError.Contains("Target gemini does not support generated skills", StringComparison.Ordinal))
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

void ExpectNpmInstallDefaultMinimal()
{
    WithNpmInstall("install --target claude", installRoot =>
    {
        ExpectTempFile(installRoot, "CLAUDE.md", "npm default minimal install did not create CLAUDE.md.");
        ExpectTempFile(installRoot, ".claude/skills/spec-new-document/SKILL.md", "npm default minimal install did not install skills.");
        ExpectTempFile(installRoot, ".specs/README.md", "npm default minimal install did not install .specs.");
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
            var content = File.ReadAllText(entryPath);
            if (!content.Contains("# Intent-Driven Development", StringComparison.Ordinal))
            {
                failures.Add("npm install with --entry full did not include the full-entry methodology marker.");
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

            if (!result.StandardError.Contains("Target gemini does not support generated skills", StringComparison.Ordinal))
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
