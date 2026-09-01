using System.Text.Json;
using Idd.Factory.Domain;

namespace Idd.Factory.Runtime;

public sealed partial class FactoryRuntime
{
    private PlanMutationService CreatePlanMutationService() => new(this);

    private async Task<FactoryCliOutcome?> PlanAsync(FactoryState state, CancellationToken cancellationToken)
    {
        var replanning = state.PendingReplanTrigger is not null;
        var initialPlanning = !state.InitialPlanningCompleted;
        var incrementalPlanning = !initialPlanning && !replanning;
        if (replanning && state.ReplanCount >= configuration.Limits.MaxReplans)
            throw new AgentProtocolException("REPLAN_BUDGET_EXHAUSTED", "Semantic replan budget exhausted.");
        if (incrementalPlanning && state.Completed.Count <= state.PlannedThroughCompletedCount)
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                "Incremental planning requires completed work that has not yet been incorporated into planning.");

        var request = await File.ReadAllTextAsync(Path.Combine(currentDirectory, state.RequestPath), cancellationToken);
        var completed = JsonSerializer.Serialize(
            state.Completed.Select(x => new { x.Id, x.Capability, x.ContractPath, x.ResultRef }),
            FactoryJson.Options);
        var future = new[] { state.Current }
            .Where(x => x is not null)
            .Concat(state.Remaining)
            .Select(x => new { x!.Capability, task = ReadContract(x.ContractPath) });
        var trigger = replanning
            ? JsonSerializer.Serialize(state.PendingReplanTrigger, FactoryJson.Options)
            : initialPlanning
                ? "initial request"
                : $"new completed work since previous planning: {state.Completed.Count - state.PlannedThroughCompletedCount} item(s)";
        var input =
            $"Original request:\n{request}\n\nCompleted immutable work:\n{completed}\n\nCurrent planning trigger:\n{trigger}\n\nExisting future plan:\n{JsonSerializer.Serialize(future, FactoryJson.Options)}\n\n" +
            "Return only the ordered work that remains to be done. Completed work is immutable and must not be reproduced. The first task executes first. Do not return IDs, dependencies, status, sequence, revisions, outlines, or mutation operations.";

        var result = await InvokeSemanticAsync(
            state,
            "planning",
            null,
            input,
            SemanticOperationKind.Planning,
            cancellationToken);

        if (result.Outcome != "ready")
            return await HandleSemanticStopAsync(
                state,
                null,
                result,
                SemanticOperationKind.Planning,
                input,
                cancellationToken);

        if (result.Tasks is not { ValueKind: JsonValueKind.Array } tasks)
            throw new AgentProtocolException(
                "MALFORMED_AGENT_RESULT",
                "Planning ready result requires top-level tasks.");

        await CreatePlanMutationService().ApplyPlanningResultAsync(
            state,
            tasks,
            replanning,
            initialPlanning,
            result.AttemptId,
            cancellationToken);

        return null;
    }

    private async Task<FactoryCliOutcome?> PrependAdditionalWorkAsync(
        FactoryState state,
        PlannedWorkItem source,
        BoundSemanticAgentResult result,
        CancellationToken cancellationToken)
    {
        if (result.Payload is not { } payload)
            throw new AgentProtocolException(
                "MALFORMED_AGENT_RESULT",
                "additional-work-required requires a payload.");

        await CreatePlanMutationService().PrependBeforeRetryAsync(
            state,
            source,
            result,
            payload,
            "worker-additional-work",
            false,
            cancellationToken);

        return null;
    }

    private async Task<FactoryCliOutcome?> PrependReviewCorrectionAsync(
        FactoryState state,
        PlannedWorkItem source,
        BoundSemanticAgentResult result,
        CancellationToken cancellationToken)
    {
        if (result.Payload is not { } correction)
            throw new AgentProtocolException(
                "MALFORMED_AGENT_RESULT",
                "Review correction requires a payload.");

        await CreatePlanMutationService().PrependBeforeRetryAsync(
            state,
            source,
            result,
            correction,
            "review-correction",
            true,
            cancellationToken);

        return null;
    }

    private async Task<FactoryCliOutcome?> RunFinalReviewAsync(
        FactoryState state,
        CancellationToken cancellationToken)
    {
        var request = await File.ReadAllTextAsync(
            Path.Combine(currentDirectory, state.RequestPath),
            cancellationToken);
        var input =
            $"Original Factory request:\n{request}\n\nCompleted work:\n{await BuildCompletedContextAsync(state, cancellationToken)}\n\nVerification evidence:\n{JsonSerializer.Serialize(state.VerificationEvidenceRefs, FactoryJson.Options)}\n\nReview the integrated result. Do not edit files.";
        var result = await InvokeSemanticAsync(
            state,
            "final-review",
            null,
            input,
            SemanticOperationKind.FinalReview,
            cancellationToken);

        if (result.Outcome == "approved")
        {
            state.FinalReview = new(
                "approved",
                $"attempts/{result.AttemptId}/result.json",
                (state.FinalReview?.AttemptCount ?? 0) + 1,
                state.PlanRevision);
            state.PendingContinuation = null;
            await SaveAsync(state, cancellationToken);
            return null;
        }

        if (result.Outcome == "global-replan-required")
        {
            var resultRef = $"attempts/{result.AttemptId}/result.json";
            state.PendingReplanTrigger = new(
                "final-review",
                null,
                resultRef,
                result.Reason,
                result.Payload?.Clone(),
                state.VerificationEvidenceRefs.ToList());
            state.PendingContinuation = null;
            state.Blocker = null;
            state.FinalReview = new(
                result.Outcome,
                resultRef,
                (state.FinalReview?.AttemptCount ?? 0) + 1,
                null);
            await SaveAsync(state, cancellationToken);
            return null;
        }

        if (result.Outcome is "correction-required" or "additional-work-required")
        {
            if (result.Payload is not { } payload)
                throw new AgentProtocolException(
                    "MALFORMED_AGENT_RESULT",
                    "Final review correction requires a payload.");

            await CreatePlanMutationService().ApplyFinalReviewCorrectionAsync(
                state,
                result,
                payload,
                cancellationToken);
            return null;
        }

        return await HandleSemanticStopAsync(
            state,
            null,
            result,
            SemanticOperationKind.FinalReview,
            input,
            cancellationToken);
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
        if (state.FinalReview?.ReviewedPlanRevision != state.PlanRevision)
            state.FinalReview = null;
    }
}
