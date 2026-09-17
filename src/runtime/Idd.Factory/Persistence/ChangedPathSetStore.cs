using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Runtime;

namespace Idd.Factory.Persistence;

internal sealed class ChangedPathSetStore(string runDirectory)
{
    private const int SchemaVersion = 1;
    private const int PreviewLimit = 50;

    public async Task MaterializeStateAsync(FactoryState state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.Combine(runDirectory, "changes"));

        for (var index = 0; index < state.Completed.Count; index++)
        {
            var item = state.Completed[index];
            var summary = await WriteAsync(WorkItemReference(item.Id), item.ChangedPaths, cancellationToken);
            state.Completed[index] = item with { Changes = summary };
        }

        if (state.Current is { } current)
        {
            current.Changes = await WriteAsync(WorkItemReference(current.Id), current.ChangedPaths, cancellationToken);
            await MaterializeTechnicalFailuresAsync(current, cancellationToken);
        }

        foreach (var item in state.Remaining)
        {
            if (item.Changes is not null || item.ChangedPaths.Count != 0)
                item.Changes = await WriteAsync(WorkItemReference(item.Id), item.ChangedPaths, cancellationToken);
            await MaterializeTechnicalFailuresAsync(item, cancellationToken);
        }

        state.RunChanges = await WriteAsync("changes/run.json", state.FactoryRunChangedPaths, cancellationToken);

