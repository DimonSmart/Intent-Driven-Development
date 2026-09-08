using System.Text;
using Idd.Factory.Domain;

namespace Idd.Factory.Runtime;

internal enum FactoryExecutionResultKind
{
    Completed,
    Retry,
    VerificationRetryNoProgress
}

internal sealed record FactoryExecutionResult(
    FactoryExecutionResultKind Kind,
    string WorkItemId,
    string? AttemptId = null);

internal sealed class ExecutionService(
    FactoryRuntimeContext context,
    SemanticExecutionService semanticExecution,
    FactoryContextReader contextReader)
{
    private static readonly UTF8Encoding HumanReadableUtf8 =
        new(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);

    public async Task<FactoryExecutionResult> ExecuteAsync(
        FactoryState state,
        string workItemId,
        CancellationToken cancellationToken)
    {
        var item = state.Current;
        if (item is null
            || item.Id != workItemId
            || state.CurrentPhase is not (CurrentWorkPhase.Ready or CurrentWorkPhase.Running))
        {
            throw new AgentProtocolException(
                "INVALID_DISPATCH",
                $"Work item {workItemId} is not Current executable work.");
        }

        var reusable = state.CurrentAttemptId is { } attempt
            && File.Exists(Path.Combine(
                context.CurrentDirectory,
                "attempts",
                attempt,
                "result.json"));
        if (!reusable
            && item.AttemptCount
            >= context.Configuration.Limits.MaxAttemptsPerTask
               + item.AdditionalAttemptBudget)
        {
            throw new AgentProtocolException(
                "RETRY_BUDGET_EXHAUSTED",
                await contextReader.BuildRetryBudgetExhaustedMessageAsync(
                    item,
                    cancellationToken));
        }

        var verificationDrivenRetry =
            item.LastVerificationDecision == VerificationDecision.UnexpectedFailure;

        state.CurrentPhase = CurrentWorkPhase.Running;
        await context.SaveAsync(state, cancellationToken);
        var input = await BuildWorkInputAsync(state, item, cancellationToken);

        BoundSemanticResult result;
        try
        {
            result = await semanticExecution.InvokeAsync(
                state,
                "implementation",
                item,
                input,
                SemanticOperationKind.WorkItemExecution,
                cancellationToken);
        }
        catch (AgentProtocolException exception) when (
            exception.Code is "AGENT_COMMAND_TIMEOUT" or "AGENT_COMMAND_INCOMPLETE")
        {
            await PrepareCommandFailureRetryAsync(
                state,
                item,
                exception,
                cancellationToken);
            return new(FactoryExecutionResultKind.Retry, item.Id);
        }

        item = state.Current
               ?? throw new FactoryStateException(
                   "CORRUPT_FACTORY_STATE",
                   "Current work disappeared during dispatch.");
        item.LastResultRef = result.SemanticResultPath;
        item.CurrentAttemptId = null;

        if (verificationDrivenRetry
            && !await semanticExecution.AttemptChangedWorkspaceAsync(
                result.AttemptId,
                cancellationToken))
        {
            return new(
                FactoryExecutionResultKind.VerificationRetryNoProgress,
                item.Id,
                result.AttemptId);
        }

        return new(
            FactoryExecutionResultKind.Completed,
            item.Id,
            result.AttemptId);
    }

    public async Task<FactoryCliOutcome?> ExtendRetryBudgetAsync(
        FactoryState state,
        int additionalAttempts,
        CancellationToken cancellationToken)
    {
        if (additionalAttempts < 1)
        {
            return new(
                "INVALID_RETRY_ATTEMPTS",
                state.RunId,
                "additionalAttempts must be at least 1.");
        }

        if (state.RunStatus != FactoryRunStatus.Blocked
            || state.Blocker?.Code != "RETRY_BUDGET_EXHAUSTED"
            || state.PendingContinuation is not { Kind: ContinuationKind.Terminal }
            || state.Current is null)
        {
            return new(
                "RETRY_NOT_AVAILABLE",
                state.RunId,
                "The current run is not blocked by an exhausted work-item retry budget.",
                "Use factory_continue for a resumable continuation, or cancel and restart.");
        }

        var effectiveBudget =
            context.Configuration.Limits.MaxAttemptsPerTask
            + state.Current.AdditionalAttemptBudget;
        var availableAttempts = 10 - effectiveBudget;
        if (additionalAttempts > availableAttempts)
        {
            return new(
                "INVALID_RETRY_ATTEMPTS",
                state.RunId,
                $"Only {availableAttempts} additional attempts are available; Factory permits at most 10 attempts per work item.");
        }

        if (state.Current.AttemptCount < effectiveBudget)
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                "Retry-budget exhaustion was recorded before the current work item consumed its effective attempt budget.");
        }

        state.Current.AdditionalAttemptBudget += additionalAttempts;
        state.CurrentPhase = CurrentWorkPhase.Ready;
        state.CurrentAttemptId = null;
        state.PendingContinuation = new(
            ContinuationKind.SemanticInvocation,
            state.Current.Id,
            null,
            "RETRY_AFTER_BUDGET_EXTENSION",
            true,
            SemanticOperationKind.WorkItemExecution);
        state.Blocker = null;
        state.RunStatus = FactoryRunStatus.Running;
        await context.Events.WriteAsync(
            state.RunId,
            "retry-budget-extended",
            new
            {
                workItemId = state.Current.Id,
                additionalAttempts,
                effectiveAttemptBudget = effectiveBudget + additionalAttempts
            },
            cancellationToken);
        await context.SaveAsync(state, cancellationToken);
        return null;
    }

    private async Task<string> BuildWorkInputAsync(
        FactoryState state,
        PlannedWorkItem item,
        CancellationToken cancellationToken)
    {
        var contract = await File.ReadAllTextAsync(
            Path.Combine(context.CurrentDirectory, item.ContractPath),
            cancellationToken);
        var completed =
            await contextReader.BuildCompletedContextAsync(state, cancellationToken);
        var prior =
            await contextReader.BuildPriorResultContextAsync(item, cancellationToken);
        var priorCommandFailures =
            await contextReader.BuildPriorCommandFailureContextAsync(
                item,
                cancellationToken);
        var verificationObservations =
            await contextReader.BuildVerificationObservationsAsync(
                item,
                cancellationToken);

        return
            $"Work item contract:\n{contract}\n\n" +
            $"Relevant completed work and results:\n{completed}\n\n" +
            $"Previous attempts for this task:\n{prior}\n\n" +
            $"Previous shell-command failures for this task:\n{priorCommandFailures}\n\n" +
            $"Authoritative verification observations:\n{verificationObservations}\n\n" +
            "Use a fresh semantic context. Do not rely on conversation history or internal planning state.";
    }

    private async Task PrepareCommandFailureRetryAsync(
        FactoryState state,
        PlannedWorkItem item,
        AgentProtocolException exception,
        CancellationToken cancellationToken)
    {
        var attemptId = state.CurrentAttemptId
                        ?? item.CurrentAttemptId
                        ?? throw new FactoryStateException(
                            "CORRUPT_FACTORY_STATE",
                            "A shell-command failure has no current semantic attempt.");
        var diagnosticReference = $"attempts/{attemptId}/stderr.log";
        var diagnosticPath = Path.Combine(
            context.CurrentDirectory,
            diagnosticReference.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(diagnosticPath))
        {
            await File.WriteAllTextAsync(
                diagnosticPath,
                exception.Message,
                HumanReadableUtf8,
                cancellationToken);
        }

        if (!item.PriorAttemptDiagnosticRefs.Contains(
                diagnosticReference,
                StringComparer.Ordinal))
        {
            item.PriorAttemptDiagnosticRefs.Add(diagnosticReference);
        }

        state.CurrentAttemptId = null;
        item.CurrentAttemptId = null;
        state.PendingContinuation = null;
        state.Blocker = null;
        state.CurrentPhase = CurrentWorkPhase.Ready;
        await context.Events.WriteAsync(
            state.RunId,
            "agent-command-failure-retry",
            new
            {
                attemptId,
                workItemId = item.Id,
                exception.Code,
                diagnosticReference
            },
            cancellationToken);
        await context.SaveAsync(state, cancellationToken);
    }
}
