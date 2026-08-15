using System.Text;
using System.Text.Json;

namespace Idd.Factory.Benchmark;

public static class ReportWriter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task WriteAsync(string outputDirectory, BenchmarkReport report)
    {
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "report.json"), JsonSerializer.Serialize(report, Json));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "report.md"), Markdown(report));
    }

    public static string Markdown(BenchmarkReport report)
    {
        var text = new StringBuilder();
        text.AppendLine("# Factory Benchmark Report").AppendLine();
        text.AppendLine($"Benchmark: {report.Benchmark}  ");
        text.AppendLine($"Model: {report.Environment.Model}  ");
        text.AppendLine($"Reasoning: {report.Environment.ReasoningEffort}  ");
        text.AppendLine($"Windows sandbox: {report.Environment.WindowsSandbox ?? "n/a"}  ");
        text.AppendLine($"Codex: {report.Environment.CodexVersion}  ");
        text.AppendLine($"Factory: {report.Environment.FactoryVersion}  ");
        text.AppendLine($"Source: {report.Environment.GitRevision}{(report.Environment.GitDirty ? " (dirty)" : "")}  ");
        text.AppendLine($"Repeats: {report.Repeats}").AppendLine();
        foreach (var warning in report.ComparabilityWarnings) text.AppendLine($"> WARNING: {warning}").AppendLine();
        text.AppendLine("| Mode | Success | Gross input | Cached input | New input | Output | Tool batches | Commands | Tool output chars | Failed output chars | Codex processes | Agent time | Acceptance time | Total time |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var pair in report.Aggregates)
        {
            var value = pair.Value; var median = value.Median;
            text.AppendLine($"| {Display(pair.Key)} | {value.SuccessfulRuns}/{value.Runs} | {N(median?.GrossInputTokens)} | {N(median?.CachedInputTokens)} | {N(median?.NewInputTokens)} | {N(median?.OutputTokens)} | {N(median?.ToolBatches)} | {N(median?.Commands)} | {N(median?.ToolOutputChars)} | {N(median?.FailedToolOutputChars)} | {N(value.MedianCodexProcessCount)} | {Duration(value.MedianAgentDurationMilliseconds)} | {Duration(value.MedianAcceptanceDurationMilliseconds)} | {Duration(value.MedianTotalDurationMilliseconds)} |");
        }
        text.AppendLine().AppendLine("## Derived observed overhead").AppendLine();
        text.AppendLine("These differences are estimates from nondeterministic model runs, not mathematically exact causal costs.").AppendLine();
        var comparisons = report.Comparisons;
        text.AppendLine($"- Factory / Direct: {Ratio(comparisons.FactoryToDirect)}");
        text.AppendLine($"- Manual isolated / Direct: {Ratio(comparisons.ManualIsolatedToDirect)}");
        text.AppendLine($"- Factory split replay / Direct: {Ratio(comparisons.FactorySplitReplayToDirect)}");
        text.AppendLine($"- Factory / Factory split replay: {Ratio(comparisons.FactoryToFactorySplitReplay)}");
        text.AppendLine($"- Structuring (B1 - B0): {Signed(comparisons.StructuringOverhead)} gross input tokens");
        text.AppendLine($"- Isolation (B2 - B1): {Signed(comparisons.IsolationOverhead)} gross input tokens");
        text.AppendLine($"- Decomposition choice (B3 - B2): {Signed(comparisons.DecompositionChoiceOverhead)} gross input tokens");
        text.AppendLine($"- Factory orchestration (B4 - B3): {Signed(comparisons.FactoryOrchestrationOverhead)} gross input tokens");
        text.AppendLine($"- Total Factory (B4 - B0): {Signed(comparisons.TotalFactoryOverhead)} gross input tokens");
        text.AppendLine().AppendLine("## Factory decomposition").AppendLine();
        foreach (var pair in report.Modes.Where(x => x.Key is BenchmarkModes.FactorySplitReplay or BenchmarkModes.Factory))
            foreach (var run in pair.Value)
            {
                var decomposition = run.FactoryDecomposition;
                text.AppendLine($"### {Display(pair.Key)} run {run.Iteration:00}").AppendLine();
                if (decomposition is null) { text.AppendLine("No decomposition was captured.").AppendLine(); continue; }
                text.AppendLine($"Shared with full Factory run: {(decomposition.SharedWithFactoryRun ? "yes" : "no; independently generated")}").AppendLine();
                foreach (var item in decomposition.WorkItems) text.AppendLine($"- `{item.Id}` ({item.Kind}): {item.Title} — `{item.ContractPath}`");
                text.AppendLine();
            }
        return text.ToString();
    }

    private static string Display(string mode) => mode switch { "direct" => "Direct", "structured-single" => "Structured single", "manual-isolated" => "Manual isolated", "factory-split-replay" => "Factory split replay", "factory" => "Factory", _ => mode };
    private static string N(long? value) => value?.ToString("N0") ?? "n/a";
    private static string Duration(long? value) => value is null ? "n/a" : TimeSpan.FromMilliseconds(value.Value).ToString(@"hh\:mm\:ss");
    private static string Ratio(double? value) => value is null ? "n/a" : $"{value:F2}x";
    private static string Signed(long? value) => value is null ? "n/a" : $"{value:+#,0;-#,0;0}";
}
