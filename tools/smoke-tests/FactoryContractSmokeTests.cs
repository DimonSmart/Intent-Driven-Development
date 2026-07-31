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
        var workDecomposer = Read(root, "src/canonical/factory/roles/work-decomposer.md");
        var implementer = Read(root, "src/canonical/factory/roles/implementer.md");
        var taskReviewer = Read(root, "src/canonical/factory/roles/task-reviewer.md");
        var finalReviewer = Read(root, "src/canonical/factory/roles/final-reviewer.md");
        var failures = new List<string>();

        foreach (var field in new[] { "Reason:", "Verified:", "Not verified:", "Resume when:" })
        {
            Check(run, field, $"run blocker {field}", failures);
            Check(review, field, $"checkpoint-review blocker {field}", failures);
            Check(finalReview, field, $"final-review blocker {field}", failures);
        }

        foreach (var check in new (string Text, string Expected, string Name)[]
        {
            (run, "Factory outcome:", "outcome label"),
            (run, "Implementation assessment:", "implementation label"),
            (run, "Verification assessment:", "verification label"),
            (run, "verification-only resume", "verification-only dispatch"),
            (execute, "In explicit verification-only mode for an unchanged diff", "verification-only worker"),
            (execute, "perform only `Not verified`", "verification-only scope"),
            (coordinator, "use verification-only resume", "coordinator resume boundary"),
            (implementer, "perform only", "implementer resume boundary"),
            (decompose, "without implementation from later tasks", "forward-dependency guard"),
            (run, "`NEEDS_REPLAN` is internal, never a Factory outcome", "replanning contract"),
            (execute, "Return `NEEDS_REPLAN`", "implementer replanning result"),
            (review, "Use `needs-replan`", "checkpoint replanning verdict"),
            (run, "do not require a separate", "automatic resume after decision"),
            (run, "an optional `run-context.md`", "optional run context"),
            (run, "Each execution task is self-contained", "self-contained execution contract"),
            (run, "Execution completion does not invoke", "no automatic task review"),
            (run, "A review checkpoint contains", "checkpoint contract"),
            (run, "active review-checkpoint -> ready", "checkpoint correction transition"),
            (run, "immediately before the checkpoint", "checkpoint correction insertion"),
            (run, "Do not add a terminal checkpoint", "terminal checkpoint guard"),
            (run, "`Changes` is a compact list", "checkpoint evidence focus"),
            (decompose, "Separate execution boundaries from review boundaries", "separate review boundaries"),
            (decompose, "Use the fewest review checkpoints", "minimal checkpoints"),
            (decompose, "do not add a terminal checkpoint", "decomposer terminal checkpoint guard"),
            (decompose, "all ordered execution-task and review-checkpoint", "decomposition item result"),
            (execute, "active execution task", "execution-only worker"),
            (execute, "Do not read `request.md`, checkpoints, or other", "execute context boundary"),
            (execute, "Changes:", "execution changes output"),
            (execute, "Do not run broad checkpoint or final integrated verification", "verification layering"),
            (review, "active review checkpoint", "checkpoint-only reviewer"),
            (review, "does not review every execution task", "legacy name boundary"),
            (review, "every completed execution task named by its `Covers`", "checkpoint coverage input"),
            (review, "Do not read `request.md`, unrelated execution tasks", "checkpoint context boundary"),
            (review, "Corrective execution task:", "checkpoint corrective output"),
            (finalReview, "all completed review checkpoints", "final checkpoint ownership"),
            (finalReview, "do not add a terminal", "final correction review gate"),
            (coordinator, "execution tasks and review checkpoints", "coordinator item kinds"),
            (coordinator, "Mark a successful execution task completed without invoking independent review", "coordinator no per-task review"),
            (workDecomposer, "Separate execution boundaries from review boundaries", "role separates boundaries"),
            (implementer, "review checkpoints", "implementer checkpoint boundary"),
            (taskReviewer, "reviews checkpoints, not every execution task", "reviewer checkpoint responsibility"),
            (finalReviewer, "review checkpoint is completed", "final reviewer checkpoint scope"),
            (decompose, "`subtask` checks", "subtask verification context"),
            (decompose, "`checkpoint` checks", "checkpoint verification context"),
            (execute, "context `subtask`", "executor verification context"),
            (review, "context `checkpoint`", "checkpoint reviewer verification context"),
            (finalReview, "context `final`", "final verification context"),
            (implementer, "NEEDS_REPLAN", "executor verification scope boundary")
        })
        {
            Check(check.Text, check.Expected, check.Name, failures);
        }

        foreach (var absent in new (string Text, string Unexpected, string Name)[]
        {
            (run, "`DONE`: run fresh `idd-factory-review-task`", "legacy per-task review dispatch"),
            (review, "Independently review one explicit `.active.md` task", "legacy single-task reviewer"),
            (review, "Do not read `request.md` or other task files; review the active task", "legacy task-only review context"),
            (execute, "run final review", "executor final-review ownership")
        })
        {
            CheckAbsent(absent.Text, absent.Unexpected, absent.Name, failures);
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

    private static void CheckAbsent(
        string text,
        string unexpected,
        string name,
        ICollection<string> failures)
    {
        if (text.Contains(unexpected, StringComparison.Ordinal))
            failures.Add($"{name} still contains '{unexpected}'.");
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
