using Idd.Factory.Domain;
using Idd.Factory.Persistence;

namespace Idd.Factory.Runtime;

internal sealed class PlanMutationService(
    FactoryRuntimeContext context,
    PlanRevisionWriter planRevisions)
{
    private readonly IntentDocumentResolver intentResolver = new(context.Workspace);

    public async Task ApplyAsync(
        FactoryState state,
        IReadOnlyList<PlannerTaskDefinition> tasks,
        string reason,
        string sourceAttemptId,
        CancellationToken cancellationToken)
    {
        ValidateTaskRelatedIntent(tasks);

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

    private void ValidateTaskRelatedIntent(IReadOnlyList<PlannerTaskDefinition> tasks)
    {
        foreach (var task in tasks)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var intentId in task.TaskRelatedIntentIds)
            {
                if (!DurableIntentId.IsCanonical(intentId))
                {
                    throw new AgentProtocolException(
                        "MALFORMED_PLANNER_OUTPUT",
                        $"Planner TaskRelatedIntent entry '{intentId}' is not a canonical IDD-NNNN identifier.");
                }
                if (!seen.Add(intentId))
                {
                    throw new AgentProtocolException(
                        "MALFORMED_PLANNER_OUTPUT",
                        $"Planner TaskRelatedIntent contains duplicate durable intent ID '{intentId}'.");
                }

                try
                {
                    _ = intentResolver.ResolvePath(intentId);
                }
                catch (IntentResolutionException exception)
                {
                    throw new AgentProtocolException(
                        "MALFORMED_PLANNER_OUTPUT",
                        $"Planner TaskRelatedIntent reference '{intentId}' is invalid: {exception.Message}");
                }
            }
        }
    }

    private static PlannedWorkItem CreatePlannedTask(
        FactoryState state,
        PlannerTaskDefinition task,
        List<(string Path, string Content)> contracts)
    {
        if (string.IsNullOrWhiteSpace(task.Contract))
        {
            throw new AgentProtocolException(
                "MALFORMED_PLANNER_OUTPUT",
                "Planned task text is required.");
        }

        var id = $"W{state.NextWorkItemNumber++:000000}";
        var path = $"work-items/{id}/contract.md";
        contracts.Add((path, task.Contract.Trim() + Environment.NewLine));
        return new PlannedWorkItem
        {
            Id = id,
            ContractPath = path,
            TaskRelatedIntentIds = task.TaskRelatedIntentIds.ToList()
        };
    }
}
