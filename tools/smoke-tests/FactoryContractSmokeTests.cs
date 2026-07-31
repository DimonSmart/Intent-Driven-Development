using System.Runtime.CompilerServices;

internal static class FactoryContractSmokeTests
{
    [ModuleInitializer]
    internal static void ValidateFactoryContracts()
    {
        var root = FindRepoRoot();
        var run = Read(root, "src/canonical/skills/idd-factory-run.md");
        var decompose = Read(root, "src/canonical/skills/idd-factory-decompose-work.md");
        var execute = Read(root, "src/canonical/skills/idd-factory-execute-task.md");
        var review = Read(root, "src/canonical/skills/idd-factory-review-task.md");
        var finalReview = Read(root, "src/canonical/skills/idd-factory-review-work-result.md");
        var coordinator = Read(root, "src/canonical/factory/roles/factory-coordinator.md");
        var implementer = Read(root, "src/canonical/factory/roles/implementer.md");
        var failures = new List<string>();

        foreach (var field in new[] { "Reason:", "Verified:", "Not verified:", "Resume when:" })
        {
            Check(run, field, $"run blocker {field}", failures);
            Check(review, field, $"task-review blocker {field}", failures);
            Check(finalReview, field, $"final-review blocker {field}", failures);
        }

        foreach (var check in new (string Text, string Expected, string Name)[]
        {
            (run, "Factory outcome:", "outcome label"),
            (run, "Implementation assessment:", "implementation label"),
            (run, "Verification assessment:", "verification label"),
            (run, "Never describe the " + "blocked task or", "blocked run wording"),
            (review, "Never describe a " + "blocked task as approved, completed, accepted, or finished.", "blocked task wording"),
            (finalReview, "Do not describe a " + "blocked result as approved, review passed, completed,", "blocked final wording"),
            (run, "in verification-only resume mode", "verification-only dispatch"),
            (execute, "In explicit verification-only mode for an unchanged diff", "verification-only worker"),
            (execute, "perform only `Not verified`", "verification-only scope"),
            (coordinator, "use verification-only resume for an", "coordinator resume boundary"),
            (implementer, "perform only `Not verified`", "implementer resume boundary"),
            (decompose, "without implementation from later tasks", "forward-dependency guard"),
            (run, "`NEEDS_REPLAN` is internal, never a Factory outcome", "replanning contract"),
            (execute, "Return `NEEDS_REPLAN`", "implementer replanning result"),
            (review, "Use `needs-replan`", "review replanning verdict"),
            (run, "do not require a separate", "automatic resume after decision")
        })
        {
            Check(check.Text, check.Expected, check.Name, failures);
        }

        if (failures.Count > 0)
            throw new InvalidOperationException(
                "Factory contract smoke checks failed:\n- " + string.Join("\n- ", failures));
    }

    private static void Check(string text, string expected, string name, ICollection<string> failures)
    {
        if (!text.Contains(expected, StringComparison.Ordinal))
            failures.Add($"{name} does not contain '{expected}'.");
    }

    private static string Read(string root, string path) =>
        File.ReadAllText(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepoRoot()
    {
        for (var current = new DirectoryInfo(Environment.CurrentDirectory);
             current is not null;
             current = current.Parent)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "canonical")) &&
                Directory.Exists(Path.Combine(current.FullName, "tools", "generate")))
                return current.FullName;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
