using Idd.Factory.Domain;

namespace Idd.Factory.Agents;

public interface IAgentBackend
{
    Task<AgentRunHandle> StartAsync(AgentInvocation invocation, CancellationToken cancellationToken);
    Task<AgentProcessResult> WaitAsync(AgentRunHandle handle, CancellationToken cancellationToken);
    Task CancelAsync(AgentRunHandle handle, CancellationToken cancellationToken);
}

public sealed class AgentProtocolException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
