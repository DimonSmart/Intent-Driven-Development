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
        foreach (var agent in agents) text.Append("    ").Append(ids[agent.ThreadId]).Append("[\"").Append(Escape(Label(agent))).Append("\"]\n");
        foreach (var agent in agents.Where(agent => agent.ParentThreadId is not null && ids.ContainsKey(agent.ParentThreadId))) text.Append("    ").Append(ids[agent.ParentThreadId!]).Append(" --> ").Append(ids[agent.ThreadId]).Append('\n');
        return text.Append("```\n").ToString();
    }

    public static string WriteTable(AgentTrace trace)
    {
        var ids = UniqueShortIds(trace.Agents);
        var rows = Ordered(trace).Select(agent => $"| {ids[agent.ThreadId]} | {(agent.ParentThreadId is null ? "—" : ids.GetValueOrDefault(agent.ParentThreadId, EscapeCell(agent.ParentThreadId)))} | {EscapeCell(agent.Role)} | {EscapeCell(agent.WorkItem)} | {EscapeCell(agent.Status)} | {agent.ToolCallCount?.ToString() ?? "—"} |");
        return "| Agent | Parent | Role | Work item | Status | Tools |\n|---|---|---|---|---|---:|\n" + string.Join('\n', rows) + "\n";
    }

    private static IEnumerable<AgentTraceNode> Ordered(AgentTrace trace) => trace.Agents.OrderBy(agent => agent.StartedAt).ThenBy(agent => agent.ThreadId, StringComparer.Ordinal);
    private static string Label(AgentTraceNode agent) { var lines = new List<string> { agent.Role }; if (agent.WorkItem is not null) lines.Add(agent.WorkItem); var details = new List<string> { agent.Status }; if (agent.DurationMs is not null) details.Add(Duration(agent.DurationMs.Value)); if (agent.ToolCallCount is not null) details.Add($"tools: {agent.ToolCallCount}"); lines.Add(string.Join(" · ", details)); return string.Join("<br/>", lines); }
    private static IReadOnlyDictionary<string, string> UniqueShortIds(IEnumerable<AgentTraceNode> agents) { var source = agents.Select(agent => agent.ThreadId).Distinct(StringComparer.Ordinal).ToArray(); var length = 8; while (length < source.Max(id => id.Length) && source.Select(id => id[..Math.Min(length, id.Length)]).Distinct(StringComparer.Ordinal).Count() != source.Length) length++; return source.ToDictionary(id => id, id => id[..Math.Min(length, id.Length)], StringComparer.Ordinal); }
    private static string Escape(string text) => text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal).Replace("[", "&#91;", StringComparison.Ordinal).Replace("]", "&#93;", StringComparison.Ordinal).Replace("{", "&#123;", StringComparison.Ordinal).Replace("}", "&#125;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
    private static string EscapeCell(string? text) => string.IsNullOrWhiteSpace(text) ? "—" : text.Replace("|", "\\|", StringComparison.Ordinal);
    private static string Duration(long ms) { var value = TimeSpan.FromMilliseconds(ms); return value.TotalMinutes >= 1 ? $"{(int)value.TotalMinutes}m {value.Seconds}s" : $"{value.Seconds}s"; }
}
