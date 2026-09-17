using System.Text.Json;
using Idd.Factory.Agents;
using Idd.Factory.Domain;

namespace Idd.Factory.Runtime;

internal sealed class ExternalBackendBlockerCoordinator(FactoryRuntimeContext context)
{
    public async Task PrepareAsync(
        FactoryState state,
        PlannedWorkItem? item,
        AgentInvocation invocation,
        AgentFailureDiagnostic diagnostic,
        string diagnosticReference,
        IReadOnlyList<string> changedPaths,
        CancellationToken cancellationToken)
    {
        if (!AgentFailureCodes.IsExternalBackendBlocker(diagnostic.FailureCode))
        {
            throw new InvalidOperationException(
                $"Failure '{diagnostic.FailureCode}' is not an external Agent Backend blocker.");
        }

        ApplyChangedPaths(state, item, changedPaths);

        if (item is not null && item.CurrentAttemptId == invocation.AttemptId)
        {
            item.AttemptCount = Math.Max(0, item.AttemptCount - 1);
            item.CurrentAttemptId = null;
            state.CurrentPhase = CurrentWorkPhase.Blocked;
        }
        state.CurrentAttemptId = null;

        var payload = JsonSerializer.SerializeToElement(
            new
            {
                attemptId = invocation.AttemptId,
                workItemId = invocation.WorkItemId,
                capability = invocation.Capability,
                failureCode = diagnostic.FailureCode,
                diagnosticReference,
                semanticAttemptNumber = invocation.SemanticAttemptNumber,
                technicalRestartNumber = invocation.TechnicalRestartNumber
            },
            FactoryJson.Options);
        state.Blocker = new(
            diagnostic.FailureCode,
            diagnostic.HumanReadableMessage,
            "Resolve the external Agent Backend condition, then use factory_continue.",
            payload);

        await WriteEventOnceAsync(
            state.RunId,
            invocation,
            diagnostic,
            diagnosticReference,
            cancellationToken);
    }

    private async Task WriteEventOnceAsync(
        string runId,
        AgentInvocation invocation,
        AgentFailureDiagnostic diagnostic,
        string diagnosticReference,
        CancellationToken cancellationToken)
    {
        if (await HasExternalBlockerEventAsync(invocation.AttemptId, cancellationToken))
            return;

        await context.Events.WriteAsync(
            runId,
            "agent-external-blocker",
            new
            {
                attemptId = invocation.AttemptId,
                workItemId = invocation.WorkItemId,
                capability = invocation.Capability,
                failureCode = diagnostic.FailureCode,
                diagnosticReference,
                semanticAttemptNumber = invocation.SemanticAttemptNumber,
                technicalRestartNumber = invocation.TechnicalRestartNumber
            },
            cancellationToken);
    }

    private async Task<bool> HasExternalBlockerEventAsync(
        string attemptId,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(context.CurrentDirectory, "events.jsonl");
        if (!File.Exists(path))
            return false;

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            useAsync: true);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("type", out var type)
                    || type.GetString() != "agent-external-blocker"
                    || !root.TryGetProperty("data", out var data)
                    || !data.TryGetProperty("attemptId", out var eventAttemptId))
                {
                    continue;
                }

                if (string.Equals(eventAttemptId.GetString(), attemptId, StringComparison.Ordinal))
                    return true;
            }
            catch (JsonException)
            {
            }
        }

        return false;
    }

    private static void ApplyChangedPaths(
        FactoryState state,
        PlannedWorkItem? item,
        IEnumerable<string> changedPaths)
    {
        foreach (var path in changedPaths)
        {
            if (item is not null && !item.ChangedPaths.Contains(path, StringComparer.Ordinal))
                item.ChangedPaths.Add(path);
            if (!state.FactoryRunChangedPaths.Contains(path, StringComparer.Ordinal))
                state.FactoryRunChangedPaths.Add(path);
        }
    }
}
