using System.Reflection;
using System.Text.Json;
using Idd.Factory.Configuration;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.Runtime;
using Idd.Factory.State;

namespace Idd.Factory.Tests;

public sealed class FactoryMcpTests
{
    [Fact]
    public async Task ProgramDispatchesExactMcpCommand()
    {
        var invoked = false;
        var exitCode = await FactoryProgram.RunAsync(["mcp"], () =>
        {
            invoked = true;
            return Task.FromResult(17);
        });

        Assert.True(invoked);
        Assert.Equal(17, exitCode);
    }

    [Fact]
    public async Task ProcessRunnerPreservesExactRunRequestAndUsesPackagedPluginRoot()
    {
        using var temp = new TestWorkspace();
        var invoker = new RecordingInvoker(Outcome());
        const string request = "  Реализуй café без изменения пробелов.  \n";

        var result = await new FactoryRuntimeProcessRunner(invoker)
            .RunAsync(FactoryRuntimeCommand.Run, temp.Path, request, null, CancellationToken.None);

        Assert.Equal("COMPLETED", result.FactoryOutcome);
        Assert.Equal(request, invoker.Invocation!.StandardInput);
        Assert.Contains("run", invoker.Invocation.Arguments);
        Assert.Contains("--request-stdin", invoker.Invocation.Arguments);
        Assert.Equal("true", ValueAfter(invoker.Invocation, "--request-stdin"));
        Assert.Equal(FactoryRuntimeProcessRunner.ResolvePluginRoot(AppContext.BaseDirectory), ValueAfter(invoker.Invocation, "--plugin-root"));
    }

    [Fact]
    public async Task ContinueAnswerUsesTemporaryUtf8FileAndCleansIt()
    {
        using var temp = new TestWorkspace();
        string? answerPath = null;
        const string answer = "Да, сохранить café и 漢字.";
        var invoker = new RecordingInvoker(Outcome(), invocation =>
        {
            answerPath = ValueAfter(invocation, "--answer-file");
            Assert.Equal(answer, File.ReadAllText(answerPath));
        });

        await new FactoryRuntimeProcessRunner(invoker).RunAsync(FactoryRuntimeCommand.Continue, temp.Path, null, answer, CancellationToken.None);

        Assert.NotNull(answerPath);
        Assert.False(File.Exists(answerPath));
    }

