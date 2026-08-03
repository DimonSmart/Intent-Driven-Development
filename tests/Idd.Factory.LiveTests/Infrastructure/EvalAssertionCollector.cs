using System.Text.Json;
using Idd.Factory.LiveTests.Models;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed class EvalAssertionCollector
{
    private readonly List<FactoryEvalAssertion> assertions = [];
    public void Require(bool condition, string category, string name, string failure) => assertions.Add(new(category, name, condition ? "PASS" : "FAIL", condition ? "Passed." : failure));
    public void Inconclusive(string category, string name, string message) => assertions.Add(new(category, name, "INCONCLUSIVE", message));
    public bool HasFailures => assertions.Any(assertion => assertion.Status == "FAIL");
    public bool HasFailuresIn(string category) => assertions.Any(assertion => assertion.Status == "FAIL" && assertion.Category == category);
    public async Task WriteAsync(FactoryEvalWorkspace workspace, FactoryEvalResult result, FactoryEvalMetrics metrics, FactoryResultReadResult factoryResult)
    {
        await File.WriteAllTextAsync(Path.Combine(workspace.RunDirectory, "assertions.json"), JsonSerializer.Serialize(assertions, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        await File.WriteAllTextAsync(Path.Combine(workspace.RunDirectory, "metrics.json"), JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        var failures = assertions.Where(a => a.Status == "FAIL").ToArray();
        var factoryStatus = factoryResult.IsSuccess ? "PASS" : result.FactoryResultExpected ? "FAIL" : "NOT EXPECTED";
        var report = $"# IDD Factory Eval Report\n\nRun: {Path.GetFileName(workspace.RunDirectory)}\nCase: two-step-catalog\n\nCodex process: {(result.CodexProcessPassed ? "PASS" : "FAIL")}\nFactory outcome: {result.FactoryOutcome ?? "unavailable"}\nExecution response: {(result.ExecutionResponsePassed ? "PASS" : "FAIL")}\nFactory result: {factoryStatus}\nProduct verification: {(result.ProductPassed ? "PASS" : "FAIL")}\nOverall: {result.Outcome}\n\n## Product\n\n- Build: {(result.FinalBuildPassed ? "PASS" : "FAIL")}\n- Tests: {(result.FinalTestsPassed ? "PASS" : "FAIL")}\n\n## Factory\n\n- Expected minimum spawned agents: 2\n- Spawn agent calls: {metrics.SpawnAgentCallCount}\n- Successfully spawned agents: {metrics.SpawnedAgentCount}\n- Failed spawn calls: {metrics.FailedSpawnAgentCallCount}\n- Subtasks: {factoryResult.Result?.Int("completedSubtaskCount")} / {factoryResult.Result?.Int("subtaskCount")}\n- Review checkpoints: {factoryResult.Result?.Int("completedReviewCheckpointCount")} / {factoryResult.Result?.Int("reviewCheckpointCount")}\n\n## Efficiency\n\n- Model: {metrics.ModelEffective ?? "unavailable"}\n- Model turns: {metrics.ModelTurnCount}\n- Tool calls: {metrics.ToolCallCount}\n- Input tokens: {metrics.InputTokens?.ToString() ?? "unavailable"}\n- Cached input tokens: {metrics.CachedInputTokens?.ToString() ?? "unavailable"}\n- Output tokens: {metrics.OutputTokens?.ToString() ?? "unavailable"}\n- Total tokens: {metrics.TotalTokens?.ToString() ?? "unavailable"}\n- Wall time: {metrics.WallTimeMs?.ToString() ?? "unavailable"} ms\n\n## Failed assertions\n\n" + (failures.Length == 0 ? "None.\n" : string.Join("\n", failures.Select((f, i) => $"{i + 1}. [{f.Category}] {f.Name}: {f.Message}")) + "\n") + "\n## Artifacts\n\n- events.jsonl\n- last-message.json\n- verification/git-diff.patch\n- assertions.json\n";
        report += $"\n## Orchestration telemetry\n\n- Wait agent calls: {metrics.WaitAgentCallCount}\n- Completed child agents: {metrics.CompletedChildAgentCount}\n";
        await File.WriteAllTextAsync(Path.Combine(workspace.RunDirectory, "report.md"), report);
    }
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
