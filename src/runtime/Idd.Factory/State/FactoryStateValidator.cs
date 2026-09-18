using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Runtime;

namespace Idd.Factory.State;

public sealed class FactoryStateValidator
{
    public void Validate(FactoryState state)
    {
        if (state.SchemaVersion != FactoryState.CurrentSchemaVersion) throw Error($"Unsupported state schema {state.SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(state.RunId) || string.IsNullOrWhiteSpace(state.MethodologyVersion) || string.IsNullOrWhiteSpace(state.RuntimeVersion)) throw Error("Factory identity is incomplete.");
        if (state.Revision < 0 || state.PlanRevision < 0 || state.NextWorkItemNumber < 1 || state.AttemptSequence < 0 || state.PlanningCycleCount < 0 || state.PlannedThroughCompletedCount < 0)
            throw Error("State counters cannot be negative or zero where a positive value is required.");
        if (state.PlannedThroughCompletedCount > state.Completed.Count) throw Error("Planning cannot include completed work that does not exist.");
        if ((state.Current is null) != (state.CurrentPhase is null)) throw Error("Current and CurrentPhase must be set or cleared together.");

        ValidateChangeSetSummary(state.RunChanges, "run");
        foreach (var completed in state.Completed)
            ValidateChangeSetSummary(completed.Changes, $"completed work {completed.Id}");

        var active = new[] { state.Current }.Where(x => x is not null).Concat(state.Remaining).Select(x => x!).ToArray();
        if (active.Any(x => x.SemanticAttemptCount < 0
                            || x.TechnicalRestartCount < 0
                            || x.AdditionalSemanticAttemptBudget < 0))
            throw Error("Work item semantic and technical retry counters cannot be negative.");
        if (active.Any(x => (x.CurrentAttemptId is null) != (x.CurrentInvocationKind is null)))
            throw Error("Current work-item invocation identity and kind must be set or cleared together.");
        if (state.Remaining.Any(x => x.CurrentAttemptId is not null || x.CurrentInvocationKind is not null))
            throw Error("Remaining work cannot have an active executor invocation.");
        if (state.Current is { } current
            && state.CurrentAttemptId is not null
            && current.CurrentAttemptId != state.CurrentAttemptId)
            throw Error("Current work-item attempt identity must match Factory attempt identity.");
        foreach (var item in active)
        {
            ValidateChangeSetSummary(item.Changes, $"work item {item.Id}");
            foreach (var failure in item.PriorTechnicalFailures)
                ValidateChangeSetSummary(failure.Changes, $"technical failure {failure.FailedAttemptId}");
        }

        var all = state.Completed.Select(x => (
                Id: x.Id,
                ContractPath: x.ContractPath,
                TaskRelatedIntentIds: (IReadOnlyList<string>)x.TaskRelatedIntentIds,
                RelevantCompletedWorkIds: (IReadOnlyList<string>)x.RelevantCompletedWorkIds))
            .Concat(state.Current is null
                ? []
                : [(state.Current.Id, state.Current.ContractPath, (IReadOnlyList<string>)state.Current.TaskRelatedIntentIds, (IReadOnlyList<string>)state.Current.RelevantCompletedWorkIds)])
            .Concat(state.Remaining.Select(x => (
                Id: x.Id,
                ContractPath: x.ContractPath,
                TaskRelatedIntentIds: (IReadOnlyList<string>)x.TaskRelatedIntentIds,
                RelevantCompletedWorkIds: (IReadOnlyList<string>)x.RelevantCompletedWorkIds)))
            .ToArray();
        if (all.Any(x => string.IsNullOrWhiteSpace(x.Id))) throw Error("Every work item requires an ID.");
        if (all.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != all.Length) throw Error("Work item IDs must be unique across Completed, Current, and Remaining.");
        foreach (var item in all)
        {
            if (string.IsNullOrWhiteSpace(item.ContractPath)) throw Error($"Work item {item.Id} has an incomplete contract.");
            ValidateContractPath(item.ContractPath, item.Id);
            ValidateTaskRelatedIntentIds(item.Id, item.TaskRelatedIntentIds);
            ValidateRelevantCompletedWorkIds(item.Id, item.RelevantCompletedWorkIds);
        }
        ValidateRelevantCompletedWorkReferences(state);
        if (state.CurrentAttemptId is not null && state.Current is null && state.PendingContinuation?.Operation != SemanticOperationKind.Planning)
            throw Error("An active attempt without Current work must be planning.");
        if (state.PendingVerificationSession is { WorkItemId: not null } session && state.Current?.Id != session.WorkItemId) throw Error("Subtask verification must target Current work.");
        if (state.PendingVerificationSession is { } verificationSession)
        {
            ValidateVerificationSession(verificationSession);
            ValidateChangeSetSummary(verificationSession.Changes, "pending verification");
        }
        if (state.FinalVerificationPassed && state.FinalVerificationPlanRevision is null) throw Error("Passed final verification requires its plan revision.");
        if (state.FinalVerificationPlanRevision < 0) throw Error("Final evidence revisions cannot be negative.");
        if (state.FinalVerificationPlanRevision > state.PlanRevision) throw Error("Final evidence cannot target a future plan revision.");
    }

    public void ValidateMutation(FactoryState previous, FactoryState next)
    {
        Validate(next);
        if (next.Revision != previous.Revision + 1) throw Error("Revision must advance by exactly one.");
        if (next.PlanRevision < previous.PlanRevision || next.NextWorkItemNumber < previous.NextWorkItemNumber || next.PlannedThroughCompletedCount < previous.PlannedThroughCompletedCount)
            throw Error("Plan, ID, and planning-knowledge counters are monotonic.");
        if (next.Completed.Count < previous.Completed.Count) throw Error("Completed history cannot shrink.");
        for (var index = 0; index < previous.Completed.Count; index++)
            if (!Equivalent(previous.Completed[index], next.Completed[index])) throw Error($"Completed work {previous.Completed[index].Id} is immutable.");
        if (next.Completed.Count > previous.Completed.Count + 1) throw Error("Only Current can be committed to Completed in one transition.");
        if (next.Completed.Count == previous.Completed.Count + 1)
        {
            if (previous.Current is null || next.Completed[^1].Id != previous.Current.Id) throw Error("New completed work must be the previous Current task.");
            if (next.Current is not null) throw Error("Current must be cleared when it is committed to Completed.");
        }

        var previousRelatedIntent = GetTaskRelatedIntentByWorkItem(previous);
        var nextRelatedIntent = GetTaskRelatedIntentByWorkItem(next);
        foreach (var (workItemId, previousIds) in previousRelatedIntent)
        {
            if (nextRelatedIntent.TryGetValue(workItemId, out var nextIds)
                && !previousIds.SequenceEqual(nextIds, StringComparer.Ordinal))
            {
                throw Error($"Task-related durable intent for work item {workItemId} is immutable.");
            }
        }

        var previousRelevantCompletedWork = GetRelevantCompletedWorkByWorkItem(previous);
        var nextRelevantCompletedWork = GetRelevantCompletedWorkByWorkItem(next);
        foreach (var (workItemId, previousIds) in previousRelevantCompletedWork)
        {
            if (nextRelevantCompletedWork.TryGetValue(workItemId, out var nextIds)
                && !previousIds.SequenceEqual(nextIds, StringComparer.Ordinal))
            {
                throw Error($"Relevant completed work selection for work item {workItemId} is immutable.");
            }
        }
    }

    private static void ValidateVerificationSession(PendingVerificationSession session)
    {
        if (session.NextCheckIndex < 0 || session.NextCheckIndex > session.CheckIds.Count)
            throw Error("Verification progress is outside the selected check range.");

        var awaitingAction = session.Stage is VerificationContinuationStage.AwaitingConfirmation or VerificationContinuationStage.AwaitingManualResult;
        var hasPendingCheck = session.PendingCheckId is not null;
        var hasPendingDefinition = session.PendingCheckDefinitionHash is not null;
        if (awaitingAction)
        {
            if (!hasPendingCheck || !hasPendingDefinition) throw Error("Verification action stage requires complete pending-check metadata.");
        }
        else if (hasPendingCheck || hasPendingDefinition)
        {
            throw Error("Verification execute stage cannot retain pending-check metadata.");
        }
    }

    private static void ValidateChangeSetSummary(ChangeSetSummary? summary, string owner)
    {
        if (summary is null)
            return;
        if (summary.Count < 0)
            throw Error($"Change-set count for {owner} cannot be negative.");
        if (summary.Preview.Count > 50 || summary.Preview.Count > summary.Count)
            throw Error($"Change-set preview for {owner} is not bounded by its count and the 50-path limit.");
        ValidateArtifactReference(summary.Reference, owner);

        string? previous = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in summary.Preview)
        {
            WorkspacePathPolicy.ValidateGitPath(path);
            if (!seen.Add(path))
                throw Error($"Change-set preview for {owner} contains duplicate path '{path}'.");
            if (previous is not null && StringComparer.Ordinal.Compare(previous, path) >= 0)
                throw Error($"Change-set preview for {owner} must be ordinal-sorted.");
            previous = path;
        }
    }

