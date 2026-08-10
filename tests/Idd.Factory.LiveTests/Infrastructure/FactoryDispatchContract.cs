using System.Text.RegularExpressions;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed record FactoryDispatchViolation(string Code, string Message);

public static class FactoryDispatchContract
{
    public const string NeutralContinueRequest =
        "Continue the current Factory run from persisted state and process exactly one next logical action.";

    private static readonly Regex ActionPattern = new(
        "(?im)^\\s*Action:[ \\t]*(?:\\r?\\n[ \\t]*)?(?<value>[^\\r\\n]+)",
        RegexOptions.Compiled);

    private static readonly Regex ResumeRequestPattern = new(
        "(?im)^\\s*Resume request:[ \\t]*(?:\\r?\\n[ \\t]*)?(?<value>[^\\r\\n]+)",
        RegexOptions.Compiled);

    private static readonly Regex ReadAndFollowPattern = new(
        "(?ims)^\\s*Read and follow:[ \\t]*\\r?\\n(?<items>(?:[ \\t]*-[ \\t]+[^\\r\\n]+(?:\\r?\\n|$))+)",
        RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, string> RoleSkills =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["task-decomposer"] = "idd-factory-decompose-task",
            ["factory-step-coordinator"] = "idd-factory-coordinate-step",
            ["implementer"] = "idd-factory-execute-subtask",
            ["checkpoint-reviewer"] = "idd-factory-review-checkpoint",
            ["final-reviewer"] = "idd-factory-review-task"
        };

    public static string? ReadAction(string? dispatch) => ReadField(ActionPattern, dispatch)?.ToUpperInvariant();

    public static IReadOnlyList<FactoryDispatchViolation> Validate(string role, string? dispatch)
    {
        if (role == "factory-root" || string.IsNullOrWhiteSpace(dispatch))
            return [];

        var violations = new List<FactoryDispatchViolation>();
        var action = ReadAction(dispatch);

        if (role == "factory-step-coordinator")
        {
            if (action == "CONTINUE")
            {
                var request = ReadField(ResumeRequestPattern, dispatch);
                if (!StringComparer.Ordinal.Equals(request, NeutralContinueRequest))
                {
                    violations.Add(new(
                        "DISPATCH_CONTINUE_REQUEST_INVALID",
                        $"CONTINUE must use the neutral persisted-state resume request exactly; actual: '{request ?? "<missing>"}'."));
                }
            }
        }
        else if (action is not null)
        {
            violations.Add(new(
                "DISPATCH_ACTION_FORBIDDEN",
                $"Role '{role}' must not receive Action, but dispatch contains Action: {action}."));
        }

        var references = ReadReferences(dispatch);
        if (references.Count > 0 && RoleSkills.TryGetValue(role, out var skill))
        {
            RequireSuffix(references, $"{skill}/SKILL.md", role, "skill", violations);
            RequireSuffix(references, $"{skill}/references/roles/{role}.md", role, "role", violations);
            RequireSuffix(references, $"{skill}/references/project-verification.md", role, "project-verification", violations);

            foreach (var reference in references.Where(IsStaticSkillReference))
            {
                if (Path.IsPathRooted(reference) && !File.Exists(reference))
                {
                    violations.Add(new(
                        "DISPATCH_REFERENCE_MISSING",
                        $"Dispatch for role '{role}' references a missing generated file: {reference}."));
                }
            }
        }

        return violations;
    }

    private static void RequireSuffix(
        IReadOnlyList<string> references,
        string expectedSuffix,
        string role,
        string kind,
        ICollection<FactoryDispatchViolation> violations)
    {
        if (references.Any(reference => Normalize(reference).EndsWith(expectedSuffix, StringComparison.Ordinal)))
            return;

        violations.Add(new(
            "DISPATCH_REFERENCE_CONTRACT",
            $"Dispatch for role '{role}' is missing the expected {kind} reference ending in '{expectedSuffix}'."));
    }

    private static string? ReadField(Regex pattern, string? dispatch)
    {
        var value = pattern.Match(dispatch ?? string.Empty).Groups["value"].Value.Trim();
        return value.Length == 0 ? null : value;
    }

    private static IReadOnlyList<string> ReadReferences(string? dispatch)
    {
        var match = ReadAndFollowPattern.Match(dispatch ?? string.Empty);
        if (!match.Success)
            return [];

        return match.Groups["items"].Value
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.TrimStart().TrimStart('-').Trim().Trim('`', '"', '\''))
            .Where(line => line.Length > 0)
            .ToArray();
    }

    private static bool IsStaticSkillReference(string reference)
    {
        var normalized = Normalize(reference);
        return normalized.Contains("/.agents/skills/", StringComparison.Ordinal) ||
               normalized.StartsWith(".agents/skills/", StringComparison.Ordinal);
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
