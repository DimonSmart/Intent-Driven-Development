using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.State;

namespace Idd.Factory.Tests;

public sealed class FactoryRestartSemanticsTests
{
    [Fact]
    public async Task RestartArchivesSupportedStateAndStartsReplacementRun()
    {
        using var temp = new TestWorkspace();
        var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        Directory.CreateDirectory(current);
        await File.WriteAllTextAsync(Path.Combine(current, "request.md"), "original request");
        var store = new FileFactoryStateStore(current, new FactoryStateValidator());
        await store.CreateAsync(StateStoreTests.State() with { RunId = "supported-run" }, default);
        var backend = new ScriptedAgentBackend();
        backend.Reply("# Done");

        var outcome = await FactoryTestRuntime.Create(temp.Path, backend)
            .RestartRequestAsync("replacement request", "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        var archive = Assert.Single(Directory.GetDirectories(Path.Combine(temp.Path, ".idd", "factory", "cancelled")));
        Assert.Equal("original request", await File.ReadAllTextAsync(Path.Combine(archive, "request.md")));
        Assert.Equal("replacement request", await File.ReadAllTextAsync(Path.Combine(outcome.ResultDirectory!, "request.md")));
    }

    [Fact]
    public async Task CancelArchivesSupportedStateWithoutStartingReplacementRun()
    {
        using var temp = new TestWorkspace();
        var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        Directory.CreateDirectory(current);
        await File.WriteAllTextAsync(Path.Combine(current, "request.md"), "original request");
        var store = new FileFactoryStateStore(current, new FactoryStateValidator());
        await store.CreateAsync(StateStoreTests.State() with { RunId = "supported-run" }, default);

        var outcome = await FactoryTestRuntime.Create(temp.Path, new ScriptedAgentBackend())
            .CancelAsync(default);

        Assert.Equal("CANCELLED", outcome.FactoryOutcome);
        Assert.False(Directory.Exists(current));
        Assert.Single(Directory.GetDirectories(Path.Combine(temp.Path, ".idd", "factory", "cancelled")));
    }

    [Fact]
    public async Task LegacyStateRecoveryMessageOffersRestartOrCancelAsAlternatives()
    {
        using var temp = new TestWorkspace();
        var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        Directory.CreateDirectory(current);
        await File.WriteAllTextAsync(
            Path.Combine(current, "state.json"),
            """{"schemaVersion":10,"runId":"legacy-run"}""");
        var store = new FileFactoryStateStore(current, new FactoryStateValidator());

        var exception = await Assert.ThrowsAsync<FactoryStateException>(() => store.LoadAsync(default));

        Assert.Equal("LEGACY_FACTORY_STATE", exception.Code);
        Assert.Contains("factory_restart", exception.Message, StringComparison.Ordinal);
        Assert.Contains("factory_cancel", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("cancel it and start a new run", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
