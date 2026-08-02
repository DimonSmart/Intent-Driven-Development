using System.Text.Json;
using Idd.Factory.LiveTests.Models;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed class EvalAssertionCollector
{
    private readonly List<FactoryEvalAssertion> assertions = [];
    public void Require(bool condition, string category, string name, string failure) => assertions.Add(new(category, name, condition ? "PASS" : "FAIL", condition ? "Passed." : failure));
    public void Inconclusive(string category, string name, string message) => assertions.Add(new(category, name, "INCONCLUSIVE", message));
    public bool HasFailures => assertions.Any(assertion => assertion.Status == "FAIL");
    public async Task WriteAsync(FactoryEvalWorkspace workspace, FactoryEvalResult result, FactoryEvalMetrics metrics, FactoryResult? factoryResult)
    {
        await File.WriteAllTextAsync(Path.Combine(workspace.RunDirectory, "assertions.json"), JsonSerializer.Serialize(assertions, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        await File.WriteAllTextAsync(Path.Combine(workspace.RunDirectory, "metrics.json"), JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        var failures = assertions.Where(a => a.Status == "FAIL").ToArray();
        var report = $"# IDD Factory Eval Report\n\nRun: {Path.GetFileName(workspace.RunDirectory)}\nCase: two-step-catalog\nResult: {result.Outcome}\n\n## Product\n\n- Build: {(result.ProductPassed ? "PASS" : "FAIL")}\n- Tests: {(result.ProductPassed ? "PASS" : "FAIL")}\n\n## Factory\n\n- Outcome: {factoryResult?.String("factoryOutcome") ?? "unavailable"}\n- Subtasks: {factoryResult?.Int("completedSubtaskCount")} / {factoryResult?.Int("subtaskCount")}\n- Review checkpoints: {factoryResult?.Int("completedReviewCheckpointCount")} / {factoryResult?.Int("reviewCheckpointCount")}\n\n## Efficiency\n\n- Model: {metrics.ModelEffective ?? "unavailable"}\n- Model turns: {metrics.ModelTurnCount}\n- Tool calls: {metrics.ToolCallCount}\n- Total tokens: {metrics.TotalTokens?.ToString() ?? "unavailable"}\n- Cached input tokens: {metrics.CachedInputTokens?.ToString() ?? "unavailable"}\n- Wall time: {metrics.WallTimeMs} ms\n\n## Failed assertions\n\n" + (failures.Length == 0 ? "None.\n" : string.Join("\n", failures.Select((f, i) => $"{i + 1}. {f.Message}")) + "\n") + "\n## Artifacts\n\n- events.jsonl\n- last-message.json\n- verification/git-diff.patch\n- assertions.json\n";
        await File.WriteAllTextAsync(Path.Combine(workspace.RunDirectory, "report.md"), report);
    }
    public void ThrowIfFailed(string runDirectory)
    {
        if (!HasFailures) return;
        throw new Xunit.Sdk.XunitException("IDD Factory live eval failed:\n" + string.Join("\n", assertions.Where(a => a.Status == "FAIL").Select(a => $"- {a.Message}")) + $"\nArtifacts: {runDirectory}");
    }
}
