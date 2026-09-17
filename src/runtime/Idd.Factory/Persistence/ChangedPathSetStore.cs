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
            var summary = await WriteAsync(
                WorkItemReference(item.Id),
                item.ChangedPaths,
                cancellationToken);
            state.Completed[index] = item with { Changes = summary };
        }

        if (state.Current is { } current)
        {
            current.Changes = await WriteAsync(
                WorkItemReference(current.Id),
                current.ChangedPaths,
                cancellationToken);
            await MaterializeTechnicalFailuresAsync(current, cancellationToken);
        }

        foreach (var item in state.Remaining)
        {
            if (item.Changes is not null || item.ChangedPaths.Count != 0)
            {
                item.Changes = await WriteAsync(
                    WorkItemReference(item.Id),
                    item.ChangedPaths,
                    cancellationToken);
            }
            await MaterializeTechnicalFailuresAsync(item, cancellationToken);
        }

        foreach (var item in state.Completed)
        {
            // Completed work does not persist technical-failure history, so there is nothing
            // additional to materialize here.
        }

        state.RunChanges = await WriteAsync(
            "changes/run.json",
            state.FactoryRunChangedPaths,
            cancellationToken);

        if (state.PendingVerificationSession is { } session)
        {
            var summary = session.Context == "final"
                ? state.RunChanges
                : state.Current?.Changes;
            if (summary is null)
            {
                throw new FactoryStateException(
                    "CORRUPT_FACTORY_STATE",
                    "Pending verification has no authoritative change-set artifact.");
            }

            state.PendingVerificationSession = session with { Changes = summary };
        }
    }

    public async Task HydrateStateAsync(FactoryState state, CancellationToken cancellationToken)
    {
        state.FactoryRunChangedPaths.Clear();
        state.FactoryRunChangedPaths.AddRange(
            await ReadOrRebuildRunAsync(state, cancellationToken));

        for (var index = 0; index < state.Completed.Count; index++)
        {
            var item = state.Completed[index];
            var paths = await ReadOrRebuildWorkItemAsync(
                item.Id,
                item.Changes,
                cancellationToken);
            item.ChangedPaths.Clear();
            item.ChangedPaths.AddRange(paths);
            state.Completed[index] = item;
        }

        if (state.Current is { } current)
        {
            var paths = await ReadOrRebuildWorkItemAsync(
                current.Id,
                current.Changes,
                cancellationToken);
            current.ChangedPaths.Clear();
            current.ChangedPaths.AddRange(paths);
            await HydrateTechnicalFailuresAsync(current, cancellationToken);
        }

        foreach (var item in state.Remaining)
        {
            if (item.Changes is not null)
            {
                var paths = await ReadAndValidateAsync(item.Changes, cancellationToken);
                item.ChangedPaths.Clear();
                item.ChangedPaths.AddRange(paths);
            }
            await HydrateTechnicalFailuresAsync(item, cancellationToken);
        }

        if (state.PendingVerificationSession is { } session)
        {
            var summary = session.Changes
                          ?? (session.Context == "final"
                              ? state.RunChanges
                              : state.Current?.Changes)
                          ?? throw new FactoryStateException(
                              "CORRUPT_FACTORY_STATE",
                              "Pending verification has no authoritative change-set summary.");
            var paths = await ReadAndValidateAsync(summary, cancellationToken);
            session.ChangedPaths.Clear();
            session.ChangedPaths.AddRange(paths);
            state.PendingVerificationSession = session with { Changes = summary };
        }
    }

    public async Task ValidateStateReferencesAsync(
        FactoryState state,
        CancellationToken cancellationToken)
    {
        if (state.RunChanges is null)
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                "Run change-set summary is missing.");
        }
        _ = await ReadAndValidateAsync(state.RunChanges, cancellationToken);

        foreach (var item in state.Completed)
        {
            if (item.Changes is null)
            {
                throw new FactoryStateException(
                    "CORRUPT_FACTORY_STATE",
                    $"Completed work item {item.Id} has no change-set summary.");
            }
            _ = await ReadAndValidateAsync(item.Changes, cancellationToken);
        }

        if (state.PendingVerificationSession?.Changes is { } pending)
            _ = await ReadAndValidateAsync(pending, cancellationToken);
    }

    public async Task<ChangeSetSummary> WriteAsync(
        string reference,
        IEnumerable<string> paths,
        CancellationToken cancellationToken)
    {
        reference = ValidateReference(reference);
        var changedPaths = WorkspacePathPolicy.NormalizeChangedPaths(paths).ToList();
        var artifact = new ChangedPathSetArtifact(SchemaVersion, changedPaths);
        var path = Resolve(reference);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(
            temporary,
            JsonSerializer.Serialize(artifact, FactoryJson.Options),
            cancellationToken);
        File.Move(temporary, path, true);
        return Summary(reference, changedPaths);
    }

    public async Task<IReadOnlyList<string>> ReadAndValidateAsync(
        ChangeSetSummary summary,
        CancellationToken cancellationToken)
    {
        var reference = ValidateReference(summary.Reference);
        var path = Resolve(reference);
        if (!File.Exists(path))
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                $"Authoritative change-set artifact '{reference}' is missing.");
        }

        ChangedPathSetArtifact artifact;
        try
        {
            artifact = JsonSerializer.Deserialize<ChangedPathSetArtifact>(
                           await File.ReadAllTextAsync(path, cancellationToken),
                           FactoryJson.Options)
                       ?? throw new FactoryStateException(
                           "CORRUPT_FACTORY_STATE",
                           $"Authoritative change-set artifact '{reference}' is empty.");
        }
        catch (FactoryStateException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                $"Cannot read authoritative change-set artifact '{reference}': {exception.Message}");
        }

        if (artifact.SchemaVersion != SchemaVersion)
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                $"Authoritative change-set artifact '{reference}' has unsupported schema {artifact.SchemaVersion}.");
        }

        var normalized = WorkspacePathPolicy.NormalizeChangedPaths(artifact.ChangedPaths);
        if (!normalized.SequenceEqual(artifact.ChangedPaths, StringComparer.Ordinal))
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                $"Authoritative change-set artifact '{reference}' is not canonical, distinct, and sorted.");
        }
        if (summary.Count != artifact.ChangedPaths.Count)
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                $"Change-set summary count for '{reference}' does not match its artifact.");
        }
        var expectedPreview = artifact.ChangedPaths.Take(PreviewLimit).ToArray();
        if (!expectedPreview.SequenceEqual(summary.Preview, StringComparer.Ordinal))
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                $"Change-set summary preview for '{reference}' does not match its artifact.");
        }

        return artifact.ChangedPaths;
    }

    private async Task MaterializeTechnicalFailuresAsync(
        PlannedWorkItem item,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < item.PriorTechnicalFailures.Count; index++)
        {
            var failure = item.PriorTechnicalFailures[index];
            var reference = AttemptReference(failure.FailedAttemptId);
            ChangeSetSummary summary;
            if (File.Exists(Resolve(reference)))
            {
                var paths = await ReadAttemptArtifactAsync(reference, cancellationToken);
                summary = Summary(reference, paths);
            }
            else
            {
                summary = await WriteAsync(reference, failure.ChangedPaths, cancellationToken);
            }
            item.PriorTechnicalFailures[index] = failure with { Changes = summary };
        }
    }

    private async Task HydrateTechnicalFailuresAsync(
        PlannedWorkItem item,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < item.PriorTechnicalFailures.Count; index++)
        {
            var failure = item.PriorTechnicalFailures[index];
            var summary = failure.Changes;
            if (summary is null)
            {
                var reference = AttemptReference(failure.FailedAttemptId);
                var paths = await ReadAttemptArtifactAsync(reference, cancellationToken);
                summary = Summary(reference, paths);
            }
            var full = await ReadAndValidateAsync(summary, cancellationToken);
            failure.ChangedPaths.Clear();
            failure.ChangedPaths.AddRange(full);
            item.PriorTechnicalFailures[index] = failure with { Changes = summary };
        }
    }

    private async Task<IReadOnlyList<string>> ReadOrRebuildWorkItemAsync(
        string workItemId,
        ChangeSetSummary? summary,
        CancellationToken cancellationToken)
    {
        if (summary is not null)
        {
            try
            {
                return await ReadAndValidateAsync(summary, cancellationToken);
            }
            catch (FactoryStateException exception) when (
                exception.Message.Contains("is missing", StringComparison.Ordinal))
            {
            }
        }

        var rebuilt = await RebuildFromAttemptsAsync(workItemId, cancellationToken);
        return (await WriteAsync(
            WorkItemReference(workItemId),
            rebuilt,
            cancellationToken)).Preview.Count >= 0
            ? rebuilt
            : rebuilt;
    }

    private async Task<IReadOnlyList<string>> ReadOrRebuildRunAsync(
        FactoryState state,
        CancellationToken cancellationToken)
    {
        if (state.RunChanges is not null)
        {
            try
            {
                return await ReadAndValidateAsync(state.RunChanges, cancellationToken);
            }
            catch (FactoryStateException exception) when (
                exception.Message.Contains("is missing", StringComparison.Ordinal))
            {
            }
        }

        var rebuilt = await RebuildFromAttemptsAsync(null, cancellationToken);
        state.RunChanges = await WriteAsync("changes/run.json", rebuilt, cancellationToken);
        return rebuilt;
    }

    private async Task<IReadOnlyList<string>> RebuildFromAttemptsAsync(
        string? workItemId,
        CancellationToken cancellationToken)
    {
        var attemptsRoot = Path.Combine(runDirectory, "attempts");
        if (!Directory.Exists(attemptsRoot))
            return [];

        var union = new HashSet<string>(StringComparer.Ordinal);
        foreach (var directory in Directory.EnumerateDirectories(attemptsRoot)
                     .OrderBy(path => path, StringComparer.Ordinal))
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
                throw new FactoryStateException(
                    "CORRUPT_FACTORY_STATE",
                    $"Cannot rebuild change aggregates from '{invocationPath}': {exception.Message}");
            }
            if (invocation is null || invocation.ExecutionProfile != AgentExecutionProfile.WorkspaceWrite)
                continue;
            if (workItemId is not null && invocation.WorkItemId != workItemId)
                continue;

            var reference = AttemptReference(invocation.AttemptId);
            foreach (var path in await ReadAttemptArtifactAsync(reference, cancellationToken))
                union.Add(path);
        }

        return union.OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }

    private async Task<IReadOnlyList<string>> ReadAttemptArtifactAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        reference = ValidateReference(reference);
        var path = Resolve(reference);
        if (!File.Exists(path))
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                $"Authoritative attempt change artifact '{reference}' is missing.");
        }

        ChangedPathSetArtifact artifact;
        try
        {
            artifact = JsonSerializer.Deserialize<ChangedPathSetArtifact>(
                           await File.ReadAllTextAsync(path, cancellationToken),
                           FactoryJson.Options)
                       ?? throw new FactoryStateException(
                           "CORRUPT_FACTORY_STATE",
                           $"Authoritative attempt change artifact '{reference}' is empty.");
        }
        catch (FactoryStateException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                $"Cannot read authoritative attempt change artifact '{reference}': {exception.Message}");
        }

        if (artifact.SchemaVersion != SchemaVersion)
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                $"Authoritative attempt change artifact '{reference}' has unsupported schema {artifact.SchemaVersion}.");
        }
        var normalized = WorkspacePathPolicy.NormalizeChangedPaths(artifact.ChangedPaths);
        if (!normalized.SequenceEqual(artifact.ChangedPaths, StringComparer.Ordinal))
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                $"Authoritative attempt change artifact '{reference}' is not canonical, distinct, and sorted.");
        }
        return artifact.ChangedPaths;
    }

    private static ChangeSetSummary Summary(string reference, IReadOnlyList<string> paths) =>
        new()
        {
            Reference = reference,
            Count = paths.Count,
            Preview = paths.Take(PreviewLimit).ToList()
        };

    private static string WorkItemReference(string id) => $"changes/{id}.json";
    private static string AttemptReference(string attemptId) => $"attempts/{attemptId}/workspace-changes.json";

    private string Resolve(string reference)
    {
        var canonical = ValidateReference(reference);
        var full = Path.GetFullPath(Path.Combine(
            runDirectory,
            canonical.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(runDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!full.StartsWith(root, comparison))
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                $"Change-set reference '{reference}' escapes the Factory run directory.");
        }
        return full;
    }

    private static string ValidateReference(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)
            || Path.IsPathRooted(reference)
            || reference.Contains('\\'))
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                $"Invalid change-set reference '{reference}'.");
        }

        var canonical = WorkspacePathPolicy.Canonicalize(reference);
        if (!string.Equals(reference, canonical, StringComparison.Ordinal))
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                $"Invalid change-set reference '{reference}'.");
        }
        return canonical;
    }

    private sealed record ChangedPathSetArtifact(int SchemaVersion, List<string> ChangedPaths);
}
