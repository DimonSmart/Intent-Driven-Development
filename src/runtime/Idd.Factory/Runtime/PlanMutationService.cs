using Idd.Factory.Domain;
using Idd.Factory.Persistence;

namespace Idd.Factory.Runtime;

internal sealed record PreparedPlan(
    IReadOnlyList<PlannedWorkItem> WorkItems,
    long NextWorkItemNumber);

internal sealed class PlanMutationService(
    FactoryRuntimeContext context,
    PlanRevisionWriter planRevisions)
{
    private readonly IntentDocumentResolver intentResolver = new(context.Workspace);

    public async Task<PreparedPlan> PrepareAsync(
        FactoryState state,
        IReadOnlyList<PlannerTaskDefinition> tasks,
        CancellationToken cancellationToken)
    {
        ValidateTaskRelatedIntent(tasks);
        ValidateRelevantCompletedWork(state, tasks);

        var nextWorkItemNumber = state.NextWorkItemNumber;
        var workItems = new List<PlannedWorkItem>(tasks.Count);
        foreach (var task in tasks)
        {
            if (string.IsNullOrWhiteSpace(task.Contract))
            {
                throw new AgentProtocolException(
                    "MALFORMED_PLANNER_OUTPUT",
                    "Planned task text is required.");
            }

            var id = $"W{nextWorkItemNumber++:000000}";
            var path = $"work-items/{id}/contract.md";
            await FactoryRuntimeContext.WriteRuntimeArtifactAtomicallyAsync(
                Path.Combine(context.CurrentDirectory, path),
                task.Contract.Trim() + Environment.NewLine,
                cancellationToken);
            workItems.Add(new PlannedWorkItem
            {
                Id = id,
                ContractPath = path,
                TaskRelatedIntentIds = task.TaskRelatedIntentIds.ToList(),
                RelevantCompletedWorkIds = task.RelevantCompletedWorkIds.ToList()
            });
        }

        return new(workItems, nextWorkItemNumber);
    }

    public async Task WriteRevisionAsync(
        FactoryState previous,
        FactoryState next,
        string reason,
        string sourceAttemptId,
        CancellationToken cancellationToken) =>
        _ = await planRevisions.WriteAsync(
            previous,
            next,
            reason,
            sourceAttemptId,
            null,
            cancellationToken);

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

    private static void ValidateRelevantCompletedWork(
        FactoryState state,
        IReadOnlyList<PlannerTaskDefinition> tasks)
    {
        var completedIds = state.Completed
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var task in tasks)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var workItemId in task.RelevantCompletedWorkIds)
            {
                if (!WorkItemId.IsCanonical(workItemId))
                {
                    throw new AgentProtocolException(
                        "MALFORMED_PLANNER_OUTPUT",
                        $"Planner RelevantCompletedWork entry '{workItemId}' is not a canonical WNNNNNN identifier.");
                }
                if (!seen.Add(workItemId))
                {
                    throw new AgentProtocolException(
                        "MALFORMED_PLANNER_OUTPUT",
                        $"Planner RelevantCompletedWork contains duplicate work-item ID '{workItemId}'.");
                }
                if (!completedIds.Contains(workItemId))
                {
                    throw new AgentProtocolException(
                        "MALFORMED_PLANNER_OUTPUT",
                        $"Planner RelevantCompletedWork reference '{workItemId}' is invalid because it was not already Completed when this planning cycle began.");
                }
            }
        }
    }
}
