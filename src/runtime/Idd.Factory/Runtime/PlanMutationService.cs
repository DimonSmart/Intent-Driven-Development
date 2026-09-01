using System.Text.Json;
using Idd.Factory.Domain;

namespace Idd.Factory.Runtime;

public sealed partial class FactoryRuntime
{
    private sealed class PlanMutationService(FactoryRuntime runtime)
    {
        public async Task ApplyPlanningResultAsync(
            FactoryState state,
            JsonElement tasks,
            bool replanning,
            bool initialPlanning,
            string sourceAttemptId,
            CancellationToken cancellationToken)
        {
            var previous = FactoryRuntime.CloneState(state);
            var candidate = FactoryRuntime.CloneState(state);
            var sourceWorkItemId = replanning ? state.PendingReplanTrigger?.SourceWorkItemId : null;

            candidate.Current = null;
            candidate.CurrentPhase = null;
            candidate.Remaining.Clear();
            var contracts = new List<(string Path, string Content)>();
            foreach (var node in tasks.EnumerateArray())
                candidate.Remaining.Add(ParsePlannedTask(candidate, node, contracts));

            candidate.InitialPlanningCompleted = true;
            candidate.PlannedThroughCompletedCount = candidate.Completed.Count;
            candidate.PendingReplanTrigger = null;
            candidate.PendingContinuation = null;
            candidate.Blocker = null;
            candidate.RunStatus = FactoryRunStatus.Running;
            candidate.PlanRevision++;
            if (replanning) candidate.ReplanCount++;
            InvalidateFinalEvidence(candidate);

            await CommitAsync(
                state,
                previous,
                candidate,
                contracts,
                replanning ? "semantic-replan" : initialPlanning ? "initial-planning" : "incremental-planning",
                sourceAttemptId,
                sourceWorkItemId,
                cancellationToken);
        }

        public async Task PrependBeforeRetryAsync(
            FactoryState state,
            PlannedWorkItem source,
            BoundSemanticAgentResult result,
            JsonElement requirement,
            string reason,
            bool correctiveCycle,
            CancellationToken cancellationToken)
        {
            var capability = RequiredString(requirement, "capability", "Additional work capability is required.");
            var task = RequiredString(requirement, "task", "Additional work task is required.");
            var previous = FactoryRuntime.CloneState(state);
            var candidate = FactoryRuntime.CloneState(state);
            var candidateSource = candidate.Current is { } current && current.Id == source.Id
                ? current
                : throw new FactoryStateException(
                    "CORRUPT_FACTORY_STATE",
                    "Dynamic work must retry the authoritative Current work item.");

            var contracts = new List<(string Path, string Content)>();
            var newWork = CreatePlannedTask(candidate, capability, task, contracts);
            if (correctiveCycle) StartCorrectiveCycle(candidate);

            candidateSource.LastResultRef = $"attempts/{result.AttemptId}/result.json";
            if (!candidateSource.PriorResultRefs.Contains(candidateSource.LastResultRef, StringComparer.Ordinal))
                candidateSource.PriorResultRefs.Add(candidateSource.LastResultRef);
            candidateSource.CurrentAttemptId = null;
            candidate.Current = null;
            candidate.CurrentPhase = null;
            candidate.Remaining.Insert(0, candidateSource);
            candidate.Remaining.Insert(0, newWork);
            candidate.PendingContinuation = null;
            candidate.Blocker = null;
            candidate.PlanRevision++;
            InvalidateFinalEvidence(candidate);

            await CommitAsync(
                state,
                previous,
                candidate,
                contracts,
                reason,
                result.AttemptId,
                source.Id,
                cancellationToken);
        }

        public async Task ApplyFinalReviewCorrectionAsync(
            FactoryState state,
            BoundSemanticAgentResult result,
            JsonElement payload,
            CancellationToken cancellationToken)
        {
            var capability = RequiredString(payload, "capability", "Final review correction capability is required.");
            var task = RequiredString(payload, "task", "Final review correction task is required.");
            var previous = FactoryRuntime.CloneState(state);
            var candidate = FactoryRuntime.CloneState(state);
            var contracts = new List<(string Path, string Content)>();

            candidate.Remaining.Add(CreatePlannedTask(candidate, capability, task, contracts));
            StartCorrectiveCycle(candidate);
            candidate.PlanRevision++;
            candidate.FinalReview = new(
                result.Outcome,
                $"attempts/{result.AttemptId}/result.json",
                (candidate.FinalReview?.AttemptCount ?? 0) + 1,
                null);
            InvalidateFinalEvidence(candidate);

            await CommitAsync(
                state,
                previous,
                candidate,
                contracts,
                "final-review-correction",
                result.AttemptId,
                null,
                cancellationToken);
        }

        private PlannedWorkItem ParsePlannedTask(
            FactoryState state,
            JsonElement node,
            List<(string Path, string Content)> contracts)
        {
            if (node.ValueKind != JsonValueKind.Object)
                throw new AgentProtocolException(
                    "MALFORMED_AGENT_RESULT",
                    "Each planned task must be an object.");

            return CreatePlannedTask(
                state,
                RequiredString(node, "capability", "Planned task capability is required."),
                RequiredString(node, "task", "Planned task text is required."),
                contracts);
        }

        private PlannedWorkItem CreatePlannedTask(
            FactoryState state,
            string capability,
            string task,
            List<(string Path, string Content)> contracts)
        {
            FactoryCapabilityCatalog.ResolveWorkItem(capability);
            if (!runtime.configuration.AllowedCapabilities.Contains(capability))
                throw new AgentProtocolException(
                    "CAPABILITY_NOT_ALLOWED",
                    $"Capability '{capability}' is not allowed.");

            var id = $"W{state.NextWorkItemNumber++:000000}";
            var path = $"work-items/{id}/contract.md";
            contracts.Add((path, task.Trim() + Environment.NewLine));
            return new PlannedWorkItem { Id = id, Capability = capability, ContractPath = path };
        }

        private void StartCorrectiveCycle(FactoryState state)
        {
            if (state.CorrectiveCycleCount >= runtime.configuration.Limits.MaxCorrectiveCycles)
                throw new AgentProtocolException(
                    "CORRECTIVE_BUDGET_EXHAUSTED",
                    "Corrective work budget exhausted.");

            state.CorrectiveCycleCount++;
        }

        private async Task CommitAsync(
            FactoryState state,
            FactoryState previous,
            FactoryState candidate,
            IReadOnlyList<(string Path, string Content)> contracts,
            string reason,
            string? sourceAttemptId,
            string? sourceWorkItemId,
            CancellationToken cancellationToken)
        {
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
                sourceWorkItemId,
                cancellationToken);

            FactoryRuntime.ApplyCandidate(state, candidate);
            await runtime.SaveAsync(state, cancellationToken);
        }

        private static string RequiredString(JsonElement node, string name, string message) =>
            OptionalString(node, name) ??
            throw new AgentProtocolException("MALFORMED_AGENT_RESULT", message);

        private static string? OptionalString(JsonElement node, string name) =>
            node.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()
                : null;

        private static void InvalidateFinalEvidence(FactoryState state)
        {
            state.FinalVerificationPassed = false;
            state.FinalVerificationPlanRevision = null;
            if (state.FinalReview?.ReviewedPlanRevision != state.PlanRevision)
                state.FinalReview = null;
        }
    }
}
