using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.State;

namespace Idd.Factory.Tests;

public sealed class FactoryCancellationTests
{
    [Fact]
    public async Task CancellationArchivesCurrentRunAndAllowsANewRun()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        var runtime = FactoryRuntimeTestHarness.CreateRuntime(temp.Path, backend);
        var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        var store = new FileFactoryStateStore(current, new FactoryStateValidator());
        Directory.CreateDirectory(current);
        await File.WriteAllTextAsync(Path.Combine(current, "request.md"), "old request");
        await store.CreateAsync(StateStoreTests.State() with { RunId = "current-run" }, default);

        var cancelled = await runtime.CancelAsync(default);

        Assert.Equal("CANCELLED", cancelled.FactoryOutcome);
        Assert.False(Directory.Exists(current));
        var archive = Assert.Single(Directory.GetDirectories(Path.Combine(temp.Path, ".idd", "factory", "cancelled")));
        Assert.Equal("old request", await File.ReadAllTextAsync(Path.Combine(archive, "request.md")));
        var archivedState = JsonSerializer.Deserialize<FactoryState>(
            await File.ReadAllTextAsync(Path.Combine(archive, "state.json")),
            FactoryJson.Options);
        Assert.Equal(FactoryRunStatus.Cancelled, archivedState!.RunStatus);
        using (var cancellation = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(archive, "cancellation.json"))))
        {
            Assert.Equal(FactoryState.CurrentSchemaVersion, cancellation.RootElement.GetProperty("sourceStateSchemaVersion").GetInt32());
            Assert.Equal("CANCELLED", cancellation.RootElement.GetProperty("factoryOutcome").GetString());
        }

        backend.Enqueue(_ => "# Done");
        var restarted = await FactoryRuntimeTestHarness.CreateRuntime(temp.Path, backend)
            .RunRequestAsync("new request", "test", default);

        Assert.Equal("COMPLETED", restarted.FactoryOutcome);
    }

    [Fact]
    public async Task CurrentRuntimeCancelsLegacyStateWithoutDeserializingIt()
    {
        using var temp = new TestWorkspace();
        var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        Directory.CreateDirectory(current);
        await File.WriteAllTextAsync(Path.Combine(current, "request.md"), "legacy request");
        await File.WriteAllTextAsync(
            Path.Combine(current, "state.json"),
            """
            {
              "schemaVersion": 10,
              "runId": "legacy-run",
              "obsoleteSemanticOutcome": "must-not-be-interpreted"
            }
            """);

        var outcome = await FactoryRuntimeTestHarness.CreateRuntime(temp.Path, new FakeAgentBackend())
            .CancelAsync(default);

        Assert.Equal("CANCELLED", outcome.FactoryOutcome);
        Assert.Equal("legacy-run", outcome.RunId);
        Assert.False(Directory.Exists(current));
        var archive = Assert.Single(Directory.GetDirectories(Path.Combine(temp.Path, ".idd", "factory", "cancelled")));
        Assert.Equal("legacy request", await File.ReadAllTextAsync(Path.Combine(archive, "request.md")));
        using var cancellation = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(archive, "cancellation.json")));
        Assert.Equal(10, cancellation.RootElement.GetProperty("sourceStateSchemaVersion").GetInt32());
        Assert.Equal(FactoryState.CurrentSchemaVersion, cancellation.RootElement.GetProperty("cancelledByRuntimeSchemaVersion").GetInt32());
    }

    [Fact]
    public async Task RestartArchivesLegacyStateAndStartsReplacementRun()
    {
        using var temp = new TestWorkspace();
        var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        Directory.CreateDirectory(current);
        await File.WriteAllTextAsync(Path.Combine(current, "request.md"), "legacy request");
        await File.WriteAllTextAsync(Path.Combine(current, "state.json"), """{"schemaVersion":10,"runId":"legacy-run"}""");
        var backend = new FakeAgentBackend();
        backend.Enqueue(_ => "# Done");

        var outcome = await FactoryRuntimeTestHarness.CreateRuntime(temp.Path, backend)
            .RestartRequestAsync("replacement request", "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        var archive = Assert.Single(Directory.GetDirectories(Path.Combine(temp.Path, ".idd", "factory", "cancelled")));
        Assert.Equal("legacy request", await File.ReadAllTextAsync(Path.Combine(archive, "request.md")));
        Assert.Equal("replacement request", await File.ReadAllTextAsync(Path.Combine(outcome.ResultDirectory!, "request.md")));
    }
}
