using System.Text.Json;
using Idd.Factory.Domain;

namespace Idd.Factory.State;

public sealed class FactoryStateValidator
{
    public void Validate(FactoryState state)
    {
        if (state.SchemaVersion != FactoryState.CurrentSchemaVersion) throw Error($"Unsupported state schema {state.SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(state.RunId) || string.IsNullOrWhiteSpace(state.MethodologyVersion) || string.IsNullOrWhiteSpace(state.RuntimeVersion)) throw Error("Factory identity is incomplete.");
        if (state.Revision < 0 || state.PlanRevision < 0 || state.NextWorkItemNumber < 1) throw Error("State counters cannot be negative or zero.");
        if ((state.Current is null) != (state.CurrentPhase is null)) throw Error("Current and CurrentPhase must be set or cleared together.");

        var all = state.Completed.Select(x => (x.Id, x.Capability, x.ContractPath))
            .Concat(state.Current is null ? [] : [(state.Current.Id, state.Current.Capability, state.Current.ContractPath)])
            .Concat(state.Remaining.Select(x => (x.Id, x.Capability, x.ContractPath))).ToArray();
        if (all.Any(x => string.IsNullOrWhiteSpace(x.Id))) throw Error("Every work item requires an ID.");
        if (all.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != all.Length) throw Error("Work item IDs must be unique across Completed, Current, and Remaining.");
        foreach (var item in all)
        {
            if (string.IsNullOrWhiteSpace(item.Capability) || string.IsNullOrWhiteSpace(item.ContractPath)) throw Error($"Work item {item.Id} has an incomplete contract.");
            _ = FactoryCapabilityCatalog.ResolveWorkItem(item.Capability);
            ValidateContractPath(item.ContractPath, item.Id);
        }
        if (state.CurrentAttemptId is not null && state.Current is null && state.PendingContinuation?.Operation is not (SemanticOperationKind.Planning or SemanticOperationKind.FinalReview))
            throw Error("An active attempt without Current work must be planning or final review.");
        if (state.PendingVerificationSession is { WorkItemId: not null } session && state.Current?.Id != session.WorkItemId) throw Error("Subtask verification must target Current work.");
        if (state.FinalVerificationPlanRevision > state.PlanRevision || state.FinalReview?.ReviewedPlanRevision > state.PlanRevision) throw Error("Final evidence cannot target a future plan revision.");
    }

    public void ValidateMutation(FactoryState previous, FactoryState next)
    {
        Validate(next);
        if (next.Revision != previous.Revision + 1) throw Error("Revision must advance by exactly one.");
        if (next.PlanRevision < previous.PlanRevision || next.NextWorkItemNumber < previous.NextWorkItemNumber) throw Error("Plan and ID counters are monotonic.");
        if (next.Completed.Count < previous.Completed.Count) throw Error("Completed history cannot shrink.");
        for (var index = 0; index < previous.Completed.Count; index++)
            if (!Equivalent(previous.Completed[index], next.Completed[index])) throw Error($"Completed work {previous.Completed[index].Id} is immutable.");
        if (next.Completed.Count > previous.Completed.Count + 1) throw Error("Only Current can be committed to Completed in one transition.");
        if (next.Completed.Count == previous.Completed.Count + 1)
        {
            if (previous.Current is null || next.Completed[^1].Id != previous.Current.Id) throw Error("New completed work must be the previous Current task.");
            if (next.Current is not null) throw Error("Current must be cleared when it is committed to Completed.");
        }
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
