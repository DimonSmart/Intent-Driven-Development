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
            [WorkItemStatus.Running] = [WorkItemStatus.AwaitingVerification, WorkItemStatus.Ready, WorkItemStatus.Blocked, WorkItemStatus.Failed, WorkItemStatus.Cancelled],
            [WorkItemStatus.AwaitingVerification] = [WorkItemStatus.Completed, WorkItemStatus.Ready, WorkItemStatus.Blocked, WorkItemStatus.Failed, WorkItemStatus.Cancelled],
            [WorkItemStatus.Blocked] = [WorkItemStatus.Ready, WorkItemStatus.Cancelled],
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
            if (newItem is not null && oldItem.Status != newItem.Status && !AllowedTransitions[oldItem.Status].Contains(newItem.Status))
                throw new FactoryStateException("INVALID_STATE_TRANSITION", $"{oldItem.Id}: {oldItem.Status} -> {newItem.Status} is not allowed.");
        }
    }

    private static bool EquivalentCompleted(WorkItemState left, WorkItemState right) =>
        left == right || (left.Id == right.Id && left.Sequence == right.Sequence && left.Kind == right.Kind &&
        left.Status == right.Status && left.ContractPath == right.ContractPath &&
        left.Dependencies.SequenceEqual(right.Dependencies) && left.CoveredWorkItems.SequenceEqual(right.CoveredWorkItems) &&
        left.AttemptCount == right.AttemptCount && left.VerificationFixAttemptCount == right.VerificationFixAttemptCount && left.LastResultRef == right.LastResultRef &&
        left.VerificationCheckIds.SequenceEqual(right.VerificationCheckIds) &&
        left.VerificationEvidenceRefs.SequenceEqual(right.VerificationEvidenceRefs));
}

public sealed class FactoryStateException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
