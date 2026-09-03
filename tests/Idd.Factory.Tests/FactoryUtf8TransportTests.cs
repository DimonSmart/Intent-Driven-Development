using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.State;

namespace Idd.Factory.Tests;

public sealed class FactoryUtf8TransportTests
{
    [Fact]
    public async Task CorruptedRequestStopsBeforeLaunchingRuntime()
    {
        using var temp = new TestWorkspace();
        var invoker = new RecordingInvoker(Outcome());

        var result = await new FactoryRuntimeProcessRunner(invoker)
            .RunAsync(FactoryRuntimeCommand.Run, temp.Path, "Русский текст уже повреждён: \uFFFD", CancellationToken.None);

        Assert.Equal("INVALID_REQUEST_ENCODING", result.FactoryOutcome);
        Assert.Contains("U+FFFD", result.Reason!, StringComparison.Ordinal);
        Assert.False(invoker.WasInvoked);
    }

    [Fact]
    public async Task RuntimeRejectsUnmaterializedPastedRequestBeforeCreatingRun()
    {
        using var temp = new TestWorkspace();
        var runtime = FactoryRuntimeTestHarness.CreateRuntime(temp.Path, new FakeAgentBackend());
        var request = """
            # Files pasted by the user:

            ## "# ТЗ: Hover marquee": C:\Users\Dorog\.codex\attachments\53e3feb0-0388-4b56-9a6e-beff84debf4b\pasted-text.txt
            """;

        var error = await Assert.ThrowsAsync<FactoryStateException>(() =>
            runtime.RunRequestAsync(request, "test", CancellationToken.None));

        Assert.Equal("UNMATERIALIZED_REQUEST_INPUT", error.Code);
        Assert.Contains("self-contained request", error.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(temp.Path, ".idd", "factory", "current")));
    }

    [Fact]
    public async Task MaterializedUnicodeRequestIsPersistedWithoutExternalAttachmentDependency()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(_ => "# Done");
        const string request = """
            Implement the supplied hover-marquee requirements.

            ## Supplied requirements: Hover marquee
            Использовать passive pointer movement без повреждения Unicode.
            Unix application panels use 1003 + 1006.
            Preserve café, Málaga, 漢字 and 🔒 exactly.
            """;

        var outcome = await FactoryRuntimeTestHarness.CreateRuntime(temp.Path, backend)
            .RunRequestAsync(request, "test", CancellationToken.None);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        var persisted = await File.ReadAllTextAsync(Path.Combine(outcome.ResultDirectory!, "request.md"));
        Assert.Equal(request, persisted);
        Assert.DoesNotContain(".codex", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('\uFFFD', persisted);
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