    private static void ValidateArtifactReference(string reference, string owner)
    {
        try
        {
            WorkspacePathPolicy.ValidateArtifactReference(reference);
        }
        catch (FactoryStateException)
        {
            throw Error($"Invalid change-set reference for {owner} '{reference}'.");
        }
    }

    private static void ValidateTaskRelatedIntentIds(string workItemId, IReadOnlyList<string> ids)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (!DurableIntentId.IsCanonical(id))
                throw Error($"Work item {workItemId} has invalid task-related durable intent ID '{id}'.");
            if (!seen.Add(id))
                throw Error($"Work item {workItemId} has duplicate task-related durable intent ID '{id}'.");
        }
    }

    private static void ValidateRelevantCompletedWorkIds(string workItemId, IReadOnlyList<string> ids)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (!WorkItemId.IsCanonical(id))
                throw Error($"Work item {workItemId} has invalid relevant completed work ID '{id}'.");
            if (!seen.Add(id))
                throw Error($"Work item {workItemId} has duplicate relevant completed work ID '{id}'.");
        }
    }

    private static void ValidateRelevantCompletedWorkReferences(FactoryState state)
    {
        var completedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in state.Completed)
        {
            foreach (var reference in item.RelevantCompletedWorkIds)
            {
                if (!completedIds.Contains(reference))
                    throw Error($"Completed work item {item.Id} references '{reference}', which was not completed before it.");
            }
            completedIds.Add(item.Id);
        }

        if (state.Current is not null)
            ValidateActiveRelevantReferences(state.Current, completedIds);
        foreach (var item in state.Remaining)
            ValidateActiveRelevantReferences(item, completedIds);
    }

    private static void ValidateActiveRelevantReferences(
        PlannedWorkItem item,
        IReadOnlySet<string> completedIds)
    {
        foreach (var reference in item.RelevantCompletedWorkIds)
        {
            if (!completedIds.Contains(reference))
                throw Error($"Work item {item.Id} references relevant completed work '{reference}' that is not in Completed history.");
        }
    }

    private static Dictionary<string, IReadOnlyList<string>> GetTaskRelatedIntentByWorkItem(FactoryState state)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var item in state.Completed)
            result[item.Id] = item.TaskRelatedIntentIds;
        if (state.Current is not null)
            result[state.Current.Id] = state.Current.TaskRelatedIntentIds;
        foreach (var item in state.Remaining)
            result[item.Id] = item.TaskRelatedIntentIds;
        return result;
    }

    private static Dictionary<string, IReadOnlyList<string>> GetRelevantCompletedWorkByWorkItem(FactoryState state)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var item in state.Completed)
            result[item.Id] = item.RelevantCompletedWorkIds;
        if (state.Current is not null)
            result[state.Current.Id] = state.Current.RelevantCompletedWorkIds;
        foreach (var item in state.Remaining)
            result[item.Id] = item.RelevantCompletedWorkIds;
        return result;
    }

    private static bool Equivalent(CompletedWorkItem left, CompletedWorkItem right) =>
        JsonSerializer.Serialize(left, FactoryJson.Options) == JsonSerializer.Serialize(right, FactoryJson.Options);

    private static void ValidateContractPath(string path, string id)
    {
        var normalized = path.Replace('\\', '/');
        if (Path.IsPathRooted(path) || normalized.Contains("../", StringComparison.Ordinal) || !normalized.StartsWith("work-items/", StringComparison.Ordinal) || !normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            throw Error($"Work item {id} has invalid contract path '{path}'.");
    }

    private static FactoryStateException Error(string message) => new("CORRUPT_FACTORY_STATE", message);
}

public sealed class FactoryStateException(string code, string message) : Exception(message) { public string Code { get; } = code; }
