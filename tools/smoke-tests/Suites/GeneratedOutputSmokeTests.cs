using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

internal sealed partial class SmokeTestSuite
{
    void ExpectFile(string relativePath)
    {
        if (!File.Exists(Path.Combine(repoRoot, relativePath)))
        {
            failures.Add($"Missing file: {relativePath}");
        }
    }

    void ExpectNoFile(string relativePath)
    {
        if (File.Exists(Path.Combine(repoRoot, relativePath)))
        {
            failures.Add($"Unexpected file: {relativePath}");
        }
    }

    void ExpectNoDirectory(string relativePath)
    {
        if (Directory.Exists(Path.Combine(repoRoot, relativePath)))
        {
            failures.Add($"Unexpected directory: {relativePath}");
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

    void ExpectPrefixedClaudeSkillOutput()
    {
        foreach (var skillName in ExpectedPrefixedClaudeSkills())
        {
            ExpectFile($"generated/claude/.claude/skills/{skillName}/SKILL.md");
        }

        foreach (var skillName in ForbiddenClaudeSkillDirectories())
        {
            ExpectNoFile($"generated/claude/.claude/skills/{skillName}/SKILL.md");
            ExpectNoDirectory($"generated/claude/.claude/skills/{skillName}");
        }
    }

    static string[] ExpectedPrefixedClaudeSkills() =>
    [
        "idd-intent-brainstorm",
        "idd-intent-change",
        "idd-code-implement",
        "idd-factory-create-work-plan"
    ];

    static string[] ForbiddenClaudeSkillDirectories() =>
    [
        "brain" + "storm",
        "cha" + "nge",
        "imple" + "ment",
        "factory" + "-create-work-plan"
    ];

}
