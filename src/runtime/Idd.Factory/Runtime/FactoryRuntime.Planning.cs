using System.Text.Json;
using Idd.Factory.Domain;

namespace Idd.Factory.Runtime;

public sealed partial class FactoryRuntime
{
    private async Task<FactoryCliOutcome?> PlanAsync(FactoryState state, CancellationToken cancellationToken)
    {
        var replanning = state.PendingReplanTrigger is not null;
        var initialPlanning = !state.InitialPlanningCompleted;
        var incrementalPlanning = !initialPlanning && !replanning;
        if (replanning && state.ReplanCount >= configuration.Limits.MaxReplans) throw new AgentProtocolException("REPLAN_BUDGET_EXHAUSTED", "Semantic replan budget exhausted.");
        if (incrementalPlanning && state.Completed.Count <= state.PlannedThroughCompletedCount)
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Incremental planning requires completed work that has not yet been incorporated into planning.");

        var request = await File.ReadAllTextAsync(Path.Combine(currentDirectory, state.RequestPath), cancellationToken);
        var completed = JsonSerializer.Serialize(state.Completed.Select(x => new { x.Id, x.Capability, x.ContractPath, x.ResultRef }), FactoryJson.Options);
        var future = new[] { state.Current }.Where(x => x is not null).Concat(state.Remaining).Select(x => new { x!.Capability, task = ReadContract(x.ContractPath) });
        var trigger = replanning
            ? JsonSerializer.Serialize(state.PendingReplanTrigger, FactoryJson.Options)
            : initialPlanning
                ? "initial request"
                : $"new completed work since previous planning: {state.Completed.Count - state.PlannedThroughCompletedCount} item(s)";
        var input = $"Original request:\n{request}\n\nCompleted immutable work:\n{completed}\n\nCurrent planning trigger:\n{trigger}\n\nExisting future plan:\n{JsonSerializer.Serialize(future, FactoryJson.Options)}\n\n" +
                    "Return only the ordered work that remains to be done. Completed work is immutable and must not be reproduced. The first task executes first. Do not return IDs, dependencies, status, sequence, revisions, outlines, or mutation operations.";
        var result = await InvokeSemanticAsync(state, "planning", null, input, SemanticOperationKind.Planning, cancellationToken);
        if (result.Outcome != "ready") return await HandleSemanticStopAsync(state, null, result, SemanticOperationKind.Planning, input, cancellationToken);
        if (result.Tasks is not { ValueKind: JsonValueKind.Array } tasks)
            throw new AgentProtocolException("MALFORMED_AGENT_RESULT", "Planning ready result requires top-level tasks.");

        var previous = CloneState(state);
        var candidate = CloneState(state);
        candidate.Current = null;
        candidate.CurrentPhase = null;
        candidate.Remaining.Clear();
        var contracts = new List<(string Path, string Content)>();
        foreach (var node in tasks.EnumerateArray()) candidate.Remaining.Add(ParsePlannedTask(candidate, node, contracts));
        candidate.InitialPlanningCompleted = true;
        candidate.PlannedThroughCompletedCount = candidate.Completed.Count;
        candidate.PendingReplanTrigger = null;
        candidate.PendingContinuation = null;
        candidate.Blocker = null;
        candidate.RunStatus = FactoryRunStatus.Running;
        candidate.PlanRevision++;
        if (replanning) candidate.ReplanCount++;
        InvalidateFinalEvidence(candidate);
        await CommitPlanMutationAsync(
            state,
            previous,
            candidate,
            contracts,
            replanning ? "semantic-replan" : initialPlanning ? "initial-planning" : "incremental-planning",
            result.AttemptId,
            state.Current?.Id,
            cancellationToken);
        return null;
    }

