using System.Text.Json;
using System.Text.Json.Serialization;
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
    public static FactoryRuntime CreateRuntime(
        string workspace,
        FakeAgentBackend backend,
        IEnumerable<string>? allowed = null,
        FactoryConfiguration? configuration = null,
        VerificationEngine? verification = null)
    {
        var current = Path.Combine(workspace, ".idd", "factory", "current");
        var clock = new FakeClock();
        configuration ??= CreateConfiguration(allowed);
        return new FactoryRuntime(
            workspace,
            configuration,
            new FileFactoryStateStore(current, new FactoryStateValidator()),
            new FactoryAgentExecutor(backend, new FactoryAgentResultValidator()),
            verification ?? new VerificationEngine(workspace, current),
            new FactoryEventWriter(current, clock),
            clock);
    }

    public static FactoryConfiguration CreateConfiguration(IEnumerable<string>? allowed = null) => new(
        1,
        new FactoryLimits(4, 3, 5, 64),
        new FinalReviewPolicy(true),
        (allowed ?? new[] { "implementation", "research", "semantic-review" }).ToHashSet(StringComparer.Ordinal),
        "test-factory.yaml",
        "test-config-hash");

    public static async Task<FactoryState> LoadState(string workspace) =>
        (await new FileFactoryStateStore(Path.Combine(workspace, ".idd", "factory", "current"), new FactoryStateValidator()).LoadAsync(default))!;

    public static object Work(string id, string capability, int sequence = 1, string[]? dependencies = null, string? contract = null) => new
    {
        capability,
        task = contract ?? $"# {id}"
    };

    public static SemanticAgentResult Envelope(AgentInvocation invocation, string outcome, object? payload = null, string? reason = null)
    {
        var body = payload is null ? (JsonElement?)null : JsonSerializer.SerializeToElement(payload, FactoryJson.Options);
        var tasks = outcome == "ready" && body is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty("tasks", out var taskArray)
            ? taskArray.Clone()
            : (JsonElement?)null;
        return new()
        {
            Outcome = outcome,
            Summary = outcome == "completed" ? $"Completed {invocation.Capability} work." : null,
            Tasks = tasks,
            Reason = reason,
            Payload = tasks is null ? body : null
        };
    }
}

internal sealed class FakeAgentBackend : IAgentBackend
{
    private static readonly JsonSerializerOptions WireOptions = new(FactoryJson.Options)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private readonly Queue<Func<AgentInvocation, SemanticAgentResult>> results = new();
    public List<AgentInvocation> Invocations { get; } = [];

    public void Enqueue(Func<AgentInvocation, SemanticAgentResult> result) => results.Enqueue(result);

    public Task<AgentRunHandle> StartAsync(AgentInvocation invocation, CancellationToken cancellationToken)
    {
        if (results.Count == 0) throw new InvalidOperationException($"No fake result queued for {invocation.Role}/{invocation.WorkItemId}.");
        Invocations.Add(invocation);
        Directory.CreateDirectory(Path.GetDirectoryName(invocation.RawResultPath)!);
        var result = results.Dequeue()(invocation);
        File.WriteAllText(invocation.RawResultPath, JsonSerializer.Serialize(result, WireOptions));
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