    [Fact]
    public void PublicMcpCatalogContainsFactoryControlAndStatusTools()
    {
        var names = typeof(FactoryMcpTools).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.GetCustomAttributesData()
                .Single(attribute => attribute.AttributeType.Name == "McpServerToolAttribute")
                .NamedArguments.Single(argument => argument.MemberName == "Name").TypedValue.Value?.ToString())
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new string?[] { "factory_cancel", "factory_continue", "factory_run", "factory_status" }, names);
    }

    [Fact]
    public async Task StatusReportsActiveRuntimeWithoutStartingAnotherRuntime()
    {
        using var temp = new TestWorkspace();
        var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        await new FileFactoryStateStore(current, new FactoryStateValidator()).CreateAsync(
            StateStoreTests.State() with { RunId = "active-run" },
            CancellationToken.None);
        var lockPath = Path.Combine(temp.Path, ".idd", "factory", "runtime.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        var startedAt = new DateTimeOffset(2026, 9, 1, 21, 29, 0, TimeSpan.Zero);

        await using var held = FactoryRuntimeLock.Acquire(lockPath, "run", startedAt);
        var status = await new FactoryStatusReader().ReadAsync(temp.Path, CancellationToken.None);

        Assert.Equal("ACTIVE", status.Status);
        Assert.Equal("active-run", status.RunId);
        Assert.Null(status.FactoryOutcome);
        Assert.Equal(Environment.ProcessId, status.RuntimeProcessId);
        Assert.Equal(Environment.MachineName, status.RuntimeMachineName);
        Assert.Equal("run", status.RuntimeOperation);
        Assert.Equal(startedAt, status.RuntimeStartedAt);
        Assert.Contains("timed-out MCP response", status.Reason!, StringComparison.Ordinal);
        Assert.Contains("Do not call factory_run or factory_continue", status.ResumeWhen!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatusReportsReadyToContinueWhenPersistedRunHasNoOwner()
    {
        using var temp = new TestWorkspace();
        var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        await new FileFactoryStateStore(current, new FactoryStateValidator()).CreateAsync(
            StateStoreTests.State() with { RunId = "interrupted-run" },
            CancellationToken.None);

        var status = await new FactoryStatusReader().ReadAsync(temp.Path, CancellationToken.None);

        Assert.Equal("READY_TO_CONTINUE", status.Status);
        Assert.Equal("interrupted-run", status.RunId);
        Assert.Contains("factory_continue", status.ResumeWhen!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatusReportsPersistedBlockerWithoutResumingIt()
    {
        using var temp = new TestWorkspace();
        var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        var state = StateStoreTests.State() with
        {
            RunId = "blocked-run",
            RunStatus = FactoryRunStatus.Blocked,
            Blocker = new("NEEDS_CLARIFICATION", "Choose one option.", "Continue with an answer."),
            PendingContinuation = new(ContinuationKind.Clarification, null, null, "NEEDS_CLARIFICATION", true, SemanticOperationKind.Planning, "original input")
        };
        await new FileFactoryStateStore(current, new FactoryStateValidator()).CreateAsync(state, CancellationToken.None);

        var status = await new FactoryStatusReader().ReadAsync(temp.Path, CancellationToken.None);

        Assert.Equal("WAITING_FOR_CONTINUATION", status.Status);
        Assert.Equal("blocked-run", status.RunId);
        Assert.Equal("NEEDS_CLARIFICATION", status.FactoryOutcome);
        Assert.Equal("Choose one option.", status.Reason);
        Assert.Equal("Continue with an answer.", status.ResumeWhen);
    }

    [Fact]
    public async Task StatusReportsLatestCompletedResultWhenCurrentRunWasFinalized()
    {
        using var temp = new TestWorkspace();
        var resultDirectory = Path.Combine(temp.Path, ".idd", "factory", "results", "completed-run");
        Directory.CreateDirectory(resultDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(resultDirectory, "factory-result.json"),
            "{\"factoryOutcome\":\"COMPLETED\"}");
        await File.WriteAllTextAsync(
            Path.Combine(resultDirectory, "state.json"),
            "{\"runId\":\"completed-run-id\"}");

        var status = await new FactoryStatusReader().ReadAsync(temp.Path, CancellationToken.None);

        Assert.Equal("COMPLETED", status.Status);
        Assert.Equal("COMPLETED", status.FactoryOutcome);
        Assert.Equal("completed-run-id", status.RunId);
        Assert.Equal(resultDirectory, status.ResultDirectory);
    }

    [Fact]
    public async Task PersistedClarificationStateCanContinueThroughCliTransportWithoutWorkflowDefinition()
    {
        using var temp = new TestWorkspace();
        var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        Directory.CreateDirectory(current);
        temp.Write(".idd/factory/current/request.md", "Task");
        var packaged = Path.Combine(AppContext.BaseDirectory, "factory.yaml");
        var configuration = new FactoryConfigurationLoader().Load(temp.Path, packaged);
        var state = StateStoreTests.State() with
        {
            RunId = "transport-run",
            FactoryConfigurationHash = configuration.Hash,
            RunStatus = FactoryRunStatus.Blocked,
            Blocker = new("NEEDS_CLARIFICATION", "Choose one option.", "Continue with an answer."),
            PendingContinuation = new(ContinuationKind.Clarification, null, null, "NEEDS_CLARIFICATION", true, SemanticOperationKind.Planning, "original input")
        };
        await new FileFactoryStateStore(current, new FactoryStateValidator()).CreateAsync(state, CancellationToken.None);

        var invocation = FactoryRuntimeProcessRunner.BuildInvocation(
            FactoryRuntimeCommand.Continue,
            temp.Path,
            null,
            null,
            Path.Combine(AppContext.BaseDirectory, "idd-factory.dll"),
            FactoryRuntimeProcessRunner.ResolvePluginRoot(AppContext.BaseDirectory));
        var process = await new SystemFactoryProcessInvoker().RunAsync(invocation, CancellationToken.None);
        var outcome = JsonSerializer.Deserialize<FactoryCliOutcome>(process.StandardOutput, FactoryJson.Options);

        Assert.NotNull(outcome);
        Assert.Equal("NEEDS_CLARIFICATION", outcome.FactoryOutcome);
        Assert.Equal("transport-run", outcome.RunId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task StructuredOutcomeIsAuthoritativeAcrossExpectedExitCodes(int exitCode)
    {
        using var temp = new TestWorkspace();
        var result = await new FactoryRuntimeProcessRunner(new RecordingInvoker(Outcome("BLOCKED", exitCode)))
            .RunAsync(FactoryRuntimeCommand.Cancel, temp.Path, null, null, CancellationToken.None);

        Assert.Equal("BLOCKED", result.FactoryOutcome);
    }

    private static FactoryProcessResult Outcome(string outcome = "COMPLETED", int exitCode = 0) =>
        new(exitCode, JsonSerializer.Serialize(new FactoryCliOutcome(outcome, "run-1", "reason", "resume", "result"), FactoryJson.Options), "diagnostic");

    private static string ValueAfter(FactoryProcessInvocation invocation, string option)
    {
        var index = invocation.Arguments.ToList().IndexOf(option);
        Assert.True(index >= 0 && index + 1 < invocation.Arguments.Count);
        return invocation.Arguments[index + 1];
    }

    private sealed class RecordingInvoker(FactoryProcessResult result, Action<FactoryProcessInvocation>? inspect = null) : IFactoryProcessInvoker
    {
        public FactoryProcessInvocation? Invocation { get; private set; }

        public Task<FactoryProcessResult> RunAsync(FactoryProcessInvocation invocation, CancellationToken cancellationToken)
        {
            Invocation = invocation;
            inspect?.Invoke(invocation);
            return Task.FromResult(result);
        }
    }
}
