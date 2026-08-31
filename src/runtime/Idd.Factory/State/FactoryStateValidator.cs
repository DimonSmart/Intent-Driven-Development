using Idd.Factory.Domain;

namespace Idd.Factory.State;

public sealed class FactoryStateValidator
{
    private static readonly IReadOnlyDictionary<WorkItemStatus, WorkItemStatus[]> AllowedTransitions =
        new Dictionary<WorkItemStatus, WorkItemStatus[]>
        {
            [WorkItemStatus.Planned] = [WorkItemStatus.Ready, WorkItemStatus.Superseded, WorkItemStatus.Cancelled],
            [WorkItemStatus.Ready] = [WorkItemStatus.Dispatching, WorkItemStatus.Superseded, WorkItemStatus.Cancelled],
            [WorkItemStatus.Dispatching] = [WorkItemStatus.Running, WorkItemStatus.Ready, WorkItemStatus.Failed, WorkItemStatus.Cancelled],
            [WorkItemStatus.Running] = [WorkItemStatus.AwaitingVerification, WorkItemStatus.AwaitingReview, WorkItemStatus.Ready, WorkItemStatus.Blocked, WorkItemStatus.Failed, WorkItemStatus.Cancelled],
            [WorkItemStatus.AwaitingVerification] = [WorkItemStatus.Completed, WorkItemStatus.Ready, WorkItemStatus.Blocked, WorkItemStatus.Failed, WorkItemStatus.Cancelled],
            [WorkItemStatus.AwaitingReview] = [WorkItemStatus.Dispatching, WorkItemStatus.Ready, WorkItemStatus.Blocked, WorkItemStatus.Failed, WorkItemStatus.Cancelled],
            [WorkItemStatus.Blocked] = [WorkItemStatus.Ready, WorkItemStatus.AwaitingVerification, WorkItemStatus.AwaitingReview, WorkItemStatus.Cancelled],
            [WorkItemStatus.Failed] = [WorkItemStatus.Ready, WorkItemStatus.Cancelled],
            [WorkItemStatus.Completed] = [], [WorkItemStatus.Superseded] = [], [WorkItemStatus.Cancelled] = []
        };

