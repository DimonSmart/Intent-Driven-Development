using Idd.Factory.Domain;

namespace Idd.Factory.Runtime;

public sealed partial class FactoryRuntime
{
    private sealed class PlanMutationService(FactoryRuntime runtime)
    {
        public async Task ApplyPlanningResultAsync(
            FactoryState state,
            IReadOnlyList<string> tasks,
            string reason,
            string sourceAttemptId,
            CancellationToken cancellationToken)
        {
            var previous = FactoryRuntime.CloneState(state);
            var candidate = FactoryRuntime.CloneState(state);
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
            FactoryRuntime.InvalidateFinalEvidence(candidate);

            runtime.ValidateRuntimeState(candidate);
            foreach (var contract in contracts)
                await FactoryRuntime.WriteRuntimeArtifactAtomicallyAsync(
                    Path.Combine(runtime.currentDirectory, contract.Path),
                    contract.Content,
                    cancellationToken);
            await runtime.planRevisions.WriteAsync(
                previous,
                candidate,
                reason,
                sourceAttemptId,
                null,
                cancellationToken);
            FactoryRuntime.ApplyCandidate(state, candidate);
            await runtime.SaveAsync(state, cancellationToken);
        }

        private static PlannedWorkItem CreatePlannedTask(
            FactoryState state,
            string task,
            List<(string Path, string Content)> contracts)
        {
            if (string.IsNullOrWhiteSpace(task))
                throw new AgentProtocolException("MALFORMED_PLANNER_OUTPUT", "Planned task text is required.");
            var id = $"W{state.NextWorkItemNumber++:000000}";
            var path = $"work-items/{id}/contract.md";
            contracts.Add((path, task.Trim() + Environment.NewLine));
            return new PlannedWorkItem { Id = id, ContractPath = path };
        }
    }
}
