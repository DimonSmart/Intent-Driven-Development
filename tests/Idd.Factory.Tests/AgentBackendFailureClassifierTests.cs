using System.Text.Json;
using Idd.Factory.Agents;
using Idd.Factory.Domain;

namespace Idd.Factory.Tests;

public sealed class AgentBackendFailureClassifierTests
{
    [Fact]
    public void StructuredUsageLimitIsCapacityUnavailable()
    {
        var process = Failure(
            stdout: "{\"type\":\"turn.failed\",\"error\":{\"message\":\"You've hit your usage limit\"}}\n");

        var diagnostic = AgentBackendFailureClassifier.Classify(process);

        Assert.Equal("AGENT_CAPACITY_UNAVAILABLE", diagnostic.FailureCode);
        Assert.Equal("structured-agent-event", diagnostic.Source);
        Assert.Equal("You've hit your usage limit", diagnostic.HumanReadableMessage);
    }

    [Theory]
    [InlineData("HTTP 429 / Too Many Requests", "AGENT_RATE_LIMITED")]
    [InlineData("HTTP 429", "AGENT_RATE_LIMITED")]
    [InlineData("rate limit exceeded", "AGENT_RATE_LIMITED")]
    [InlineData("login required", "AGENT_AUTHENTICATION_REQUIRED")]
    [InlineData("401 Unauthorized", "AGENT_AUTHENTICATION_REQUIRED")]
    [InlineData("HTTP 401", "AGENT_AUTHENTICATION_REQUIRED")]
    [InlineData("403 Forbidden", "AGENT_TRANSPORT_FAILURE")]
    [InlineData("unexpected backend error", "AGENT_TRANSPORT_FAILURE")]
    public void StderrClassificationIsConservative(string stderr, string expectedCode)
    {
        var diagnostic = AgentBackendFailureClassifier.Classify(Failure(stderr: stderr));

        Assert.Equal(expectedCode, diagnostic.FailureCode);
        Assert.Equal("stderr", diagnostic.Source);
    }

    [Fact]
    public void ArbitraryStdoutWordsDoNotCreateExternalBlocker()
    {
        var diagnostic = AgentBackendFailureClassifier.Classify(
            Failure(stdout: "The task text mentions HTTP 429, quota, authentication and limit as examples."));

        Assert.Equal("AGENT_TRANSPORT_FAILURE", diagnostic.FailureCode);
        Assert.Equal("stdout", diagnostic.Source);
    }

    [Fact]
    public void CommandTimeoutKeepsTimeoutSemanticsEvenWhenDiagnosticMentionsQuota()
    {
        var diagnostic = AgentBackendFailureClassifier.Classify(
            Failure(
                stderr: "quota exhausted",
                terminationKind: AgentTerminationKind.CommandTimeout));

        Assert.Equal("AGENT_COMMAND_TIMEOUT", diagnostic.FailureCode);
    }