    private PlannedWorkItem ParsePlannedTask(FactoryState state, JsonElement node, List<(string Path, string Content)> contracts)
    {
        if (node.ValueKind != JsonValueKind.Object) throw new AgentProtocolException("MALFORMED_AGENT_RESULT", "Each planned task must be an object.");
        return CreatePlannedTask(
            state,
            RequiredString(node, "capability", "Planned task capability is required."),
            RequiredString(node, "task", "Planned task text is required."),
            contracts);
    }

    private PlannedWorkItem CreatePlannedTask(FactoryState state, string capability, string task, List<(string Path, string Content)> contracts)
    {
        FactoryCapabilityCatalog.ResolveWorkItem(capability);
        if (!configuration.AllowedCapabilities.Contains(capability)) throw new AgentProtocolException("CAPABILITY_NOT_ALLOWED", $"Capability '{capability}' is not allowed.");
        var id = $"W{state.NextWorkItemNumber++:000000}";
        var path = $"work-items/{id}/contract.md";
        contracts.Add((path, task.Trim() + Environment.NewLine));
        return new PlannedWorkItem { Id = id, Capability = capability, ContractPath = path };
    }

    private async Task<FactoryCliOutcome?> PrependAdditionalWorkAsync(FactoryState state, PlannedWorkItem source, BoundSemanticAgentResult result, CancellationToken cancellationToken)
    {
        if (result.Payload is not { } payload) throw new AgentProtocolException("MALFORMED_AGENT_RESULT", "additional-work-required requires a payload.");
        return await PrependBeforeRetryAsync(state, source, result, payload, "worker-additional-work", false, cancellationToken);
    }

    private async Task<FactoryCliOutcome?> PrependReviewCorrectionAsync(FactoryState state, PlannedWorkItem source, BoundSemanticAgentResult result, CancellationToken cancellationToken)
    {
        if (result.Payload is not { } correction)
            throw new AgentProtocolException("MALFORMED_AGENT_RESULT", "Review correction requires a payload.");
        return await PrependBeforeRetryAsync(state, source, result, correction, "review-correction", true, cancellationToken);
    }

    private void StartCorrectiveCycle(FactoryState state)
    {
        if (state.CorrectiveCycleCount >= configuration.Limits.MaxCorrectiveCycles)
            throw new AgentProtocolException("CORRECTIVE_BUDGET_EXHAUSTED", "Corrective work budget exhausted.");
        state.CorrectiveCycleCount++;
    }

    private async Task<FactoryCliOutcome?> PrependBeforeRetryAsync(
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
        var previous = CloneState(state);
        var candidate = CloneState(state);
        var candidateSource = candidate.Current is { } current && current.Id == source.Id
            ? current
            : throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Dynamic work must retry the authoritative Current work item.");
        var contracts = new List<(string Path, string Content)>();
        var newWork = CreatePlannedTask(candidate, capability, task, contracts);
        if (correctiveCycle) StartCorrectiveCycle(candidate);
        candidateSource.LastResultRef = $"attempts/{result.AttemptId}/result.json";
        if (!candidateSource.PriorResultRefs.Contains(candidateSource.LastResultRef, StringComparer.Ordinal)) candidateSource.PriorResultRefs.Add(candidateSource.LastResultRef);
        candidateSource.CurrentAttemptId = null;
        candidate.Current = null;
        candidate.CurrentPhase = null;
        candidate.Remaining.Insert(0, candidateSource);
        candidate.Remaining.Insert(0, newWork);
        candidate.PendingContinuation = null;
        candidate.Blocker = null;
        candidate.PlanRevision++;
        InvalidateFinalEvidence(candidate);
        await CommitPlanMutationAsync(state, previous, candidate, contracts, reason, result.AttemptId, source.Id, cancellationToken);
        return null;
    }

