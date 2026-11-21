using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Idd.Factory.Report;

public static class ReportWriters
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private static string FormatExecutionSettings(string? model, string? reasoning) =>
        $"model={model ?? "unknown"}, reasoning={reasoning ?? "unknown"}";

    public static void WriteList(IEnumerable<FactoryRunReport> reports, TextWriter writer)
    {
        writer.WriteLine("#  Started                      Result       Thread");
        var i = 0;
        foreach (var report in reports.OrderByDescending(x => x.Run.StartedAt ?? DateTimeOffset.MinValue))
        {
            writer.WriteLine($"{++i,-2} {FormatTime(report.Run.StartedAt),-28} {report.Run.Result.ToUpperInvariant(),-12} {report.Run.RootThreadId}");
        }
    }

    public static void WriteConsole(FactoryRunReport report, TextWriter writer, bool verbose)
    {
        writer.WriteLine("IDD Factory Run Report");
        writer.WriteLine();
        writer.WriteLine("Repository");
        writer.WriteLine("----------");
        writer.WriteLine(report.Repository);
        writer.WriteLine();
        writer.WriteLine("Factory run");
        writer.WriteLine("-----------");
        writer.WriteLine($"Thread:   {report.Run.RootThreadId}");
        writer.WriteLine($"Run:      {report.Run.RunIndex}");
        writer.WriteLine($"Result:   {report.Run.Result.ToUpperInvariant()}");
        writer.WriteLine($"Started:  {FormatTime(report.Run.StartedAt)}");
        writer.WriteLine($"Finished: {FormatTime(report.Run.FinishedAt)}");
        writer.WriteLine($"Duration: {FormatDuration(report.Metrics.DurationMilliseconds)}");
        if (report.Run.ResultEvidence.Count > 0)
            writer.WriteLine($"Evidence: {string.Join(", ", report.Run.ResultEvidence)}");
        if (!string.IsNullOrWhiteSpace(report.Run.Reason))
            writer.WriteLine($"Reason:   {report.Run.Reason}");

        writer.WriteLine();
        writer.WriteLine("Agents");
        writer.WriteLine("------");
        foreach (var line in AgentTreeLines(report.Agents))
            writer.WriteLine(line);
        writer.WriteLine();
        writer.WriteLine($"Planner invocations: {report.Metrics.PlannerInvocations}");
        writer.WriteLine($"Worker invocations:  {report.Metrics.WorkerInvocations}");
        writer.WriteLine($"Other child agents:  {report.Metrics.OtherChildAgents}");
        writer.WriteLine($"Maximum depth:        {report.Metrics.MaximumDepth}");

        writer.WriteLine();
        writer.WriteLine("Tasks");
        writer.WriteLine("-----");
        if (report.Tasks.Count == 0)
            writer.WriteLine("unavailable");
        foreach (var task in report.Tasks)
        {
            var agent = report.Agents.FirstOrDefault(x => x.ThreadId == task.AgentThreadId);
            writer.WriteLine($"#{task.Number}  {agent?.TaskTitle ?? Short(task.Text, 80)}");
            writer.WriteLine($"    Status:    {task.Status}");
            writer.WriteLine($"    Worker:    {task.AgentThreadId}");
            writer.WriteLine($"    Profile:   {task.ExecutionProfile ?? "unknown"}");
            if (task.SpawnExecutionProfile is not null)
                writer.WriteLine($"    Spawn profile: {task.SpawnExecutionProfile}");
            writer.WriteLine($"    Requested: {FormatExecutionSettings(task.RequestedModel, task.RequestedReasoningEffort)}");
            writer.WriteLine($"    Actual:    {FormatExecutionSettings(task.ActualModel, task.ActualReasoningEffort)}");
            writer.WriteLine($"    Duration:  {FormatDuration(task.DurationMilliseconds)}");
            writer.WriteLine($"    Tokens:    {FormatTaskTokens(agent?.Tokens)}");
        }

        writer.WriteLine();
        writer.WriteLine("Completion");
        writer.WriteLine("----------");
        writer.WriteLine($"Planner done:          {BoolStatus(report.Completion.PlannerDone)}");
        writer.WriteLine($"Project verification: {report.Completion.ProjectVerification}");
        writer.WriteLine($"Declared result:       {report.Completion.DeclaredResult.ToUpperInvariant()}");
        writer.WriteLine($"Protocol validation:   {report.Completion.ProtocolValidation}");

        writer.WriteLine();
        writer.WriteLine("Tokens");
        writer.WriteLine("------");
        if (report.Metrics.RootReportedTokens.Available)
        {
            writer.WriteLine("Root reported usage");
            writer.WriteLine("-------------------");
            writer.WriteLine($"Input:      {Num(report.Metrics.RootReportedTokens.InputTokens)}");
            writer.WriteLine($"Cached:     {Num(report.Metrics.RootReportedTokens.CachedInputTokens)}");
            writer.WriteLine($"New input:  {Num(report.Metrics.RootReportedTokens.NewInputTokens)}");
            writer.WriteLine($"Output:     {Num(report.Metrics.RootReportedTokens.OutputTokens)}");
            writer.WriteLine();
        }

        writer.WriteLine($"{"Agent",-20} {"Input",10} {"Cached",10} {"New input",10} {"Output",10}");
        foreach (var agent in report.Agents)
        {
            var label = agent.Role == "root" ? "Factory root" : $"{Cap(agent.Role)} #{agent.Sequence}";
            writer.WriteLine($"{label,-20} {Num(agent.Tokens.InputTokens),10} {Num(agent.Tokens.CachedInputTokens),10} {Num(agent.Tokens.NewInputTokens),10} {Num(agent.Tokens.OutputTokens),10}");
        }
        writer.WriteLine($"{"Total",-20} {Num(report.Metrics.Tokens.InputTokens),10} {Num(report.Metrics.Tokens.CachedInputTokens),10} {Num(report.Metrics.Tokens.NewInputTokens),10} {Num(report.Metrics.Tokens.OutputTokens),10}");
        writer.WriteLine($"Aggregation: {report.Metrics.TokenAggregationStatus}");

        writer.WriteLine();
        writer.WriteLine("Tools");
        writer.WriteLine("-----");
        writer.WriteLine($"Native operations: {report.Metrics.Tools.NativeOperations}");
        writer.WriteLine($"Commands:          {report.Metrics.Tools.Commands}");
        writer.WriteLine($"File changes:      {report.Metrics.Tools.FileChanges}");
        writer.WriteLine($"Spawn agent:       {report.Metrics.Tools.SpawnAgentCalls}");
        writer.WriteLine($"Wait agent:        {report.Metrics.Tools.WaitAgentCalls}");
        writer.WriteLine($"Failed commands:   {report.Metrics.Tools.FailedCommands}");
        writer.WriteLine($"Tool batches:      {NullableNum(report.Metrics.Tools.ToolBatches)}");
        if (verbose)
            writer.WriteLine($"Host wrapper calls:{report.Metrics.Tools.HostWrapperCalls,4}");

        if (report.Metrics.Tools.FailedCommandItems.Count > 0)        if (report.Metrics.Tools.FailedCommandItems.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("Failed commands");
            writer.WriteLine("---------------");
            foreach (var failed in report.Metrics.Tools.FailedCommandItems)
                writer.WriteLine($"{failed.Agent}  {failed.ExitCode?.ToString() ?? failed.Status ?? "failed"}  {failed.Command ?? "unavailable"}");
        }

        writer.WriteLine();
        writer.WriteLine("Timeline");
        writer.WriteLine("--------");
        foreach (var e in report.Timeline)
            writer.WriteLine($"{FormatClock(e.Timestamp),8}  {e.Text}");

        writer.WriteLine();
        writer.WriteLine("Factory project state");
        writer.WriteLine("---------------------");
        writer.WriteLine($"Active request:          {Present(report.FactoryProjectState.CurrentRequestPresent)}");
        writer.WriteLine($"Remaining plan:          {Present(report.FactoryProjectState.CurrentPlanPresent)}");
        writer.WriteLine($"Pending question:        {Present(report.FactoryProjectState.CurrentQuestionPresent)}");
        writer.WriteLine($"Verification failure:    {Present(report.FactoryProjectState.VerificationFailurePresent)}");
        writer.WriteLine($"Archived result:         {Present(report.FactoryProjectState.ArchivedResultPresent)}");

        if (verbose)
        {
            writer.WriteLine();
            writer.WriteLine("Codex capabilities");
            writer.WriteLine("------------------");
            writer.WriteLine($"State DB:          {Available(report.Host.Capabilities.StateDb)}");
            writer.WriteLine($"Spawn edges:       {Available(report.Host.Capabilities.SpawnEdges)}");
            writer.WriteLine($"Agent role:        {Available(report.Host.Capabilities.AgentRole)}");
            writer.WriteLine($"Token accounting:  {report.Host.Capabilities.TokenAccounting}");
            writer.WriteLine($"Codex home:         {report.Host.CodexHome ?? "unavailable"}");
            writer.WriteLine($"Start boundary:     {report.Run.StartBoundary.Confidence} ({report.Run.StartBoundary.Evidence ?? "unavailable"})");
            writer.WriteLine($"End boundary:       {report.Run.EndBoundary.Confidence} ({report.Run.EndBoundary.Evidence ?? "unavailable"})");
            foreach (var agent in report.Agents.Where(x => x.RolloutPath is not null))
                writer.WriteLine($"{agent.ThreadId}: {agent.RolloutPath}");
        }

        if (report.Diagnostics.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("Diagnostics");
            writer.WriteLine("-----------");
            foreach (var diagnostic in report.Diagnostics)
                writer.WriteLine($"{diagnostic.Severity.ToUpperInvariant()} [{diagnostic.Category}/{diagnostic.Code}] {diagnostic.Message}");
        }
    }

    public static void WriteJson(FactoryRunReport report, string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine, Encoding.UTF8);
    }

    public static void WriteMarkdown(FactoryRunReport report, string path, bool verbose)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("# IDD Factory Run Report");
        writer.WriteLine();
        writer.WriteLine($"- Repository: `{report.Repository}`");
        writer.WriteLine($"- Thread: `{report.Run.RootThreadId}`");
        writer.WriteLine($"- Run: {report.Run.RunIndex}");
        writer.WriteLine($"- Result: **{report.Run.Result.ToUpperInvariant()}**");
        if (!string.IsNullOrWhiteSpace(report.Run.Reason))
            writer.WriteLine($"- Reason: {report.Run.Reason}");
        writer.WriteLine($"- Started: {FormatTime(report.Run.StartedAt)}");
        writer.WriteLine($"- Finished: {FormatTime(report.Run.FinishedAt)}");
        writer.WriteLine($"- Duration: {FormatDuration(report.Metrics.DurationMilliseconds)}");
        writer.WriteLine();
        writer.WriteLine("## Agents");
        writer.WriteLine();
        writer.WriteLine("```text");
        foreach (var line in AgentTreeLines(report.Agents))
            writer.WriteLine(line);
        writer.WriteLine("```");

        writer.WriteLine();
        writer.WriteLine("## Tasks");
        writer.WriteLine();
        if (report.Tasks.Count == 0)
            writer.WriteLine("unavailable");
        foreach (var task in report.Tasks)
        {
            var agent = report.Agents.FirstOrDefault(x => x.ThreadId == task.AgentThreadId);
            writer.WriteLine($"### #{task.Number} {agent?.TaskTitle ?? Short(task.Text, 80)}");
            writer.WriteLine();
            writer.WriteLine($"- Status: {task.Status}");
            writer.WriteLine("- Worker: " + task.AgentThreadId);
            writer.WriteLine($"- Execution profile: {task.ExecutionProfile ?? "unknown"}");
            if (task.SpawnExecutionProfile is not null)
                writer.WriteLine($"- Spawn execution profile: {task.SpawnExecutionProfile}");
            writer.WriteLine($"- Requested settings: {FormatExecutionSettings(task.RequestedModel, task.RequestedReasoningEffort)}");
            writer.WriteLine($"- Actual settings: {FormatExecutionSettings(task.ActualModel, task.ActualReasoningEffort)}");
            writer.WriteLine($"- Duration: {FormatDuration(task.DurationMilliseconds)}");
            writer.WriteLine($"- Tokens: {FormatTaskTokens(agent?.Tokens)}");
            if (!string.IsNullOrWhiteSpace(task.Text))
            {
                writer.WriteLine("- Full task:");
                writer.WriteLine();
                writer.WriteLine("  " + task.Text.Replace("\r\n", "\n").Replace("\n", "\n  "));
            }
            writer.WriteLine();
        }

        writer.WriteLine("## Completion");
        writer.WriteLine();
        writer.WriteLine($"- Planner done: {BoolStatus(report.Completion.PlannerDone)}");
        writer.WriteLine($"- Project verification: {report.Completion.ProjectVerification}");
        writer.WriteLine($"- Declared result: {report.Completion.DeclaredResult.ToUpperInvariant()}");
        writer.WriteLine($"- Protocol validation: {report.Completion.ProtocolValidation}");

        writer.WriteLine();
        writer.WriteLine("## Token accounting");
        writer.WriteLine();
        writer.WriteLine($"- Root reported input: {Num(report.Metrics.RootReportedTokens.InputTokens)}");
        writer.WriteLine($"- Root reported cached: {Num(report.Metrics.RootReportedTokens.CachedInputTokens)}");
        writer.WriteLine($"- Root reported new input: {Num(report.Metrics.RootReportedTokens.NewInputTokens)}");
        writer.WriteLine($"- Root reported output: {Num(report.Metrics.RootReportedTokens.OutputTokens)}");
        writer.WriteLine($"- Aggregate status: {report.Metrics.TokenAggregationStatus}");
        writer.WriteLine();
        writer.WriteLine("## Tools");
        writer.WriteLine();
        writer.WriteLine($"- Native operations: {report.Metrics.Tools.NativeOperations}");
        writer.WriteLine($"- Commands: {report.Metrics.Tools.Commands}");
        writer.WriteLine($"- File changes: {report.Metrics.Tools.FileChanges}");
        writer.WriteLine($"- Spawn agent calls: {report.Metrics.Tools.SpawnAgentCalls}");
        writer.WriteLine($"- Wait agent calls: {report.Metrics.Tools.WaitAgentCalls}");
        writer.WriteLine($"- Failed commands: {report.Metrics.Tools.FailedCommands}");
        writer.WriteLine($"- Tool batches: {NullableNum(report.Metrics.Tools.ToolBatches)}");
        if (verbose)
            writer.WriteLine($"- Host wrapper calls: {report.Metrics.Tools.HostWrapperCalls}");

        writer.WriteLine();
        writer.WriteLine("## Timeline");
        writer.WriteLine();
        foreach (var e in report.Timeline)
            writer.WriteLine($"- {FormatTime(e.Timestamp)} — {e.Text}");
        writer.WriteLine();
        writer.WriteLine("## Diagnostics");
        writer.WriteLine();
        if (report.Diagnostics.Count == 0)
            writer.WriteLine("None.");
        foreach (var d in report.Diagnostics)
            writer.WriteLine($"- **{d.Severity.ToUpperInvariant()}** `{d.Category}/{d.Code}`: {d.Message}");

        if (verbose)
        {
            writer.WriteLine();
            writer.WriteLine("## Reporter details");
            writer.WriteLine();
            writer.WriteLine($"- Codex home: `{report.Host.CodexHome ?? "unavailable"}`");
            writer.WriteLine($"- Start boundary: {report.Run.StartBoundary.Confidence} — {report.Run.StartBoundary.Evidence ?? "unavailable"}");
            writer.WriteLine($"- End boundary: {report.Run.EndBoundary.Confidence} — {report.Run.EndBoundary.Evidence ?? "unavailable"}");
        }
    }

    private static IEnumerable<string> AgentTreeLines(IReadOnlyList<AgentReport> agents)
    {
        var byParent = agents
            .Where(x => x.Role != "root" && x.ParentThreadId is not null)
            .GroupBy(x => x.ParentThreadId!, StringComparer.Ordinal)
            .ToDictionary(
                x => x.Key,
                x => x.OrderBy(a => a.StartedAt).ThenBy(a => a.ThreadId, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        var root = agents.FirstOrDefault(x => x.Role == "root");
        if (root is null)
            yield break;

        yield return FormatAgentTreeLine(root, agents);

        var visited = new HashSet<string>(StringComparer.Ordinal) { root.ThreadId };
        foreach (var line in RenderChildren(root.ThreadId, "", byParent, agents, visited))
            yield return line;

        foreach (var orphan in agents.Where(x => x.Role != "root" && !visited.Contains(x.ThreadId)))
            yield return "?  " + FormatAgentTreeLine(orphan, agents);
    }

    private static IEnumerable<string> RenderChildren(
        string parentId,
        string prefix,
        IReadOnlyDictionary<string, AgentReport[]> byParent,
        IReadOnlyList<AgentReport> agents,
        HashSet<string> visited)
    {
        if (!byParent.TryGetValue(parentId, out var children))
            yield break;

        for (var i = 0; i < children.Length; i++)
        {
            var child = children[i];
            var last = i == children.Length - 1;
            var branch = last ? "└─ " : "├─ ";
            if (!visited.Add(child.ThreadId))
            {
                yield return prefix + branch + "[cycle] " + child.ThreadId;
                continue;
            }

            yield return prefix + branch + FormatAgentTreeLine(child, agents);
            foreach (var line in RenderChildren(
                         child.ThreadId,
                         prefix + (last ? "   " : "│  "),
                         byParent,
                         agents,
                         visited))
                yield return line;
        }
    }

    private static string FormatAgentTreeLine(AgentReport agent, IReadOnlyList<AgentReport> agents)
    {
        var label = AgentDisplayName(agent, agents);
        var tokenSuffix = agent.Tokens.Available
            ? $"   {Compact(agent.Tokens.InputTokens)} in / {Compact(agent.Tokens.CachedInputTokens)} cached / {Compact(agent.Tokens.OutputTokens)} out"
            : "";
        var taskSuffix = agent.Role == "worker" && !string.IsNullOrWhiteSpace(agent.TaskTitle)
            ? $"   {agent.TaskTitle}"
            : "";
        return $"{label,-18} {FormatDuration(agent.DurationMilliseconds),8}{tokenSuffix}{taskSuffix}";
    }

    private static string AgentDisplayName(AgentReport agent, IReadOnlyCollection<AgentReport> agents)
    {
        if (agent.Role == "root")
            return "Factory root";
        if (agent.Role == "planner" && agents.Count(x => x.Role == "planner") == 1)
            return "Planner";
        return $"{Cap(agent.Role)} #{agent.Sequence}";
    }

    private static string FormatTaskTokens(TokenMetrics? tokens) =>
        tokens?.Available == true
            ? $"{Compact(tokens.InputTokens)} input / {Compact(tokens.CachedInputTokens)} cached / {Compact(tokens.OutputTokens)} output"
            : "unavailable";

    private static string Compact(long? value)
    {
        if (value is null)
            return "unavailable";
        if (value >= 1_000_000)
            return $"{value.Value / 1_000_000d:0.#}M";
        if (value >= 1_000)
            return $"{value.Value / 1_000d:0.#}k";
        return value.Value.ToString();
    }

    private static string BoolStatus(bool? value) => value is null ? "unavailable" : value.Value ? "yes" : "no";
    private static string NullableNum(long? value) => value?.ToString() ?? "unavailable";

    private static string FormatTime(DateTimeOffset? value) =>
        value is null ? "unavailable" : value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");

    private static string FormatClock(DateTimeOffset? value) =>
        value is null ? "--:--:--" : value.Value.ToLocalTime().ToString("HH:mm:ss");

    private static string FormatDuration(long? ms)
    {
        if (ms is null)
            return "unavailable";
        var duration = TimeSpan.FromMilliseconds(ms.Value);
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s"
            : duration.TotalMinutes >= 1
                ? $"{duration.Minutes}m {duration.Seconds}s"
                : $"{duration.TotalSeconds:0.#}s";
    }

    private static string Num(long? value) => value?.ToString() ?? "unavailable";
    private static string Short(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unavailable";
        var normalized = Regex.Replace(value, @"\s+", " ").Trim();
        return normalized.Length <= max ? normalized : normalized[..max] + " [truncated]";
    }

    private static string Present(bool value) => value ? "PRESENT" : "absent";
    private static string Available(bool value) => value ? "available" : "unavailable";
    private static string Cap(string value) => string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
