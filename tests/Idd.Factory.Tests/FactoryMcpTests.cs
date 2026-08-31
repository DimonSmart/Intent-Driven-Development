using System.Reflection;
using System.Text.Json;
using Idd.Factory.Configuration;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
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
    public void PublicMcpCatalogContainsOnlyFactoryControlTools()
    {
        var names = typeof(FactoryMcpTools).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.GetCustomAttributesData()
                .Single(attribute => attribute.AttributeType.Name == "McpServerToolAttribute")
                .NamedArguments.Single(argument => argument.MemberName == "Name").TypedValue.Value?.ToString())
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new string?[] { "factory_cancel", "factory_continue", "factory_run" }, names);
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
            PendingContinuation = new(ContinuationKind.Clarification, null, null, "NEEDS_CLARIFICATION", true, SemanticOperationKind.Decomposition, "original input")
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
}