        if (state.PendingVerificationSession is { } session)
        {
            var summary = session.Context == "final" ? state.RunChanges : state.Current?.Changes;
            if (summary is null)
                throw Corrupt("Pending verification has no authoritative change-set artifact.");
            state.PendingVerificationSession = session with { Changes = summary };
        }
    }

    public async Task HydrateStateAsync(FactoryState state, CancellationToken cancellationToken)
    {
        var run = await ReadOrRebuildAggregateAsync("changes/run.json", state.RunChanges, null, cancellationToken);
        state.RunChanges = run.Summary;
        Replace(state.FactoryRunChangedPaths, run.Paths);

        for (var index = 0; index < state.Completed.Count; index++)
        {
            var item = state.Completed[index];
            var aggregate = await ReadOrRebuildAggregateAsync(
                WorkItemReference(item.Id), item.Changes, item.Id, cancellationToken);
            Replace(item.ChangedPaths, aggregate.Paths);
            state.Completed[index] = item with { Changes = aggregate.Summary };
        }

        if (state.Current is { } current)
        {
            var aggregate = await ReadOrRebuildAggregateAsync(
                WorkItemReference(current.Id), current.Changes, current.Id, cancellationToken);
            current.Changes = aggregate.Summary;
            Replace(current.ChangedPaths, aggregate.Paths);
            await HydrateTechnicalFailuresAsync(current, cancellationToken);
        }

        foreach (var item in state.Remaining)
        {
            if (item.Changes is not null)
            {
                var expected = WorkItemReference(item.Id);
                if (!string.Equals(item.Changes.Reference, expected, StringComparison.Ordinal))
                    throw Corrupt($"Work item {item.Id} has unexpected change-set reference '{item.Changes.Reference}'.");
                var paths = await ReadAndValidateAsync(item.Changes, cancellationToken);
                Replace(item.ChangedPaths, paths);
            }
            await HydrateTechnicalFailuresAsync(item, cancellationToken);
        }

        if (state.PendingVerificationSession is { } session)
        {
            var authoritative = session.Context == "final" ? state.RunChanges : state.Current?.Changes;
            if (authoritative is null)
                throw Corrupt("Pending verification has no authoritative change-set summary.");
            if (session.Changes is not null
                && !string.Equals(session.Changes.Reference, authoritative.Reference, StringComparison.Ordinal))
            {
                throw Corrupt("Pending verification references a different change-set artifact than its verification scope.");
            }
            var paths = await ReadAndValidateAsync(authoritative, cancellationToken);
            Replace(session.ChangedPaths, paths);
            state.PendingVerificationSession = session with { Changes = authoritative };
        }
    }

    public async Task ValidateStateReferencesAsync(FactoryState state, CancellationToken cancellationToken)
    {
        if (state.RunChanges is null)
            throw Corrupt("Run change-set summary is missing.");
        if (!string.Equals(state.RunChanges.Reference, "changes/run.json", StringComparison.Ordinal))
            throw Corrupt("Run change-set summary has an unexpected reference.");
        _ = await ReadAndValidateAsync(state.RunChanges, cancellationToken);

        foreach (var item in state.Completed)
        {
            if (item.Changes is null)
                throw Corrupt($"Completed work item {item.Id} has no change-set summary.");
            if (!string.Equals(item.Changes.Reference, WorkItemReference(item.Id), StringComparison.Ordinal))
                throw Corrupt($"Completed work item {item.Id} has an unexpected change-set reference.");
            _ = await ReadAndValidateAsync(item.Changes, cancellationToken);
        }
    }

    public async Task<ChangeSetSummary> WriteAsync(
        string reference,
        IEnumerable<string> paths,
        CancellationToken cancellationToken)
    {
        reference = ValidateReference(reference);
        var changedPaths = WorkspacePathPolicy.NormalizeChangedPaths(paths).ToList();
        await WriteArtifactAtomicallyAsync(reference, changedPaths, cancellationToken);
        return Summary(reference, changedPaths);
    }

    public async Task<IReadOnlyList<string>> ReadAndValidateAsync(
        ChangeSetSummary summary,
        CancellationToken cancellationToken)
    {
        var reference = ValidateReference(summary.Reference);
        var paths = await ReadArtifactAsync(reference, cancellationToken, "change-set");
        if (summary.Count != paths.Count)
            throw Corrupt($"Change-set summary count for '{reference}' does not match its artifact.");
        if (!paths.Take(PreviewLimit).SequenceEqual(summary.Preview, StringComparer.Ordinal))
            throw Corrupt($"Change-set summary preview for '{reference}' does not match its artifact.");
        return paths;
    }

    private async Task<AggregateRead> ReadOrRebuildAggregateAsync(
        string expectedReference,
        ChangeSetSummary? persistedSummary,
        string? workItemId,
        CancellationToken cancellationToken)
    {
        if (persistedSummary is not null
            && !string.Equals(persistedSummary.Reference, expectedReference, StringComparison.Ordinal))
        {
            throw Corrupt($"Change-set summary references '{persistedSummary.Reference}' instead of '{expectedReference}'.");
        }

        if (persistedSummary is not null)
        {
            try
            {
                var paths = await ReadAndValidateAsync(persistedSummary, cancellationToken);
                return new(paths, persistedSummary);
            }
            catch (FactoryStateException)
            {
                // Aggregates are materialized views. A crash may leave the aggregate newer than
                // state.json or temporarily absent; immutable attempt artifacts are authoritative.
            }
        }

        var rebuilt = await RebuildFromAttemptsAsync(workItemId, cancellationToken);
        var summary = await WriteAsync(expectedReference, rebuilt, cancellationToken);
        return new(rebuilt, summary);
    }

    private async Task MaterializeTechnicalFailuresAsync(PlannedWorkItem item, CancellationToken cancellationToken)
    {
        for (var index = 0; index < item.PriorTechnicalFailures.Count; index++)
        {
            var failure = item.PriorTechnicalFailures[index];
            var reference = AttemptReference(failure.FailedAttemptId);
            IReadOnlyList<string> paths;
            if (File.Exists(Resolve(reference)))
            {
                paths = await ReadArtifactAsync(reference, cancellationToken, "attempt change");
            }
            else
            {
                paths = WorkspacePathPolicy.NormalizeChangedPaths(failure.ChangedPaths);
                await WriteArtifactAtomicallyAsync(reference, paths, cancellationToken);
            }
            item.PriorTechnicalFailures[index] = failure with { Changes = Summary(reference, paths) };
        }
    }

    private async Task HydrateTechnicalFailuresAsync(PlannedWorkItem item, CancellationToken cancellationToken)
    {
        for (var index = 0; index < item.PriorTechnicalFailures.Count; index++)
        {
            var failure = item.PriorTechnicalFailures[index];
            var reference = AttemptReference(failure.FailedAttemptId);
            if (failure.Changes is not null
                && !string.Equals(failure.Changes.Reference, reference, StringComparison.Ordinal))
            {
                throw Corrupt($"Technical failure {failure.FailedAttemptId} references the wrong attempt change artifact.");
            }

            var paths = await ReadArtifactAsync(reference, cancellationToken, "attempt change");
            var summary = Summary(reference, paths);
            if (failure.Changes is not null
                && (failure.Changes.Count != summary.Count
                    || !failure.Changes.Preview.SequenceEqual(summary.Preview, StringComparer.Ordinal)))
            {
                throw Corrupt($"Technical failure {failure.FailedAttemptId} change summary does not match its immutable attempt artifact.");
            }
            Replace(failure.ChangedPaths, paths);
            item.PriorTechnicalFailures[index] = failure with { Changes = summary };
        }
    }

    private async Task<IReadOnlyList<string>> RebuildFromAttemptsAsync(
        string? workItemId,
        CancellationToken cancellationToken)
    {
        var attemptsRoot = Path.Combine(runDirectory, "attempts");
        if (!Directory.Exists(attemptsRoot))
            return [];

        var union = new HashSet<string>(StringComparer.Ordinal);
        foreach (var directory in Directory.EnumerateDirectories(attemptsRoot).OrderBy(path => path, StringComparer.Ordinal))
        {
            var invocationPath = Path.Combine(directory, "invocation.json");
            var changesPath = Path.Combine(directory, "workspace-changes.json");
            if (!File.Exists(invocationPath) || !File.Exists(changesPath))
                continue;

            AgentInvocation? invocation;
            try
            {
                invocation = JsonSerializer.Deserialize<AgentInvocation>(
                    await File.ReadAllTextAsync(invocationPath, cancellationToken),
                    FactoryJson.Options);
            }
            catch (JsonException exception)
            {
                throw Corrupt($"Cannot rebuild change aggregates from '{invocationPath}': {exception.Message}");
            }
            if (invocation is null || invocation.ExecutionProfile != AgentExecutionProfile.WorkspaceWrite)
                continue;
            if (workItemId is not null && invocation.WorkItemId != workItemId)
                continue;

            foreach (var path in await ReadArtifactAsync(
                         AttemptReference(invocation.AttemptId), cancellationToken, "attempt change"))
            {
                union.Add(path);
            }
        }

        return union.OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }

    private async Task<IReadOnlyList<string>> ReadArtifactAsync(
        string reference,
        CancellationToken cancellationToken,
        string label)
    {
        reference = ValidateReference(reference);
        var path = Resolve(reference);
        if (!File.Exists(path))
            throw Corrupt($"Authoritative {label} artifact '{reference}' is missing.");

        ChangedPathSetArtifact artifact;
        try
        {
            artifact = JsonSerializer.Deserialize<ChangedPathSetArtifact>(
                           await File.ReadAllTextAsync(path, cancellationToken), FactoryJson.Options)
                       ?? throw Corrupt($"Authoritative {label} artifact '{reference}' is empty.");
        }
        catch (FactoryStateException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            throw Corrupt($"Cannot read authoritative {label} artifact '{reference}': {exception.Message}");
        }

        if (artifact.SchemaVersion != SchemaVersion)
            throw Corrupt($"Authoritative {label} artifact '{reference}' has unsupported schema {artifact.SchemaVersion}.");
        var normalized = WorkspacePathPolicy.NormalizeChangedPaths(artifact.ChangedPaths);
        if (!normalized.SequenceEqual(artifact.ChangedPaths, StringComparer.Ordinal))
            throw Corrupt($"Authoritative {label} artifact '{reference}' is not canonical, distinct, and sorted.");
        return artifact.ChangedPaths;
    }

    private async Task WriteArtifactAtomicallyAsync(
        string reference,
        IReadOnlyList<string> changedPaths,
        CancellationToken cancellationToken)
    {
        var path = Resolve(reference);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(new ChangedPathSetArtifact(SchemaVersion, changedPaths.ToList()), FactoryJson.Options),
                cancellationToken);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static ChangeSetSummary Summary(string reference, IReadOnlyList<string> paths) => new()
    {
        Reference = reference,
        Count = paths.Count,
        Preview = paths.Take(PreviewLimit).ToList()
    };

    private static void Replace(List<string> target, IEnumerable<string> source)
    {
        target.Clear();
        target.AddRange(source);
    }

    private static string WorkItemReference(string id) => $"changes/{id}.json";
    private static string AttemptReference(string attemptId) => $"attempts/{attemptId}/workspace-changes.json";

    private string Resolve(string reference)
    {
        var canonical = ValidateReference(reference);
        var full = Path.GetFullPath(Path.Combine(runDirectory, canonical.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(runDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(root, comparison))
            throw Corrupt($"Change-set reference '{reference}' escapes the Factory run directory.");
        return full;
    }

    private static string ValidateReference(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference) || Path.IsPathRooted(reference) || reference.Contains('\\'))
            throw Corrupt($"Invalid change-set reference '{reference}'.");
        var canonical = WorkspacePathPolicy.Canonicalize(reference);
        if (!string.Equals(reference, canonical, StringComparison.Ordinal))
            throw Corrupt($"Invalid change-set reference '{reference}'.");
        return canonical;
    }

    private static FactoryStateException Corrupt(string message) => new("CORRUPT_FACTORY_STATE", message);

    private sealed record ChangedPathSetArtifact(int SchemaVersion, List<string> ChangedPaths);
    private sealed record AggregateRead(IReadOnlyList<string> Paths, ChangeSetSummary Summary);
}
