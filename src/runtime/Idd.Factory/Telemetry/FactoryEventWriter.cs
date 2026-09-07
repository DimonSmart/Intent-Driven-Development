using System.Text;
using System.Text.Json;

namespace Idd.Factory.Telemetry;

public interface IClock { DateTimeOffset UtcNow { get; } }
public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

public sealed class FactoryEventWriter(string currentDirectory, IClock clock)
{
    private static readonly TimeSpan[] OpenRetryDelays =
    [
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200)
    ];
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task WriteAsync(string runId, string type, object data, CancellationToken cancellationToken)
    {
        var entry = JsonSerializer.Serialize(new { schemaVersion = 1, timestamp = clock.UtcNow, runId, type, data });
        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(currentDirectory);
            await AppendAsync(Path.Combine(currentDirectory, "events.jsonl"), entry, cancellationToken);
        }
        finally { gate.Release(); }
    }

    private static async Task AppendAsync(string path, string entry, CancellationToken cancellationToken)
    {
        FileStream stream;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                stream = new FileStream(
                    path,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 4096,
                    useAsync: true);
                break;
            }
            catch (IOException) when (attempt < OpenRetryDelays.Length)
            {
                await Task.Delay(OpenRetryDelays[attempt], cancellationToken);
            }
        }

        await using (stream)
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            await writer.WriteLineAsync(entry.AsMemory(), cancellationToken);
    }
}
