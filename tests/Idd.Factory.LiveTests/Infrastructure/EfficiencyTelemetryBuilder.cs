using Idd.Factory.LiveTests.Models;

namespace Idd.Factory.LiveTests.Infrastructure;

public static class EfficiencyTelemetryBuilder
{
    public static EfficiencyTelemetry Build(AgentTrace trace, FactoryEvalMetrics metrics)
    {
        var diagnostics = trace.Diagnostics.ToList();
        var agents = trace.Agents.Select(ToAgent).ToArray();
        var rootAgents = trace.Agents.Where(agent => agent.Role == "factory-root").ToArray();
        var workerAgents = trace.Agents.Where(agent => agent.Role != "factory-root").ToArray();
        if (workerAgents.Length > 0 && workerAgents.Any(agent => agent.InputTokens is null)) diagnostics.Add(new("EFFICIENCY_INPUT_INCOMPLETE", "info", "At least one semantic worker has no input-token counter; semantic-worker and end-to-end totals are unavailable.", null, null));
        if (workerAgents.Length > 0 && workerAgents.Any(agent => agent.CachedInputTokens is null)) diagnostics.Add(new("EFFICIENCY_CACHED_INPUT_INCOMPLETE", "info", "At least one semantic worker has no cached-input counter; semantic-worker cached and fresh totals are unavailable.", null, null));

        var root = rootAgents.Length == 0 || rootAgents.Any(agent => agent.InputTokens is null)
            ? FromMetrics(metrics)
            : FromAgents(rootAgents);
        var workers = FromAgents(workerAgents);
        var endToEnd = Add(root, workers);
        var input = endToEnd.InputTokens;
        var cached = endToEnd.CachedInputTokens;
        var fresh = Fresh(input, cached);
        if (input is not null && cached is not null && fresh is null) diagnostics.Add(new("EFFICIENCY_TOKEN_COUNTER_INCONSISTENT", "warning", "Cached input exceeds input; aggregate fresh input is unavailable.", null, null));
        var output = endToEnd.OutputTokens;
        var reasoning = endToEnd.ReasoningOutputTokens;
        var total = endToEnd.TotalTokens;
        var toolCalls = trace.Agents.SelectMany(agent => (agent.ToolCalls ?? []).Select(call => (Agent: agent, Call: call))).Select(item => new EfficiencyToolCall(item.Call.Sequence, item.Agent.ThreadId, item.Agent.Role, item.Call.CallId, item.Call.Tool, item.Call.StartedAt, item.Call.CompletedAt, item.Call.DurationMs, item.Call.Status, item.Call.IsFailure, item.Call.IsRejected, item.Call.IsRetryOrFallback, item.Call.Operation, item.Call.CommandSummary, item.Call.ExitCode, item.Call.ResultBytes, item.Call.ChildThreadIds, item.Call.IsTerminalWait, item.Call.RepeatedWaitNumber, item.Call.ChildRole, item.Call.DispatchCharacters, item.Call.DispatchUtf8Bytes)).OrderBy(call => call.StartedAt).ThenBy(call => call.ThreadId, StringComparer.Ordinal).ThenBy(call => call.Sequence).ToArray();
        var fileAccess = trace.Agents.SelectMany(agent => (agent.FileReads ?? []).Select(read => (agent.ThreadId, Read: read))).GroupBy(item => item.Read.Path, StringComparer.OrdinalIgnoreCase).Select(group => new EfficiencyFileAccess(CodexRolloutReader.NormalizePath(group.First().Read.Path), group.Count(), group.Select(item => item.ThreadId).Distinct(StringComparer.Ordinal).Count(), group.Sum(item => item.Read.ReturnedBytes), group.Select(item => item.ThreadId).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray())).OrderByDescending(file => file.ReadCount).ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        var roles = trace.Agents.GroupBy(agent => agent.Role, StringComparer.Ordinal).Select(group => ToRole(group.Key, group, input, fresh, toolCalls.Length)).OrderBy(role => role.Role, StringComparer.Ordinal).ToArray();
        var groups = new[]
        {
            ToGroup("orchestration", trace.Agents.Where(agent => agent.Role == "factory-root"), input, fresh),
            ToGroup("decomposition", trace.Agents.Where(agent => agent.Role == "task-decomposer"), input, fresh),
            ToGroup("implementation", trace.Agents.Where(agent => agent.Role == "implementer"), input, fresh),
            ToGroup("checkpoint-review", trace.Agents.Where(agent => agent.Role == "checkpoint-reviewer"), input, fresh),
            ToGroup("final-review", trace.Agents.Where(agent => agent.Role == "final-reviewer"), input, fresh),
            ToGroup("replan", trace.Agents.Where(agent => agent.Role == "factory-replanner"), input, fresh)
        };
        var failedToolCalls = trace.Agents.Count == 0 ? toolCalls.Count(call => call.IsFailure) : trace.Agents.Sum(agent => agent.FailedToolCallCount);
        var rejectedToolCalls = trace.Agents.Count == 0 ? toolCalls.Count(call => call.IsRejected) : trace.Agents.Sum(agent => agent.RejectedToolCallCount);
        var retryOrFallbackCalls = trace.Agents.Count == 0 ? toolCalls.Count(call => call.IsRetryOrFallback) : trace.Agents.Sum(agent => agent.RetryOrFallbackCallCount);
        var summary = new EfficiencySummary(input, cached, fresh, Percentage(cached, input), output, reasoning, total, trace.Agents.Count, trace.Agents.Count == 0 ? Count(metrics.ModelTurnCount) : trace.Agents.Sum(agent => agent.TurnCount), trace.Agents.Count == 0 ? Count(metrics.ToolCallCount) : toolCalls.Length, failedToolCalls, rejectedToolCalls, retryOrFallbackCalls, metrics.WallTimeMs);
        return new(2, summary, root, workers, endToEnd, roles, agents, toolCalls, fileAccess, groups, Hotspots(agents, toolCalls, fileAccess), diagnostics);
    }

