using Idd.Factory.Domain;
using Idd.Factory.Persistence;

namespace Idd.Factory.Runtime;

internal sealed class PlanMutationService(
    FactoryRuntimeContext context,
    PlanRevisionWriter planRevisions)
{
    public async Task ApplyAsync(
        FactoryState state,
        IReadOnlyList<string> tasks,
        string reason,
        string sourceAttemptId,
        CancellationToken cancellationToken)
    {
        var previous = context.CloneState(state);
        var candidate = context.CloneState(state);
        candidate.Current = null;
        candidate.CurrentPhase = null;
        candidate.Remaining.Clear();

        var contracts = new List<(string Path, string Content)>();
        foreach (var task in tasks)
            candidate.Remaining.Add(CreatePlannedTask(candidate, task, contracts));

        candidate.PlanningCycleCount++;
        candidate.PlannedThroughCompletedCount = candidate.Completed.Count;
        candidate.PendingContinuation = null;
        candidate.Blocker = null;
        candidate.RunStatus = FactoryRunStatus.Running;
        candidate.PlanRevision++;
        FactoryRuntimeContext.InvalidateFinalEvidence(candidate);

        context.ValidateRuntimeState(candidate);
        foreach (var contract in contracts)
        {
            await FactoryRuntimeContext.WriteRuntimeArtifactAtomicallyAsync(
                Path.Combine(context.CurrentDirectory, contract.Path),
                contract.Content,
                cancellationToken);
        }

        await planRevisions.WriteAsync(
            previous,
            candidate,
            reason,
            sourceAttemptId,
            null,
            cancellationToken);
        FactoryRuntimeContext.ApplyCandidate(state, candidate);
        await context.SaveAsync(state, cancellationToken);
    }

    private static PlannedWorkItem CreatePlannedTask(
        FactoryState state,
        string task,
        List<(string Path, string Content)> contracts)
    {
        if (string.IsNullOrWhiteSpace(task))
        {
            throw new AgentProtocolException(
                "MALFORMED_PLANNER_OUTPUT",
                "Planned task text is required.");
        }

        var id = $"W{state.NextWorkItemNumber++:000000}";
        var path = $"work-items/{id}/contract.md";
        contracts.Add((path, task.Trim() + Environment.NewLine));
        return new PlannedWorkItem { Id = id, ContractPath = path };
    }
}
