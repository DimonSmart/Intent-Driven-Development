using System.Text.Json;

namespace Idd.Factory.Benchmark;

public static class CodexJsonlAnalyzer
{
    public static InvocationMetrics Analyze(string path, TimeSpan duration, int exitCode, string role)
    {
        long input = 0, cached = 0, output = 0, batches = 0, commands = 0, toolChars = 0, failedCommands = 0, failedChars = 0;
        string? errorMessage = null;
        var active = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var eventType = String(root, "type");
                if (eventType == "error") errorMessage = String(root, "message") ?? errorMessage;
                if (eventType == "turn.failed" && root.TryGetProperty("error", out var failure)) errorMessage = String(failure, "message") ?? errorMessage;
                if (eventType == "turn.completed" && root.TryGetProperty("usage", out var usage))
                {
                    input = Number(usage, "input_tokens") ?? input;
                    cached = Number(usage, "cached_input_tokens") ?? cached;
                    output = Number(usage, "output_tokens") ?? output;
                }
                if (eventType is not ("item.started" or "item.completed") || !root.TryGetProperty("item", out var item) || !IsTool(item)) continue;
                var id = String(item, "id") ?? String(item, "call_id") ?? $"anonymous-{batches}-{commands}";
                if (eventType == "item.started")
                {
                    if (active.Count == 0) batches++;
                    active.Add(id);
                    continue;
                }
                active.Remove(id);
                var isCommand = IsCommand(item);
                var failed = IsFailed(item);
                var chars = OutputCharacters(item);
                toolChars += chars;
                if (failed) failedChars += chars;
                if (isCommand) { commands++; if (failed) failedCommands++; }
            }
            catch (JsonException) { }
        }
        if (cached > input) cached = 0;
        return new(input, cached, input - cached, output, batches, commands, toolChars, failedCommands, failedChars, (long)duration.TotalMilliseconds, exitCode, role, path, errorMessage);
    }

    private static bool IsTool(JsonElement item)
    {
        var type = String(item, "type");
        return type is "function_call" or "mcp_tool_call" or "collab_tool_call" or "custom_tool_call" or "local_shell_call" or "command_execution";
    }

    private static bool IsCommand(JsonElement item)
    {
        var type = String(item, "type");
        var name = String(item, "name") ?? String(item, "tool") ?? "";
        return type is "local_shell_call" or "command_execution" || name.Contains("exec_command", StringComparison.OrdinalIgnoreCase) || name.Contains("write_stdin", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFailed(JsonElement item)
    {
        var status = String(item, "status");
        if (status is not null && status is "failed" or "error" or "declined") return true;
        if (item.TryGetProperty("exit_code", out var exit) && exit.TryGetInt32(out var value) && value != 0) return true;
        return item.TryGetProperty("error", out var error) && error.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
    }

    private static long OutputCharacters(JsonElement item)
    {
        foreach (var name in new[] { "aggregated_output", "output", "result", "content" })
            if (item.TryGetProperty(name, out var value)) return value.ValueKind == JsonValueKind.String ? value.GetString()?.Length ?? 0 : value.GetRawText().Length;
        return 0;
    }

    private static string? String(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    private static long? Number(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) && property.TryGetInt64(out var result) ? result : null;
}

public static class BenchmarkStatistics
{
    public static long Median(IEnumerable<long> source)
    {
        var values = source.Order().ToArray();
        if (values.Length == 0) throw new ArgumentException("Median requires at least one value.");
        return values.Length % 2 == 1 ? values[values.Length / 2] : checked((values[values.Length / 2 - 1] + values[values.Length / 2]) / 2);
    }

    public static ModeAggregate Aggregate(IReadOnlyList<BenchmarkRunResult> runs)
    {
        var successful = runs.Where(x => x.Successful).ToArray();
        return new(runs.Count, successful.Length, runs.Count == 0 ? 0 : (double)successful.Length / runs.Count,
            successful.Length == 0 ? null : Metric(successful, Median),
            successful.Length == 0 ? null : Metric(successful, values => values.Min()),
            successful.Length == 0 ? null : Metric(successful, values => values.Max()),
            successful.Length == 0 ? null : Median(successful.Select(x => (long)x.CodexProcessCount)),
            successful.Length == 0 ? null : Median(successful.Select(x => x.AgentDurationMilliseconds)),
            successful.Length == 0 ? null : Median(successful.Select(x => x.Acceptance.DurationMilliseconds)),
            successful.Length == 0 ? null : Median(successful.Select(x => x.TotalDurationMilliseconds)));
    }

    public static ComparisonReport Compare(IReadOnlyDictionary<string, ModeAggregate> modes)
    {
        long? Value(string mode) => modes.GetValueOrDefault(mode)?.Median?.GrossInputTokens;
        static long? Difference(long? right, long? left) => right is not null && left is not null ? right - left : null;
        static double? Ratio(long? numerator, long? denominator) => numerator is not null && denominator is > 0 ? (double)numerator / denominator : null;
        var b0 = Value(BenchmarkModes.Direct); var b1 = Value(BenchmarkModes.StructuredSingle); var b2 = Value(BenchmarkModes.ManualIsolated);
        var b3 = Value(BenchmarkModes.FactorySplitReplay); var b4 = Value(BenchmarkModes.Factory);
        return new(Difference(b1, b0), Difference(b2, b1), Difference(b3, b2), Difference(b4, b3), Difference(b4, b0), Ratio(b4, b0), Ratio(b2, b0), Ratio(b3, b0), Ratio(b4, b3));
    }

    private static AggregateMetrics Metric(IReadOnlyList<BenchmarkRunResult> values, Func<IEnumerable<long>, long> selector) =>
        new(selector(values.Select(x => x.Metrics.GrossInputTokens)), selector(values.Select(x => x.Metrics.CachedInputTokens)), selector(values.Select(x => x.Metrics.NewInputTokens)),
            selector(values.Select(x => x.Metrics.OutputTokens)), selector(values.Select(x => x.Metrics.ToolBatches)), selector(values.Select(x => x.Metrics.Commands)),
            selector(values.Select(x => x.Metrics.ToolOutputChars)), selector(values.Select(x => x.Metrics.FailedCommands)), selector(values.Select(x => x.Metrics.FailedToolOutputChars)));
}
