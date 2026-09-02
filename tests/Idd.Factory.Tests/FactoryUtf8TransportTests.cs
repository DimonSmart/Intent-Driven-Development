using System.Text.Json;
using Idd.Factory.Domain;

namespace Idd.Factory.Tests;

public sealed class FactoryUtf8TransportTests
{
    [Fact]
    public async Task CorruptedRequestStopsBeforeLaunchingRuntime()
    {
        using var temp = new TestWorkspace();
        var invoker = new RecordingInvoker(Outcome());

        var result = await new FactoryRuntimeProcessRunner(invoker)
            .RunAsync(FactoryRuntimeCommand.Run, temp.Path, "Русский текст уже повреждён: \uFFFD", null, CancellationToken.None);

        Assert.Equal("INVALID_REQUEST_ENCODING", result.FactoryOutcome);
        Assert.Contains("U+FFFD", result.Reason, StringComparison.Ordinal);
        Assert.False(invoker.WasInvoked);
    }

    [Fact]
    public async Task ClarificationTransportFileUsesUtf8BomAndPreservesUnicode()
    {
        using var temp = new TestWorkspace();
        const string answer = "Да, сохранить café, Málaga, 漢字 и 🔒.";
        byte[]? bytes = null;
        var invoker = new RecordingInvoker(Outcome(), invocation =>
        {
            var index = invocation.Arguments.ToList().IndexOf("--answer-file");
            Assert.True(index >= 0 && index + 1 < invocation.Arguments.Count);
            bytes = File.ReadAllBytes(invocation.Arguments[index + 1]);
            Assert.Equal(answer, File.ReadAllText(invocation.Arguments[index + 1]));
        });

        await new FactoryRuntimeProcessRunner(invoker)
            .RunAsync(FactoryRuntimeCommand.Continue, temp.Path, null, answer, CancellationToken.None);

        Assert.NotNull(bytes);
        Assert.True(bytes!.Length >= 3);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
    }

    private static FactoryProcessResult Outcome() =>
        new(0, JsonSerializer.Serialize(new FactoryCliOutcome("COMPLETED", "run-1"), FactoryJson.Options), "");

    private sealed class RecordingInvoker(FactoryProcessResult result, Action<FactoryProcessInvocation>? inspect = null) : IFactoryProcessInvoker
    {
        public bool WasInvoked { get; private set; }

        public Task<FactoryProcessResult> RunAsync(FactoryProcessInvocation invocation, CancellationToken cancellationToken)
        {
            WasInvoked = true;
            inspect?.Invoke(invocation);
            return Task.FromResult(result);
        }
    }
}
