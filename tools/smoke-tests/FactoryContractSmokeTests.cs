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
            (run, "do not require a separate", "automatic resume after decision"),
            (run, "an optional `run-context.md`", "optional run context"),
            (run, "Each task is a self-contained implementation contract", "self-contained task contract"),
            (run, "Workers must not need `request.md`", "worker request boundary"),
            (decompose, "Do not copy the complete request", "decomposition copy guard"),
            (decompose, "all ordered self-contained task Markdown", "self-contained decomposition result"),
            (execute, "Do not read `request.md` or other task files", "execute request boundary"),
            (review, "Do not read `request.md` or other task files", "review request boundary"),
            (finalReview, "Read the original request, optional run context", "final review request ownership"),
            (coordinator, "Ensure implementation and task-review workers do not need `request.md`", "coordinator context boundary"),
            (workDecomposer, "Do not make workers read `request.md`", "decomposer context boundary"),
            (implementer, "Do not read `request.md` or other task files", "implementer context boundary"),
            (taskReviewer, "Do not read `request.md` or other task files", "task reviewer context boundary"),
            (finalReviewer, "Verify the original request, optional shared run context", "final reviewer request ownership"),
            (run, "Run `idd-factory-decompose-work` with the complete request before creating", "intent preflight before state"),
            (run, "create no Factory state and no task for the intent change", "no intent task on preflight"),
            (run, "Factory tasks are implementation-only", "implementation-only task invariant"),
            (run, "never turn the intent change", "mid-run intent orchestration boundary"),
            (decompose, "Perform intent preflight before returning implementation tasks", "decomposer intent preflight"),
            (decompose, "Never represent intent work as a Factory task", "decomposer no intent task"),
            (decompose, "return no work slug, run context, or", "intent-required no partial plan"),
            (execute, "If it asks to edit `.idd/intent/`", "executor rejects intent task"),
            (execute, "return `NEEDS_REPLAN` and identify removal of that scope", "executor intent task verdict"),
            (review, "If its contract owns an edit to", "review rejects intent task"),
            (review, "return `needs-replan`; do not review intent work", "review intent task verdict"),
            (coordinator, "Run intent preflight before creating Factory state", "coordinator intent preflight"),
            (coordinator, "Handle mid-run `INTENT_REQUIRED` as coordinator-owned intent orchestration", "coordinator mid-run intent ownership"),
            (workDecomposer, "Never represent intent work as a Factory task", "role no intent task"),
            (implementer, "If the task asks to edit `.idd/intent/`", "implementer rejects intent task"),
            (taskReviewer, "If the task owns an edit to `.idd/intent/`", "task reviewer rejects intent task"),
            (finalReview, "absence of intent-changing work recorded as a completed Factory task", "final review intent-task guard"),
            (finalReview, "Return no corrective task until the coordinator", "final review intent handoff boundary"),
            (finalReviewer, "intent-changing work incorrectly recorded as a completed Factory task", "final reviewer intent-task guard"),
            (finalReviewer, "do not define a corrective task until the coordinator resolves intent outside the task list", "final reviewer intent handoff boundary")
        })
        {
            Check(check.Text, check.Expected, check.Name, failures);
        }

        CheckAbsent(
            execute,
            "Read the active task (including resumed `Blocker`), `request.md`",
            "legacy execute request input",
            failures);
        CheckAbsent(
            review,
            "Read `request.md`, the active task",
            "legacy review request input",
            failures);
        CheckAbsent(
            run,
            "INTENT_REQUIRED`: persist the intent blocker and use its workflow",
            "legacy intent task-loop wording",
            failures);
        CheckAbsent(
            decompose,
            "task that updates intent",
            "ambiguous intent task allowance",
            failures);

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
