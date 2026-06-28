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

}
