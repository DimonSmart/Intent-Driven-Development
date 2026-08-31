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
            [WorkItemStatus.Running] = [WorkItemStatus.Waiting, WorkItemStatus.AwaitingVerification, WorkItemStatus.Completed, WorkItemStatus.Ready, WorkItemStatus.Blocked, WorkItemStatus.Failed, WorkItemStatus.Cancelled],
            [WorkItemStatus.Waiting] = [WorkItemStatus.Ready, WorkItemStatus.Blocked, WorkItemStatus.Superseded, WorkItemStatus.Cancelled],
            [WorkItemStatus.AwaitingVerification] = [WorkItemStatus.Completed, WorkItemStatus.Ready, WorkItemStatus.Blocked, WorkItemStatus.Failed, WorkItemStatus.Superseded, WorkItemStatus.Cancelled],
            [WorkItemStatus.Blocked] = [WorkItemStatus.Ready, WorkItemStatus.Waiting, WorkItemStatus.AwaitingVerification, WorkItemStatus.Superseded, WorkItemStatus.Cancelled],
            [WorkItemStatus.Failed] = [WorkItemStatus.Ready, WorkItemStatus.Superseded, WorkItemStatus.Cancelled],
            [WorkItemStatus.Completed] = [],
            [WorkItemStatus.Superseded] = [],
            [WorkItemStatus.Cancelled] = []
        };

    public void Validate(FactoryState state)
    {
        if (state.SchemaVersion != FactoryState.CurrentSchemaVersion)
            throw new FactoryStateException("UNSUPPORTED_STATE_SCHEMA", $"Unsupported state schema {state.SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(state.RunId) || state.Revision < 0 || state.GraphRevision < 0 || string.IsNullOrWhiteSpace(state.FactoryConfigurationHash))
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "State identity, revisions, and Factory configuration hash are required.");
        if (state.WorkItems.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != state.WorkItems.Count)
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Work-item IDs must be unique.");
        if (state.WorkItems.Select(x => x.Sequence).Distinct().Count() != state.WorkItems.Count || state.WorkItems.Any(x => x.Sequence < 1))
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Work-item sequences must be positive and unique.");

        var ids = state.WorkItems.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var item in state.WorkItems)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || item.ContractRevision < 1 || !SafeRelativePath(item.ContractPath) || !item.ContractPath.StartsWith("work-items/", StringComparison.Ordinal))
                throw new FactoryStateException("CORRUPT_FACTORY_STATE", $"Work item {item.Id} has invalid identity or contract provenance.");
            if (item.DefinitionState == WorkDefinitionState.Executable)
            {
                if (string.IsNullOrWhiteSpace(item.Capability))
                    throw new FactoryStateException("CORRUPT_FACTORY_STATE", $"Executable work item {item.Id} requires a capability.");
                try { FactoryCapabilityCatalog.ResolveWorkItem(item.Capability); }
                catch (AgentProtocolException exception) { throw new FactoryStateException("CORRUPT_FACTORY_STATE", exception.Message); }
            }
            if (item.DefinitionState == WorkDefinitionState.Outline && item.Status is WorkItemStatus.Dispatching or WorkItemStatus.Running or WorkItemStatus.AwaitingVerification or WorkItemStatus.Completed)
                throw new FactoryStateException("CORRUPT_FACTORY_STATE", $"Outline work item {item.Id} cannot enter executable lifecycle state {item.Status}.");
            if (item.Dependencies.Any(id => !ids.Contains(id)) || item.CoveredWorkItems.Any(id => !ids.Contains(id)))
                throw new FactoryStateException("CORRUPT_FACTORY_STATE", $"Work item {item.Id} has an unknown reference.");
            if (item.Dependencies.Contains(item.Id, StringComparer.Ordinal))
                throw new FactoryStateException("CORRUPT_FACTORY_STATE", $"Work item {item.Id} depends on itself.");
            if (item.VerificationCheckIds.Any(string.IsNullOrWhiteSpace) || item.VerificationCheckIds.Distinct(StringComparer.Ordinal).Count() != item.VerificationCheckIds.Count)
                throw new FactoryStateException("CORRUPT_FACTORY_STATE", $"Work item {item.Id} has invalid verification check IDs.");
            if (item.VerificationExpectations.Keys.Any(string.IsNullOrWhiteSpace))
                throw new FactoryStateException("CORRUPT_FACTORY_STATE", $"Work item {item.Id} has an invalid verification expectation.");
            if (item.IsFinalReview && (item.Capability != "semantic-review" || item.ReviewTargetGraphRevision is null))
                throw new FactoryStateException("CORRUPT_FACTORY_STATE", $"Final review work item {item.Id} has invalid review metadata.");
            if (!item.IsFinalReview && item.ReviewTargetGraphRevision is not null)
                throw new FactoryStateException("CORRUPT_FACTORY_STATE", $"Non-final review work item {item.Id} cannot carry a final review graph target.");
        }

        EnsureAcyclic(state.WorkItems);
        ValidateRemainingDependencies(state.WorkItems);
        ValidatePendingVerificationSession(state, ids);
        ValidateContinuation(state, ids);

        if (state.FinalVerificationPassed && state.FinalVerificationGraphRevision is null)
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Passed final verification requires its graph revision.");
        if (state.FinalVerificationGraphRevision > state.GraphRevision)
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Final verification cannot refer to a future graph revision.");
        if (state.FinalReview?.ReviewedGraphRevision > state.GraphRevision)
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Final review cannot refer to a future graph revision.");
    }

    public void ValidateMutation(FactoryState previous, FactoryState next)
    {
        Validate(next);
        if (previous.RunId != next.RunId || previous.FactoryConfigurationHash != next.FactoryConfigurationHash || previous.RequestPath != next.RequestPath ||
            previous.MethodologyVersion != next.MethodologyVersion || previous.RuntimeVersion != next.RuntimeVersion)
            throw new FactoryStateException("IMMUTABLE_STATE_CHANGED", "Run identity, versions, request, and Factory configuration provenance are immutable.");
        if (next.GraphRevision < previous.GraphRevision || next.GraphRevision > previous.GraphRevision + 1)
            throw new FactoryStateException("INVALID_GRAPH_REVISION", "GraphRevision must stay unchanged or advance by exactly one.");

        var graphChanged = !EquivalentGraph(previous.WorkItems, next.WorkItems);
        if (graphChanged != (next.GraphRevision == previous.GraphRevision + 1))
            throw new FactoryStateException("INVALID_GRAPH_REVISION", graphChanged
                ? "Task-graph topology or definition changed without advancing GraphRevision."
                : "GraphRevision advanced without a task-graph topology or definition change.");

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
            if (newItem is not null && oldItem.Status != newItem.Status && !AllowedTransitions[oldItem.Status].Contains(newItem.Status))
                throw new FactoryStateException("INVALID_STATE_TRANSITION", $"{oldItem.Id}: {oldItem.Status} -> {newItem.Status} is not allowed.");
        }
    }

    private static void ValidateContinuation(FactoryState state, ISet<string> ids)
    {
        if (state.PendingContinuation is not { } continuation) return;
        if (continuation.WorkItemId is { } continuationItem && !ids.Contains(continuationItem))
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Continuation references unknown work item.");
        if (continuation.Kind == ContinuationKind.VerificationGate && continuation.VerificationContext is not ("subtask" or "final"))
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Verification continuation requires a supported verification context.");
        if (continuation.VerificationContext == "subtask" && continuation.WorkItemId is null)
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Work-item verification continuation requires a work item.");
        if (continuation.Kind == ContinuationKind.SemanticInvocation && continuation.Operation == SemanticOperationKind.None)
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Semantic continuation requires an exact operation.");
        if (continuation.Operation is SemanticOperationKind.ScopedRefinement or SemanticOperationKind.WorkItemExecution && continuation.WorkItemId is null)
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Work-item semantic continuation requires a work item.");
    }

    private static void ValidatePendingVerificationSession(FactoryState state, ISet<string> ids)
    {
        var session = state.PendingVerificationSession;
        if (session is null) return;
        if (session.Context is not ("subtask" or "final"))
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Verification session requires a supported context.");
        if (session.Context == "final" && session.WorkItemId is not null)
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Final verification session must not reference a work item.");
        if (session.Context == "subtask" && (session.WorkItemId is null || !ids.Contains(session.WorkItemId)))
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Work-item verification session requires an existing work item.");
        if (string.IsNullOrWhiteSpace(session.PolicyHash) || session.NextCheckIndex < 0 || session.NextCheckIndex > session.CheckIds.Count)
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Verification session cursor and policy hash are invalid.");
        if (session.CheckIds.Any(string.IsNullOrWhiteSpace) || session.CheckIds.Distinct(StringComparer.Ordinal).Count() != session.CheckIds.Count)
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Verification session check IDs must be unique.");
        if (!session.CompletedCheckIds.SequenceEqual(session.CheckIds.Take(session.NextCheckIndex), StringComparer.Ordinal))
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Completed verification checks must match the session cursor.");
        if (session.FailedCheckIds.Any(id => !session.CompletedCheckIds.Contains(id, StringComparer.Ordinal)))
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Failed verification checks must be completed checks.");
        if (session.ChangedPaths.Any(path => !SafeRelativePath(path)))
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Verification session changed paths must be normalized repository-relative paths.");
        var awaiting = session.Stage is VerificationContinuationStage.AwaitingConfirmation or VerificationContinuationStage.AwaitingManualResult;
        if (awaiting != (session.PendingCheckId is not null && session.PendingCheckDefinitionHash is not null))
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Verification session pending action does not match its stage.");
        if (awaiting && (session.NextCheckIndex == session.CheckIds.Count || session.PendingCheckId != session.CheckIds[session.NextCheckIndex]))
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Verification session pending check does not match its cursor.");
        if (awaiting && state.PendingContinuation is not { Kind: ContinuationKind.VerificationGate })
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "A pending verification action requires a verification-gate continuation.");
    }

    private static void ValidateRemainingDependencies(IEnumerable<WorkItemState> workItems)
    {
        var items = workItems.ToDictionary(x => x.Id, StringComparer.Ordinal);
        foreach (var item in workItems.Where(IsRequiredIncomplete))
        foreach (var dependencyId in item.Dependencies)
        {
            var dependency = items[dependencyId];
            if (dependency.Status is WorkItemStatus.Superseded or WorkItemStatus.Cancelled or WorkItemStatus.Failed)
                throw new FactoryStateException("CORRUPT_FACTORY_STATE", $"Required work item {item.Id} depends on {dependencyId}, which can no longer complete.");
        }
    }

    private static bool EquivalentGraph(IReadOnlyList<WorkItemState> left, IReadOnlyList<WorkItemState> right)
    {
        if (left.Count != right.Count) return false;
        var rightById = right.ToDictionary(x => x.Id, StringComparer.Ordinal);
        foreach (var item in left)
        {
            if (!rightById.TryGetValue(item.Id, out var other) || !EquivalentDefinition(item, other)) return false;
        }
        return true;
    }

    private static bool EquivalentDefinition(WorkItemState left, WorkItemState right) =>
        left.Id == right.Id && left.Sequence == right.Sequence && left.Kind == right.Kind && left.Capability == right.Capability &&
        left.DefinitionState == right.DefinitionState && left.ContractPath == right.ContractPath && left.ContractRevision == right.ContractRevision &&
        left.Dependencies.SequenceEqual(right.Dependencies) && left.CoveredWorkItems.SequenceEqual(right.CoveredWorkItems) &&
        left.VerificationCheckIds.SequenceEqual(right.VerificationCheckIds) && DictionaryEqual(left.VerificationExpectations, right.VerificationExpectations) &&
        left.IsFinalReview == right.IsFinalReview && left.ReviewTargetGraphRevision == right.ReviewTargetGraphRevision &&
        IsSuperseded(left) == IsSuperseded(right);

    private static bool EquivalentCompleted(WorkItemState left, WorkItemState right) =>
        EquivalentDefinition(left, right) && left.Status == right.Status && left.AttemptCount == right.AttemptCount &&
        left.CurrentAttemptId == right.CurrentAttemptId && left.LastResultRef == right.LastResultRef && left.LastSemanticOutcome == right.LastSemanticOutcome &&
        left.LastVerificationDecision == right.LastVerificationDecision && left.VerificationEvidenceRefs.SequenceEqual(right.VerificationEvidenceRefs) &&
        left.PriorResultRefs.SequenceEqual(right.PriorResultRefs) && left.ChangedPaths.SequenceEqual(right.ChangedPaths);

    private static bool DictionaryEqual(IReadOnlyDictionary<string, VerificationExpectation> left, IReadOnlyDictionary<string, VerificationExpectation> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private static bool IsSuperseded(WorkItemState item) => item.Status == WorkItemStatus.Superseded;

    private static bool IsRequiredIncomplete(WorkItemState item) =>
        item.Status is not (WorkItemStatus.Completed or WorkItemStatus.Superseded or WorkItemStatus.Cancelled);

    private static bool SafeRelativePath(string path) =>
        !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) && !path.Contains('\\') && !path.Split('/').Contains("..", StringComparer.Ordinal);

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
            visiting.Remove(id);
            visited.Add(id);
            return cyclic;
        }
        if (items.Keys.Any(Visit))
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Work-item dependencies contain a cycle.");
    }
}

public sealed class FactoryStateException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
