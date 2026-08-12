using System.Text.Json;

namespace Idd.Factory.Telemetry;

public interface IClock { DateTimeOffset UtcNow { get; } }
public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

public sealed class FactoryEventWriter(string currentDirectory, IClock clock)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task WriteAsync(string runId, string type, object data, CancellationToken cancellationToken)
    {
        var entry = JsonSerializer.Serialize(new { schemaVersion = 1, timestamp = clock.UtcNow, runId, type, data });
        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(currentDirectory);
            await File.AppendAllTextAsync(Path.Combine(currentDirectory, "events.jsonl"), entry + Environment.NewLine, cancellationToken);
        }
        finally { gate.Release(); }
    }
}
