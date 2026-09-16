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
        text.AppendLine("| Mode | Success | Agent invocations | Gross input | New input | Output | Tool batches | Duration |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var pair in report.Aggregates)
        {
            var value = pair.Value;
            var median = value.Median;
            text.AppendLine($"| {Display(pair.Key)} | {value.SuccessfulRuns}/{value.Runs} | {N(value.MedianCodexProcessCount)} | {N(median?.GrossInputTokens)} | {N(median?.NewInputTokens)} | {N(median?.OutputTokens)} | {N(median?.ToolBatches)} | {Duration(value.MedianTotalDurationMilliseconds)} |");
        }

        text.AppendLine().AppendLine("## Direct vs Factory").AppendLine();
        text.AppendLine("These values summarize repeated nondeterministic model runs; they are observations, not an exact causal decomposition.").AppendLine();
        text.AppendLine($"- Factory / Direct gross input: {Ratio(report.Comparisons.FactoryToDirect)}");
        text.AppendLine($"- Factory - Direct gross input: {Signed(report.Comparisons.FactoryOverhead)} tokens");

        text.AppendLine().AppendLine("## Factory decomposition").AppendLine();
        if (!report.Modes.TryGetValue(BenchmarkModes.Factory, out var factoryRuns))
        {
            text.AppendLine("Factory mode was not run.").AppendLine();
        }
        else
        {
            foreach (var run in factoryRuns)
            {
                var decomposition = run.FactoryDecomposition;
                text.AppendLine($"### Factory run {run.Iteration:00}").AppendLine();
                if (decomposition is null)
                {
                    text.AppendLine("No decomposition was captured.").AppendLine();
                    continue;
                }
                foreach (var item in decomposition.WorkItems)
                    text.AppendLine($"- `{item.Id}` ({item.Kind}): {item.Title} — `{item.ContractPath}`");
                text.AppendLine();
            }
        }
        return text.ToString();
    }

    private static string Display(string mode) => mode switch { "direct" => "Direct", "factory" => "Factory", _ => mode };
    private static string N(long? value) => value?.ToString("N0") ?? "n/a";
    private static string Duration(long? value) => value is null ? "n/a" : TimeSpan.FromMilliseconds(value.Value).ToString(@"hh\:mm\:ss");
    private static string Ratio(double? value) => value is null ? "n/a" : $"{value:F2}x";
    private static string Signed(long? value) => value is null ? "n/a" : $"{value:+#,0;-#,0;0}";
}