    private static EfficiencyTokenBreakdown FromMetrics(FactoryEvalMetrics metrics) =>
        new(metrics.InputTokens, metrics.CachedInputTokens, Fresh(metrics.InputTokens, metrics.CachedInputTokens), metrics.OutputTokens, metrics.ReasoningOutputTokens, metrics.TotalTokens);

    private static EfficiencyTokenBreakdown FromAgents(IReadOnlyList<AgentTraceNode> agents)
    {
        if (agents.Count == 0) return new(0, 0, 0, 0, 0, 0);
        var input = Sum(agents.Select(agent => agent.InputTokens));
        var cached = Sum(agents.Select(agent => agent.CachedInputTokens));
        return new(input, cached, Fresh(input, cached), Sum(agents.Select(agent => agent.OutputTokens)), Sum(agents.Select(agent => agent.ReasoningOutputTokens)), Sum(agents.Select(agent => agent.TotalTokens)));
    }

    private static EfficiencyTokenBreakdown Add(EfficiencyTokenBreakdown left, EfficiencyTokenBreakdown right) =>
        new(Add(left.InputTokens, right.InputTokens), Add(left.CachedInputTokens, right.CachedInputTokens), Add(left.FreshInputTokens, right.FreshInputTokens), Add(left.OutputTokens, right.OutputTokens), Add(left.ReasoningOutputTokens, right.ReasoningOutputTokens), Add(left.TotalTokens, right.TotalTokens));

    private static long? Add(long? left, long? right) => left is not null && right is not null ? left + right : null;

