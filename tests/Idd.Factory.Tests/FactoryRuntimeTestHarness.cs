using System.Text.Json;
using Idd.Factory.Agents;
using Idd.Factory.Configuration;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.Runtime;
using Idd.Factory.State;
using Idd.Factory.Telemetry;
using Idd.Factory.Verification;

namespace Idd.Factory.Tests;

internal static class FactoryRuntimeTestHarness
{
    public static FactoryRuntime CreateRuntime(string workspace, FakeAgentBackend backend, IEnumerable<string>? allowed = null)
    {
        var current = Path.Combine(workspace, ".idd", "factory", "current");
        var clock = new FakeClock();
        var configuration = Configuration(allowed);
        return new FactoryRuntime(
            workspace,
            configuration,
            new FileFactoryStateStore(current, new FactoryStateValidator()),
            new FactoryAgentExecutor(backend, new FactoryAgentResultValidator()),
            new VerificationEngine(workspace, current),
            new FactoryEventWriter(current, clock),
            clock);
    }

    public static FactoryConfiguration Configuration(IEnumerable<string>? allowed = null) => new(
        1,
        new FactoryLimits(4, 3, 5, 64),
        new FinalReviewPolicy(true),
        (allowed ?? new[] { "implementation", "research", "semantic-review", "documentation" }).ToHashSet(StringComparer.Ordinal),
        "test-factory.yaml",
        "test-config-hash");

    public static async Task<FactoryState> LoadState(string workspace) =>
        (await new FileFactoryStateStore(Path.Combine(workspace, ".idd", "factory", "current"), new FactoryStateValidator()).LoadAsync(default))!;

    public static object Work(string id, string capability, int sequence = 1, string[]? dependencies = null, string? contract = null) => new
    {
        id,
        sequence,
        kind = "subtask",
        definitionState = "executable",
        capability,
        contractMarkdown = contract ?? $"# {id}",
        dependencies = dependencies ?? Array.Empty<string>(),
        verificationCheckIds = Array.Empty<string>()
    };

    public static AgentResultEnvelope Envelope(AgentInvocation invocation, string outcome, object? payload = null, string? reason = null) => new()
    {
        ProtocolVersion = AgentInvocation.CurrentProtocolVersion,
        RunId = invocation.RunId,
        AttemptId = invocation.AttemptId,
        Role = invocation.Role,
        Outcome = outcome,
        Reason = reason,
        Payload = payload is null ? null : JsonSerializer.SerializeToElement(payload, FactoryJson.Options)
    };
}

internal sealed class FakeAgentBackend : IAgentBackend
{
    private readonly Queue<Func<AgentInvocation, AgentResultEnvelope>> results = new();
    public List<AgentInvocation> Invocations { get; } = [];

    public void Enqueue(Func<AgentInvocation, AgentResultEnvelope> result) => results.Enqueue(result);

    public Task<AgentRunHandle> StartAsync(AgentInvocation invocation, CancellationToken cancellationToken)
    {
        if (results.Count == 0) throw new InvalidOperationException($"No fake result queued for {invocation.Role}/{invocation.WorkItemId}.");
        Invocations.Add(invocation);
        Directory.CreateDirectory(Path.GetDirectoryName(invocation.ResultPath)!);
        var result = results.Dequeue()(invocation);
        File.WriteAllText(invocation.ResultPath, JsonSerializer.Serialize(result, FactoryJson.Options));
        return Task.FromResult(new AgentRunHandle(invocation.AttemptId, 1, invocation.AttemptId));
    }

    public Task<AgentProcessResult> WaitAsync(AgentRunHandle handle, CancellationToken cancellationToken) =>
        Task.FromResult(new AgentProcessResult(0, "", "", true, false, AgentTerminationKind.CleanExit));

    public Task CancelAsync(AgentRunHandle handle, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class FakeClock : IClock
{
    private DateTimeOffset now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    public DateTimeOffset UtcNow => now = now.AddMilliseconds(1);
}
