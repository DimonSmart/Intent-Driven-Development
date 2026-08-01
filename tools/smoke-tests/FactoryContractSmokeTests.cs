using System.Runtime.CompilerServices;

internal static class FactoryContractSmokeTests
{
    [ModuleInitializer]
    internal static void ValidateFactoryContracts()
    {
        var root = FindRepoRoot();
        var run = Read(root, "src/canonical/skills/idd-factory-run.md");
        var decompose = Read(root, "src/canonical/skills/idd-factory-decompose-task.md");
        var execute = Read(root, "src/canonical/skills/idd-factory-execute-subtask.md");
        var review = Read(root, "src/canonical/skills/idd-factory-review-checkpoint.md");
        var finalReview = Read(root, "src/canonical/skills/idd-factory-review-task.md");
        var coordinator = Read(root, "src/canonical/factory/roles/factory-coordinator.md");
        var taskDecomposer = Read(root, "src/canonical/factory/roles/task-decomposer.md");
        var implementer = Read(root, "src/canonical/factory/roles/implementer.md");
        var checkpointReviewer = Read(root, "src/canonical/factory/roles/checkpoint-reviewer.md");
        var finalReviewer = Read(root, "src/canonical/factory/roles/final-reviewer.md");
        var manifest = Read(root, "src/canonical/plugins/plugin-manifest.json");
        var failures = new List<string>();

        foreach (var field in new[] { "Reason:", "Verified:", "Not verified:", "Resume when:" })
        {
            Check(run, field, $"run blocker {field}", failures);
            Check(execute, field, $"execution blocker {field}", failures);
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
            (run, "Each Subtask is self-contained", "self-contained execution contract"),
            (run, "Subtask completion does", "no automatic checkpoint review"),
            (run, "A Review checkpoint contains", "checkpoint contract"),
            (run, "active review-checkpoint -> ready", "checkpoint correction transition"),
            (run, "immediately before the checkpoint", "checkpoint correction insertion"),
            (run, "Do not add a terminal checkpoint", "terminal checkpoint guard"),
            (run, "`Changes` is a compact list", "checkpoint evidence focus"),
            (decompose, "Separate execution boundaries from review boundaries", "separate review boundaries"),
            (decompose, "Use the fewest Review checkpoints", "minimal checkpoints"),
            (decompose, "do not add a terminal checkpoint", "decomposer terminal checkpoint guard"),
            (decompose, "all ordered Subtask and Review checkpoint", "decomposition item result"),
            (execute, "active Subtask", "execution-only worker"),
            (execute, "Do not read `request.md`, checkpoints, or other", "execute context boundary"),
            (execute, "Changes:", "execution changes output"),
            (execute, "run exactly the check IDs assigned", "exact subtask verification set"),
            (execute, "return `BLOCKED`, never `DONE`", "incomplete subtask verification blocks"),
            (review, "active Review checkpoint", "checkpoint-only reviewer"),
            (review, "every completed Subtask named by its `Covers`", "checkpoint coverage input"),
            (review, "Do not read `request.md`, unrelated Subtasks", "checkpoint context boundary"),
            (review, "Corrective Subtask:", "checkpoint corrective output"),
            (finalReview, "all completed Review checkpoints", "final checkpoint ownership"),
            (finalReview, "do not add a terminal", "final correction review gate"),
            (finalReview, "run every assigned automatic check", "final automatic check ownership"),
            (finalReview, "Read-only review forbids code", "final read-only verification boundary"),
            (finalReview, "requires `blocked`, never", "unverified final check blocks approval"),
            (coordinator, "Subtasks and Review checkpoints", "coordinator item kinds"),
            (coordinator, "Accept Subtask `DONE` only", "coordinator verification completion guard"),
            (taskDecomposer, "Separate execution boundaries from review boundaries", "role separates boundaries"),
            (implementer, "Never add checks selected only for checkpoint or final", "implementer exact verification boundary"),
            (implementer, "`BLOCKED`, never `DONE`", "implementer incomplete verification guard"),
            (checkpointReviewer, "active Review checkpoint", "reviewer checkpoint responsibility"),
            (finalReviewer, "run every assigned automatic final check", "final role verification ownership"),
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
            (run, "`DONE`: run fresh `idd-factory-review-checkpoint`", "legacy per-task review dispatch"),
            (review, "Independently review one explicit `.active.md` task", "legacy single-task reviewer"),
            (review, "Do not read `request.md` or other task files; review the active task", "legacy task-only review context"),
            (execute, "run final review", "executor final-review ownership"),
            (execute, "unless the task contract explicitly requires it", "subtask broad-verification exception"),
            (implementer, "unless the task contract requires it", "implementer broad-verification exception")
        })
        {
            CheckAbsent(absent.Text, absent.Unexpected, absent.Name, failures);
        }

        Check(run, "The original user request defines the Factory Task", "request defines task", failures);
        Check(run, "a Subtask, identified by a `## Goal` section", "subtask persisted contract", failures);
        Check(run, "a Review checkpoint, identified by a `## Review Checkpoint` section", "checkpoint persisted contract", failures);
        Check(execute, "is a Subtask, not", "subtask executor rejects checkpoint", failures);
        Check(finalReview, "all work items are `.completed.md`", "final review completion precondition", failures);
        foreach (var skill in new[]
        {
            "idd-factory-run",
            "idd-factory-decompose-task",
            "idd-factory-execute-subtask",
            "idd-factory-review-checkpoint",
            "idd-factory-review-task",
            "idd-factory-finalize-run"
        })
        {
            Check(manifest, $"\"{skill}\"", $"manifest skill {skill}", failures);
        }

        foreach (var role in new[] { "task-decomposer", "checkpoint-reviewer" })
        {
            Check(manifest, $"\"{role}\"", $"manifest role {role}", failures);
        }

        foreach (var text in new[] { run, decompose, execute, review, finalReview, coordinator, taskDecomposer, implementer, checkpointReviewer, finalReviewer })
        {
            CheckAbsent(text, "execution task", "obsolete execution-task terminology", failures);
        }

        foreach (var obsoleteSkill in new[]
        {
            "idd-factory-decompose-work",
            "idd-factory-execute-task",
            "idd-factory-review-work-result",
            "idd-factory-finish-work"
        })
        {
            CheckAbsent(manifest, obsoleteSkill, $"obsolete manifest skill {obsoleteSkill}", failures);
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
