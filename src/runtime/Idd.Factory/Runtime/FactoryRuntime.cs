using Idd.Factory.Configuration;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.Processes;
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
        : this(
            workspace,
            configuration,
            stateStore,
            agentExecutor,
            verification,
            events,
            clock,
            null)
    {
    }

    internal FactoryRuntime(
        string workspace,
        FactoryConfiguration configuration,
        IFactoryStateStore stateStore,
        FactoryAgentExecutor agentExecutor,
        VerificationEngine verification,
        FactoryEventWriter events,
        IClock clock,
        IProcessExecutor? workspaceProcessExecutor)
    {
        var context = new FactoryRuntimeContext(
            workspace,
            configuration,
            stateStore,
            events,
            clock);
        var semanticExecution = new SemanticExecutionService(context, agentExecutor, workspaceProcessExecutor);
        var contextReader = new FactoryContextReader(context);
        var planning = new PlanningService(
            context,
            contextReader,
            new PlannerMarkdownParser());
        var execution = new ExecutionService(context, contextReader);
        var runtimeVerification = new RuntimeVerificationService(context, verification);
        var stop = new FactoryStopService(context);
        var planMutation = new PlanMutationService(
            context,
            new PlanRevisionWriter(context.CurrentDirectory, clock));
        var stateMachine = new FactoryStateMachine(
            context,
            planning,
            planMutation,
            execution,
            semanticExecution,
            runtimeVerification,
            stop);
        runs = new FactoryRunService(context, stateMachine);
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
