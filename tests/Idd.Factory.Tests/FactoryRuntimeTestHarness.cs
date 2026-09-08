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
    public static FactoryRuntime CreateRuntime(
        string workspace,
        FakeAgentBackend backend,
        IEnumerable<string>? allowed = null,
        FactoryConfiguration? configuration = null,
        VerificationEngine? verification = null)
    {
        var current = Path.Combine(workspace, ".idd", "factory", "current");
        var clock = new FakeClock();
        configuration ??= CreateConfiguration();
        return new FactoryRuntime(
            workspace,
            configuration,
            new FileFactoryStateStore(current, new FactoryStateValidator()),
            new FactoryAgentExecutor(backend),
            verification ?? new VerificationEngine(workspace, current),
            new FactoryEventWriter(current, clock),
            clock);
    }

    public static FactoryConfiguration CreateConfiguration(IEnumerable<string>? allowed = null) => new(
        3,
        new FactoryLimits(4, 12, 64, TimeSpan.FromMinutes(10)),
        "test-factory.yaml",
        "test-config-hash");

    public static async Task<FactoryState> LoadState(string workspace) =>
        (await new FileFactoryStateStore(Path.Combine(workspace, ".idd", "factory", "current"), new FactoryStateValidator()).LoadAsync(default))!;

    public static object Work(string id, string capability = "implementation", int sequence = 1, string[]? dependencies = null, string? contract = null) => new
    {
        capability,
        task = contract ?? id
    };

    public static string Envelope(AgentInvocation invocation, string outcome, object? payload = null, string? reason = null)
    {
        if (invocation.Capability != "planning") return reason ?? $"Completed {invocation.WorkItemId ?? "assigned work"}.";
        if (outcome != "ready") return string.Empty;
        if (payload is null) return string.Empty;
        var body = JsonSerializer.SerializeToElement(payload, FactoryJson.Options);
        if (!body.TryGetProperty("tasks", out var tasks) || tasks.ValueKind != JsonValueKind.Array) return string.Empty;
        return string.Join("\n\n", tasks.EnumerateArray().Select(task =>
            $"# Task\n\n{task.GetProperty("task").GetString()!.Trim()}"));
    }
}

internal sealed class FakeAgentBackend : IAgentBackend
{
    private readonly Queue<FakeResponse> results = new();
    private readonly Dictionary<string, AgentProcessResult> active = new(StringComparer.Ordinal);
    public List<AgentInvocation> Invocations { get; } = [];

    public void Enqueue(Func<AgentInvocation, string> result) => results.Enqueue(new(invocation => result(invocation), SuccessfulProcess()));
    public void EnqueueCommandFailure(AgentTerminationKind terminationKind, string diagnostic) =>
        results.Enqueue(new(_ => null, new AgentProcessResult(0, "", diagnostic, false, terminationKind == AgentTerminationKind.CommandTimeout, terminationKind)));

    public Task<AgentRunHandle> StartAsync(AgentInvocation invocation, CancellationToken cancellationToken)
    {
        if (results.Count == 0) throw new InvalidOperationException($"No fake result queued for {invocation.Role}/{invocation.WorkItemId}.");
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
            : new AgentProcessResult(-1, "", "Missing fake process.", false, false, AgentTerminationKind.TransportFailure));

    public Task CancelAsync(AgentRunHandle handle, CancellationToken cancellationToken) => Task.CompletedTask;

    private static AgentProcessResult SuccessfulProcess() => new(0, "", "", true, false, AgentTerminationKind.CleanExit);
    private sealed record FakeResponse(Func<AgentInvocation, string?> Result, AgentProcessResult Process);
}

internal sealed class FakeClock : IClock
{
    private DateTimeOffset now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    public DateTimeOffset UtcNow => now = now.AddMilliseconds(1);
}
