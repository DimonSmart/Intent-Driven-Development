using Idd.Factory.Domain;

namespace Idd.Factory.Agents;

public interface IAgentBackend
{
    Task<AgentRunHandle> StartAsync(AgentInvocation invocation, CancellationToken cancellationToken);
    Task<AgentProcessResult> WaitAsync(AgentRunHandle handle, CancellationToken cancellationToken);
    Task CancelAsync(AgentRunHandle handle, CancellationToken cancellationToken);
}

public sealed class AgentProtocolException : Exception
{
    public AgentProtocolException(
        string code,
        string message,
        string? diagnosticReference = null,
        AgentFailureDiagnostic? failureDiagnostic = null)
        : base(message)
    {
        Code = code;
        DiagnosticReference = diagnosticReference;
        FailureDiagnostic = failureDiagnostic;
    }

    public string Code { get; }
    public string? DiagnosticReference { get; }
    public AgentFailureDiagnostic? FailureDiagnostic { get; }
}