    public void Validate(FactoryState state)
    {
        if (state.SchemaVersion != FactoryState.CurrentSchemaVersion)
            throw new FactoryStateException("UNSUPPORTED_STATE_SCHEMA", $"Unsupported state schema {state.SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(state.RunId) || state.Revision < 0 || string.IsNullOrWhiteSpace(state.WorkflowHash))
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "State identity, revision, and workflow hash are required.");
        if (state.WorkItems.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != state.WorkItems.Count)
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Work-item IDs must be unique.");
        if (state.WorkItems.Select(x => x.Sequence).Distinct().Count() != state.WorkItems.Count)
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Work-item sequences must be unique.");
        var ids = state.WorkItems.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var item in state.WorkItems)
        {
            if (item.Dependencies.Any(id => !ids.Contains(id)) || item.CoveredWorkItems.Any(id => !ids.Contains(id)))
                throw new FactoryStateException("CORRUPT_FACTORY_STATE", $"Work item {item.Id} has an unknown reference.");
            if (item.Dependencies.Contains(item.Id, StringComparer.Ordinal))
                throw new FactoryStateException("CORRUPT_FACTORY_STATE", $"Work item {item.Id} depends on itself.");
        }
        EnsureAcyclic(state.WorkItems);
        ValidatePendingVerificationSession(state, ids);
        if (state.PendingContinuation is { } continuation)
        {
            if (string.IsNullOrWhiteSpace(continuation.WorkflowStep))
                throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Continuation workflow step is required.");
            if (continuation.WorkItemId is { } continuationItem && !ids.Contains(continuationItem))
                throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Continuation references unknown work item.");
            if (continuation.Kind == ContinuationKind.VerificationGate && continuation.VerificationContext is not ("subtask" or "checkpoint" or "final"))
                throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Verification continuation requires a supported verification context.");
            if (continuation.VerificationContext is "subtask" or "checkpoint" && continuation.WorkItemId is null)
                throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Work-item verification continuation requires a work item.");
            if (continuation.Kind == ContinuationKind.SemanticInvocation && continuation.Operation == SemanticOperationKind.None)
                throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Semantic continuation requires an exact operation.");
            if (continuation.Operation is SemanticOperationKind.SubtaskVerificationFix or SemanticOperationKind.CheckpointVerificationFix or SemanticOperationKind.FinalVerificationFix)
            {
                if (string.IsNullOrWhiteSpace(continuation.OperationInput) || continuation.VerificationContext is null)
                    throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Verification-fix continuation requires its context and input.");
            }
        }
    }

    private static void ValidatePendingVerificationSession(FactoryState state, ISet<string> ids)
    {
        var session = state.PendingVerificationSession;
        if (session is null) return;
        if (session.Context is not ("subtask" or "checkpoint" or "final"))
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Verification session requires a supported context.");
        if (session.Context == "final" && session.WorkItemId is not null)
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Final verification session must not reference a work item.");
        if (session.Context is "subtask" or "checkpoint" && (session.WorkItemId is null || !ids.Contains(session.WorkItemId)))
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Work-item verification session requires an existing work item.");
        if (string.IsNullOrWhiteSpace(session.PolicyHash) || session.NextCheckIndex < 0 || session.NextCheckIndex > session.CheckIds.Count)
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Verification session cursor and policy hash are invalid.");
        if (session.CheckIds.Any(string.IsNullOrWhiteSpace) || session.CheckIds.Distinct(StringComparer.Ordinal).Count() != session.CheckIds.Count)
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Verification session check IDs must be unique.");
        if (!session.CompletedCheckIds.SequenceEqual(session.CheckIds.Take(session.NextCheckIndex), StringComparer.Ordinal))
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Completed verification checks must match the session cursor.");
        if (session.ChangedPaths.Any(path => string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\') || path.Split('/').Contains("..", StringComparer.Ordinal)))
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Verification session changed paths must be normalized repository-relative paths.");
        var awaiting = session.Stage is VerificationContinuationStage.AwaitingConfirmation or VerificationContinuationStage.AwaitingManualResult;
        if (awaiting != (session.PendingCheckId is not null && session.PendingCheckDefinitionHash is not null))
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Verification session pending action does not match its stage.");
        if (awaiting && (session.NextCheckIndex == session.CheckIds.Count || session.PendingCheckId != session.CheckIds[session.NextCheckIndex]))
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Verification session pending check does not match its cursor.");
        if (state.PendingContinuation is { } continuation)
        {
            if (continuation.Kind == ContinuationKind.VerificationGate &&
                (continuation.VerificationContext != session.Context || continuation.WorkItemId != session.WorkItemId || continuation.VerificationStage != session.Stage))
                throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Verification continuation contradicts its persisted session.");
            if (awaiting && continuation.Kind != ContinuationKind.VerificationGate)
                throw new FactoryStateException("CORRUPT_FACTORY_STATE", "A pending verification action requires a verification-gate continuation.");
        }
        else if (awaiting)
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "A pending verification action requires a continuation.");
    }

    public void ValidateMutation(FactoryState previous, FactoryState next)
    {
        Validate(next);
        if (previous.RunId != next.RunId || previous.WorkflowHash != next.WorkflowHash || previous.WorkflowName != next.WorkflowName || previous.RequestPath != next.RequestPath ||
            previous.MethodologyVersion != next.MethodologyVersion || previous.RuntimeVersion != next.RuntimeVersion)
            throw new FactoryStateException("IMMUTABLE_STATE_CHANGED", "Run identity, versions, request, and workflow provenance are immutable.");
        foreach (var completed in previous.WorkItems.Where(x => x.Status == WorkItemStatus.Completed))
        {
            var candidate = next.WorkItems.SingleOrDefault(x => x.Id == completed.Id)
                ?? throw new FactoryStateException("COMPLETED_ITEM_MUTATED", $"Completed item {completed.Id} was removed.");
            if (!EquivalentCompleted(completed, candidate))
                throw new FactoryStateException("COMPLETED_ITEM_MUTATED", $"Completed item {completed.Id} was changed.");
        }
        foreach (var oldItem in previous.WorkItems)
        {
            var newItem = next.WorkItems.SingleOrDefault(x => x.Id == oldItem.Id);
            if (newItem is not null && oldItem.Status != newItem.Status && !IsAllowedTransition(oldItem, newItem))
                throw new FactoryStateException("INVALID_STATE_TRANSITION", $"{oldItem.Id}: {oldItem.Status} -> {newItem.Status} is not allowed.");
        }
    }

    private static bool IsAllowedTransition(WorkItemState previous, WorkItemState next) =>
        AllowedTransitions[previous.Status].Contains(next.Status) ||
        previous.Kind == WorkItemKind.ReviewCheckpoint &&
        previous.Status == WorkItemStatus.Running &&
        next.Status == WorkItemStatus.Planned &&
        next.CurrentAttemptId is null &&
        !string.IsNullOrWhiteSpace(next.LastResultRef);

    private static bool EquivalentCompleted(WorkItemState left, WorkItemState right) =>
        left == right || (left.Id == right.Id && left.Sequence == right.Sequence && left.Kind == right.Kind &&
        left.Status == right.Status && left.ContractPath == right.ContractPath &&
        left.Dependencies.SequenceEqual(right.Dependencies) && left.CoveredWorkItems.SequenceEqual(right.CoveredWorkItems) &&
        left.AttemptCount == right.AttemptCount && left.VerificationFixAttemptCount == right.VerificationFixAttemptCount && left.LastResultRef == right.LastResultRef &&
        left.VerificationCheckIds.SequenceEqual(right.VerificationCheckIds) &&
        left.VerificationEvidenceRefs.SequenceEqual(right.VerificationEvidenceRefs));

    private static void EnsureAcyclic(IEnumerable<WorkItemState> workItems)
    {
        var items = workItems.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        bool Visit(string id)
        {
            if (visited.Contains(id)) return false;
            if (!visiting.Add(id)) return true;
            var cyclic = items[id].Dependencies.Any(Visit);
            visiting.Remove(id); visited.Add(id);
            return cyclic;
        }
        if (items.Keys.Any(Visit)) throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Work-item dependencies contain a cycle.");
    }
}

public sealed class FactoryStateException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
