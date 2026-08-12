using System.Text.Json;
using Idd.Factory.LiveTests.Models;

namespace Idd.Factory.LiveTests.Infrastructure;

public static class FactoryRuntimeTraceReader
{
    public static AgentTrace? TryRead(string workspace, string rootThreadId)
    {
        var results = Path.Combine(workspace, ".idd", "factory", "results");
        if (!Directory.Exists(results)) return null;
        var eventLogs = Directory.GetFiles(results, "events.jsonl", SearchOption.AllDirectories); if (eventLogs.Length != 1) return null;
        var attempts = new Dictionary<string, Mutable>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(eventLogs[0]))
        {
            try
            {
                using var document = JsonDocument.Parse(line); var root = document.RootElement;
                var type = root.GetProperty("type").GetString(); var timestamp = root.TryGetProperty("timestamp", out var time) ? time.GetDateTimeOffset() : (DateTimeOffset?)null;
                if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("attemptId", out var idNode)) continue;
                var id = idNode.GetString()!; if (!attempts.TryGetValue(id, out var attempt)) attempts[id] = attempt = new(id);
                if (data.TryGetProperty("role", out var role)) attempt.Role = role.GetString() ?? "unknown";
                if (data.TryGetProperty("workItemId", out var work) && work.ValueKind == JsonValueKind.String) attempt.WorkItem = work.GetString();
                if (type == "agent-dispatching") attempt.StartedAt = timestamp;
                if (type is "agent-completed" or "agent-result-reused") { attempt.CompletedAt = timestamp; if (data.TryGetProperty("metrics", out var metrics) && metrics.ValueKind == JsonValueKind.Object) attempt.ReadMetrics(metrics); }
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException) { }
        }
        if (attempts.Count == 0) return null;
        var nodes = new List<AgentTraceNode> { new(rootThreadId, null, "factory-root", null, null, "completed", null, null, null, 0, 0, null, null, null, null, null) };
        nodes.AddRange(attempts.Values.OrderBy(x => x.StartedAt).Select(x => x.Node(rootThreadId)));
        return new(2, rootThreadId, nodes, []);
    }

    private sealed class Mutable(string id)
    {
        public string Id { get; } = id; public string Role { get; set; } = "unknown"; public string? WorkItem { get; set; } public DateTimeOffset? StartedAt { get; set; } public DateTimeOffset? CompletedAt { get; set; }
        public long? Input { get; set; } public long? Cached { get; set; } public long? Output { get; set; }
        public void ReadMetrics(JsonElement metrics) { Input = Number(metrics, "input_tokens", "inputTokens"); Cached = Number(metrics, "cached_input_tokens", "cachedInputTokens"); Output = Number(metrics, "output_tokens", "outputTokens"); }
        public AgentTraceNode Node(string root) => new(Id, root, Role, WorkItem, null, "completed", StartedAt, CompletedAt, StartedAt is not null && CompletedAt is not null ? (long)(CompletedAt.Value - StartedAt.Value).TotalMilliseconds : null, 1, 0, Input, Cached, Output, null, Input is not null && Output is not null ? Input + Output : null);
        private static long? Number(JsonElement value, params string[] names) { foreach (var name in names) if (value.TryGetProperty(name, out var node) && node.TryGetInt64(out var number)) return number; return null; }
    }
}
