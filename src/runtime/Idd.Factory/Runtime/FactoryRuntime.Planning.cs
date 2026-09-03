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
        var completed = await BuildCompletedContextAsync(state, cancellationToken);
        var finalFailure = state.FinalVerificationPlanRevision == state.PlanRevision && !state.FinalVerificationPassed;
        var trigger = state.PlanningCycleCount == 0
            ? "Initial planning."
            : finalFailure
                ? "Strict final verification failed. Use the authoritative evidence below to determine the next correction batch."
                : "The previous batch is exhausted. Reassess integrated product reality and determine the next batch.";
        var input =
            $"Original request:\n{request}\n\nCurrent planning trigger:\n{trigger}\n\nCompleted immutable work:\n{completed}\n\n" +
            $"Authoritative verification evidence references:\n{string.Join("\n", state.VerificationEvidenceRefs.Select(x => "- " + x))}\n\n" +
            "Read current durable intent from .idd/intent and inspect the current repository directly. " +
            "Materialize every task whose self-contained contract can be determined reliably now, in execution order. " +
            "Stop at the first material uncertainty that requires evidence from this batch. If no semantic work remains, return an empty response.";

        var result = await InvokeSemanticAsync(
            state,
            "planning",
            null,
            input,
            SemanticOperationKind.Planning,
            cancellationToken);
        var tasks = PlannerBatchParser.Parse(result.SemanticResult);
        var reason = state.PlanningCycleCount == 0
            ? "initial-planning"
            : finalFailure ? "final-verification-replanning" : "batch-exhausted-replanning";
        await CreatePlanMutationService().ApplyPlanningResultAsync(
            state,
            tasks,
            reason,
            result.AttemptId,
            cancellationToken);
        return null;
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

internal static class PlannerBatchParser
{
    private const string TaskHeading = "# Task";

    public static IReadOnlyList<string> Parse(string markdown)
    {
        var normalized = markdown.TrimStart('\uFEFF').Replace("\r\n", "\n");
        if (string.IsNullOrWhiteSpace(normalized)) return [];

        var lines = normalized.Split('\n');
        var tasks = new List<string>();
        List<string>? current = null;
        foreach (var line in lines)
        {
            if (line == TaskHeading)
            {
                AddCurrent(tasks, current);
                current = [];
                continue;
            }
            if (current is null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    throw new AgentProtocolException("MALFORMED_PLANNER_OUTPUT", "Planner output must be empty or consist only of '# Task' sections.");
                continue;
            }
            current.Add(line);
        }
        AddCurrent(tasks, current);
        if (tasks.Count == 0)
            throw new AgentProtocolException("MALFORMED_PLANNER_OUTPUT", "Planner output contains no readable task sections.");
        return tasks;
    }

    private static void AddCurrent(List<string> tasks, List<string>? current)
    {
        if (current is null) return;
        var task = string.Join("\n", current).Trim();
        if (task.Length == 0)
            throw new AgentProtocolException("MALFORMED_PLANNER_OUTPUT", "Planner task sections must be non-empty.");
        tasks.Add(task);
    }
}
