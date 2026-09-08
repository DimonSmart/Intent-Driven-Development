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

    public Task<FactoryCliOutcome> ApplyAsync(
        FactoryState state,
        FactoryBlockResult block,
        CancellationToken cancellationToken) =>
        StopAsync(
            state,
            block.Code,
            block.Reason,
            block.ResumeWhen,
            cancellationToken,
            block.Continuation,
            block.Payload);

    public async Task<FactoryCliOutcome> StopBlockedAsync(
        FactoryState state,
        CancellationToken cancellationToken)
    {
        if (state.Blocker is not null)
            return OutcomeFromBlocker(state, state.Blocker.Code);

        return await StopAsync(
            state,
            "FACTORY_BLOCKED",
            "No deterministic action is applicable.",
            "Resolve the blocker or cancel/restart.",
            cancellationToken,
            new(ContinuationKind.Terminal, state.Current?.Id, null, "FACTORY_BLOCKED", false));
    }

    public async Task<FactoryCliOutcome> StopForAgentProtocolExceptionAsync(
        FactoryState state,
        AgentProtocolException exception,
        CancellationToken cancellationToken)
    {
        if (exception.Code == "AGENT_TRANSPORT_FAILURE"
            && state.CurrentAttemptId is { } attemptId
            && state.Current is { AttemptCount: > 0 } current
            && current.CurrentAttemptId == attemptId)
        {
            current.AttemptCount--;
        }

        var existing = state.PendingContinuation is { IsResumable: true } value ? value : null;
        var hard = exception.Code.EndsWith("_BUDGET_EXHAUSTED", StringComparison.Ordinal)
            || exception.Code is "UNKNOWN_CAPABILITY" or "INVALID_RUNTIME_STATE";
        var resume = exception.Code == "RETRY_BUDGET_EXHAUSTED"
            ? "Resolve the condition, then call factory_retry with additional attempts (maximum 10 total), or cancel/restart."
            : hard || existing is null
                ? "Cancel/restart after resolving the condition."
                : "Resolve the condition, then continue the exact operation.";

        return await StopAsync(
            state,
            exception.Code,
            exception.Message,
            resume,
            cancellationToken,
            hard || existing is null
                ? new(ContinuationKind.Terminal, state.Current?.Id, null, exception.Code, false)
                : existing);
    }

    public async Task<FactoryCliOutcome> StopAsync(
        FactoryState state,
        string code,
        string reason,
        string resumeWhen,
        CancellationToken cancellationToken,
        PendingContinuation? continuation = null,
        JsonElement? payload = null)
    {
        state.RunStatus = FactoryRunStatus.Blocked;
        state.Blocker = new(code, reason, resumeWhen, payload);
        if (continuation is not null)
            state.PendingContinuation = continuation;
        await context.SaveAsync(state, cancellationToken);
        return new(code, state.RunId, reason, resumeWhen, Payload: payload);
    }
}
