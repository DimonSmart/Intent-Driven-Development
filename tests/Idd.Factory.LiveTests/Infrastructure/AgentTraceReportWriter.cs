using System.Text;
using Idd.Factory.LiveTests.Models;

namespace Idd.Factory.LiveTests.Infrastructure;

public static class AgentTraceReportWriter
{
    public static string WriteMermaid(AgentTrace trace)
    {
        var agents = Ordered(trace).ToArray();
        var ids = agents.Select((agent, index) => (agent.ThreadId, Id: $"n{index}")).ToDictionary(item => item.ThreadId, item => item.Id, StringComparer.Ordinal);
        var text = new StringBuilder("```mermaid\nflowchart TD\n");
        foreach (var agent in agents) text.Append("    ").Append(ids[agent.ThreadId]).Append("[\"").Append(Label(agent)).Append("\"]\n");
        foreach (var agent in agents.Where(agent => agent.ParentThreadId is not null && ids.ContainsKey(agent.ParentThreadId))) text.Append("    ").Append(ids[agent.ParentThreadId!]).Append(" --> ").Append(ids[agent.ThreadId]).Append('\n');
        var runtimeAttempts = agents.Where(agent => IsRuntimeAttempt(agent.ThreadId)).OrderBy(agent => agent.StartedAt).ThenBy(agent => agent.ThreadId, StringComparer.Ordinal).ToArray();
        for (var index = 1; index < runtimeAttempts.Length; index++)
            text.Append("    ").Append(ids[runtimeAttempts[index - 1].ThreadId]).Append(" -. next .-> ").Append(ids[runtimeAttempts[index].ThreadId]).Append('\n');
        return text.Append("```\n").ToString();
    }

    public static string WriteTable(AgentTrace trace)
    {
        var ids = UniqueShortIds(trace.Agents);
        var rows = Ordered(trace).Select(agent => $"| {ids[agent.ThreadId]} | {(agent.ParentThreadId is null ? "—" : ids.GetValueOrDefault(agent.ParentThreadId, EscapeCell(agent.ParentThreadId)))} | {EscapeCell(agent.Role)} | {EscapeCell(agent.WorkItem ?? agent.Action)} | {EscapeCell(agent.Status)} | {(agent.DurationMs is null ? "—" : Duration(agent.DurationMs.Value))} | {agent.TurnCount} | {agent.ToolCallCount} | {Number(agent.InputTokens)} | {Number(agent.CachedInputTokens)} | {Number(agent.FreshInputTokens)} | {Percent(agent.CachedInputPercentage)} | {Number(agent.OutputTokens)} | {Number(agent.ReasoningOutputTokens)} | {Number(agent.TotalTokens)} | {agent.FailedToolCallCount} | {agent.RepeatedFileReadCount} | {Duration(agent.WaitAgentMs)} |");
        return "| Agent | Parent | Role | Work item / action | Status | Duration | Turns | Tools | Input tokens | Cached input | Fresh input | Cache % | Output tokens | Reasoning tokens | Total tokens | Failed tools | Repeated reads | Wait time |\n|---|---|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|\n" + string.Join('\n', rows) + "\n";
    }

    private static IEnumerable<AgentTraceNode> Ordered(AgentTrace trace) => trace.Agents.OrderBy(agent => agent.StartedAt).ThenBy(agent => agent.ThreadId, StringComparer.Ordinal);
    private static bool IsRuntimeAttempt(string id) => id.Length == 7 && id[0] == 'A' && id[1..].All(char.IsAsciiDigit);
    private static string Label(AgentTraceNode agent)
    {
        var lines = new List<string> { agent.Role };
        if ((agent.WorkItem ?? agent.Action) is { } logicalAction) lines.Add(logicalAction);
        var details = new List<string>();
        if (!agent.Status.Equals("completed", StringComparison.OrdinalIgnoreCase)) details.Add(agent.Status.ToUpperInvariant());
        if (agent.DurationMs is not null) details.Add(Duration(agent.DurationMs.Value));
        if (agent.TotalTokens is not null) details.Add(FormatTokens(agent.TotalTokens.Value));
        details.Add($"{agent.TurnCount} turns");
        details.Add($"{agent.ToolCallCount} tools");
        lines.Add(string.Join(" · ", details));
        return string.Join("<br/>", lines.Select(Escape));
    }
    private static IReadOnlyDictionary<string, string> UniqueShortIds(IEnumerable<AgentTraceNode> agents) { var source = agents.Select(agent => agent.ThreadId).Distinct(StringComparer.Ordinal).ToArray(); var length = 8; while (length < source.Max(id => id.Length) && source.Select(id => id[..Math.Min(length, id.Length)]).Distinct(StringComparer.Ordinal).Count() != source.Length) length++; return source.ToDictionary(id => id, id => id[..Math.Min(length, id.Length)], StringComparer.Ordinal); }
    private static string Escape(string text) => text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal).Replace("[", "&#91;", StringComparison.Ordinal).Replace("]", "&#93;", StringComparison.Ordinal).Replace("{", "&#123;", StringComparison.Ordinal).Replace("}", "&#125;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
    private static string EscapeCell(string? text) => string.IsNullOrWhiteSpace(text) ? "—" : text.Replace("|", "\\|", StringComparison.Ordinal);
    private static string Duration(long ms) { var value = TimeSpan.FromMilliseconds(ms); return value.TotalMinutes >= 1 ? $"{(int)value.TotalMinutes}m {value.Seconds}s" : $"{value.Seconds}s"; }
    public static string FormatTokens(long tokens) => tokens switch
    {
        < 1_000 => $"{tokens} tok",
        < 1_000_000 => (tokens / 1_000d).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "k tok",
        _ => (tokens / 1_000_000d).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "M tok"
    };
    private static string Number(long? value) => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—";
    private static string Percent(double? value) => value?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) ?? "—";
}
