using System.Diagnostics;
using System.Security.Cryptography;

var repoRoot = FindRepoRoot();
var failures = new List<string>();

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

ExpectGeneratedHeaders();
ExpectNoGeneratedText("Worklog-driven development");
ExpectNoGeneratedText(".worklog");
ExpectNoGeneratedText("Generated files are not source of truth");
ExpectNoCanonicalAgentCoupling();
ExpectNoEntryIncludes("generated/claude/CLAUDE.md", "AGENTS.md");
ExpectNoEntryIncludes("generated/gemini/GEMINI.md", "AGENTS.md");
ExpectEntryPointLineLimits();
ExpectNoFullMethodologyInEntryPoints();
ExpectAllSkillsGenerated();
ExpectInstallEntryNone();
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
        "spec-import",
        "spec-create",
        "spec-update-from-implementation",
        "spec-reorganize",
        "spec-check-implementation"
    };

    foreach (var skill in expected)
    {
        ExpectFile($"{skillsRoot}/{skill}/SKILL.md");
    }
}

void ExpectGeneratedHeaders()
{
    foreach (var path in GeneratedFiles())
    {
        var text = File.ReadAllText(path);
        if (!text.ReplaceLineEndings("\n").StartsWith("<!--\nGenerated from Intent-Driven-Development canonical sources.", StringComparison.Ordinal))
        {
            failures.Add($"Missing generated header: {Relative(path)}");
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

void ExpectGeneratorCheckPasses()
{
    var exitCode = RunProcess("dotnet", "run --project tools/generate -- --check");
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
        var toolProject = Path.Combine(repoRoot, "tools", "idd-tool");
        var exitCode = RunProcess("dotnet", $"run --project \"{toolProject}\" -- install --target claude --entry none", tempRoot);
        if (exitCode != 0)
        {
            failures.Add("Install with --entry none failed.");
            return;
        }

        if (File.Exists(Path.Combine(tempRoot, "CLAUDE.md")))
        {
            failures.Add("Install with --entry none created CLAUDE.md.");
        }

        if (!File.Exists(Path.Combine(tempRoot, ".claude", "skills", "spec-create", "SKILL.md")))
        {
            failures.Add("Install with --entry none did not install skills.");
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
    var exitCode = RunProcess("dotnet", "run --project tools/generate");
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
        return 1;
    }

    process.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine(e.Data); };
    process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.Error.WriteLine(e.Data); };
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    process.WaitForExit();
    return process.ExitCode;
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
