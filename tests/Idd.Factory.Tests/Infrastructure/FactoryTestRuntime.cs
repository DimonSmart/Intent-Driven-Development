using System.Diagnostics;
using Idd.Factory.Agents;
using Idd.Factory.Configuration;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.Processes;
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
        VerificationEngine? verification = null,
        IProcessExecutor? workspaceProcessExecutor = null)
    {
        EnsureGitRepository(workspace);
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
            clock,
            workspaceProcessExecutor);
    }

    public static FactoryContextReader ContextReader(string workspace)
    {
        EnsureGitRepository(workspace);
        var current = Path.Combine(workspace, ".idd", "factory", "current");
        var clock = new FakeClock();
        return new FactoryContextReader(new FactoryRuntimeContext(
            workspace,
            Configuration(),
            new FileFactoryStateStore(current, new FactoryStateValidator()),
            new FactoryEventWriter(current, clock),
            clock));
    }

    public static FactoryConfiguration Configuration(
        int maxAttemptsPerTask = 4,
        int maxTechnicalRestartsPerTask = 1) => new(
        4,
        new FactoryLimits(
            maxAttemptsPerTask,
            maxTechnicalRestartsPerTask,
            12,
            64,
            TimeSpan.FromMinutes(10)),
        "test-factory.yaml",
        "test-config-hash");

    private static void EnsureGitRepository(string workspace)
    {
        Directory.CreateDirectory(workspace);
        if (!Directory.Exists(Path.Combine(workspace, ".git"))
            && !File.Exists(Path.Combine(workspace, ".git")))
        {
            RunGit(workspace, "init", "--quiet");
        }
        RunGit(workspace, "config", "user.email", "factory-tests@example.invalid");
        RunGit(workspace, "config", "user.name", "IDD Factory Tests");
    }

    private static void RunGit(string workspace, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workspace,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)
                            ?? throw new InvalidOperationException("git did not start for Factory test setup");
        var stderr = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {stderr}");
    }
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
        CommandFailure(terminationKind, diagnostic, _ => { });

    public void CommandFailure(
        AgentTerminationKind terminationKind,
        string diagnostic,
        Action<AgentInvocation> beforeFailure) =>
        results.Enqueue(new(
            invocation =>
            {
                beforeFailure(invocation);
                return null;
            },
            new AgentProcessResult(
                0,
                "",
                diagnostic,
                false,
                terminationKind == AgentTerminationKind.CommandTimeout,
                terminationKind)));

    public void TransportTerminationAfterResult(Func<AgentInvocation, string> result) =>
        results.Enqueue(new(
            invocation => result(invocation),
            new AgentProcessResult(
                0,
                "",
                "transport ended after complete semantic result",
                true,
                false,
                AgentTerminationKind.TransportFailure)));

    public void MissingResult() =>
        results.Enqueue(new(
            _ => null,
            SuccessfulProcess()));

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