    private async Task<FactoryCliOutcome?> RunFinalReviewAsync(FactoryState state, CancellationToken cancellationToken)
    {
        var request = await File.ReadAllTextAsync(Path.Combine(currentDirectory, state.RequestPath), cancellationToken);
        var input = $"Original Factory request:\n{request}\n\nCompleted work:\n{await BuildCompletedContextAsync(state, cancellationToken)}\n\nVerification evidence:\n{JsonSerializer.Serialize(state.VerificationEvidenceRefs, FactoryJson.Options)}\n\nReview the integrated result. Do not edit files.";
        var result = await InvokeSemanticAsync(state, "final-review", null, input, SemanticOperationKind.FinalReview, cancellationToken);
        if (result.Outcome == "approved")
        {
            state.FinalReview = new("approved", $"attempts/{result.AttemptId}/result.json", (state.FinalReview?.AttemptCount ?? 0) + 1, state.PlanRevision);
            state.PendingContinuation = null;
            await SaveAsync(state, cancellationToken);
            return null;
        }
        if (result.Outcome == "global-replan-required")
        {
            var resultRef = $"attempts/{result.AttemptId}/result.json";
            state.PendingReplanTrigger = new("final-review", null, resultRef, result.Reason, result.Payload?.Clone(), state.VerificationEvidenceRefs.ToList());
            state.PendingContinuation = null;
            state.Blocker = null;
            state.FinalReview = new(result.Outcome, resultRef, (state.FinalReview?.AttemptCount ?? 0) + 1, null);
            await SaveAsync(state, cancellationToken);
            return null;
        }
        if (result.Outcome is "correction-required" or "additional-work-required")
        {
            if (result.Payload is not { } payload) throw new AgentProtocolException("MALFORMED_AGENT_RESULT", "Final review correction requires a payload.");
            var capability = RequiredString(payload, "capability", "Final review correction capability is required.");
            var task = RequiredString(payload, "task", "Final review correction task is required.");
            var previous = CloneState(state);
            var candidate = CloneState(state);
            var contracts = new List<(string Path, string Content)>();
            candidate.Remaining.Add(CreatePlannedTask(candidate, capability, task, contracts));
            StartCorrectiveCycle(candidate);
            candidate.PlanRevision++;
            candidate.FinalReview = new(result.Outcome, $"attempts/{result.AttemptId}/result.json", (candidate.FinalReview?.AttemptCount ?? 0) + 1, null);
            InvalidateFinalEvidence(candidate);
            await CommitPlanMutationAsync(state, previous, candidate, contracts, "final-review-correction", result.AttemptId, null, cancellationToken);
            return null;
        }
        return await HandleSemanticStopAsync(state, null, result, SemanticOperationKind.FinalReview, input, cancellationToken);
    }

    private async Task CommitPlanMutationAsync(
        FactoryState state,
        FactoryState previous,
        FactoryState candidate,
        IReadOnlyList<(string Path, string Content)> contracts,
        string reason,
        string? sourceAttemptId,
        string? sourceWorkItemId,
        CancellationToken cancellationToken)
    {
        ValidateRuntimeState(candidate);
        foreach (var contract in contracts)
            await WriteRuntimeArtifactAtomicallyAsync(Path.Combine(currentDirectory, contract.Path), contract.Content, cancellationToken);
        await planRevisions.WriteAsync(previous, candidate, reason, sourceAttemptId, sourceWorkItemId, cancellationToken);
        ApplyCandidate(state, candidate);
        await SaveAsync(state, cancellationToken);
    }

    private string ReadContract(string path) => File.Exists(Path.Combine(currentDirectory, path)) ? File.ReadAllText(Path.Combine(currentDirectory, path)) : "[missing contract]";
    private static string RequiredString(JsonElement node, string name, string message) => OptionalString(node, name) ?? throw new AgentProtocolException("MALFORMED_AGENT_RESULT", message);
    private static string? OptionalString(JsonElement node, string name) => node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString() : null;
    private static async Task WriteRuntimeArtifactAtomicallyAsync(string path, string content, CancellationToken cancellationToken)
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
        if (state.FinalReview?.ReviewedPlanRevision != state.PlanRevision) state.FinalReview = null;
    }
}
