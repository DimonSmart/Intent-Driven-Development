using System.Text.Json;
using System.Text.RegularExpressions;
using Idd.Factory.Domain;
using Idd.Factory.Verification;

namespace Idd.Factory.Finalization;

public sealed class FinalizeHandler
{
    private readonly string workspace;
    private readonly Action<FinalizationStage>? transitionObserver;

    public FinalizeHandler(string workspace) : this(workspace, null) { }

    internal FinalizeHandler(string workspace, Action<FinalizationStage>? transitionObserver)
    {
        this.workspace = workspace;
        this.transitionObserver = transitionObserver;
    }

    public async Task<string> FinalizeAsync(FactoryState state, CancellationToken cancellationToken)
    {
        ValidateState(state);

        var current = Path.Combine(workspace, ".idd", "factory", "current");
        foreach (var reference in state.VerificationEvidenceRefs)
            _ = JsonSerializer.Deserialize<VerificationEvidence>(await File.ReadAllTextAsync(Path.Combine(current, reference), cancellationToken), FactoryJson.Options)
                ?? throw new InvalidOperationException($"Invalid verification evidence: {reference}");

        var request = await File.ReadAllTextAsync(Path.Combine(current, state.RequestPath), cancellationToken);
        var resultsRoot = Path.Combine(workspace, ".idd", "factory", "results");
        Directory.CreateDirectory(resultsRoot);
        var manifest = await ReadOrCreateManifestAsync(current, resultsRoot, state, request, cancellationToken);
        var destination = Path.Combine(resultsRoot, manifest.ResultDirectoryName);
        if (Directory.Exists(destination))
            throw new InvalidOperationException($"Finalization destination already exists while current run is still active: {destination}");

        await PrepareResultArtifactsAsync(current, destination, state, request, cancellationToken);
        ValidatePreparedResult(current, state);
        transitionObserver?.Invoke(FinalizationStage.Prepared);

        // This is the only destructive boundary. Before it, authoritative state and all run
        // artifacts remain under current and FinalizeAsync can be retried. After it, the entire
        // completed run exists under results, including events.jsonl and all attempt diagnostics.
        cancellationToken.ThrowIfCancellationRequested();
        Directory.Move(current, destination);
        transitionObserver?.Invoke(FinalizationStage.Committed);
        return destination;
    }

    private static void ValidateState(FactoryState state)
    {
        if (state.Current is not null || state.Remaining.Count != 0 || state.CurrentAttemptId is not null || state.PendingContinuation is not null || state.PendingVerificationSession is not null)
            throw new InvalidOperationException("Finalization requires quiescent linear work state.");
        if (!state.FinalVerificationPassed || state.FinalVerificationPlanRevision != state.PlanRevision)
            throw new InvalidOperationException("Finalization requires current strict verification.");
    }

    private async Task<FinalizationManifest> ReadOrCreateManifestAsync(
        string current,
        string resultsRoot,
        FactoryState state,
        string request,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(current, "finalization.json");
        if (File.Exists(path))
        {
            var persisted = JsonSerializer.Deserialize<FinalizationManifest>(await File.ReadAllTextAsync(path, cancellationToken), FactoryJson.Options)
                ?? throw new InvalidOperationException("Invalid finalization manifest.");
            if (persisted.SchemaVersion != 1 || persisted.RunId != state.RunId || !IsSafeDirectoryName(persisted.ResultDirectoryName))
                throw new InvalidOperationException("Finalization manifest does not belong to the current run.");
            return persisted;
        }

        var baseName = $"{DateTimeOffset.UtcNow:yyyy-MM-dd_HH-mm-ssZ}_{Slug(request.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "factory-result")}";
        var name = baseName;
        for (var suffix = 2; Directory.Exists(Path.Combine(resultsRoot, name)); suffix++) name = baseName + "-" + suffix;
        var manifest = new FinalizationManifest(1, state.RunId, name);
        await WriteJsonAtomicallyAsync(path, manifest, cancellationToken);
        return manifest;
    }

    private async Task PrepareResultArtifactsAsync(
        string current,
        string destination,
        FactoryState state,
        string request,
        CancellationToken cancellationToken)
    {
        var subject = request.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim().TrimStart('#', ' ') ?? "Complete Factory task";
        if (subject.Length > 72) subject = subject[..72].TrimEnd();
        await WriteTextAtomicallyAsync(
            Path.Combine(current, "commit-message.md"),
            $"{subject.TrimEnd('.')}\n\nPerformed by: IDD Factory\n\nResult:\n- Completed {state.Completed.Count} ordered work items\n- Passed strict integrated verification\n",
            cancellationToken);

        var plan = new
        {
            schemaVersion = 1,
            state.PlanRevision,
            completed = state.Completed.Select(x => new { x.Id, x.ContractPath, x.ResultRef, x.ChangedPaths, x.VerificationEvidenceRefs })
        };
        await WriteJsonAtomicallyAsync(Path.Combine(current, "completed-work.json"), plan, cancellationToken);

        var result = new
        {
            schemaVersion = 4,
            state.MethodologyVersion,
            state.RuntimeVersion,
            factoryOutcome = "COMPLETED",
            state.PlanRevision,
            state.FactoryConfigurationHash,
            completedWorkCount = state.Completed.Count,
            verificationStatus = "passed",
            finalVerificationPlanRevision = state.FinalVerificationPlanRevision,
            commitMessagePath = Path.GetRelativePath(workspace, Path.Combine(destination, "commit-message.md")).Replace('\\', '/'),
            planHistoryPath = Directory.Exists(Path.Combine(current, "plan-revisions"))
                ? Path.GetRelativePath(workspace, Path.Combine(destination, "plan-revisions")).Replace('\\', '/')
                : null
        };
        await WriteJsonAtomicallyAsync(Path.Combine(current, "factory-result.json"), result, cancellationToken);
    }

    private static void ValidatePreparedResult(string current, FactoryState state)
    {
        foreach (var path in new[]
        {
            Path.Combine(current, "state.json"),
            Path.Combine(current, state.RequestPath),
            Path.Combine(current, "events.jsonl"),
            Path.Combine(current, "factory-result.json"),
            Path.Combine(current, "completed-work.json"),
            Path.Combine(current, "commit-message.md"),
            Path.Combine(current, "finalization.json")
        })
            if (!File.Exists(path)) throw new InvalidOperationException($"Required finalization artifact is missing: {path}");

        if (!Directory.Exists(Path.Combine(current, "attempts")))
            throw new InvalidOperationException("Required finalization artifact is missing: attempts directory.");

        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(current, "factory-result.json")));
        if (!document.RootElement.TryGetProperty("factoryOutcome", out var outcome) || outcome.GetString() != "COMPLETED")
            throw new InvalidOperationException("Prepared factory-result.json is invalid.");
    }

    private static bool IsSafeDirectoryName(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value == Path.GetFileName(value)
        && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static string Slug(string value)
    {
        var slug = Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrEmpty(slug) ? "factory-result" : slug[..Math.Min(slug.Length, 40)].TrimEnd('-');
    }

    private static async Task WriteJsonAtomicallyAsync<T>(string path, T value, CancellationToken cancellationToken) =>
        await WriteTextAtomicallyAsync(path, JsonSerializer.Serialize(value, FactoryJson.Options), cancellationToken);

    private static async Task WriteTextAtomicallyAsync(string path, string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, content, cancellationToken);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private sealed record FinalizationManifest(int SchemaVersion, string RunId, string ResultDirectoryName);
}

internal enum FinalizationStage { Prepared, Committed }
