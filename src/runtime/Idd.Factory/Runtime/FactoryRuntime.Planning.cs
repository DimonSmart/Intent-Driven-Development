using System.Text.Json;
using Idd.Factory.Domain;

namespace Idd.Factory.Runtime;

public sealed partial class FactoryRuntime
{
    private PlanMutationService CreatePlanMutationService() => new(this);

    private async Task<FactoryCliOutcome?> PlanAsync(FactoryState state, CancellationToken cancellationToken)
    {
        if (state.Current is not null || state.Remaining.Count != 0)
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Planning requires an exhausted batch.");
        if (state.PlanningCycleCount >= configuration.Limits.MaxPlanningCycles)
            throw new AgentProtocolException("PLANNING_BUDGET_EXHAUSTED", "Factory planning-cycle budget exhausted.");

        var request = await File.ReadAllTextAsync(Path.Combine(currentDirectory, state.RequestPath), cancellationToken);
        var completed = await BuildPlanningCompletedContextAsync(state, cancellationToken);
        var userAnswers = await BuildPlanningAnswerContextAsync(cancellationToken);
        var verificationEvidence = BuildPlanningVerificationEvidenceContext(state.VerificationEvidenceRefs);
        var finalFailure = state.FinalVerificationPlanRevision == state.PlanRevision && !state.FinalVerificationPassed;
        var trigger = state.PlanningCycleCount == 0
            ? "Initial planning."
            : finalFailure
                ? "Strict final verification failed. Use the authoritative evidence below to determine the next correction batch."
                : "The previous batch is exhausted. Reassess integrated product reality and determine the next batch.";
        var input =
            $"Original request:\n{request}\n\nCurrent planning trigger:\n{trigger}\n\nCompleted immutable work:\n{completed}\n\n" +
            $"User answers to earlier planning questions:\n{userAnswers}\n\n" +
            $"Authoritative verification evidence references:\n{verificationEvidence}\n\n" +
            "Read current durable intent from .idd/intent and inspect the current repository directly. " +
            "Materialize every task whose self-contained contract can be determined reliably now, in execution order. " +
            "Stop at the first material uncertainty that requires evidence from this batch. " +
            "Return one or more '# Task' sections, or exactly one '# Question' section when a user decision is required, or exactly '# Done' when no semantic work remains. " +
            "Do not mix these forms.";

        var result = await InvokeSemanticAsync(
            state,
            "planning",
            null,
            input,
            SemanticOperationKind.Planning,
            cancellationToken);
        var plan = plannerMarkdownParser.Parse(result.SemanticResult);
        if (plan.Question is not null)
        {
            state.PlanningCycleCount++;
            state.PlannedThroughCompletedCount = state.Completed.Count;
            state.FinalVerificationPassed = false;
            state.FinalVerificationPlanRevision = null;
            var payload = JsonSerializer.SerializeToElement(new { question = plan.Question }, FactoryJson.Options);
            return await StopAsync(
                state,
                "USER_DECISION_REQUIRED",
                plan.Question,
                "Answer the planner question to continue this run, or cancel the Factory run.",
                cancellationToken,
                new(ContinuationKind.UserQuestion, null, null, "USER_DECISION_REQUIRED", true),
                payload);
        }

        var reason = state.PlanningCycleCount == 0
            ? "initial-planning"
            : finalFailure ? "final-verification-replanning" : "batch-exhausted-replanning";
        await CreatePlanMutationService().ApplyPlanningResultAsync(
            state,
            plan.Tasks,
            reason,
            result.AttemptId,
            cancellationToken);
        return null;
    }

    private async Task<string> BuildPlanningCompletedContextAsync(FactoryState state, CancellationToken cancellationToken)
    {
        if (state.Completed.Count == 0) return "none";

        var planningState = CloneState(state);
        foreach (var completed in planningState.Completed)
        {
            if (completed.VerificationEvidenceRefs.Count == 0) continue;
            var visiblePaths = completed.VerificationEvidenceRefs.Select(GetWorkspaceVisibleEvidencePath).ToArray();
            completed.VerificationEvidenceRefs.Clear();
            completed.VerificationEvidenceRefs.AddRange(visiblePaths);
        }
        return await BuildCompletedContextAsync(planningState, cancellationToken);
    }

    private string BuildPlanningVerificationEvidenceContext(IEnumerable<string> references) =>
        string.Join("\n", references.Select(reference => "- " + GetWorkspaceVisibleEvidencePath(reference)));

    private string GetWorkspaceVisibleEvidencePath(string reference)
    {
        var runDirectory = Path.GetFullPath(currentDirectory);
        if (string.IsNullOrWhiteSpace(reference) || Path.IsPathRooted(reference))
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", $"Invalid authoritative verification evidence reference '{reference}'.");

        string resolvedPath;
        try
        {
            resolvedPath = Path.GetFullPath(Path.Combine(runDirectory, reference));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", $"Invalid authoritative verification evidence reference '{reference}': {exception.Message}");
        }

        var runRelative = Path.GetRelativePath(runDirectory, resolvedPath);
        if (Path.IsPathRooted(runRelative)
            || runRelative.Equals("..", StringComparison.Ordinal)
            || runRelative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || runRelative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                $"Authoritative verification evidence reference '{reference}' resolves outside Factory run directory '{runDirectory}'.");
        }

        if (!File.Exists(resolvedPath))
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                $"Authoritative verification evidence reference '{reference}' is missing at '{resolvedPath}'.");
        }

        return RelativePath(resolvedPath);
    }

    private async Task<string> BuildPlanningAnswerContextAsync(CancellationToken cancellationToken)
    {
        var directory = Path.Combine(currentDirectory, "planning-answers");
        if (!Directory.Exists(directory)) return "none";
        var files = Directory.GetFiles(directory, "*.md")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0) return "none";
        var answers = new List<string>(files.Length);
        foreach (var file in files)
            answers.Add((await File.ReadAllTextAsync(file, cancellationToken)).Trim());
        return string.Join("\n\n", answers);
    }

    private async Task PersistPlanningAnswerAsync(string question, string answer, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(currentDirectory, "planning-answers");
        Directory.CreateDirectory(directory);
        var sequence = Directory.GetFiles(directory, "*.md").Length + 1;
        var path = Path.Combine(directory, $"Q{sequence:000000}.md");
        var content = $"# Question\n\n{question.Trim()}\n\n# Answer\n\n{answer.Trim()}\n";
        await WriteRuntimeArtifactAtomicallyAsync(path, content, cancellationToken);
    }

    private string ReadContract(string path) =>
        File.Exists(Path.Combine(currentDirectory, path))
            ? File.ReadAllText(Path.Combine(currentDirectory, path))
            : "[missing contract]";

    private static async Task WriteRuntimeArtifactAtomicallyAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, content, cancellationToken);
        File.Move(temporary, path, true);
    }

    private static void InvalidateFinalEvidence(FactoryState state)
    {
        state.FinalVerificationPassed = false;
        state.FinalVerificationPlanRevision = null;
    }
}
