using System.Text.Json;
using Idd.Factory.Agents;
using Idd.Factory.Domain;

namespace Idd.Factory.Runtime;

internal enum SemanticAttemptRecoveryKind
{
    NoPendingAttempt,
    InvocationNeverStarted,
    CompletedResultAvailable,
    RecoverableSemanticOutput,
    InterruptedWithoutResult
}

internal sealed record SemanticAttemptRecoveryResult(
    SemanticAttemptRecoveryKind Kind,
    string? AttemptId = null,
    SemanticOperationKind Operation = SemanticOperationKind.None,
    IReadOnlyList<string>? ChangedPaths = null);

internal sealed class SemanticAttemptReconciler(
    string currentDirectory,
    Func<AgentInvocation, CancellationToken, Task<IReadOnlyList<string>>> recoverWorkspaceChanges,
    Func<FactoryState, PlannedWorkItem?, AgentInvocation, AgentFailureDiagnostic, string, IReadOnlyList<string>, CancellationToken, Task> prepareExternalBlocker)
{
    public async Task<SemanticAttemptRecoveryResult> AnalyzeAsync(
        FactoryState state,
        CancellationToken cancellationToken)
    {
        var attemptId = state.CurrentAttemptId ?? state.Current?.CurrentAttemptId;
        if (attemptId is null)
            return new(SemanticAttemptRecoveryKind.NoPendingAttempt);

        var directory = Path.Combine(currentDirectory, "attempts", attemptId);
        var invocationPath = Path.Combine(directory, "invocation.json");
        if (!File.Exists(invocationPath))
        {
            return new(
                SemanticAttemptRecoveryKind.InvocationNeverStarted,
                attemptId,
                state.PendingContinuation?.Operation ?? SemanticOperationKind.None,
                []);
        }

        var invocation = JsonSerializer.Deserialize<AgentInvocation>(
                             await File.ReadAllTextAsync(invocationPath, cancellationToken),
                             FactoryJson.Options)
                         ?? throw new AgentProtocolException(
                             "UNKNOWN_ATTEMPT",
                             $"Persisted attempt {attemptId} is malformed.");
        ValidateIdentity(state, attemptId, invocation, directory);
        var changedPaths = await recoverWorkspaceChanges(invocation, cancellationToken);
        var operation = ResolveOperation(invocation);

        var resultPath = Path.Combine(directory, "result.json");
        if (File.Exists(resultPath))
        {
            return new(
                SemanticAttemptRecoveryKind.CompletedResultAvailable,
                attemptId,
                operation,
                changedPaths);
        }

        var semanticOutputExists = File.Exists(invocation.SemanticOutputPath);
        if (semanticOutputExists
            && await TryRecoverSemanticResultAsync(
                attemptId,
                directory,
                invocation,
                resultPath,
                cancellationToken))
        {
            return new(
                SemanticAttemptRecoveryKind.RecoverableSemanticOutput,
                attemptId,
                operation,
                changedPaths);
        }

        var failure = await AgentFailureDiagnosticStore.ReadOrRecoverAsync(
            attemptId,
            directory,
            cancellationToken);
        if (failure is not null && AgentFailureCodes.IsExternalBackendBlocker(failure.FailureCode))
        {
            var diagnosticReference = AgentFailureDiagnosticStore.ReferenceFor(attemptId);
            await prepareExternalBlocker(
                state,
                state.Current,
                invocation,
                failure,
                diagnosticReference,
                changedPaths,
                cancellationToken);
            throw new AgentProtocolException(
                failure.FailureCode,
                failure.HumanReadableMessage,
                diagnosticReference,
                failure);
        }

        if (semanticOutputExists && failure is null)
        {
            throw new AgentProtocolException(
                "ATTEMPT_RECOVERY_UNSAFE",
                $"Attempt '{attemptId}' has semantic output without telemetry proving a complete trusted result or a known process failure.");
        }

        if (operation == SemanticOperationKind.WorkItemExecution && state.Current is { } item)
        {
            if (failure is not null
                && TechnicalFailureClassifier.Classify(
                    operation,
                    new AgentProtocolException(failure.FailureCode, failure.HumanReadableMessage))
                == TechnicalFailureClassification.RestartableTechnicalFailure)
            {
                var diagnostic = new TechnicalFailureDiagnostic(
                    attemptId,
                    failure.FailureCode,
                    AgentFailureDiagnosticStore.ReferenceFor(attemptId),
                    failure.HumanReadableMessage,
                    invocation.SemanticAttemptNumber!.Value,
                    changedPaths.ToList());
                item.NextInvocationKind = WorkItemInvocationKind.TechnicalRestart;
                if (!item.PriorTechnicalFailures.Any(x => x.FailedAttemptId == attemptId))
                    item.PriorTechnicalFailures.Add(diagnostic);
                if (!item.PriorAttemptDiagnosticRefs.Contains(
                        diagnostic.DiagnosticReference,
                        StringComparer.Ordinal))
                {
                    item.PriorAttemptDiagnosticRefs.Add(diagnostic.DiagnosticReference);
                }
            }
            else
            {
                item.NextInvocationKind = WorkItemInvocationKind.SemanticRetry;
            }
        }

        return new(
            SemanticAttemptRecoveryKind.InterruptedWithoutResult,
            attemptId,
            operation,
            changedPaths);
    }

    private async Task<bool> TryRecoverSemanticResultAsync(
        string attemptId,
        string directory,
        AgentInvocation invocation,
        string resultPath,
        CancellationToken cancellationToken)
    {
        var telemetryPath = Path.Combine(directory, "process-telemetry.json");
        if (!File.Exists(telemetryPath))
            return false;

        AgentProcessResult? process;
        try
        {
            process = JsonSerializer.Deserialize<AgentProcessResult>(
                await File.ReadAllTextAsync(telemetryPath, cancellationToken),
                FactoryJson.Options);
        }
        catch (JsonException exception)
        {
            throw new AgentProtocolException(
                "ATTEMPT_RECOVERY_UNSAFE",
                $"Attempt '{attemptId}' has invalid process telemetry: {exception.Message}");
        }

        if (process is null
            || !process.CompleteResultObserved
            || process.TerminationKind == AgentTerminationKind.Cancelled)
        {
            return false;
        }

        var semantic = await File.ReadAllTextAsync(invocation.SemanticOutputPath, cancellationToken);
        if (invocation.Capability == "implementation" && string.IsNullOrWhiteSpace(semantic))
        {
            throw new AgentProtocolException(
                "MALFORMED_AGENT_RESULT",
                $"Executor result for attempt '{attemptId}' is empty.");
        }

        var relative = Path.GetRelativePath(currentDirectory, invocation.SemanticOutputPath)
            .Replace('\\', '/');
        var persisted = new PersistedAttemptResult
        {
            Invocation = AttemptIdentity.From(invocation),
            SemanticResultPath = relative,
            ReceivedAt = DateTimeOffset.UtcNow,
            TerminationKind = process.TerminationKind
        };
        await WriteJsonAtomicallyAsync(resultPath, persisted, cancellationToken);
        return true;
    }

    internal static SemanticOperationKind ResolveOperation(AgentInvocation invocation) =>
        invocation.Capability switch
        {
            "planning" => SemanticOperationKind.Planning,
            "implementation" => SemanticOperationKind.WorkItemExecution,
            _ => throw new AgentProtocolException(
                "UNKNOWN_CAPABILITY",
                $"Unknown Factory capability '{invocation.Capability}'.")
        };

    internal static void ValidateIdentity(
        FactoryState state,
        string attemptId,
        AgentInvocation invocation,
        string? attemptDirectory = null)
    {
        var expectedCapability = state.Current is null ? "planning" : "implementation";
        var expectedAgent = FactoryCapabilityCatalog.Resolve(expectedCapability);
        if (invocation.SchemaVersion != AgentInvocation.CurrentSchemaVersion
            || invocation.RunId != state.RunId
            || invocation.AttemptId != attemptId
            || invocation.Capability != expectedCapability
            || invocation.Role != expectedAgent.Role
            || invocation.SkillName != expectedAgent.SkillName
            || invocation.ExecutionProfile != expectedAgent.ExecutionProfile
            || invocation.WorkItemId != state.Current?.Id)
        {
            throw new AgentProtocolException(
                "UNKNOWN_ATTEMPT",
                $"Persisted attempt {attemptId} identity is invalid.");
        }

        if (state.Current is { } item)
        {
            if (invocation.InvocationKind != item.CurrentInvocationKind
                || invocation.SemanticAttemptNumber != item.SemanticAttemptCount
                || invocation.TechnicalRestartNumber != item.TechnicalRestartCount)
            {
                throw new AgentProtocolException(
                    "UNKNOWN_ATTEMPT",
                    $"Persisted attempt {attemptId} work-item invocation metadata is invalid.");
            }
        }
        else if (invocation.InvocationKind is not null
                 || invocation.SemanticAttemptNumber is not null
                 || invocation.TechnicalRestartNumber is not null)
        {
            throw new AgentProtocolException(
                "UNKNOWN_ATTEMPT",
                $"Persisted planning attempt {attemptId} contains work-item invocation metadata.");
        }

        if (state.PendingContinuation is
            {
                Kind: ContinuationKind.SemanticInvocation,
                Operation: not SemanticOperationKind.None
            } pending
            && (pending.Operation != ResolveOperation(invocation)
                || pending.WorkItemId != state.Current?.Id))
        {
            throw new AgentProtocolException(
                "UNKNOWN_ATTEMPT",
                $"Persisted attempt {attemptId} does not belong to the pending semantic operation.");
        }

        if (attemptDirectory is not null)
        {
            var expectedName = expectedCapability == "planning"
                ? "planning-output.md"
                : "semantic-result.md";
            var expectedOutput = Path.Combine(attemptDirectory, expectedName);
            if (!SamePath(expectedOutput, invocation.SemanticOutputPath))
            {
                throw new AgentProtocolException(
                    "UNKNOWN_ATTEMPT",
                    $"Persisted attempt {attemptId} points to semantic output outside its exact attempt directory.");
            }
        }
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private static async Task WriteJsonAtomicallyAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(
            temporary,
            JsonSerializer.Serialize(value, FactoryJson.Options),
            cancellationToken);
        File.Move(temporary, path, true);
    }
}