    private static EfficiencyAgent ToAgent(AgentTraceNode agent) => new(agent.ThreadId, agent.ParentThreadId, agent.Role, agent.WorkItem, agent.Action, agent.DurationMs, agent.TurnCount, agent.ToolCallCount, agent.InputTokens, agent.CachedInputTokens, agent.FreshInputTokens, agent.CachedInputPercentage, agent.OutputTokens, agent.ReasoningOutputTokens, agent.TotalTokens, agent.DispatchCharacters, agent.DispatchUtf8Bytes, agent.FailedToolCallCount, agent.RejectedToolCallCount, agent.RetryOrFallbackCallCount, agent.FileReadCount, agent.UniqueFileReadCount, agent.RepeatedFileReadCount, agent.FileReadBytes, agent.WaitAgentMs, agent.ToolCallCount == 0 || agent.InputTokens is null ? null : (double)agent.InputTokens.Value / agent.ToolCallCount, agent.TokenProgression ?? [], agent.DispatchReferences ?? [], agent.TerminalResult);
    private static EfficiencyRole ToRole(string role, IEnumerable<AgentTraceNode> source, long? totalInput, long? totalFresh, int totalTools)
    {
        var agents = source.ToArray(); var input = Sum(agents.Select(agent => agent.InputTokens)); var cached = Sum(agents.Select(agent => agent.CachedInputTokens)); var fresh = Fresh(input, cached);
        var references = agents.SelectMany(agent => agent.DispatchReferences ?? []).Where(reference => reference.Kind is "skill" or "role" or "project-verification" or "platform-dispatch").DistinctBy(reference => reference.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        return new(role, agents.Length, input, cached, fresh, Percentage(cached, input), Sum(agents.Select(agent => agent.OutputTokens)), Sum(agents.Select(agent => agent.TotalTokens)), agents.Sum(agent => agent.ToolCallCount), agents.Sum(agent => agent.DurationMs ?? 0), Percentage(input, totalInput), Percentage(fresh, totalFresh), totalTools == 0 ? null : 100d * agents.Sum(agent => agent.ToolCallCount) / totalTools, SumInt(references.Select(reference => reference.Characters)), SumInt(references.Select(reference => reference.Utf8Bytes)));
    }
    private static EfficiencyGroup ToGroup(string name, IEnumerable<AgentTraceNode> source, long? totalInput, long? totalFresh)
    {
        var agents = source.ToArray(); var input = Sum(agents.Select(agent => agent.InputTokens)); var fresh = Sum(agents.Select(agent => agent.FreshInputTokens));
        return new(name, agents.Length, input, fresh, agents.Sum(agent => agent.ToolCallCount), agents.Sum(agent => agent.DurationMs ?? 0), Percentage(input, totalInput), Percentage(fresh, totalFresh));
    }

    private static EfficiencyHotspots Hotspots(IReadOnlyList<EfficiencyAgent> agents, IReadOnlyList<EfficiencyToolCall> tools, IReadOnlyList<EfficiencyFileAccess> files)
    {
        var directFailures = tools.Where(tool => tool.IsFailure || tool.IsRejected)
            .Select(tool => $"{tool.Tool} [{tool.ThreadId}] → {(tool.IsRejected ? "rejected" : "failed")}")
            .ToList();
        foreach (var agent in agents)
        {
            var representedFailures = tools.Count(tool => tool.ThreadId == agent.ThreadId && tool.IsFailure);
            var representedRejections = tools.Count(tool => tool.ThreadId == agent.ThreadId && tool.IsRejected);
            var unrepresentedFailures = Math.Max(0, agent.FailedToolCalls - representedFailures);
            var unrepresentedRejections = Math.Max(0, agent.RejectedToolCalls - representedRejections);
            if (unrepresentedFailures > 0 || unrepresentedRejections > 0)
                directFailures.Add($"{agent.Role} [{agent.ThreadId}] → {unrepresentedFailures} process-log failure(s), {unrepresentedRejections} rejection(s)");
        }

        return new(
            agents.Where(agent => agent.InputTokens is not null).OrderByDescending(agent => agent.InputTokens).Take(5).Select(agent => agent.ThreadId).ToArray(),
            agents.Where(agent => agent.FreshInputTokens is not null).OrderByDescending(agent => agent.FreshInputTokens).Take(5).Select(agent => agent.ThreadId).ToArray(),
            agents.OrderByDescending(agent => agent.ToolCallCount).Take(5).Select(agent => agent.ThreadId).ToArray(),
            files.Where(file => file.ReadCount > 1).Take(10).Select(file => file.Path).ToArray(),
            tools.GroupBy(tool => tool.Tool, StringComparer.OrdinalIgnoreCase).OrderByDescending(group => group.Count()).Take(10).Select(group => $"{group.Key} ({group.Count()})").ToArray(),
            tools.Where(tool => tool.DurationMs is not null).OrderByDescending(tool => tool.DurationMs).Take(10).Select(tool => $"{tool.Tool} [{tool.ThreadId}] {tool.DurationMs} ms").ToArray(),
            tools.Where(tool => tool.Tool == "wait_agent").OrderByDescending(tool => tool.DurationMs).Take(10).Select(tool => $"{tool.ThreadId} → {tool.ChildThreadIds.FirstOrDefault() ?? "unknown"}: {tool.DurationMs?.ToString() ?? "unknown"} ms").ToArray(),
            directFailures.ToArray(),
            agents.Where(agent => agent.CachedInputPercentage is not null).OrderByDescending(agent => agent.CachedInputPercentage).Take(5).Select(agent => agent.ThreadId).ToArray(),
            agents.Where(agent => agent.InputPerToolCall is not null).OrderByDescending(agent => agent.InputPerToolCall).Take(5).Select(agent => agent.ThreadId).ToArray());
    }

    private static long? Sum(IEnumerable<long?> values) { var array = values.ToArray(); return array.Length > 0 && array.All(value => value is not null) ? array.Sum(value => value!.Value) : null; }
    private static long? Fresh(long? input, long? cached) => input is not null && cached is not null && input >= cached && cached >= 0 ? input - cached : null;
    private static double? Percentage(long? part, long? total) => part is not null && total is > 0 && part >= 0 && part <= total ? 100d * part.Value / total.Value : null;
    private static int Count(long value) => value >= int.MaxValue ? int.MaxValue : value <= 0 ? 0 : (int)value;
    private static int? SumInt(IEnumerable<int?> values) { var array = values.ToArray(); return array.Length > 0 && array.All(value => value is not null) ? array.Sum(value => value!.Value) : null; }
}
