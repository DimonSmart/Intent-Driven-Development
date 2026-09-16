using System.Text.Json;
using Idd.Factory.Domain;

namespace Idd.Factory.Runtime;

internal sealed record FactoryBlockResult(
    string Code,
    string Reason,
    string ResumeWhen,
    PendingContinuation Continuation,
    JsonElement? Payload = null);

internal sealed class FactoryStopService(FactoryRuntimeContext context)
{
    public FactoryCliOutcome OutcomeFromBlocker(FactoryState state, string fallback) =>
        new(
            state.Blocker?.Code ?? fallback,
            state.RunId,
            state.Blocker?.Reason,
            state.Blocker?.ResumeWhen,
            Payload: state.Blocker?.Payload);

    public FactoryCliOutcome Outcome(FactoryState state, FactoryBlockResult block) =>
        new(
            block.Code,
            state.RunId,
            block.Reason,
            block.ResumeWhen,
            Payload: block.Payload);

    public FactoryBlockResult FromAgentProtocolException(
        FactoryState state,
        AgentProtocolException exception)
    {
        if (exception.Code == "TECHNICAL_RESTART_BUDGET_EXHAUSTED")
            return TechnicalRestartBudgetExhausted(state, exception);

        var existing = state.PendingContinuation is { IsResumable: true } value ? value : null;
        var hard = exception.Code.EndsWith("_BUDGET_EXHAUSTED", StringComparison.Ordinal)
            || exception.Code is "UNKNOWN_CAPABILITY" or "INVALID_RUNTIME_STATE";
        var resume = exception.Code == "RETRY_BUDGET_EXHAUSTED"
            ? "Resolve the condition, then call factory_retry with additional attempts (maximum 10 total), use factory_restart to replace the run, or factory_cancel if no replacement run is wanted."
            : hard || existing is null
                ? "Resolve the condition, then use factory_restart to replace the run, or factory_cancel if no replacement run is wanted."
                : "Resolve the condition, then continue the exact operation.";

        return new(
            exception.Code,
            exception.Message,
            resume,
            hard || existing is null
                ? new(ContinuationKind.Terminal, state.Current?.Id, null, exception.Code, false)
                : existing);
    }

    private FactoryBlockResult TechnicalRestartBudgetExhausted(
        FactoryState state,
        AgentProtocolException exception)
    {
        var item = state.Current;
        var failure = item?.PriorTechnicalFailures.LastOrDefault();
        JsonElement? payload = item is null || failure is null
            ? null
            : JsonSerializer.SerializeToElement(
                new
                {
                    workItemId = item.Id,
                    failedAttemptId = failure.FailedAttemptId,
                    failureCode = failure.FailureCode,
                    diagnosticReference = failure.DiagnosticReference,
                    technicalRestartCount = item.TechnicalRestartCount,
                    technicalRestartBudget = context.Configuration.Limits.MaxTechnicalRestartsPerTask
                },
                FactoryJson.Options);

        return new(
            exception.Code,
            exception.Message,
            "Resolve the execution-layer instability, then use factory_restart to replace the run, or factory_cancel if no replacement run is wanted.",
            new(
                ContinuationKind.Terminal,
                item?.Id,
                null,
                exception.Code,
                false),
            payload);
    }
}
