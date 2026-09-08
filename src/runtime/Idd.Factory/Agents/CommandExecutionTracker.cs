using System.Text.Json;

namespace Idd.Factory.Agents;

internal sealed record IncompleteCommandExecution(
    string Id,
    string? Command,
    DateTimeOffset? StartedAt = null);

internal sealed record CommandExecutionOverlap(
    IncompleteCommandExecution Active,
    IncompleteCommandExecution Started);

internal sealed class CommandExecutionTracker
{
    private readonly object gate = new();
    private readonly Dictionary<string, IncompleteCommandExecution> active = new(StringComparer.Ordinal);
    private CommandExecutionOverlap? overlap;

    public void Observe(string line, DateTimeOffset observedAt)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var eventType)
                || !root.TryGetProperty("item", out var item)
                || !item.TryGetProperty("id", out var idValue)
                || !item.TryGetProperty("type", out var itemType)
                || itemType.GetString() is not ("command_execution" or "local_shell_call"))
                return;

            var id = idValue.GetString();
            if (string.IsNullOrWhiteSpace(id)) return;
            lock (gate)
            {
                if (eventType.GetString() == "item.started")
                {
                    var command = item.TryGetProperty("command", out var commandValue)
                        && commandValue.ValueKind == JsonValueKind.String
                        ? commandValue.GetString()
                        : null;
                    var started = new IncompleteCommandExecution(
                        id,
                        string.IsNullOrWhiteSpace(command) ? null : command,
                        observedAt);
                    if (!active.ContainsKey(id)
                        && active.Values.OrderBy(value => value.StartedAt).FirstOrDefault() is { } earlier)
                        overlap ??= new(earlier, started);
                    active[id] = started;
                }
                else if (eventType.GetString() == "item.completed")
                {
                    active.Remove(id);
                }
            }
        }
        catch (JsonException)
        {
        }
    }

    public IncompleteCommandExecution? FindTimedOut(DateTimeOffset now, TimeSpan timeout)
    {
        lock (gate)
        {
            return active.Values
                .Where(command => command.StartedAt is not null && now - command.StartedAt >= timeout)
                .OrderBy(command => command.StartedAt)
                .FirstOrDefault();
        }
    }

    public CommandExecutionOverlap? FindOverlap()
    {
        lock (gate) return overlap;
    }
}
