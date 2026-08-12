using System.Text.Json;
using Idd.Factory.LiveTests.Models;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed class EvalAssertionCollector
{
    private readonly List<FactoryEvalAssertion> assertions = [];
    public IReadOnlyList<FactoryEvalAssertion> Assertions => assertions;
    public void Require(bool condition, string category, string name, string failure) => assertions.Add(new(category, name, condition ? "PASS" : "FAIL", condition ? "Passed." : failure));
    public void Inconclusive(string category, string name, string message) => assertions.Add(new(category, name, "INCONCLUSIVE", message));
    public bool HasFailures => assertions.Any(assertion => assertion.Status == "FAIL");
    public bool HasFailuresIn(string category) => assertions.Any(assertion => assertion.Status == "FAIL" && assertion.Category == category);
    public async Task WriteAsync(FactoryEvalWorkspace workspace, FactoryEvalResult result, FactoryEvalMetrics metrics, FactoryResultReadResult factoryResult, AgentTrace agentTrace)
    {
        var efficiency = BuildEfficiency(agentTrace, metrics);
        var efficiencyJsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        await File.WriteAllTextAsync(Path.Combine(workspace.RunDirectory, "assertions.json"), JsonSerializer.Serialize(assertions, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        await File.WriteAllTextAsync(Path.Combine(workspace.RunDirectory, "metrics.json"), JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        await File.WriteAllTextAsync(workspace.EfficiencyJsonPath, JsonSerializer.Serialize(efficiency, efficiencyJsonOptions) + "\n");
        await File.WriteAllTextAsync(workspace.EfficiencyMarkdownPath, EfficiencyReportWriter.Write(efficiency));
        var failures = assertions.Where(a => a.Status == "FAIL").ToArray();
        var factoryStatus = factoryResult.IsSuccess ? "PASS" : result.FactoryResultExpected ? "FAIL" : "NOT EXPECTED";
        var report = $"# IDD Factory Eval Report\n\nRun: {Path.GetFileName(workspace.RunDirectory)}\nCase: two-step-catalog\n\nCodex process: {(result.CodexProcessPassed ? "PASS" : "FAIL")}\nFactory outcome: {result.FactoryOutcome ?? "unavailable"}\nExecution response: {(result.ExecutionResponsePassed ? "PASS" : "FAIL")}\nFactory result: {factoryStatus}\nProduct verification: {(result.ProductPassed ? "PASS" : "FAIL")}\nOverall: {result.Outcome}\n\n## Product\n\n- Build: {(result.FinalBuildPassed ? "PASS" : "FAIL")}\n- Tests: {(result.FinalTestsPassed ? "PASS" : "FAIL")}\n\n## Factory\n\n- Semantic subprocess workers: {metrics.TotalSpawnedAgentCount?.ToString() ?? "unavailable"}\n- Coordinator collaboration spawns: {metrics.SpawnAgentCallCount}\n- Subtasks: {factoryResult.Result?.Int("completedSubtaskCount")} / {factoryResult.Result?.Int("subtaskCount")}\n- Review checkpoints: {factoryResult.Result?.Int("completedReviewCheckpointCount")} / {factoryResult.Result?.Int("reviewCheckpointCount")}\n\n## Efficiency\n\n- Model: {metrics.ModelEffective ?? "unavailable"}\n- Model turns: {metrics.ModelTurnCount}\n- Tool calls: {metrics.ToolCallCount}\n- Input tokens: {metrics.InputTokens?.ToString() ?? "unavailable"}\n- Cached input tokens: {metrics.CachedInputTokens?.ToString() ?? "unavailable"}\n- Output tokens: {metrics.OutputTokens?.ToString() ?? "unavailable"}\n- Total tokens: {metrics.TotalTokens?.ToString() ?? "unavailable"}\n- Wall time: {metrics.WallTimeMs?.ToString() ?? "unavailable"} ms\n\n## Failed assertions\n\n" + (failures.Length == 0 ? "None.\n" : string.Join("\n", failures.Select((f, i) => $"{i + 1}. [{f.Category}] {f.Name}: {f.Message}")) + "\n") + "\n## Artifacts\n\n- events.jsonl\n- last-message.json\n- verification/git-diff.patch\n- assertions.json\n";
        var oldEfficiency = $"- Model: {metrics.ModelEffective ?? "unavailable"}\n- Model turns: {metrics.ModelTurnCount}\n- Tool calls: {metrics.ToolCallCount}\n- Input tokens: {metrics.InputTokens?.ToString() ?? "unavailable"}\n- Cached input tokens: {metrics.CachedInputTokens?.ToString() ?? "unavailable"}\n- Output tokens: {metrics.OutputTokens?.ToString() ?? "unavailable"}\n- Total tokens: {metrics.TotalTokens?.ToString() ?? "unavailable"}\n- Wall time: {metrics.WallTimeMs?.ToString() ?? "unavailable"} ms";
        var newEfficiency = $"- Model: {metrics.ModelEffective ?? "unavailable"}\n- Model turns: {efficiency.Summary.ModelTurns}\n- Input tokens: {Value(efficiency.Summary.InputTokens)}\n- Cached input tokens: {Value(efficiency.Summary.CachedInputTokens)}\n- Fresh input tokens: {Value(efficiency.Summary.FreshInputTokens)}\n- Cache %: {Percent(efficiency.Summary.CachedInputPercentage)}\n- Output tokens: {Value(efficiency.Summary.OutputTokens)}\n- Agent threads: {efficiency.Summary.AgentThreads}\n- Tool calls: {efficiency.Summary.ToolCalls}\n- Failed/rejected tool calls: {efficiency.Summary.FailedToolCalls}/{efficiency.Summary.RejectedToolCalls}\n- Wall time: {Value(efficiency.Summary.WallTimeMs)} ms\n- Largest token consumer: {Largest(efficiency.Agents, false)}\n- Largest fresh-input consumer: {Largest(efficiency.Agents, true)}\n\nDetailed efficiency diagnostics: efficiency.md / efficiency.json";
        report = report.Replace(oldEfficiency, newEfficiency, StringComparison.Ordinal);
        report = report.Replace("- assertions.json\n", "- assertions.json\n- agent-trace.json\n- efficiency.json\n- efficiency.md\n", StringComparison.Ordinal);
        report += $"\n## Orchestration telemetry\n\n- Wait agent calls: {metrics.WaitAgentCallCount}\n- Completed child agents: {metrics.CompletedChildAgentCount}\n";
        report += AgentTraceSection(agentTrace);
        await File.WriteAllTextAsync(Path.Combine(workspace.RunDirectory, "report.md"), report);
    }

    private static string Value(long? value) => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unavailable";
    private static string Percent(double? value) => value?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%" ?? "unavailable";
    private static string Largest(IEnumerable<EfficiencyAgent> agents, bool fresh)
    {
        var agent = agents.Where(value => (fresh ? value.FreshInputTokens : value.InputTokens) is not null).OrderByDescending(value => fresh ? value.FreshInputTokens : value.InputTokens).FirstOrDefault();
        return agent is null ? "unavailable" : $"{agent.Role} ({agent.ThreadId[..Math.Min(12, agent.ThreadId.Length)]}, {Value(fresh ? agent.FreshInputTokens : agent.InputTokens)})";
    }

    private static EfficiencyTelemetry BuildEfficiency(AgentTrace trace, FactoryEvalMetrics metrics)
    {
        try { return EfficiencyTelemetryBuilder.Build(trace, metrics); }
        catch (Exception exception)
        {
            var fresh = metrics.InputTokens is not null && metrics.CachedInputTokens is not null && metrics.InputTokens >= metrics.CachedInputTokens ? metrics.InputTokens - metrics.CachedInputTokens : null;
            double? cachePercentage = metrics.InputTokens is > 0 && metrics.CachedInputTokens is not null && metrics.CachedInputTokens <= metrics.InputTokens ? 100d * metrics.CachedInputTokens.Value / metrics.InputTokens.Value : null;
            var emptyHotspots = new EfficiencyHotspots([], [], [], [], [], [], [], [], [], []);
            return new(1, new(metrics.InputTokens, metrics.CachedInputTokens, fresh, cachePercentage, metrics.OutputTokens, metrics.ReasoningOutputTokens, metrics.TotalTokens, trace.Agents.Count, SafeCount(metrics.ModelTurnCount), SafeCount(metrics.ToolCallCount), 0, 0, 0, metrics.WallTimeMs), [], [], [], [], [], emptyHotspots, trace.Diagnostics.Concat([new("EFFICIENCY_BUILD_FAILED", "warning", "Detailed efficiency telemetry could not be aggregated: " + exception.Message, null, null)]).ToArray());
        }
    }

    private static int SafeCount(long value) => value >= int.MaxValue ? int.MaxValue : value <= 0 ? 0 : (int)value;

    private static string AgentTraceSection(AgentTrace trace)
    {
        if (trace.Agents.Count == 0)
        {
            var reason = trace.Diagnostics.FirstOrDefault(d => d.Code is "ROOT_ROLLOUT_NOT_FOUND" or "ROOT_THREAD_ID_NOT_FOUND" or "CODEX_HOME_NOT_FOUND")?.Message ?? "Root thread rollout was not found in the standard Codex session storage.";
            return $"\n## Agent trace\n\nAgent trace: unavailable\n\nReason: {reason}\n\nDiagnostics:\n\n" + string.Join('\n', trace.Diagnostics.Select(d => $"- `{d.Code}`: {d.Message}")) + "\n";
        }
        var diagnosticList = trace.Diagnostics.Count == 0 ? string.Empty : "\n\n" + string.Join('\n', trace.Diagnostics.Select(d => $"- {d.Code} — thread {d.ThreadId ?? "unknown"}: {d.Message}"));
        return $"\n## Agent trace\n\nAgent threads: {trace.Agents.Count}  \nMaximum depth: {MaximumDepth(trace)}  \nTrace diagnostics: {trace.Diagnostics.Count}{diagnosticList}\n\n{AgentTraceReportWriter.WriteMermaid(trace)}\n{AgentTraceReportWriter.WriteTable(trace)}";
    }

    private static int MaximumDepth(AgentTrace trace)
    {
        var byId = trace.Agents.ToDictionary(agent => agent.ThreadId, StringComparer.Ordinal);
        return trace.Agents.Select(agent => Depth(agent, byId, new HashSet<string>(StringComparer.Ordinal))).DefaultIfEmpty(0).Max();
    }

    private static int Depth(AgentTraceNode agent, IReadOnlyDictionary<string, AgentTraceNode> byId, HashSet<string> seen) => agent.ParentThreadId is not null && seen.Add(agent.ThreadId) && byId.TryGetValue(agent.ParentThreadId, out var parent) ? 1 + Depth(parent, byId, seen) : 0;
    public void ThrowIfFailed(string runDirectory)
    {
        var failures = assertions.Where(assertion => assertion.Status == "FAIL").ToArray();
        if (failures.Length == 0) return;

        var consoleFailures = SelectConsoleFailures(failures);
        var reportPath = Path.Combine(runDirectory, "report.md");
        throw new Xunit.Sdk.XunitException(
            $"IDD Factory eval failed:{Environment.NewLine}" +
            string.Join(Environment.NewLine, consoleFailures.Select(FormatFailure)) +
            $"{Environment.NewLine}Report: {reportPath}");
    }

    private static FactoryEvalAssertion[] SelectConsoleFailures(IEnumerable<FactoryEvalAssertion> failures)
    {
        var result = new List<FactoryEvalAssertion>();
        var orchestrationFailureShown = false;
        foreach (var failure in failures)
        {
            if (failure.Category == "Orchestration failure")
            {
                if (orchestrationFailureShown) continue;
                orchestrationFailureShown = true;
            }
            result.Add(failure);
        }
        return result.ToArray();
    }

    private static string FormatFailure(FactoryEvalAssertion assertion)
    {
        var lines = assertion.Message
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var title = $"- [{assertion.Category}] {assertion.Name}";
        return lines.Length == 0
            ? title
            : title + Environment.NewLine + string.Join(Environment.NewLine, lines.Select(line => $"  {line}"));
    }
}
