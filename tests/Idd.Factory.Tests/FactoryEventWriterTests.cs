using Idd.Factory.Telemetry;

namespace Idd.Factory.Tests;

public sealed class FactoryEventWriterTests
{
    [Fact]
    public async Task WriteAsync_RetriesBriefExclusiveFileLock()
    {
        using var temp = new TestWorkspace();
        var eventsPath = temp.Write("events.jsonl", string.Empty);
        var clock = new FixedClock(DateTimeOffset.Parse("2026-09-07T12:00:00Z"));
        var writer = new FactoryEventWriter(temp.Path, clock);
        using var fileLock = new FileStream(eventsPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var write = writer.WriteAsync("run-1", "test-event", new { value = 1 }, CancellationToken.None);
        Assert.False(write.IsCompleted);

        fileLock.Dispose();
        await write;

        var line = Assert.Single(await File.ReadAllLinesAsync(eventsPath));
        Assert.Contains("\"runId\":\"run-1\"", line, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"test-event\"", line, StringComparison.Ordinal);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }
}
