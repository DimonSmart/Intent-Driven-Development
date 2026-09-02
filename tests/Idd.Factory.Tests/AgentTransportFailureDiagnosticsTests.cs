using System.Text;
using System.Text.Json;
using Idd.Factory.Agents;
using Idd.Factory.Domain;

namespace Idd.Factory.Tests;

public sealed class AgentTransportFailureDiagnosticsTests
{
    [Fact]
    public async Task StructuredCodexFailureFromStdoutIsReportedWhenStderrIsEmpty()
    {
        using var temp = new TestWorkspace();
        var invocation = Invocation(temp);
        const string stdout = """
            {"type":"error","message":"You've hit your usage limit. Try again later."}
            {"type":"turn.failed","error":{"message":"You've hit your usage limit. Try again at Sep 2nd, 2026 12:11 AM."}}
            """;
        var backend = new ProcessResultBackend(new AgentProcessResult(
            1,
            stdout,
            "",
            CompleteResultObserved: false,
            KillRequired: false,
            TerminationKind: AgentTerminationKind.TransportFailure));

        var exception = await Assert.ThrowsAsync<AgentProtocolException>(() =>
            new FactoryAgentExecutor(backend, new FactoryAgentResultValidator()).ExecuteAsync(invocation, default));

        Assert.Equal("AGENT_TRANSPORT_FAILURE", exception.Code);
        Assert.Contains("You've hit your usage limit.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Sep 2nd, 2026 12:11 AM", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StderrIsReportedWhenStdoutHasNoStructuredFailure()
    {
        using var temp = new TestWorkspace();
        var invocation = Invocation(temp);
        var backend = new ProcessResultBackend(new AgentProcessResult(
            17,
            "plain stdout",
            "backend connection failed",
            CompleteResultObserved: false,
            KillRequired: false,
            TerminationKind: AgentTerminationKind.TransportFailure));

        var exception = await Assert.ThrowsAsync<AgentProtocolException>(() =>
            new FactoryAgentExecutor(backend, new FactoryAgentResultValidator()).ExecuteAsync(invocation, default));

        Assert.Equal("AGENT_TRANSPORT_FAILURE", exception.Code);
        Assert.Equal("Agent exited with 17: backend connection failed", exception.Message);
    }

    [Fact]
    public async Task StdoutIsLastResortWhenNoStructuredFailureOrStderrExists()
    {
        using var temp = new TestWorkspace();
        var invocation = Invocation(temp);
        var backend = new ProcessResultBackend(new AgentProcessResult(
            1,
            "process stopped before producing a semantic result",
            "",
            CompleteResultObserved: false,
            KillRequired: false,
            TerminationKind: AgentTerminationKind.TransportFailure));

        var exception = await Assert.ThrowsAsync<AgentProtocolException>(() =>
            new FactoryAgentExecutor(backend, new FactoryAgentResultValidator()).ExecuteAsync(invocation, default));

        Assert.Equal("Agent exited with 1: process stopped before producing a semantic result", exception.Message);
    }

    [Fact]
    public async Task ProcessTelemetryReferencesLogsWithoutEmbeddingDiagnosticStreams()
    {
        using var temp = new TestWorkspace();
        var invocation = Invocation(temp);
        const string stdout = "stdout diagnostic payload";
        const string stderr = "stderr diagnostic payload";
        var backend = new ProcessResultBackend(new AgentProcessResult(
            1,
            stdout,
            stderr,
            CompleteResultObserved: false,
            KillRequired: false,
            TerminationKind: AgentTerminationKind.TransportFailure));

        await Assert.ThrowsAsync<AgentProtocolException>(() =>
            new FactoryAgentExecutor(backend, new FactoryAgentResultValidator()).ExecuteAsync(invocation, default));

        var telemetryPath = Path.Combine(Path.GetDirectoryName(invocation.RawResultPath)!, "process-telemetry.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(telemetryPath));
        var root = document.RootElement;

        Assert.False(root.TryGetProperty("stdout", out _));
        Assert.False(root.TryGetProperty("stderr", out _));
        Assert.Equal("stdout.log", root.GetProperty("stdoutLogPath").GetString());
        Assert.Equal("stderr.log", root.GetProperty("stderrLogPath").GetString());
        Assert.Equal(Encoding.UTF8.GetByteCount(stdout), root.GetProperty("stdoutBytes").GetInt32());
        Assert.Equal(Encoding.UTF8.GetByteCount(stderr), root.GetProperty("stderrBytes").GetInt32());
    }

    [Fact]
    public void LegacyProcessTelemetryStillDeserializesForRecoveryEvidence()
    {
        const string json = """
            {
              "exitCode": 0,
              "stdout": "legacy stdout",
              "stderr": "legacy stderr",
              "completeResultObserved": true,
              "killRequired": false,
              "terminationKind": "CleanExit"
            }
            """;

        var process = JsonSerializer.Deserialize<AgentProcessResult>(json, FactoryJson.Options);

        Assert.NotNull(process);
        Assert.True(process.CompleteResultObserved);
        Assert.False(process.KillRequired);
        Assert.Equal(AgentTerminationKind.CleanExit, process.TerminationKind);
    }

    private static AgentInvocation Invocation(TestWorkspace temp)
    {
        var agent = FactoryCapabilityCatalog.ResolveWorkItem("implementation").Agent;
        var attemptDirectory = Path.Combine(temp.Path, ".idd", "factory", "current", "attempts", "A000001");
        Directory.CreateDirectory(attemptDirectory);
        return new AgentInvocation
        {
            RunId = "run",
            AttemptId = "A000001",
            Capability = "implementation",
            Role = agent.Role,
            Workspace = temp.Path,
            RawResultPath = Path.Combine(attemptDirectory, "raw-result.json"),
            SkillName = agent.SkillName,
            ExecutionProfile = agent.ExecutionProfile,
            SemanticResultSchema = SemanticResultContracts.SchemaForCapability("implementation"),
            Input = "focused input",
            StartedAt = DateTimeOffset.UnixEpoch
        };
    }

    private sealed class ProcessResultBackend(AgentProcessResult result) : IAgentBackend
    {
        public Task<AgentRunHandle> StartAsync(AgentInvocation invocation, CancellationToken cancellationToken) =>
            Task.FromResult(new AgentRunHandle(invocation.AttemptId, 1, invocation.AttemptId));

        public Task<AgentProcessResult> WaitAsync(AgentRunHandle handle, CancellationToken cancellationToken) =>
            Task.FromResult(result);

        public Task CancelAsync(AgentRunHandle handle, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
