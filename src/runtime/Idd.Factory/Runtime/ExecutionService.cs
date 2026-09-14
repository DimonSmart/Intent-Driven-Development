using System.Text;
using Idd.Factory.Domain;

namespace Idd.Factory.Runtime;

internal enum FactoryExecutionResultKind
{
    Ready,
    RetryBudgetExhausted
}

internal sealed record FactoryExecutionPreparation(
    FactoryExecutionResultKind Kind,
    string WorkItemId,
    string? Input = null,
    bool VerificationDrivenRetry = false,
    string? Detail = null);

internal sealed class ExecutionService(
    FactoryRuntimeContext context,
    FactoryContextReader contextReader)
{
    private static readonly UTF8Encoding HumanReadableUtf8 =
        new(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);
    private readonly IntentDocumentResolver intentResolver = new(context.Workspace);

    public async Task<FactoryExecutionPreparation> PrepareAsync(
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
            return new(
                FactoryExecutionResultKind.RetryBudgetExhausted,
                item.Id,
                Detail: await contextReader.BuildRetryBudgetExhaustedMessageAsync(
                    item,
                    cancellationToken));
        }

        return new(
            FactoryExecutionResultKind.Ready,
            item.Id,
            await BuildWorkInputAsync(state, item, cancellationToken),
            item.LastVerificationDecision == VerificationDecision.UnexpectedFailure);
    }

    public async Task<string> PersistCommandFailureDiagnosticAsync(
        string attemptId,
        AgentProtocolException exception,
        CancellationToken cancellationToken)
    {
        var diagnosticReference = $"attempts/{attemptId}/stderr.log";
        var diagnosticPath = Path.Combine(
            context.CurrentDirectory,
            diagnosticReference.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(diagnosticPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(diagnosticPath)!);
            await File.WriteAllTextAsync(
                diagnosticPath,
                exception.Message,
                HumanReadableUtf8,
                cancellationToken);
        }

        return diagnosticReference;
    }

    public int AvailableAdditionalAttempts(PlannedWorkItem item) =>
        10 - (context.Configuration.Limits.MaxAttemptsPerTask + item.AdditionalAttemptBudget);

    public int EffectiveAttemptBudget(PlannedWorkItem item) =>
        context.Configuration.Limits.MaxAttemptsPerTask + item.AdditionalAttemptBudget;

    private async Task<string> BuildWorkInputAsync(
        FactoryState state,
        PlannedWorkItem item,
        CancellationToken cancellationToken)
    {
        var contract = await File.ReadAllTextAsync(
            Path.Combine(context.CurrentDirectory, item.ContractPath),
            cancellationToken);
        var taskRelatedIntent = await BuildTaskRelatedIntentContextAsync(item, cancellationToken);
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
            $"Task-related durable intent:\n{taskRelatedIntent}\n\n" +
            $"Relevant completed work and results:\n{completed}\n\n" +
            $"Previous attempts for this task:\n{prior}\n\n" +
            $"Previous shell-command failures for this task:\n{priorCommandFailures}\n\n" +
            $"Authoritative verification observations:\n{verificationObservations}\n\n" +
            "The task contract defines the concrete work. Supplied task-related durable intent is normative product input and both constrain implementation. " +
            "The planner already selected the explicitly referenced intent; Factory persisted, resolved, and loaded it. Correctness for it must not depend on rediscovering those files. " +
            "Inspect the current repository and additional intent only when genuine implementation discovery requires it. " +
            "Use a fresh semantic context. Do not rely on conversation history or internal planning state.";
    }

    private async Task<string> BuildTaskRelatedIntentContextAsync(
        PlannedWorkItem item,
        CancellationToken cancellationToken)
    {
        if (item.TaskRelatedIntentIds.Count == 0)
            return "none";

        var builder = new StringBuilder();
        foreach (var intentId in item.TaskRelatedIntentIds)
        {
            ResolvedIntentDocument document;
            try
            {
                document = await intentResolver.ResolveAsync(intentId, cancellationToken);
            }
            catch (IntentResolutionException exception)
            {
                throw new AgentProtocolException(
                    "TASK_RELATED_INTENT_UNRESOLVABLE",
                    $"Work item {item.Id} has persisted durable intent reference '{intentId}' that no longer resolves uniquely: {exception.Message}");
            }

            if (builder.Length != 0)
                builder.AppendLine();
            builder.AppendLine($"--- {document.Id} ---");
            builder.Append(document.Content);
            if (!document.Content.EndsWith('\n'))
                builder.AppendLine();
        }

        return builder.ToString();
    }
}
