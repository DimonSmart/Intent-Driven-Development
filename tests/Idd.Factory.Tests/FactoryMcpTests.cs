using System.Reflection;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.State;
using Idd.Factory.Workflow;

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
    public async Task FactoryRunUsesPackagedRuntimePluginRootAndExactRequest()
    {
        using var workspace = new TemporaryDirectory();
        var invoker = new RecordingInvoker(Outcome(exitCode: 0));
        var runner = new FactoryRuntimeProcessRunner(invoker);
        const string request = "  Реализуй café без изменения пробелов.  \n";

        var result = await runner.RunAsync(FactoryRuntimeCommand.Run, workspace.Path, request, null, CancellationToken.None);

        Assert.Equal("COMPLETED", result.FactoryOutcome);
        Assert.Equal(request, invoker.Invocation!.StandardInput);
        AssertArguments(invoker.Invocation, "run", "--workspace", workspace.Path, "--plugin-root", FactoryRuntimeProcessRunner.ResolvePluginRoot(AppContext.BaseDirectory), "--request-stdin", "true");
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "idd-factory.dll"), invoker.Invocation.Arguments[0]);
        Assert.NotEqual(workspace.Path, ValueAfter(invoker.Invocation, "--plugin-root"));
    }

    [Fact]
    public async Task FactoryContinueWithoutAnswerDoesNotCreateAnswerArgument()
    {
        using var workspace = new TemporaryDirectory();
        var invoker = new RecordingInvoker(Outcome());

        await new FactoryRuntimeProcessRunner(invoker).RunAsync(FactoryRuntimeCommand.Continue, workspace.Path, null, null, CancellationToken.None);

        Assert.Contains("continue", invoker.Invocation!.Arguments);
        Assert.DoesNotContain("--answer-file", invoker.Invocation.Arguments);
        Assert.Null(invoker.Invocation.StandardInput);
    }

    [Fact]
    public async Task FactoryContinueUsesUtf8TemporaryAnswerAndDeletesIt()
    {
        using var workspace = new TemporaryDirectory();
        const string answer = "  Да, сохранить café и 漢字.  ";
        string? observedFile = null;
        var invoker = new RecordingInvoker(Outcome(), invocation =>
        {
            observedFile = ValueAfter(invocation, "--answer-file");
            Assert.Equal(answer, File.ReadAllText(observedFile));
            Assert.Equal(new byte[] { 0x20, 0x20, 0xD0 }, File.ReadAllBytes(observedFile)[..3]);
        });

        await new FactoryRuntimeProcessRunner(invoker).RunAsync(FactoryRuntimeCommand.Continue, workspace.Path, null, answer, CancellationToken.None);

        Assert.NotNull(observedFile);
        Assert.False(File.Exists(observedFile));
    }

    [Fact]
    public async Task TemporaryAnswerCleanupFailureCannotMaskValidFactoryResult()
    {
        using var workspace = new TemporaryDirectory();
        foreach (var cleanupFailure in new Exception[] { new IOException("locked"), new UnauthorizedAccessException("denied") })
        {
            var runner = new FactoryRuntimeProcessRunner(
                new RecordingInvoker(Outcome()),
                _ => throw cleanupFailure);

            var result = await runner.RunAsync(FactoryRuntimeCommand.Continue, workspace.Path, null, "answer", CancellationToken.None);

            Assert.Equal("COMPLETED", result.FactoryOutcome);
        }
    }

    [Fact]
    public async Task FactoryCancelUsesCliCancelVerb()
    {
        using var workspace = new TemporaryDirectory();
        var invoker = new RecordingInvoker(Outcome("CANCELLATION_REQUESTED", exitCode: 2));

        var result = await new FactoryRuntimeProcessRunner(invoker).RunAsync(FactoryRuntimeCommand.Cancel, workspace.Path, null, null, CancellationToken.None);

        Assert.Equal("CANCELLATION_REQUESTED", result.FactoryOutcome);
        Assert.Contains("cancel", invoker.Invocation!.Arguments);
        Assert.DoesNotContain("--request-stdin", invoker.Invocation.Arguments);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ValidOutcomeIsReturnedRegardlessOfProcessExitCode(int exitCode)
    {
        using var workspace = new TemporaryDirectory();
        var result = await new FactoryRuntimeProcessRunner(new RecordingInvoker(Outcome("BLOCKED", exitCode)))
            .RunAsync(FactoryRuntimeCommand.Cancel, workspace.Path, null, null, CancellationToken.None);
        Assert.Equal("BLOCKED", result.FactoryOutcome);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    public async Task MissingOrInvalidStdoutIsBoundedTransportProtocolError(string stdout)
    {
        using var workspace = new TemporaryDirectory();
        var stderr = new string('x', 5000);
        var exception = await Assert.ThrowsAsync<FactoryTransportException>(() =>
            new FactoryRuntimeProcessRunner(new RecordingInvoker(new(9, stdout, stderr)))
                .RunAsync(FactoryRuntimeCommand.Cancel, workspace.Path, null, null, CancellationToken.None));

        Assert.Equal("FACTORY_TRANSPORT_PROTOCOL_ERROR", exception.Code);
        Assert.Contains("Exit code: 9", exception.Message);
        Assert.True(exception.Message.Length < 2300);
    }

    [Fact]
    public async Task CancellationIsPropagatedToOwnedProcessInvocation()
    {
        using var workspace = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        var invoker = new CancellationAwareInvoker();
        var task = new FactoryRuntimeProcessRunner(invoker)
            .RunAsync(FactoryRuntimeCommand.Cancel, workspace.Path, null, null, cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(invoker.CancellationObserved);
    }

    [Theory]
    [InlineData("relative")]
    [InlineData("")]
    public async Task WorkspaceMustBeAbsolute(string workspace)
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            new FactoryRuntimeProcessRunner(new RecordingInvoker(Outcome()))
                .RunAsync(FactoryRuntimeCommand.Cancel, workspace, null, null, CancellationToken.None));
        Assert.Contains("absolute", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublicMcpCatalogContainsOnlyFactoryControlTools()
    {
        var names = typeof(FactoryMcpTools).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.GetCustomAttributesData()
                .Single(attribute => attribute.AttributeType.Name == "McpServerToolAttribute")
                .NamedArguments.Single(argument => argument.MemberName == "Name").TypedValue.Value?.ToString()
                ?? throw new InvalidOperationException("MCP tool name is missing."))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["factory_cancel", "factory_continue", "factory_run"], names);
    }

    [Fact]
    public async Task CliStateCanContinueThroughMcpTransport()
    {
        using var workspace = new TemporaryDirectory();
        await SeedClarificationStateAsync(workspace.Path, "cli-run");

        var result = await new FactoryRuntimeProcessRunner(new SystemFactoryProcessInvoker())
            .RunAsync(FactoryRuntimeCommand.Continue, workspace.Path, null, null, CancellationToken.None);

        Assert.Equal("NEEDS_CLARIFICATION", result.FactoryOutcome);
        Assert.Equal("cli-run", result.RunId);
    }

    [Fact]
    public async Task McpStateCanContinueThroughCliProcess()
    {
        using var workspace = new TemporaryDirectory();
        await SeedClarificationStateAsync(workspace.Path, "mcp-run");
        var invocation = FactoryRuntimeProcessRunner.BuildInvocation(
            FactoryRuntimeCommand.Continue,
            workspace.Path,
            null,
            null,
            Path.Combine(AppContext.BaseDirectory, "idd-factory.dll"),
            FactoryRuntimeProcessRunner.ResolvePluginRoot(AppContext.BaseDirectory));

        var process = await new SystemFactoryProcessInvoker().RunAsync(invocation, CancellationToken.None);
        var outcome = JsonSerializer.Deserialize<FactoryCliOutcome>(process.StandardOutput, FactoryJson.Options);

        Assert.NotNull(outcome);
        Assert.Equal("NEEDS_CLARIFICATION", outcome.FactoryOutcome);
        Assert.Equal("mcp-run", outcome.RunId);
    }

    [Fact]
    public async Task StdioServerPublishesOnlyProductionToolsWithoutDiagnosticsOnStdout()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "idd-factory.dll"));
        start.ArgumentList.Add("mcp");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("MCP test server did not start.");
        try
        {
            await process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"test\",\"version\":\"1\"}}}");
            var initialized = await ReadJsonLineAsync(process.StandardOutput);
            Assert.Equal(1, initialized.RootElement.GetProperty("id").GetInt32());
            await process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
            await process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}");
            var tools = await ReadJsonLineAsync(process.StandardOutput);

            var names = tools.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray()
                .Select(tool => tool.GetProperty("name").GetString())
                .Order(StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(new string?[] { "factory_cancel", "factory_continue", "factory_run" }, names);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    [Fact]
    public async Task CancellingProcessInvocationTerminatesTheOwnedChild()
    {
        var started = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invoker = new SystemFactoryProcessInvoker(processId => started.TrySetResult(processId));
        var invocation = new FactoryProcessInvocation(
            "dotnet",
            [Path.Combine(AppContext.BaseDirectory, "idd-factory.dll"), "mcp"],
            AppContext.BaseDirectory,
            null);
        using var cancellation = new CancellationTokenSource();
        var running = invoker.RunAsync(invocation, cancellation.Token);
        var processId = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(processId));
    }

    private static async Task<JsonDocument> ReadJsonLineAsync(StreamReader reader)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var line = await reader.ReadLineAsync(timeout.Token);
        Assert.False(string.IsNullOrWhiteSpace(line));
        return JsonDocument.Parse(line);
    }

    private static async Task SeedClarificationStateAsync(string workspace, string runId)
    {
        var workflow = new WorkflowDefinitionLoader().Load(workspace, Path.Combine(AppContext.BaseDirectory, "factory-workflow.yaml"));
        var current = Path.Combine(workspace, ".idd", "factory", "current");
        Directory.CreateDirectory(current);
        var state = StateStoreTests.State() with
        {
            RunId = runId,
            WorkflowName = workflow.Name,
            WorkflowHash = workflow.Hash,
            RunStatus = FactoryRunStatus.Blocked,
            Blocker = new("NEEDS_CLARIFICATION", "Choose one option.", "Continue with an answer.")
        };
        await new FileFactoryStateStore(current, new FactoryStateValidator()).CreateAsync(state, CancellationToken.None);
    }

    private static FactoryProcessResult Outcome(string outcome = "COMPLETED", int exitCode = 0) =>
        new(exitCode, JsonSerializer.Serialize(new FactoryCliOutcome(outcome, "run-1", "reason", "resume", "result"), FactoryJson.Options), "diagnostic");

    private static void AssertArguments(FactoryProcessInvocation invocation, params string[] expectedAfterAssembly) =>
        Assert.Equal(expectedAfterAssembly, invocation.Arguments.Skip(1));

    private static string ValueAfter(FactoryProcessInvocation invocation, string option)
    {
        var index = invocation.Arguments.IndexOf(option);
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

    private sealed class CancellationAwareInvoker : IFactoryProcessInvoker
    {
        public bool CancellationObserved { get; private set; }
        public async Task<FactoryProcessResult> RunAsync(FactoryProcessInvocation invocation, CancellationToken cancellationToken)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { CancellationObserved = true; throw; }
            throw new InvalidOperationException();
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "idd-mcp-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

internal static class ReadOnlyListTestExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> items, T value)
    {
        for (var index = 0; index < items.Count; index++)
            if (EqualityComparer<T>.Default.Equals(items[index], value)) return index;
        return -1;
    }
}
