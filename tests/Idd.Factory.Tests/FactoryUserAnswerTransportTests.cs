using System.Text.Json;
using Idd.Factory.Domain;

namespace Idd.Factory.Tests;

public sealed class FactoryUserAnswerTransportTests
{
    [Fact]
    public async Task ProcessRunnerPassesExactContinueAnswerThroughTemporaryUtf8BomFile()
    {
        using var temp = new TestWorkspace();
        string? answerPath = null;
        byte[]? answerBytes = null;
        const string answer = "  Требовать подтверждение удаления café, Málaga и 🔒.  \n";
        var invoker = new RecordingInvoker(Outcome(), invocation =>
        {
            answerPath = ValueAfter(invocation, "--answer-file");
            answerBytes = File.ReadAllBytes(answerPath);
            Assert.Equal(answer, File.ReadAllText(answerPath));
            Assert.DoesNotContain("--request-file", invocation.Arguments);
        });

        var result = await new FactoryRuntimeProcessRunner(invoker)
            .RunAsync(FactoryRuntimeCommand.Continue, temp.Path, answer, CancellationToken.None);

        Assert.Equal("COMPLETED", result.FactoryOutcome);
        Assert.Contains("continue", invoker.Invocation!.Arguments);
        Assert.NotNull(answerPath);
        Assert.False(File.Exists(answerPath));
        Assert.NotNull(answerBytes);
        Assert.True(answerBytes!.Length >= 3);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, answerBytes[..3]);
    }

    private static FactoryProcessResult Outcome() =>
        new(0, JsonSerializer.Serialize(new FactoryCliOutcome("COMPLETED", "run-1"), FactoryJson.Options), "");

    private static string ValueAfter(FactoryProcessInvocation invocation, string option)
    {
        var index = invocation.Arguments.ToList().IndexOf(option);
        Assert.True(index >= 0 && index + 1 < invocation.Arguments.Count);
        return invocation.Arguments[index + 1];
    }

    private sealed class RecordingInvoker(FactoryProcessResult result, Action<FactoryProcessInvocation> inspect) : IFactoryProcessInvoker
    {
        public FactoryProcessInvocation? Invocation { get; private set; }

        public Task<FactoryProcessResult> RunAsync(FactoryProcessInvocation invocation, CancellationToken cancellationToken)
        {
            Invocation = invocation;
            inspect(invocation);
            return Task.FromResult(result);
        }
    }
}
