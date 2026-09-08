using System.Text.Json;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed record MinimalCodexTrace(int FactoryRunCalls, int FactoryStatusCalls, int ModelTurnsDuringFactoryRun, bool FactoryCallCompleted);

public static class MinimalCodexTraceReader
{
    public static MinimalCodexTrace Read(string eventsPath)
    {
        var factoryRuns = new HashSet<string>(StringComparer.Ordinal);
        var factoryStatuses = new HashSet<string>(StringComparer.Ordinal);
        var activeFactoryRuns = new HashSet<string>(StringComparer.Ordinal);
        var completedFactoryRuns = new HashSet<string>(StringComparer.Ordinal);
        var modelTurnsDuringFactoryRun = 0;

        foreach (var line in File.ReadLines(eventsPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var eventType = String(root, "type");
                if (eventType == "turn.completed" && activeFactoryRuns.Count != 0) modelTurnsDuringFactoryRun++;
                if (eventType is not ("item.started" or "item.completed") || !root.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object) continue;

                var name = String(item, "name") ?? String(item, "tool");
                var id = String(item, "id") ?? String(item, "call_id") ?? $"anonymous-{factoryRuns.Count + factoryStatuses.Count}";
                if (IsFactoryTool(name, "factory_run"))
                {
                    factoryRuns.Add(id);
                    if (eventType == "item.started") activeFactoryRuns.Add(id);
                    else
                    {
                        activeFactoryRuns.Remove(id);
                        completedFactoryRuns.Add(id);
                    }
                }
                else if (IsFactoryTool(name, "factory_status"))
                {
                    factoryStatuses.Add(id);
                }
            }
            catch (JsonException)
            {
                // Unrelated malformed diagnostic lines are preserved in events.jsonl; they are not a Live acceptance criterion.
            }
        }

        return new(factoryRuns.Count, factoryStatuses.Count, modelTurnsDuringFactoryRun, completedFactoryRuns.Count != 0);
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool IsFactoryTool(string? name, string tool) =>
        name is not null && (name.Equals(tool, StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("." + tool, StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("__" + tool, StringComparison.OrdinalIgnoreCase));
}
