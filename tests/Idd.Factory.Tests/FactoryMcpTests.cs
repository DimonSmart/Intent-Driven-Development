using System.Reflection;
using System.Text.Json;
using Idd.Factory.Configuration;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.Runtime;
using Idd.Factory.State;
using ModelContextProtocol;

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
    public async Task ProcessRunnerPreservesExactRunRequestInTemporaryUtf8BomFileAndUsesPackagedPluginRoot()
    {
        using var temp = new TestWorkspace();
        string? requestPath = null;
        byte[]? requestBytes = null;
        const string request = "  Реализуй café, Málaga, 漢字 и 🔒 без изменения пробелов.  \n";
        var invoker = new RecordingInvoker(Outcome(), invocation =>
        {
            requestPath = ValueAfter(invocation, "--request-file");
            requestBytes = File.ReadAllBytes(requestPath);
            Assert.Equal(request, File.ReadAllText(requestPath));
        });

        var result = await new FactoryRuntimeProcessRunner(invoker)
            .RunAsync(FactoryRuntimeCommand.Run, temp.Path, request, CancellationToken.None);

        Assert.Equal("COMPLETED", result.FactoryOutcome);
        Assert.Null(invoker.Invocation!.StandardInput);
        Assert.Contains("run", invoker.Invocation.Arguments);
        Assert.DoesNotContain("--request-stdin", invoker.Invocation.Arguments);
        Assert.NotNull(requestPath);
        Assert.False(File.Exists(requestPath));
        Assert.NotNull(requestBytes);
        Assert.True(requestBytes!.Length >= 3);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, requestBytes[..3]);
        Assert.Equal(FactoryRuntimeProcessRunner.ResolvePluginRoot(AppContext.BaseDirectory), ValueAfter(invoker.Invocation, "--plugin-root"));
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

    [Theory]
    [InlineData(nameof(FactoryMcpTools.FactoryRunAsync))]
    [InlineData(nameof(FactoryMcpTools.FactoryContinueAsync))]
    [InlineData(nameof(FactoryMcpTools.FactoryCancelAsync))]
    public void BlockingMcpToolsAcceptProgressSink(string methodName)
    {
        var method = typeof(FactoryMcpTools).GetMethod(methodName);

        Assert.NotNull(method);
        Assert.Contains(method.GetParameters(), parameter => parameter.ParameterType == typeof(IProgress<ProgressNotificationValue>));
    }

    [Fact]
    public void ActiveProgressNamesWorkAttemptCountsAndElapsedTime()
    {
        var startedAt = new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);
        var status = new FactoryStatusResult
        {
            Status = "ACTIVE",
            RunId = "run-1",
            CurrentWorkItemId = "W000003",
            CurrentAttemptId = "A000007",
            CurrentPhase = "Running",
            CompletedWorkCount = 2,
            RemainingWorkCount = 3,
            RuntimeOperation = "continue",
            RuntimeStartedAt = startedAt
        };

        var message = FactoryMcpTools.FormatActiveProgress(status, startedAt.AddMinutes(1).AddSeconds(5));

        Assert.Contains("Factory continue", message, StringComparison.Ordinal);
        Assert.Contains("work item W000003", message, StringComparison.Ordinal);
        Assert.Contains("attempt A000007", message, StringComparison.Ordinal);
        Assert.Contains("completed 2, remaining 3", message, StringComparison.Ordinal);
        Assert.Contains("active 1:05", message, StringComparison.Ordinal);
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
            PendingContinuation = new(ContinuationKind.Terminal, null, null, "NEEDS_CLARIFICATION", false)
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

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task StructuredOutcomeIsAuthoritativeAcrossExpectedExitCodes(int exitCode)
    {
        using var temp = new TestWorkspace();
        var result = await new FactoryRuntimeProcessRunner(new RecordingInvoker(Outcome("BLOCKED", exitCode)))
            .RunAsync(FactoryRuntimeCommand.Cancel, temp.Path, null, CancellationToken.None);

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
