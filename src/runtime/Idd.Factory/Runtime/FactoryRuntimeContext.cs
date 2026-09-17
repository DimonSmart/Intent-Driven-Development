using System.Text.Json;
using Idd.Factory.Configuration;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.Telemetry;

namespace Idd.Factory.Runtime;

internal sealed class FactoryRuntimeContext(
    string workspace,
    FactoryConfiguration configuration,
    IFactoryStateStore stateStore,
    FactoryEventWriter events,
    IClock clock)
{
    private readonly FactoryStateValidator stateValidator = new();

    public string Workspace { get; } = workspace;
    public string CurrentDirectory { get; } = Path.Combine(workspace, ".idd", "factory", "current");
    public FactoryConfiguration Configuration { get; } = configuration;
    public IFactoryStateStore StateStore { get; } = stateStore;
    public FactoryEventWriter Events { get; } = events;
    public IClock Clock { get; } = clock;

    public Task<FactoryState?> LoadAsync(CancellationToken cancellationToken) =>
        StateStore.LoadAsync(cancellationToken);

    public Task CreateAsync(FactoryState state, CancellationToken cancellationToken) =>
        StateStore.CreateAsync(state, cancellationToken);

    public Task SaveAsync(FactoryState state, CancellationToken cancellationToken) =>
        StateStore.SaveAsync(state, state.Revision, cancellationToken);

    public void ValidateRuntimeState(FactoryState state)
    {
        stateValidator.Validate(state);
        if (state.Completed.Count + state.Remaining.Count + (state.Current is null ? 0 : 1) > Configuration.Limits.MaxWorkItems)
            throw new AgentProtocolException("WORK_EXPANSION_BUDGET_EXHAUSTED", "Factory work exceeds the configured maximum.");
    }

    public FactoryState CloneState(FactoryState state)
    {
        var clone = JsonSerializer.Deserialize<FactoryState>(
            JsonSerializer.Serialize(state, FactoryJson.Options),
            FactoryJson.Options)!;
        CopyTransientChangeSets(state, clone);
        return clone;
    }

    public static void ApplyCandidate(FactoryState state, FactoryState candidate)
    {
        state.PlanRevision = candidate.PlanRevision;
        state.NextWorkItemNumber = candidate.NextWorkItemNumber;
        state.RunStatus = candidate.RunStatus;
        state.Completed.Clear();
        state.Completed.AddRange(candidate.Completed);
        state.Current = candidate.Current;
        state.CurrentPhase = candidate.CurrentPhase;
        state.Remaining.Clear();
        state.Remaining.AddRange(candidate.Remaining);
        state.CurrentAttemptId = candidate.CurrentAttemptId;
        state.AttemptSequence = candidate.AttemptSequence;
        state.PlanningCycleCount = candidate.PlanningCycleCount;
        state.PlannedThroughCompletedCount = candidate.PlannedThroughCompletedCount;
        state.RepositoryFallbackBaselineAccepted = candidate.RepositoryFallbackBaselineAccepted;
        state.FinalVerificationPassed = candidate.FinalVerificationPassed;
        state.FinalVerificationPlanRevision = candidate.FinalVerificationPlanRevision;
        state.Blocker = candidate.Blocker;
        state.PendingContinuation = candidate.PendingContinuation;
        state.PendingVerificationSession = candidate.PendingVerificationSession;
        state.VerificationEvidenceRefs.Clear();
        state.VerificationEvidenceRefs.AddRange(candidate.VerificationEvidenceRefs);
        state.RunChanges = candidate.RunChanges;
        state.FactoryRunChangedPaths.Clear();
        state.FactoryRunChangedPaths.AddRange(candidate.FactoryRunChangedPaths);
    }

    public static void InvalidateFinalEvidence(FactoryState state)
    {
        state.FinalVerificationPassed = false;
        state.FinalVerificationPlanRevision = null;
    }

    public static async Task WriteRuntimeArtifactAtomicallyAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, content, cancellationToken);
        File.Move(temporary, path, true);
    }

    private static void CopyTransientChangeSets(FactoryState source, FactoryState target)
    {
        target.FactoryRunChangedPaths.Clear();
        target.FactoryRunChangedPaths.AddRange(source.FactoryRunChangedPaths);

        var sourceCompleted = source.Completed.ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var item in target.Completed)
        {
            if (sourceCompleted.TryGetValue(item.Id, out var original))
                CopyPaths(original.ChangedPaths, item.ChangedPaths);
        }

        if (source.Current is { } sourceCurrent && target.Current is { } targetCurrent)
        {
            CopyPaths(sourceCurrent.ChangedPaths, targetCurrent.ChangedPaths);
            CopyFailurePaths(sourceCurrent, targetCurrent);
        }

        var sourceRemaining = source.Remaining.ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var item in target.Remaining)
        {
            if (!sourceRemaining.TryGetValue(item.Id, out var original))
                continue;
            CopyPaths(original.ChangedPaths, item.ChangedPaths);
            CopyFailurePaths(original, item);
        }

        if (source.PendingVerificationSession is { } sourceSession
            && target.PendingVerificationSession is { } targetSession)
        {
            CopyPaths(sourceSession.ChangedPaths, targetSession.ChangedPaths);
        }
    }

    private static void CopyFailurePaths(PlannedWorkItem source, PlannedWorkItem target)
    {
        var failures = source.PriorTechnicalFailures.ToDictionary(
            failure => failure.FailedAttemptId,
            StringComparer.Ordinal);
        foreach (var targetFailure in target.PriorTechnicalFailures)
        {
            if (failures.TryGetValue(targetFailure.FailedAttemptId, out var sourceFailure))
                CopyPaths(sourceFailure.ChangedPaths, targetFailure.ChangedPaths);
        }
    }

    private static void CopyPaths(IReadOnlyCollection<string> source, List<string> target)
    {
        target.Clear();
        target.AddRange(source);
    }
}
