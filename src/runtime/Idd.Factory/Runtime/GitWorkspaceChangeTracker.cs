using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Processes;

namespace Idd.Factory.Runtime;

internal sealed record WorkspaceBaselineStats(
    int BaselineCandidateCount,
    string? BaselineHead,
    string? BaselineTree,
    long DurationMs);

internal sealed record WorkspaceTrackingResult(
    IReadOnlyList<string> ChangedPaths,
    int BaselineCandidateCount,
    int CurrentStatusCandidateCount,
    int TreeCandidateCount,
    int CandidateCount,
    long DurationMs,
    bool WasMaterialized);

internal sealed class GitWorkspaceChangeTracker(
    string workspace,
    IProcessExecutor? executor = null)
{
    private const int BaselineSchemaVersion = 2;
    private const int ChangesSchemaVersion = 1;
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(30);
    private readonly IProcessExecutor processExecutor = executor ?? ProcessExecutor.Shared;

    public async Task<WorkspaceBaselineStats> PersistBaselineAsync(
        string attemptDirectory,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var path = Path.Combine(attemptDirectory, "workspace-before.json");
        if (File.Exists(path))
        {
            var existing = await ReadBaselineAsync(path, cancellationToken);
            return new(
                existing.BaselineEntries.Count,
                existing.BaselineHead,
                existing.BaselineTree,
                stopwatch.ElapsedMilliseconds);
        }

        await EnsureRepositoryAsync(cancellationToken);
        var baselineHead = await TryResolveAsync("HEAD", cancellationToken);
        var baselineTree = await TryResolveAsync("HEAD^{tree}", cancellationToken);
        var statusPaths = await ReadStatusPathsAsync(cancellationToken);
        var entries = new List<WorkspaceBaselineEntry>(statusPaths.Count);
        foreach (var candidate in statusPaths)
        {
            var pathState = await SnapshotFilesystemAsync(candidate.Path, cancellationToken);
            entries.Add(new(
                candidate.Path,
                candidate.IsUntracked ? "untracked" : "tracked-dirty",
                pathState.Exists,
                pathState.Hash));
        }

        var baseline = new WorkspaceBaselineArtifact(
            BaselineSchemaVersion,
            baselineHead,
            baselineTree,
            entries.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToList());
        await WriteJsonAtomicallyAsync(path, baseline, cancellationToken);
        return new(
            entries.Count,
            baselineHead,
            baselineTree,
            stopwatch.ElapsedMilliseconds);
    }

    public async Task<WorkspaceTrackingResult> MaterializeChangesAsync(
        string attemptDirectory,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var changesPath = Path.Combine(attemptDirectory, "workspace-changes.json");
        if (File.Exists(changesPath))
        {
            var existing = await ReadChangesAsync(changesPath, cancellationToken);
            return new(
                existing.ChangedPaths,
                0,
                0,
                0,
                existing.ChangedPaths.Count,
                stopwatch.ElapsedMilliseconds,
                false);
        }

        var baselinePath = Path.Combine(attemptDirectory, "workspace-before.json");
        if (!File.Exists(baselinePath))
        {
            throw TrackingFailure(
                "Persisted workspace baseline is missing; executor changes cannot be attributed safely.");
        }

        var baseline = await ReadBaselineAsync(baselinePath, cancellationToken);
        await EnsureRepositoryAsync(cancellationToken);
        var currentStatus = await ReadStatusPathsAsync(cancellationToken);
        var currentTree = await TryResolveAsync("HEAD^{tree}", cancellationToken);
        var treePaths = await ReadTreeDiffPathsAsync(
            baseline.BaselineTree,
            currentTree,
            cancellationToken);

        var baselineByPath = baseline.BaselineEntries.ToDictionary(
            entry => entry.Path,
            StringComparer.Ordinal);
        var candidates = WorkspacePathPolicy.NormalizeChangedPaths(
            baselineByPath.Keys
                .Concat(currentStatus.Select(candidate => candidate.Path))
                .Concat(treePaths));

        var changed = new List<string>();
        foreach (var path in candidates)
        {
            var differs = baselineByPath.TryGetValue(path, out var baselineEntry)
                ? !await MatchesPersistedBaselineAsync(baselineEntry, cancellationToken)
                : !await MatchesCleanTrackedBaselineAsync(
                    baseline.BaselineTree,
                    path,
                    cancellationToken);
            if (differs)
                changed.Add(path);
        }

        var normalizedChanges = WorkspacePathPolicy.NormalizeChangedPaths(changed).ToList();
        await WriteJsonAtomicallyAsync(
            changesPath,
            new WorkspaceChangesArtifact(ChangesSchemaVersion, normalizedChanges),
            cancellationToken);
        return new(
            normalizedChanges,
            baseline.BaselineEntries.Count,
            currentStatus.Count,
            treePaths.Count,
            candidates.Count,
            stopwatch.ElapsedMilliseconds,
            true);
    }

    public async Task<IReadOnlyList<string>> ReadMaterializedChangesAsync(
        string attemptDirectory,
        CancellationToken cancellationToken) =>
        (await ReadChangesAsync(
            Path.Combine(attemptDirectory, "workspace-changes.json"),
            cancellationToken)).ChangedPaths;

    private async Task<bool> MatchesPersistedBaselineAsync(
        WorkspaceBaselineEntry baseline,
        CancellationToken cancellationToken)
    {
        var current = await SnapshotFilesystemAsync(baseline.Path, cancellationToken);
        return current.Exists == baseline.Exists
               && string.Equals(current.Hash, baseline.Hash, StringComparison.Ordinal);
    }

    private async Task<bool> MatchesCleanTrackedBaselineAsync(
        string? baselineTree,
        string path,
        CancellationToken cancellationToken)
    {
        var currentExists = File.Exists(Absolute(path)) || Directory.Exists(Absolute(path));
        if (baselineTree is null)
            return !currentExists;

        var baselineEntry = await ReadTreeEntryAsync(baselineTree, path, cancellationToken);
        if (baselineEntry is null)
            return !currentExists;
        if (!currentExists)
            return false;

        if (baselineEntry.Type == "commit")
            return Directory.Exists(Absolute(path));
        if (baselineEntry.Type != "blob" || !File.Exists(Absolute(path)))
            return false;

        var currentBlob = await RunRequiredAsync(
            ["hash-object", $"--path={path}", "--", path],
            cancellationToken);
        return string.Equals(
            currentBlob.StandardOutput.Trim(),
            baselineEntry.ObjectId,
            StringComparison.Ordinal);
    }

    private async Task<FilesystemState> SnapshotFilesystemAsync(
        string canonicalPath,
        CancellationToken cancellationToken)
    {
        var absolute = Absolute(canonicalPath);
        if (File.Exists(absolute))
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(absolute, cancellationToken);
                return new(true, Convert.ToHexString(SHA256.HashData(bytes)));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                throw TrackingFailure(
                    $"Cannot read workspace candidate '{canonicalPath}': {exception.Message}");
            }
        }

        return Directory.Exists(absolute)
            ? new(true, null)
            : new(false, null);
    }

    private async Task<IReadOnlyList<StatusCandidate>> ReadStatusPathsAsync(
        CancellationToken cancellationToken)
    {
        var result = await RunRequiredAsync(
            ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--no-renames"],
            cancellationToken);
        var candidates = new List<StatusCandidate>();
        foreach (var record in result.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (record.Length < 4 || record[2] != ' ')
                throw TrackingFailure("Git returned an invalid porcelain status record.");

            var canonical = WorkspacePathPolicy.Canonicalize(record[3..]);
            if (WorkspacePathPolicy.IsOperationalArtifact(canonical))
                continue;
            candidates.Add(new(canonical, record[0] == '?' && record[1] == '?'));
        }

        return candidates
            .DistinctBy(candidate => candidate.Path, StringComparer.Ordinal)
            .OrderBy(candidate => candidate.Path, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<IReadOnlyList<string>> ReadTreeDiffPathsAsync(
        string? baselineTree,
        string? currentTree,
        CancellationToken cancellationToken)
    {
        if (baselineTree is null && currentTree is null)
            return [];

        ProcessExecutionResult result;
        if (baselineTree is null)
        {
            result = await RunRequiredAsync(
                ["ls-tree", "-r", "--name-only", "-z", currentTree!],
                cancellationToken);
        }
        else if (currentTree is null)
        {
            result = await RunRequiredAsync(
                ["ls-tree", "-r", "--name-only", "-z", baselineTree],
                cancellationToken);
        }
        else
        {
            result = await RunRequiredAsync(
                ["diff", "--name-only", "-z", "--no-renames", baselineTree, currentTree, "--"],
                cancellationToken);
        }

        return WorkspacePathPolicy.NormalizeChangedPaths(
            result.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries));
    }

    private async Task<TreeEntry?> ReadTreeEntryAsync(
        string tree,
        string path,
        CancellationToken cancellationToken)
    {
        var result = await RunRequiredAsync(
            ["ls-tree", "-z", tree, "--", $":(literal){path}"],
            cancellationToken);
        if (string.IsNullOrEmpty(result.StandardOutput))
            return null;

        var record = result.StandardOutput.TrimEnd('\0');
        var tab = record.IndexOf('\t');
        if (tab <= 0)
            throw TrackingFailure("Git returned an invalid tree entry.");
        var fields = record[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 3)
            throw TrackingFailure("Git returned an invalid tree entry header.");
        return new(fields[1], fields[2]);
    }

    private async Task EnsureRepositoryAsync(CancellationToken cancellationToken)
    {
        var inside = await RunRequiredAsync(
            ["rev-parse", "--is-inside-work-tree"],
            cancellationToken);
        if (!string.Equals(inside.StandardOutput.Trim(), "true", StringComparison.OrdinalIgnoreCase))
            throw TrackingFailure("Workspace is no longer a usable Git worktree.");

        var topLevel = await RunRequiredAsync(
            ["rev-parse", "--show-toplevel"],
            cancellationToken);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(
                NormalizeFullPath(workspace),
                NormalizeFullPath(topLevel.StandardOutput.Trim()),
                comparison))
        {
            throw TrackingFailure("Workspace is no longer the Git worktree root.");
        }
    }

    private async Task<string?> TryResolveAsync(
        string revision,
        CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(
            ["rev-parse", "--verify", revision],
            cancellationToken);
        if (result.CompletionReason == ProcessCompletionReason.Cancelled)
            throw new OperationCanceledException(cancellationToken);
        if (result.CompletionReason != ProcessCompletionReason.Exited)
            throw TrackingFailure($"Git could not resolve repository identity ({result.CompletionReason}).");
        return result.ExitCode == 0
            ? result.StandardOutput.Trim()
            : null;
    }

    private async Task<ProcessExecutionResult> RunRequiredAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(arguments, cancellationToken);
        if (result.CompletionReason == ProcessCompletionReason.Cancelled)
            throw new OperationCanceledException(cancellationToken);
        if (result.CompletionReason != ProcessCompletionReason.Exited || result.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.CompletionReason.ToString()
                : result.StandardError.Trim();
            throw TrackingFailure($"Git command failed: git {string.Join(' ', arguments)}. {detail}");
        }

        return result;
    }

    private Task<ProcessExecutionResult> RunGitAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken) =>
        processExecutor.RunAsync(
            new ProcessExecutionRequest("git", arguments, workspace)
            {
                Timeout = GitTimeout
            },
            cancellationToken);

    private async Task<WorkspaceBaselineArtifact> ReadBaselineAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var artifact = JsonSerializer.Deserialize<WorkspaceBaselineArtifact>(
                               await File.ReadAllTextAsync(path, cancellationToken),
                               FactoryJson.Options)
                           ?? throw TrackingFailure("Workspace baseline artifact is empty.");
            if (artifact.SchemaVersion != BaselineSchemaVersion)
                throw TrackingFailure("Workspace baseline artifact has an unsupported schema version.");

            var normalized = WorkspacePathPolicy.NormalizeChangedPaths(
                artifact.BaselineEntries.Select(entry => entry.Path));
            if (!normalized.SequenceEqual(
                    artifact.BaselineEntries.Select(entry => entry.Path),
                    StringComparer.Ordinal))
            {
                throw TrackingFailure("Workspace baseline paths are not canonical and sorted.");
            }

            return artifact;
        }
        catch (AgentProtocolException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            throw TrackingFailure($"Cannot read workspace baseline artifact: {exception.Message}");
        }
    }

    private async Task<WorkspaceChangesArtifact> ReadChangesAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            throw TrackingFailure("Workspace changes artifact is missing.");

        try
        {
            var artifact = JsonSerializer.Deserialize<WorkspaceChangesArtifact>(
                               await File.ReadAllTextAsync(path, cancellationToken),
                               FactoryJson.Options)
                           ?? throw TrackingFailure("Workspace changes artifact is empty.");
            if (artifact.SchemaVersion != ChangesSchemaVersion)
                throw TrackingFailure("Workspace changes artifact has an unsupported schema version.");

            var normalized = WorkspacePathPolicy.NormalizeChangedPaths(artifact.ChangedPaths);
            if (!normalized.SequenceEqual(artifact.ChangedPaths, StringComparer.Ordinal))
                throw TrackingFailure("Workspace changes paths are not canonical, distinct, and sorted.");
            return artifact;
        }
        catch (AgentProtocolException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            throw TrackingFailure($"Cannot read workspace changes artifact: {exception.Message}");
        }
    }

    private static async Task WriteJsonAtomicallyAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(
            temporary,
            JsonSerializer.Serialize(value, FactoryJson.Options),
            cancellationToken);
        File.Move(temporary, path, true);
    }

    private string Absolute(string canonicalPath) =>
        Path.GetFullPath(Path.Combine(
            workspace,
            canonicalPath.Replace('/', Path.DirectorySeparatorChar)));

    private static string NormalizeFullPath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static AgentProtocolException TrackingFailure(string message) =>
        new("WORKSPACE_TRACKING_FAILED", message);

    private sealed record StatusCandidate(string Path, bool IsUntracked);
    private sealed record FilesystemState(bool Exists, string? Hash);
    private sealed record TreeEntry(string Type, string ObjectId);
    private sealed record WorkspaceBaselineEntry(string Path, string Kind, bool Exists, string? Hash);
    private sealed record WorkspaceBaselineArtifact(
        int SchemaVersion,
        string? BaselineHead,
        string? BaselineTree,
        List<WorkspaceBaselineEntry> BaselineEntries);
    private sealed record WorkspaceChangesArtifact(int SchemaVersion, List<string> ChangedPaths);
}
