using Idd.Factory.Configuration;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.Telemetry;
using Idd.Factory.Verification;

namespace Idd.Factory.Runtime;

public sealed class FactoryRuntime
{
    private readonly FactoryRunService runs;

    public FactoryRuntime(
        string workspace,
        FactoryConfiguration configuration,
        IFactoryStateStore stateStore,
        FactoryAgentExecutor agentExecutor,
        VerificationEngine verification,
        FactoryEventWriter events,
        IClock clock)
    {
        var context = new FactoryRuntimeContext(
            workspace,
            configuration,
            stateStore,
            events,
            clock);
        var semanticExecution = new SemanticExecutionService(context, agentExecutor);
        var contextReader = new FactoryContextReader(context);
        var planning = new PlanningService(
            context,
            semanticExecution,
            contextReader,
            new PlannerMarkdownParser());
        var execution = new ExecutionService(
            context,
            semanticExecution,
            contextReader);
        var runtimeVerification = new RuntimeVerificationService(context, verification);
        var stop = new FactoryStopService(context);
        var planMutation = new PlanMutationService(
            context,
            new PlanRevisionWriter(context.CurrentDirectory, clock));
        var finalization = new FinalizationService(context);
        var stateMachine = new FactoryStateMachine(
            context,
            planning,
            planMutation,
            execution,
            runtimeVerification,
            finalization,
            stop);
        runs = new FactoryRunService(
            context,
            semanticExecution,
            planning,
            execution,
            runtimeVerification,
            stateMachine,
            stop);
    }

    public async Task<FactoryCliOutcome> RunAsync(
        string requestPath,
        string methodologyVersion,
        CancellationToken cancellationToken) =>
        await runs.RunRequestAsync(
            await File.ReadAllTextAsync(requestPath, cancellationToken),
            methodologyVersion,
            cancellationToken);

    public Task<FactoryCliOutcome> RunRequestAsync(
        string request,
        string methodologyVersion,
        CancellationToken cancellationToken) =>
        runs.RunRequestAsync(request, methodologyVersion, cancellationToken);

    public async Task<FactoryCliOutcome> RestartAsync(
        string requestPath,
        string methodologyVersion,
        CancellationToken cancellationToken) =>
        await runs.RestartRequestAsync(
            await File.ReadAllTextAsync(requestPath, cancellationToken),
            methodologyVersion,
            cancellationToken);

    public Task<FactoryCliOutcome> RestartRequestAsync(
        string request,
        string methodologyVersion,
        CancellationToken cancellationToken) =>
        runs.RestartRequestAsync(request, methodologyVersion, cancellationToken);

    public Task<FactoryCliOutcome> ContinueAsync(
        CancellationToken cancellationToken,
        VerificationConfirmation confirmation = VerificationConfirmation.None,
        bool? verificationPassed = null,
        string? userAnswer = null) =>
        runs.ContinueAsync(
            cancellationToken,
            confirmation,
            verificationPassed,
            userAnswer);

    public Task<FactoryCliOutcome> CancelAsync(
        CancellationToken cancellationToken) =>
        runs.CancelAsync(cancellationToken);

    public Task<FactoryCliOutcome> RetryExhaustedAsync(
        int additionalAttempts,
        CancellationToken cancellationToken) =>
        runs.RetryExhaustedAsync(additionalAttempts, cancellationToken);
}
