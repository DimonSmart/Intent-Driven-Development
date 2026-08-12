using System.Text.Json;
using Idd.Factory.LiveTests.Models;

namespace Idd.Factory.LiveTests.Infrastructure;

public static class FactoryRuntimeTraceReader
{
    public static AgentTrace? TryRead(string workspace, string rootThreadId, bool processInterrupted = false)
    {
        var runDirectory = FindRunDirectory(workspace);
        if (runDirectory is null) return null;

        var eventLog = Path.Combine(runDirectory, "events.jsonl");
        var attempts = new Dictionary<string, Mutable>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(eventLog))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var type = root.GetProperty("type").GetString();
                var timestamp = root.TryGetProperty("timestamp", out var time) ? time.GetDateTimeOffset() : (DateTimeOffset?)null;
                if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("attemptId", out var idNode)) continue;

                var id = idNode.GetString()!;
                if (!attempts.TryGetValue(id, out var attempt)) attempts[id] = attempt = new(id);
                if (data.TryGetProperty("role", out var role)) attempt.Role = role.GetString() ?? "unknown";
                if (data.TryGetProperty("workItemId", out var work) && work.ValueKind == JsonValueKind.String) attempt.WorkItem = work.GetString();
                if (type == "agent-dispatching") attempt.StartedAt = timestamp;
                if (type is "agent-completed" or "agent-result-reused")
                {
                    attempt.CompletedAt = timestamp;
                    attempt.CompletionWasRecorded = true;
                    if (data.TryGetProperty("Outcome", out var outcome) || data.TryGetProperty("outcome", out outcome))
                        attempt.Outcome = outcome.GetString();
                    if (data.TryGetProperty("metrics", out var metrics) && metrics.ValueKind == JsonValueKind.Object) attempt.ReadMetrics(metrics);
                }
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException) { }
        }
        if (attempts.Count == 0) return null;

        foreach (var attempt in attempts.Values) attempt.ReadArtifacts(runDirectory);
        var diagnostics = attempts.Values
            .Where(attempt => attempt.ResultWasProduced && !attempt.CompletionWasRecorded)
            .Select(attempt => new AgentTraceDiagnostic(
                "RUNTIME_RESULT_NOT_RECORDED",
                "warning",
                $"Attempt {attempt.Id} produced result.json, but the runtime did not record agent completion.",
                attempt.Id,
                $"attempts/{attempt.Id}/result.json"))
            .ToArray();

        var rootStatus = processInterrupted ? "interrupted" : "completed";
        var nodes = new List<AgentTraceNode> { new(rootThreadId, null, "factory-root", null, null, rootStatus, null, null, null, 0, 0, null, null, null, null, null) };
        nodes.AddRange(attempts.Values.OrderBy(attempt => attempt.StartedAt).Select(attempt => attempt.Node(rootThreadId, processInterrupted)));
        return new(2, rootThreadId, nodes, diagnostics);
    }

    private static string? FindRunDirectory(string workspace)
    {
        var factory = Path.Combine(workspace, ".idd", "factory");
        var current = Path.Combine(factory, "current");
        if (File.Exists(Path.Combine(current, "events.jsonl"))) return current;

        var results = Path.Combine(factory, "results");
        if (!Directory.Exists(results)) return null;
        var eventLogs = Directory.GetFiles(results, "events.jsonl", SearchOption.AllDirectories);
        return eventLogs.Length == 1 ? Path.GetDirectoryName(eventLogs[0]) : null;
    }

    private sealed class Mutable(string id)
    {
        public string Id { get; } = id;
        public string Role { get; set; } = "unknown";
        public string? WorkItem { get; set; }
        public string? Outcome { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public bool CompletionWasRecorded { get; set; }
        public bool ResultWasProduced { get; set; }
        public long? Input { get; set; }
        public long? Cached { get; set; }
        public long? Output { get; set; }
        public long? Reasoning { get; set; }

        public void ReadMetrics(JsonElement metrics)
        {
            Input = Number(metrics, "input_tokens", "inputTokens");
            Cached = Number(metrics, "cached_input_tokens", "cachedInputTokens");
            Output = Number(metrics, "output_tokens", "outputTokens");
            Reasoning = Number(metrics, "reasoning_output_tokens", "reasoningOutputTokens");
        }

        public void ReadArtifacts(string runDirectory)
        {
            var attemptDirectory = Path.Combine(runDirectory, "attempts", Id);
            var invocationPath = Path.Combine(attemptDirectory, "invocation.json");
            if (File.Exists(invocationPath))
            {
                try
                {
                    using var invocation = JsonDocument.Parse(File.ReadAllText(invocationPath));
                    var root = invocation.RootElement;
                    if (root.TryGetProperty("role", out var role)) Role = role.GetString() ?? Role;
                    if (root.TryGetProperty("workItemId", out var work) && work.ValueKind == JsonValueKind.String) WorkItem = work.GetString();
                    if (root.TryGetProperty("startedAt", out var started)) StartedAt ??= started.GetDateTimeOffset();
                }
                catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException) { }
            }

            var resultPath = Path.Combine(attemptDirectory, "result.json");
            if (!File.Exists(resultPath)) return;
            ResultWasProduced = true;
            try
            {
                using var result = JsonDocument.Parse(File.ReadAllText(resultPath));
                var root = result.RootElement;
                if (root.TryGetProperty("outcome", out var outcome)) Outcome ??= outcome.GetString();
            }
            catch (JsonException) { }
        }

        public AgentTraceNode Node(string root, bool processInterrupted)
        {
            var status = CompletionWasRecorded
                ? "completed"
                : ResultWasProduced
                    ? "result-produced"
                    : processInterrupted ? "interrupted" : "running";
            long? duration = StartedAt is not null && CompletedAt is not null ? (long)(CompletedAt.Value - StartedAt.Value).TotalMilliseconds : null;
            var terminal = Outcome is null ? null : new AgentTerminalResult(Outcome, null, null, null, null);
            var fresh = Input is not null && Cached is not null && Input >= Cached ? Input - Cached : null;
            double? cachePercentage = Input is > 0 && Cached is not null && Cached <= Input ? 100d * Cached.Value / Input.Value : null;
            return new(Id, root, Role, WorkItem, null, status, StartedAt, CompletedAt, duration, 1, 0, Input, Cached, Output, Reasoning,
                Input is not null && Output is not null ? Input + Output : null, fresh, cachePercentage, TerminalResult: terminal);
        }

        private static long? Number(JsonElement value, params string[] names)
        {
            foreach (var name in names)
                if (value.TryGetProperty(name, out var node) && node.TryGetInt64(out var number)) return number;
            return null;
        }
    }
}
