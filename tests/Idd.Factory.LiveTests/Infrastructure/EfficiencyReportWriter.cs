using System.Globalization;
using System.Text;
using Idd.Factory.LiveTests.Models;

namespace Idd.Factory.LiveTests.Infrastructure;

public static class EfficiencyReportWriter
{
    public static string Write(EfficiencyTelemetry data)
    {
        var text = new StringBuilder("# IDD Factory Efficiency Diagnostics\n\n");
        text.AppendLine($"- Total Factory tokens: {Number(data.EndToEndFactory.TotalTokens)}");
        text.AppendLine($"- End-to-end input: {Number(data.EndToEndFactory.InputTokens)}");
        text.AppendLine($"- Cached input: {Number(data.EndToEndFactory.CachedInputTokens)}");
        text.AppendLine($"- Fresh input: {Number(data.EndToEndFactory.FreshInputTokens)}");
        text.AppendLine($"- Cache %: {Percent(data.Summary.CachedInputPercentage)}");
        text.AppendLine($"- Total output: {Number(data.Summary.OutputTokens)}");
        text.AppendLine($"- Wall time: {Duration(data.Summary.WallTimeMs)}");
        text.AppendLine($"- Agent count: {data.Summary.AgentThreads}");
        text.AppendLine($"- Tool calls: {data.Summary.ToolCalls}");
        text.AppendLine($"- Failed/rejected calls: {data.Summary.FailedToolCalls}/{data.Summary.RejectedToolCalls}\n");

        text.AppendLine("## Factory token scopes\n");
        text.AppendLine("| Scope | Input | Cached | Fresh | Output | Reasoning output | Total |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
        WriteScope(text, "Root launcher", data.RootLauncher);
        WriteScope(text, "Semantic workers", data.SemanticWorkers);
        WriteScope(text, "End-to-end Factory", data.EndToEndFactory);

        text.AppendLine("## Token usage by role\n");
        text.AppendLine("| Role | Agents | Input | Cached | Fresh | Output | Tools | Duration | Input share | Fresh share | Mandatory refs chars | Mandatory refs bytes |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var role in data.Roles) text.AppendLine($"| {Cell(role.Role)} | {role.Agents} | {Number(role.InputTokens)} | {Number(role.CachedInputTokens)} | {Number(role.FreshInputTokens)} | {Number(role.OutputTokens)} | {role.ToolCalls} | {Duration(role.DurationMs)} | {Percent(role.InputSharePercentage)} | {Percent(role.FreshInputSharePercentage)} | {Number(role.MandatoryReferenceCharacters)} | {Number(role.MandatoryReferenceUtf8Bytes)} |");
        text.AppendLine("\n### Semantic groups\n");
        foreach (var group in data.Groups) text.AppendLine($"- {group.Group}: input {Number(group.InputTokens)} ({Percent(group.InputSharePercentage)}), fresh {Number(group.FreshInputTokens)} ({Percent(group.FreshInputSharePercentage)}), tools {group.ToolCalls}");

        text.AppendLine("\n## Token usage by agent\n");
        text.AppendLine("| Thread | Role | Action / work item | Input | Cached | Fresh | Cache % | Output | Tools | Failed | Reads | Unique reads | Repeated reads | Wait |");
        text.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var agent in data.Agents.OrderByDescending(agent => agent.InputTokens)) text.AppendLine($"| {Cell(Short(agent.ThreadId))} | {Cell(agent.Role)} | {Cell(agent.WorkItem ?? agent.Action)} | {Number(agent.InputTokens)} | {Number(agent.CachedInputTokens)} | {Number(agent.FreshInputTokens)} | {Percent(agent.CachedInputPercentage)} | {Number(agent.OutputTokens)} | {agent.ToolCallCount} | {agent.FailedToolCalls} | {agent.FileReads} | {agent.UniqueFileReads} | {agent.RepeatedFileReads} | {Duration(agent.WaitAgentMs)} |");

        text.AppendLine("\n## Worker results\n");
        var resultAgents = data.Agents.Where(agent => agent.TerminalResult is not null).ToArray();
        if (resultAgents.Length == 0) text.AppendLine("None observed.");
        else
        {
            text.AppendLine("| Thread | Role | Work item | Result | Detail |");
            text.AppendLine("|---|---|---|---|---|");
            foreach (var agent in resultAgents) text.AppendLine($"| {Cell(Short(agent.ThreadId))} | {Cell(agent.Role)} | {Cell(agent.WorkItem)} | {Cell(agent.TerminalResult!.Kind)} | {Cell(agent.TerminalResult.Detail)} |");
        }

        text.AppendLine("\n## Token progression\n");
        foreach (var agent in data.Agents.Where(agent => agent.TokenProgression.Count > 0))
        {
            text.AppendLine($"### {Cell(agent.Role)} `{Short(agent.ThreadId)}`\n");
            text.AppendLine("| Seq | Source | Input | Cached | Output | Input delta | Cached delta | Fresh delta | Tools in interval | Reset |");
            text.AppendLine("|---:|---|---:|---:|---:|---:|---:|---:|---|---|");
            foreach (var snapshot in agent.TokenProgression) text.AppendLine($"| {snapshot.Sequence} | {Cell(snapshot.SourceEventType)} | {Number(snapshot.InputTokens)} | {Number(snapshot.CachedInputTokens)} | {Number(snapshot.OutputTokens)} | {Number(snapshot.InputDelta)} | {Number(snapshot.CachedInputDelta)} | {Number(snapshot.FreshInputDelta)} | {Cell(string.Join(", ", snapshot.ToolCallIdsInInterval))} | {(snapshot.Discontinuity ? "yes" : "no")} |");
            text.AppendLine();
        }

        text.AppendLine("## Tool-call hotspots\n");
        List(text, "Most called tools", data.Hotspots.TopTools);
        List(text, "Longest calls", data.Hotspots.LongestToolCalls);
        List(text, "Top agents by input", data.Hotspots.TopAgentsByInput);
        List(text, "Top agents by fresh input", data.Hotspots.TopAgentsByFreshInput);
        List(text, "Top agents by tool calls", data.Hotspots.TopAgentsByToolCalls);
        List(text, "Highest cache ratio", data.Hotspots.HighestCacheRatioAgents);
        List(text, "Highest input/tool-call indicator", data.Hotspots.HighestInputPerToolAgents);

        text.AppendLine("\n## Repeated file reads\n");
        text.AppendLine("| Path | Reads | Agents | Returned bytes |"); text.AppendLine("|---|---:|---:|---:|");
        foreach (var file in data.FileAccess.Where(file => file.ReadCount > 1).Take(10)) text.AppendLine($"| {Cell(file.Path)} | {file.ReadCount} | {file.DistinctAgentCount} | {file.TotalReturnedBytes} |");

        text.AppendLine("\n## Dispatch/reference sizes\n");
        text.AppendLine("| Agent | Role | Dispatch chars | Dispatch UTF-8 bytes | Reference | Kind | Chars | UTF-8 bytes |"); text.AppendLine("|---|---|---:|---:|---|---|---:|---:|");
        foreach (var agent in data.Agents)
        {
            if (agent.DispatchReferences.Count == 0) text.AppendLine($"| {Short(agent.ThreadId)} | {Cell(agent.Role)} | {agent.DispatchCharacters} | {agent.DispatchUtf8Bytes} | — | — | — | — |");
            foreach (var reference in agent.DispatchReferences) text.AppendLine($"| {Short(agent.ThreadId)} | {Cell(agent.Role)} | {agent.DispatchCharacters} | {agent.DispatchUtf8Bytes} | {Cell(reference.Path)} | {reference.Kind} | {Number(reference.Characters)} | {Number(reference.Utf8Bytes)} |");
        }

        text.AppendLine("\n## Failures and retries\n");
        text.AppendLine($"Failed tool calls: {data.Summary.FailedToolCalls}  \nRejected calls: {data.Summary.RejectedToolCalls}  \nRetries/fallbacks: {data.Summary.RetryOrFallbackCalls}\n");
        List(text, "Important failures", data.Hotspots.FailedOrRejectedCalls);

        text.AppendLine("\n## Wait-agent telemetry\n");
        var waitAgentCalls = data.ToolCalls.Where(call => call.Tool == "wait_agent").ToArray();
        if (waitAgentCalls.Length == 0) text.AppendLine("No structured `wait_agent` calls were observed in per-thread rollout telemetry.");
        else
        {
            text.AppendLine("| Agent | Child | Duration | Terminal | Repeated wait number |"); text.AppendLine("|---|---|---:|---|---:|");
            foreach (var call in waitAgentCalls) text.AppendLine($"| {Short(call.ThreadId)} | {Cell(string.Join(", ", call.ChildThreadIds))} | {Duration(call.DurationMs)} | {Bool(call.IsTerminalWait)} | {call.RepeatedWaitNumber} |");
        }

        text.AppendLine("\n## Diagnostics\n");
        if (data.Diagnostics.Count == 0) text.AppendLine("None."); else foreach (var diagnostic in data.Diagnostics) text.AppendLine($"- `{diagnostic.Code}` ({diagnostic.Severity}): {diagnostic.Message}");
        return text.ToString();
    }

    private static void WriteScope(StringBuilder text, string name, EfficiencyTokenBreakdown value) =>
        text.AppendLine($"| {name} | {Number(value.InputTokens)} | {Number(value.CachedInputTokens)} | {Number(value.FreshInputTokens)} | {Number(value.OutputTokens)} | {Number(value.ReasoningOutputTokens)} | {Number(value.TotalTokens)} |");

    private static void List(StringBuilder text, string title, IEnumerable<string> values) { var items = values.ToArray(); text.AppendLine($"\n### {title}\n"); if (items.Length == 0) text.AppendLine("None."); else foreach (var item in items) text.AppendLine($"- {item}"); }
    private static string Number(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "—";
    private static string Number(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "—";
    private static string Percent(double? value) => value?.ToString("0.0", CultureInfo.InvariantCulture) + "%" ?? "—";
    private static string Duration(long? milliseconds) => milliseconds is null ? "—" : milliseconds < 1000 ? $"{milliseconds} ms" : TimeSpan.FromMilliseconds(milliseconds.Value).TotalMinutes >= 1 ? $"{(int)TimeSpan.FromMilliseconds(milliseconds.Value).TotalMinutes}m {TimeSpan.FromMilliseconds(milliseconds.Value).Seconds}s" : $"{TimeSpan.FromMilliseconds(milliseconds.Value).TotalSeconds:0.0}s";
    private static string Cell(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
    private static string Short(string value) => value[..Math.Min(12, value.Length)];
    private static string Bool(bool? value) => value switch { true => "yes", false => "no", _ => "—" };
}
