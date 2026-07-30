using System.Runtime.CompilerServices;

internal static class FactoryContractSmokeTests
{
    [ModuleInitializer]
    internal static void ValidateFactoryContracts()
    {
        var repoRoot = FindRepoRoot();
        var failures = new List<string>();

        var run = Read(repoRoot, "src/canonical/skills/idd-factory-run.md");
        var execute = Read(repoRoot, "src/canonical/skills/idd-factory-execute-task.md");
        var taskReview = Read(repoRoot, "src/canonical/skills/idd-factory-review-task.md");
        var finalReview = Read(repoRoot, "src/canonical/skills/idd-factory-review-work-result.md");
        var coordinatorRole = Read(repoRoot, "src/canonical/factory/roles/factory-coordinator.md");
        var implementerRole = Read(repoRoot, "src/canonical/factory/roles/implementer.md");

        foreach (var field in new[] { "Reason:", "Verified:", "Not verified:", "Resume when:" })
        {
            ExpectContains(run, field, $"Factory run blocker field {field}", failures);
            ExpectContains(taskReview, field, $"Task reviewer blocker field {field}", failures);
            ExpectContains(finalReview, field, $"Final reviewer blocker field {field}", failures);
        }

        foreach (var label in new[]
        {
            "Factory outcome:",
            "Implementation assessment:",
            "Verification assessment:"
        })
        {
            ExpectContains(run, label, $"Factory outcome report label {label}", failures);
        }

        ExpectContains(
            run,
            "describe the blocked task or run as approved, review passed, completed,",
            "blocked Factory wording guard",
            failures);
        ExpectContains(
            taskReview,
            "Do not describe a blocked task as approved, review passed, completed, accepted,",
            "blocked task-review wording guard",
            failures);
        ExpectContains(
            finalReview,
            "Do not describe a blocked result as approved, review passed, completed,",
            "blocked final-review wording guard",
            failures);

        ExpectContains(
            run,
            "invoke `idd-factory-execute-task` in verification-only resume mode",
            "coordinator verification-only resume dispatch",
            failures);
        ExpectContains(
            execute,
            "explicitly requests verification-only resume",
            "implementer verification-only resume contract",
            failures);
        ExpectContains(
            execute,
            "Perform only the work listed under",
            "implementer missing-verification scope",
            failures);
        ExpectContains(
            coordinatorRole,
            "verification-only resume limited to that missing evidence",
            "coordinator role resume boundary",
            failures);
        ExpectContains(
            implementerRole,
            "perform only the task's",
            "implementer role resume boundary",
            failures);

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "Factory contract smoke checks failed:\n- " + string.Join("\n- ", failures));
        }
    }

    private static string Read(string repoRoot, string relativePath)
    {
        var path = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }

    private static void ExpectContains(
        string text,
        string expected,
        string context,
        ICollection<string> failures)
    {
        if (!text.Contains(expected, StringComparison.Ordinal))
        {
            failures.Add($"{context} does not contain '{expected}'.");
        }
    }

    private static string FindRepoRoot()
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

        throw new InvalidOperationException("Could not locate repository root for Factory contract checks.");
    }
}
