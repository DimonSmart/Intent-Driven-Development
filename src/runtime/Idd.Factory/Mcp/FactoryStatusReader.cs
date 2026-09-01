using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.Runtime;
using Idd.Factory.State;

internal sealed class FactoryStatusReader
{
    public async Task<FactoryStatusResult> ReadAsync(string workspace, CancellationToken cancellationToken)
    {
        FactoryRuntimeProcessRunner.ValidateWorkspace(workspace);

        var factoryDirectory = Path.Combine(workspace, ".idd", "factory");
        var currentDirectory = Path.Combine(factoryDirectory, "current");
        var statePath = Path.Combine(currentDirectory, "state.json");
        var lockPath = Path.Combine(factoryDirectory, "runtime.lock");

        var owner = FactoryRuntimeLock.TryReadDescriptor(lockPath);
        var lockHeld = FactoryRuntimeLock.IsHeld(lockPath);
        FactoryState? state = null;
        FactoryStateException? stateError = null;
        try
        {
            state = await new FileFactoryStateStore(currentDirectory, new FactoryStateValidator()).LoadAsync(cancellationToken);
        }
        catch (FactoryStateException exception)
        {
            stateError = exception;
        }

        if (lockHeld)
        {
            return Snapshot(
                "ACTIVE",
                state,
                owner,
                reason: "A Factory runtime currently owns this workspace. A lost or timed-out MCP response does not imply cancellation.",
                resumeWhen: "Wait for the active runtime to release the workspace. Do not call factory_run or factory_continue while status is ACTIVE.");
        }

        if (stateError is not null && File.Exists(statePath))
        {
            return new FactoryStatusResult
            {
                Status = "STATE_ERROR",
                RunId = "unknown",
                FactoryOutcome = stateError.Code,
                Reason = stateError.Message,
                ResumeWhen = "Resolve the persisted Factory state problem before starting or continuing the workflow."
            };
        }

        if (state is not null && !File.Exists(statePath))
        {
            state = null;
        }

        if (state is not null)
        {
            if (state.RunStatus == FactoryRunStatus.Blocked)
            {
                return Snapshot(
                    "WAITING_FOR_CONTINUATION",
                    state,
                    null,
                    state.Blocker?.Code,
                    state.Blocker?.Reason ?? "Factory is blocked and waiting for an explicit continuation condition.",
                    state.Blocker?.ResumeWhen ?? "Resolve the blocker, then call factory_continue.",
                    payload: state.Blocker?.Payload);
            }

            if (state.RunStatus == FactoryRunStatus.Cancelled)
            {
                return Snapshot(
                    "CANCELLED",
                    state,
                    null,
                    "CANCELLED",
                    state.Blocker?.Reason ?? "The Factory run was cancelled.",
                    state.Blocker?.ResumeWhen);
            }

            return Snapshot(
                "READY_TO_CONTINUE",
                state,
                null,
                reason: "Persisted Factory state exists, but no runtime currently owns the workspace. The previous runtime may have been interrupted after its caller timed out or disconnected.",
                resumeWhen: "Call factory_continue once to reconcile and resume the existing run.");
        }

        var completed = await FindLatestCompletedResultAsync(Path.Combine(factoryDirectory, "results"), cancellationToken);
        if (completed is not null)
        {
            return new FactoryStatusResult
            {
                Status = "COMPLETED",
                RunId = completed.RunId,
                FactoryOutcome = "COMPLETED",
                Reason = "The latest persisted Factory run completed successfully.",
                ResultDirectory = completed.ResultDirectory
            };
        }

        return new FactoryStatusResult
        {
            Status = "IDLE",
            RunId = "unknown",
            Reason = "No active or persisted Factory run was found for this workspace."
        };
    }

    private static FactoryStatusResult Snapshot(
        string status,
        FactoryState? state,
        FactoryRuntimeLockDescriptor? owner,
        string? factoryOutcome = null,
        string? reason = null,
        string? resumeWhen = null,
        string? resultDirectory = null,
        JsonElement? payload = null) => new()
        {
            Status = status,
            RunId = state?.RunId ?? "unknown",
            FactoryOutcome = factoryOutcome,
            Reason = reason,
            ResumeWhen = resumeWhen,
            ResultDirectory = resultDirectory,
            CurrentWorkItemId = state?.Current?.Id,
            CurrentAttemptId = state?.CurrentAttemptId ?? state?.Current?.CurrentAttemptId,
            CurrentPhase = state?.CurrentPhase?.ToString(),
            CompletedWorkCount = state?.Completed.Count ?? 0,
            RemainingWorkCount = state?.Remaining.Count ?? 0,
            RuntimeProcessId = owner?.ProcessId,
            RuntimeMachineName = owner?.MachineName,
            RuntimeOperation = owner?.Operation,
            RuntimeStartedAt = owner?.StartedAt,
            Payload = payload
        };

    private static async Task<CompletedResult?> FindLatestCompletedResultAsync(string resultsDirectory, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(resultsDirectory))
        {
            return null;
        }

        var candidates = Directory.GetDirectories(resultsDirectory)
            .Select(directory => new
            {
                Directory = directory,
                ResultPath = Path.Combine(directory, "factory-result.json")
            })
            .Where(candidate => File.Exists(candidate.ResultPath))
            .OrderByDescending(candidate => File.GetLastWriteTimeUtc(candidate.ResultPath));

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var result = JsonDocument.Parse(await File.ReadAllTextAsync(candidate.ResultPath, cancellationToken));
                if (!result.RootElement.TryGetProperty("factoryOutcome", out var outcome) ||
                    outcome.ValueKind != JsonValueKind.String ||
                    !StringComparer.Ordinal.Equals(outcome.GetString(), "COMPLETED"))
                {
                    continue;
                }

                var runId = "unknown";
                var statePath = Path.Combine(candidate.Directory, "state.json");
                if (File.Exists(statePath))
                {
                    using var state = JsonDocument.Parse(await File.ReadAllTextAsync(statePath, cancellationToken));
                    if (state.RootElement.TryGetProperty("runId", out var runIdNode) && runIdNode.ValueKind == JsonValueKind.String)
                    {
                        runId = runIdNode.GetString() ?? "unknown";
                    }
                }

                return new(runId, candidate.Directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
            }
        }

        return null;
    }

    private sealed record CompletedResult(string RunId, string ResultDirectory);
}

internal sealed record FactoryStatusResult
{
    public required string Status { get; init; }
    public required string RunId { get; init; }
    public string? FactoryOutcome { get; init; }
    public string? Reason { get; init; }
    public string? ResumeWhen { get; init; }
    public string? ResultDirectory { get; init; }
    public string? CurrentWorkItemId { get; init; }
    public string? CurrentAttemptId { get; init; }
    public string? CurrentPhase { get; init; }
    public int CompletedWorkCount { get; init; }
    public int RemainingWorkCount { get; init; }
    public int? RuntimeProcessId { get; init; }
    public string? RuntimeMachineName { get; init; }
    public string? RuntimeOperation { get; init; }
    public DateTimeOffset? RuntimeStartedAt { get; init; }
    public JsonElement? Payload { get; init; }
}
