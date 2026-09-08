using Idd.Factory.Domain;

namespace Idd.Factory.Runtime;

internal enum PlanningResultKind
{
    Plan,
    Question,
    BudgetExhausted
}

internal sealed record PlanningResult(
    PlanningResultKind Kind,
    string? AttemptId,
    IReadOnlyList<string> Tasks,
    string? Question,
    string Reason,
    string? Detail = null);

internal sealed class PlanningService(
    FactoryRuntimeContext context,
    SemanticExecutionService semanticExecution,
    FactoryContextReader contextReader,
    PlannerMarkdownParser parser)
{
    public async Task<PlanningResult> PlanAsync(
        FactoryState state,
        CancellationToken cancellationToken)
    {
        if (state.Current is not null || state.Remaining.Count != 0)
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                "Planning requires an exhausted batch.");
        }

        if (state.PlanningCycleCount >= context.Configuration.Limits.MaxPlanningCycles)
        {
            return new(
                PlanningResultKind.BudgetExhausted,
                null,
                [],
                null,
                "planning-budget-exhausted",
                "Factory planning-cycle budget exhausted.");
        }

        var request = await File.ReadAllTextAsync(
            Path.Combine(context.CurrentDirectory, state.RequestPath),
            cancellationToken);
        var completed = await contextReader.BuildPlanningCompletedContextAsync(
            state,
            cancellationToken);
        var userAnswers = await BuildPlanningAnswerContextAsync(cancellationToken);
        var verificationEvidence = await contextReader.BuildPlanningVerificationEvidenceContextAsync(
            state.VerificationEvidenceRefs,
            cancellationToken);
        var finalFailure = state.FinalVerificationPlanRevision == state.PlanRevision
                           && !state.FinalVerificationPassed;
        var trigger = state.PlanningCycleCount == 0
            ? "Initial planning."
            : finalFailure
                ? "Strict final verification failed. Use the authoritative evidence below to determine the next correction batch."
                : "The previous batch is exhausted. Reassess integrated product reality and determine the next batch.";
        var input =
            $"Original request:\n{request}\n\nCurrent planning trigger:\n{trigger}\n\nCompleted immutable work:\n{completed}\n\n" +
            $"User answers to earlier planning questions:\n{userAnswers}\n\n" +
            $"Authoritative verification evidence summaries:\n{verificationEvidence}\n\n" +
            "Read current durable intent from .idd/intent and inspect the current repository directly. " +
            "Materialize every task whose self-contained contract can be determined reliably now, in execution order. " +
            "Stop at the first material uncertainty that requires evidence from this batch. " +
            "Return one or more '# Task' sections, or exactly one '# Question' section when a user decision is required, or exactly '# Done' when no semantic work remains. " +
            "Do not mix these forms.";

        var result = await semanticExecution.InvokeAsync(
            state,
            "planning",
            null,
            input,
            SemanticOperationKind.Planning,
            cancellationToken);
        var plan = parser.Parse(result.SemanticResult);
        if (plan.Question is not null)
        {
            return new(
                PlanningResultKind.Question,
                result.AttemptId,
                [],
                plan.Question,
                "user-question");
        }

        var reason = state.PlanningCycleCount == 0
            ? "initial-planning"
            : finalFailure
                ? "final-verification-replanning"
                : "batch-exhausted-replanning";
        return new(
            PlanningResultKind.Plan,
            result.AttemptId,
            plan.Tasks,
            null,
            reason);
    }

    public async Task PersistAnswerAsync(
        string question,
        string answer,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(context.CurrentDirectory, "planning-answers");
        Directory.CreateDirectory(directory);
        var sequence = Directory.GetFiles(directory, "*.md").Length + 1;
        var path = Path.Combine(directory, $"Q{sequence:000000}.md");
        var content = $"# Question\n\n{question.Trim()}\n\n# Answer\n\n{answer.Trim()}\n";
        await FactoryRuntimeContext.WriteRuntimeArtifactAtomicallyAsync(
            path,
            content,
            cancellationToken);
    }

    private async Task<string> BuildPlanningAnswerContextAsync(
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(context.CurrentDirectory, "planning-answers");
        if (!Directory.Exists(directory))
            return "none";

        var files = Directory.GetFiles(directory, "*.md")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0)
            return "none";

        var answers = new List<string>(files.Length);
        foreach (var file in files)
            answers.Add((await File.ReadAllTextAsync(file, cancellationToken)).Trim());
        return string.Join("\n\n", answers);
    }
}
