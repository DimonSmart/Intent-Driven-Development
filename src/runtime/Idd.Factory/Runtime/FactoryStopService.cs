using System.Text.Json;
using Idd.Factory.Domain;

namespace Idd.Factory.Runtime;

internal sealed record FactoryBlockResult(
    string Code,
    string Reason,
    string ResumeWhen,
    PendingContinuation Continuation,
    JsonElement? Payload = null);

internal sealed class FactoryStopService
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
        var existing = state.PendingContinuation is { IsResumable: true } value ? value : null;
        var hard = exception.Code.EndsWith("_BUDGET_EXHAUSTED", StringComparison.Ordinal)
            || exception.Code is "UNKNOWN_CAPABILITY" or "INVALID_RUNTIME_STATE";
        var resume = exception.Code == "RETRY_BUDGET_EXHAUSTED"
            ? "Resolve the condition, then call factory_retry with additional attempts (maximum 10 total), or cancel/restart."
            : hard || existing is null
                ? "Cancel/restart after resolving the condition."
                : "Resolve the condition, then continue the exact operation.";

        return new(
            exception.Code,
            exception.Message,
            resume,
            hard || existing is null
                ? new(ContinuationKind.Terminal, state.Current?.Id, null, exception.Code, false)
                : existing);
    }
}