    [Fact]
    public async Task ExecutorPersistsNormalizedStructuredDiagnostic()
    {
        using var workspace = new TestWorkspace();
        var invocation = Invocation(workspace.Path, "A000001");
        var backend = new SingleProcessBackend(
            new AgentProcessResult(
                1,
                "{\"type\":\"turn.failed\",\"error\":{\"message\":\"You've hit your usage limit\"}}\n",
                "",
                false,
                false,
                AgentTerminationKind.TransportFailure));
        var executor = new FactoryAgentExecutor(backend);

        var exception = await Assert.ThrowsAsync<AgentProtocolException>(
            () => executor.ExecuteAsync(invocation, default));

        Assert.Equal("AGENT_CAPACITY_UNAVAILABLE", exception.Code);
        Assert.Equal("attempts/A000001/failure-diagnostic.json", exception.DiagnosticReference);
        var diagnosticPath = Path.Combine(
            Path.GetDirectoryName(invocation.SemanticOutputPath)!,
            "failure-diagnostic.json");
        var diagnostic = JsonSerializer.Deserialize<AgentFailureDiagnostic>(
            await File.ReadAllTextAsync(diagnosticPath),
            FactoryJson.Options)!;
        Assert.Equal("structured-agent-event", diagnostic.Source);
        Assert.Equal("You've hit your usage limit", diagnostic.HumanReadableMessage);
        var json = await File.ReadAllTextAsync(diagnosticPath);
        Assert.DoesNotContain("turn.failed", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecoveryReconstructsStructuredDiagnosticWhenArtifactIsMissing()
    {
        using var workspace = new TestWorkspace();
        var attemptDirectory = Path.Combine(workspace.Path, "attempts", "A000001");
        Directory.CreateDirectory(attemptDirectory);
        var process = Failure();
        await File.WriteAllTextAsync(
            Path.Combine(attemptDirectory, "process-telemetry.json"),
            JsonSerializer.Serialize(process, FactoryJson.Options));
        await File.WriteAllTextAsync(
            Path.Combine(attemptDirectory, "stdout.log"),
            "{\"type\":\"turn.failed\",\"error\":{\"message\":\"You've hit your usage limit\"}}\n");
        await File.WriteAllTextAsync(Path.Combine(attemptDirectory, "stderr.log"), "");

        var recovered = await AgentFailureDiagnosticStore.ReadOrRecoverAsync(
            "A000001",
            attemptDirectory,
            default);

        Assert.NotNull(recovered);
        Assert.Equal("AGENT_CAPACITY_UNAVAILABLE", recovered!.FailureCode);
        Assert.Equal("structured-agent-event", recovered.Source);
        Assert.True(File.Exists(Path.Combine(attemptDirectory, "failure-diagnostic.json")));
        var repeated = await AgentFailureDiagnosticStore.ReadOrRecoverAsync(
            "A000001",
            attemptDirectory,
            default);
        Assert.Equal(recovered, repeated);
    }

    [Fact]
    public async Task RecoveryDoesNotInventFailureForCleanExit()
    {
        using var workspace = new TestWorkspace();
        var attemptDirectory = Path.Combine(workspace.Path, "attempts", "A000001");
        Directory.CreateDirectory(attemptDirectory);
        var process = new AgentProcessResult(
            0,
            "",
            "",
            false,
            false,
            AgentTerminationKind.CleanExit);
        await File.WriteAllTextAsync(
            Path.Combine(attemptDirectory, "process-telemetry.json"),
            JsonSerializer.Serialize(process, FactoryJson.Options));
        await File.WriteAllTextAsync(
            Path.Combine(attemptDirectory, "stdout.log"),
            "{\"type\":\"turn.failed\",\"error\":{\"message\":\"quota exhausted\"}}\n");

        var recovered = await AgentFailureDiagnosticStore.ReadOrRecoverAsync(
            "A000001",
            attemptDirectory,
            default);

        Assert.Null(recovered);
        Assert.False(File.Exists(Path.Combine(attemptDirectory, "failure-diagnostic.json")));
    }

    [Fact]
    public async Task TrustedCompleteResultWinsOverTrailingQuotaDiagnostic()
    {
        using var workspace = new TestWorkspace();
        var invocation = Invocation(workspace.Path, "A000001");
        var backend = new SingleProcessBackend(
            new AgentProcessResult(
                1,
                "",
                "You've hit your usage limit",
                true,
                false,
                AgentTerminationKind.TransportFailure),
            "Trusted semantic result.");
        var executor = new FactoryAgentExecutor(backend);

        var result = await executor.ExecuteAsync(invocation, default);

        Assert.Equal("Trusted semantic result.", result.Result.SemanticResult);
        Assert.False(File.Exists(Path.Combine(
            Path.GetDirectoryName(invocation.SemanticOutputPath)!,
            "failure-diagnostic.json")));
    }

    private static AgentProcessResult Failure(
        string stdout = "",
        string stderr = "",
        AgentTerminationKind terminationKind = AgentTerminationKind.TransportFailure) =>
        new(1, stdout, stderr, false, false, terminationKind);

    private static AgentInvocation Invocation(string workspace, string attemptId)
    {
        var directory = Path.Combine(workspace, ".idd", "factory", "current", "attempts", attemptId);
        Directory.CreateDirectory(directory);
        return new AgentInvocation
        {
            RunId = "run",
            AttemptId = attemptId,
            Capability = "implementation",
            Role = "executor",
            WorkItemId = "W000001",
            InvocationKind = WorkItemInvocationKind.Initial,
            SemanticAttemptNumber = 1,
            TechnicalRestartNumber = 0,
            Workspace = workspace,
            SemanticOutputPath = Path.Combine(directory, "semantic-result.md"),
            SkillName = "idd-factory-execute-subtask",
            ExecutionProfile = AgentExecutionProfile.WorkspaceWrite,
            Input = "work",
            StartedAt = DateTimeOffset.UtcNow
        };
    }

    private sealed class SingleProcessBackend(
        AgentProcessResult process,
        string? semanticResult = null) : IAgentBackend
    {
        public Task<AgentRunHandle> StartAsync(AgentInvocation invocation, CancellationToken cancellationToken)
        {
            if (semanticResult is not null)
                File.WriteAllText(invocation.SemanticOutputPath, semanticResult);
            return Task.FromResult(new AgentRunHandle(invocation.AttemptId, 1, invocation.AttemptId));
        }

        public Task<AgentProcessResult> WaitAsync(AgentRunHandle handle, CancellationToken cancellationToken) =>
            Task.FromResult(process);

        public Task CancelAsync(AgentRunHandle handle, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
