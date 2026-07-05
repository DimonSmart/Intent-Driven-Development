using System.Text.RegularExpressions;

internal sealed partial class SmokeTestSuite
{
    private static readonly string[] LegacySkillNames =
    [
        "spec" + "-audit",
        "spec" + "-brainstorm",
        "spec" + "-change",
        "spec" + "-import",
        "spec" + "-lint",
        "spec" + "-new-document",
        "spec" + "-normalize-current",
        "spec" + "-implement",
        "spec" + "-check-implementation",
        "spec" + "-update-from-implementation",
        "factory" + "-create-work-plan",
        "factory" + "-execute-work-plan",
        "factory" + "-review-task",
        "factory" + "-review-work-result",
        "factory" + "-finish-work"
    ];

    private static readonly string[] LegacyTextNeedles =
    [
        ".sp" + "ecs/",
        ".sp" + "ecs",
        "`spec-" + "*` skills",
        "`factory-" + "*` skills"
    ];

    private static readonly string[] LegacyPublicNameRoots =
    [
        "README.md",
        "docs",
        "src/canonical",
        "npm",
        "tools/idd-tool",
        "tools/generate",
        "tools/smoke-tests",
        "generated"
    ];

    private static readonly string[] LegacyPublicNameTextExtensions =
    [
        ".bat",
        ".cs",
        ".js",
        ".json",
        ".md",
        ".ps1",
        ".xml"
    ];

    void ExpectNoLegacyPublicNames()
    {
        foreach (var path in LegacyPublicNameFiles())
        {
            var text = File.ReadAllText(path);
            foreach (var name in LegacySkillNames)
            {
                var match = Regex.Match(
                    text,
                    $@"(?<![A-Za-z0-9_-]){Regex.Escape(name)}(?![A-Za-z0-9_-])",
                    RegexOptions.CultureInvariant);
                if (match.Success)
                {
                    failures.Add($"Legacy public skill name '{name}' found in {Relative(path)}:{LineNumber(text, match.Index)}.");
                }
            }

            foreach (var needle in LegacyTextNeedles)
            {
                var index = text.IndexOf(needle, StringComparison.Ordinal);
                if (index >= 0)
                {
                    failures.Add($"Legacy public terminology '{needle}' found in {Relative(path)}:{LineNumber(text, index)}.");
                }
            }
        }
    }

    void ExpectNoUnprefixedPublicCommandNames()
    {
        foreach (var path in LegacyPublicNameFiles())
        {
            var text = File.ReadAllText(path);
            foreach (var name in ForbiddenUnprefixedPublicNames())
            {
                foreach (var pattern in ForbiddenUnprefixedCommandPatterns(name))
                {
                    var match = Regex.Match(text, pattern, RegexOptions.CultureInvariant);
                    if (match.Success)
                    {
                        failures.Add($"Unprefixed public command name '{name}' found in {Relative(path)}:{LineNumber(text, match.Index)}.");
                    }
                }
            }
        }
    }

    IEnumerable<string> LegacyPublicNameFiles()
    {
        foreach (var root in LegacyPublicNameRoots)
        {
            var path = Path.Combine(repoRoot, root.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path))
            {
                if (IsLegacyPublicNameTextFile(path))
                {
                    yield return path;
                }
                continue;
            }

            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                .Where(IsLegacyPublicNameTextFile)
                .OrderBy(Relative, StringComparer.Ordinal))
            {
                yield return file;
            }
        }
    }

    static bool IsLegacyPublicNameTextFile(string path) =>
        LegacyPublicNameTextExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    static int LineNumber(string text, int index) =>
        text.Take(index).Count(character => character == '\n') + 1;

    static string[] ForbiddenUnprefixedPublicNames() =>
    [
        "brain" + "storm",
        "cha" + "nge",
        "au" + "dit",
        "im" + "port",
        "li" + "nt",
        "new" + "-document",
        "normalize" + "-current",
        "imple" + "ment",
        "check" + "-implementation",
        "update" + "-intent",
        "create" + "-work-plan",
        "execute" + "-work-plan",
        "review" + "-task",
        "review" + "-work-result",
        "finish" + "-work",
        "factory" + "-create-work-plan",
        "factory" + "-execute-work-plan",
        "factory" + "-review-task",
        "factory" + "-review-work-result",
        "factory" + "-finish-work"
    ];

    static string[] ForbiddenUnprefixedCommandPatterns(string name)
    {
        var escapedName = Regex.Escape(name);
        var pathPatterns = new[]
        {
            SkillPathPattern(".claude/skills/", escapedName),
            SkillPathPattern(".agents/skills/", escapedName),
            SkillPathPattern(".github/skills/", escapedName),
            SkillPathPattern("generated/claude/.claude/skills/", escapedName),
            SkillPathPattern("generated/codex/.agents/skills/", escapedName),
            SkillPathPattern("generated/copilot/.github/skills/", escapedName)
        };

        return name.Contains('-', StringComparison.Ordinal)
            ?
            [
                SlashCommandPattern(escapedName),
                "`" + escapedName + "`",
                .. pathPatterns
            ]
            :
            [
                SlashCommandPattern(escapedName),
                .. pathPatterns
            ];
    }

    static string SlashCommandPattern(string escapedName) =>
        $@"(?<![A-Za-z0-9_.-])/{escapedName}(?![A-Za-z0-9_-])";

    static string SkillPathPattern(string prefix, string escapedName) =>
        Regex.Escape(prefix) + escapedName + @"(?:[/\\]|$)";
}
