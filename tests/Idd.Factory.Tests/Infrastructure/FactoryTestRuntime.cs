using Idd.Factory.Agents;
using Idd.Factory.Configuration;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.Runtime;
using Idd.Factory.State;
using Idd.Factory.Telemetry;
using Idd.Factory.Verification;

namespace Idd.Factory.Tests;

internal static class FactoryTestRuntime
{
    public static FactoryRuntime Create(
        string workspace,
        ScriptedAgentBackend backend,
        FactoryConfiguration? configuration = null,
        VerificationEngine? verification = null)
    {
        var current = Path.Combine(workspace, ".idd", "factory", "current");
        var clock = new FakeClock();
        configuration ??= Configuration();
        return new FactoryRuntime(
            workspace,
            configuration,
            new FileFactoryStateStore(current, new FactoryStateValidator()),
            new FactoryAgentExecutor(backend),
            verification ?? new VerificationEngine(workspace, current),
            new FactoryEventWriter(current, clock),
            clock);
    }

    public static FactoryContextReader ContextReader(string workspace)
    {
        var current = Path.Combine(workspace, ".idd", "factory", "current");
        var clock = new FakeClock();
        return new FactoryContextReader(new FactoryRuntimeContext(
            workspace,
            Configuration(),
            new FileFactoryStateStore(current, new FactoryStateValidator()),
            new FactoryEventWriter(current, clock),
            clock));
    }

    public static FactoryConfiguration Configuration() => new(
        3,
        new FactoryLimits(4, 12, 64, TimeSpan.FromMinutes(10)),
        "test-factory.yaml",
        "test-config-hash");
}

internal sealed class ScriptedAgentBackend : IAgentBackend
{
    private readonly Queue<FakeResponse> results = new();
    private readonly Dictionary<string, AgentProcessResult> active = new(StringComparer.Ordinal);
    public List<AgentInvocation> Invocations { get; } = [];

    public void Reply(string result) => Reply(_ => result);

    public void Reply(Func<AgentInvocation, string> result) =>
        results.Enqueue(new(invocation => result(invocation), SuccessfulProcess()));

    public void CommandFailure(AgentTerminationKind terminationKind, string diagnostic) =>
        results.Enqueue(new(
            _ => null,
            new AgentProcessResult(0, "", diagnostic, false, terminationKind == AgentTerminationKind.CommandTimeout, terminationKind)));

    public Task<AgentRunHandle> StartAsync(AgentInvocation invocation, CancellationToken cancellationToken)
    {
        if (results.Count == 0)
            throw new InvalidOperationException($"No scripted result queued for {invocation.Role}/{invocation.WorkItemId}.");
        Invocations.Add(invocation);
        Directory.CreateDirectory(Path.GetDirectoryName(invocation.SemanticOutputPath)!);
        var response = results.Dequeue();
        var semanticResult = response.Result(invocation);
        if (semanticResult is not null) File.WriteAllText(invocation.SemanticOutputPath, semanticResult);
        if (!string.IsNullOrEmpty(response.Process.Stderr))
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(invocation.SemanticOutputPath)!, "stderr.log"), response.Process.Stderr);
        active.Add(invocation.AttemptId, response.Process);
        return Task.FromResult(new AgentRunHandle(invocation.AttemptId, 1, invocation.AttemptId));
    }

    public Task<AgentProcessResult> WaitAsync(AgentRunHandle handle, CancellationToken cancellationToken) =>
        Task.FromResult(active.Remove(handle.AttemptId, out var process)
            ? process
            : new AgentProcessResult(-1, "", "Missing scripted process.", false, false, AgentTerminationKind.TransportFailure));

    public Task CancelAsync(AgentRunHandle handle, CancellationToken cancellationToken) => Task.CompletedTask;

    private static AgentProcessResult SuccessfulProcess() =>
        new(0, "", "", true, false, AgentTerminationKind.CleanExit);

    private sealed record FakeResponse(Func<AgentInvocation, string?> Result, AgentProcessResult Process);
}

internal sealed class FakeClock : IClock
{
    private DateTimeOffset now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    public DateTimeOffset UtcNow => now = now.AddMilliseconds(1);
}
