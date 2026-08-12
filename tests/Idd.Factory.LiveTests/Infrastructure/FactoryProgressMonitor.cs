using System.Text.Json;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed class FactoryProgressMonitor(FactoryEvalWorkspace workspace)
{
    private readonly HashSet<string> observedEvents = new(StringComparer.Ordinal);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await ProjectChangesAsync(cancellationToken);
            await Task.Delay(250, cancellationToken);
        }
    }

    public async Task ProjectChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var path in EventLogs())
        {
            string[] lines;
            try { lines = await File.ReadAllLinesAsync(path, cancellationToken); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FileNotFoundException) { continue; }
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || !observedEvents.Add(line)) continue;
                var projected = Project(line);
                if (projected is not null)
                    await File.AppendAllTextAsync(workspace.ProgressPath, projected + Environment.NewLine, cancellationToken);
            }
        }
    }

    private IEnumerable<string> EventLogs()
    {
        var factory = Path.Combine(workspace.WorkspaceDirectory, ".idd", "factory");
        var current = Path.Combine(factory, "current", "events.jsonl");
        if (File.Exists(current)) yield return current;
        var results = Path.Combine(factory, "results");
        if (!Directory.Exists(results)) yield break;
        foreach (var path in Directory.EnumerateFiles(results, "events.jsonl", SearchOption.AllDirectories)) yield return path;
    }

    internal static string? Project(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var timestamp = root.TryGetProperty("timestamp", out var timestampNode) && timestampNode.TryGetDateTimeOffset(out var parsed)
                ? parsed.ToLocalTime().ToString("HH:mm:ss")
                : DateTimeOffset.Now.ToString("HH:mm:ss");
            var type = root.GetProperty("type").GetString();
            var data = root.TryGetProperty("data", out var value) ? value : default;
            string? Text(string name) => data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var node) && node.ValueKind == JsonValueKind.String ? node.GetString() : null;
            var detail = type switch
            {
                "run-created" => "Factory run created",
                "workflow-step-started" => $"workflow {Text("id") ?? Text("Id") ?? "unknown"} started",
                "workflow-step-finished" => $"workflow {Text("id") ?? Text("Id") ?? "unknown"} {Text("outcome") ?? "finished"}",
                "agent-dispatching" => $"{Text("attemptId")} {Text("role")} {Text("workItemId") ?? "task"} started",
                "agent-completed" => $"{Text("attemptId")} {Text("role")} {Text("Outcome") ?? Text("outcome") ?? "unknown"}",
                "agent-result-reused" => $"{Text("attemptId")} {Text("role")} result reused",
                "verification-started" => string.Join(' ', new[] { "verification", Text("verificationContext") ?? "unknown", Text("workItemId"), "started" }.Where(part => !string.IsNullOrWhiteSpace(part))),
                "verification-completed" => $"verification {Text("verificationContext") ?? "unknown"} {Text("verificationStatus") ?? "completed"}",
                "run-blocked" => $"Factory blocked: {Text("code") ?? "unknown"}",
                "run-cancelled" => "Factory cancelled",
                "run-completed" => "Factory completed",
                _ => null
            };
            return detail is null ? null : $"{timestamp} {detail}";
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException) { return null; }
    }
}
