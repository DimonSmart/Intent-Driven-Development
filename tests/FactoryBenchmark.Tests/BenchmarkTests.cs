using System.Text.Json;
using Idd.Factory.Benchmark;
using Xunit;

namespace FactoryBenchmark.Tests;

public sealed class BenchmarkTests
{
    [Fact]
    public void YamlParsing_LoadsDeclarativeDefinition()
    {
        using var fixture = BenchmarkFixture.Create();
        var result = BenchmarkDefinitionLoader.Load(fixture.Path);
        Assert.Equal("fixture", result.Name);
        Assert.Equal("high", result.Reasoning.Effort);
        Assert.Equal(["direct", "factory"], result.Modes);
        Assert.Equal("elevated", result.WindowsSandbox);
    }

    [Fact]
    public void CliParsing_ReadsAllOptions()
    {
        using var fixture = BenchmarkFixture.Create();
        var result = BenchmarkCliParser.Parse(["run", fixture.Path, "--repeat", "5", "--model", "gpt-test", "--output", fixture.Path, "--modes", "direct,factory", "--keep-workspaces", "--timeout-minutes", "9", "--windows-sandbox", "elevated", "--force"]);
        Assert.Equal(5, result.Repeat);
        Assert.Equal("gpt-test", result.Model);
        Assert.Equal(["direct", "factory"], result.Modes);
        Assert.True(result.KeepWorkspaces);
        Assert.Equal("elevated", result.WindowsSandbox);
        Assert.True(result.Force);
    }

    [Theory]
    [InlineData(new long[] { 7 }, 7)]
    [InlineData(new long[] { 9, 1, 5 }, 5)]
    [InlineData(new long[] { 10, 2, 6, 4 }, 5)]
    public void Median_IsStable(long[] values, long expected) => Assert.Equal(expected, BenchmarkStatistics.Median(values));

    [Fact]
    public void JsonlAnalysis_ReadsTokensCachedNewAndCommands()
    {
        var result = Analyze(
            "{\"type\":\"item.started\",\"item\":{\"id\":\"a\",\"type\":\"command_execution\"}}",
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"a\",\"type\":\"command_execution\",\"status\":\"completed\",\"aggregated_output\":\"abcd\"}}",
            "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":100,\"cached_input_tokens\":60,\"output_tokens\":20}}");
        Assert.Equal(100, result.InputTokens);
        Assert.Equal(60, result.CachedInputTokens);
        Assert.Equal(40, result.NewInputTokens);
        Assert.Equal(20, result.OutputTokens);
        Assert.Equal(1, result.Commands);
        Assert.Equal(4, result.ToolOutputChars);
    }

    [Fact]
    public void JsonlAnalysis_DetectsParallelToolsAsOneBatchAndSequentialToolAsNext()
    {
        var result = Analyze(
            Start("a"), Start("b"), Complete("a"), Complete("b"), Start("c"), Complete("c"));
        Assert.Equal(2, result.ToolBatches);
        Assert.Equal(3, result.Commands);
    }

    [Fact]
    public void MultiCodexAggregation_SumsInvocationTelemetry()
    {
        var aggregate = AggregateMetrics.From([Invocation(10, 6, 2), Invocation(20, 5, 3)]);
        Assert.Equal(30, aggregate.GrossInputTokens);
        Assert.Equal(11, aggregate.CachedInputTokens);
        Assert.Equal(19, aggregate.NewInputTokens);
        Assert.Equal(5, aggregate.OutputTokens);
    }

    [Fact]
    public void FailedAcceptance_IsExcludedFromSuccessfulMedian()
    {
        var runs = new[] { Run(true, 10, 0), Run(false, 1000, 1), Run(true, 20, 0) };
        var aggregate = BenchmarkStatistics.Aggregate(runs);
        Assert.Equal(2, aggregate.SuccessfulRuns);
        Assert.Equal(15, aggregate.Median!.GrossInputTokens);
    }

    [Fact]
    public void FailedCommand_TracksFailedOutputCharacters()
    {
        var result = Analyze(Start("a"), "{\"type\":\"item.completed\",\"item\":{\"id\":\"a\",\"type\":\"command_execution\",\"status\":\"failed\",\"output\":\"failure\"}}");
        Assert.Equal(1, result.FailedCommands);
        Assert.Equal(7, result.FailedToolOutputChars);
    }

    [Fact]
    public void OverheadCalculations_UseModeMedians()
    {
        var modes = new Dictionary<string, ModeAggregate>
        {
            ["direct"] = Mode(10), ["structured-single"] = Mode(15), ["manual-isolated"] = Mode(25), ["factory-split-replay"] = Mode(40), ["factory"] = Mode(60)
        };
        var result = BenchmarkStatistics.Compare(modes);
        Assert.Equal(5, result.StructuringOverhead);
        Assert.Equal(10, result.IsolationOverhead);
        Assert.Equal(15, result.DecompositionChoiceOverhead);
        Assert.Equal(20, result.FactoryOrchestrationOverhead);
        Assert.Equal(6, result.FactoryToDirect);
    }

    [Fact]
    public void ReportGeneration_ContainsMetricsAndDecomposition()
    {
        var run = Run(true, 10, 0);
        var environment = run.Environment;
        var aggregate = BenchmarkStatistics.Aggregate([run]);
        var report = new BenchmarkReport
        {
            Benchmark = "fixture", Environment = environment, Repeats = 1,
            Modes = new Dictionary<string, IReadOnlyList<BenchmarkRunResult>> { ["direct"] = [run] },
            Aggregates = new Dictionary<string, ModeAggregate> { ["direct"] = aggregate },
            Comparisons = BenchmarkStatistics.Compare(new Dictionary<string, ModeAggregate> { ["direct"] = aggregate }),
            ComparabilityWarnings = [], TotalBenchmarkDurationMilliseconds = 1
        };
        var markdown = ReportWriter.Markdown(report);
        Assert.Contains("Factory Benchmark Report", markdown);
        Assert.Contains("Gross input", markdown);
        Assert.Contains("Derived observed overhead", markdown);
    }

    [Fact]
    public void DecompositionParsing_ReadsPlannerMarkdown()
    {
        var path = System.IO.Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "# Task\n\n# Work\n\n# Task\n\nSecond work item");
            var result = BenchmarkRunner.ParseDecomposition(path);
            Assert.Equal(2, result.Count);
            Assert.Equal("WI-001", result[0].Id);
            Assert.Equal("implementation", result[0].Kind);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ProcessExecution_AllowsCommandsWithoutStandardInput()
    {
        var result = await ProcessExecution.RunAsync("dotnet", ["--version"], Environment.CurrentDirectory, TimeSpan.FromSeconds(30));
        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Stdout));
    }

    [Fact]
    public void SandboxCompatiblePathExcludesWindowsAppsDirectories()
    {
        var path = string.Join(';', @"C:\\Program Files\\dotnet", @"C:\\Users\\u\\AppData\\Local\\Microsoft\\WindowsApps", @"C:\\Tools");
        Assert.Equal(string.Join(';', @"C:\\Program Files\\dotnet", @"C:\\Tools"), ProcessExecution.PrepareSandboxCompatiblePath(path));
    }

    [Fact]
    public void AcceptanceSnapshot_CopiesSourcesAndExcludesOperationalDirectories()
    {
        using var fixture = BenchmarkFixture.Create();
        var source = System.IO.Path.Combine(fixture.Path, "source");
        Directory.CreateDirectory(System.IO.Path.Combine(source, "src", "bin"));
        Directory.CreateDirectory(System.IO.Path.Combine(source, ".idd", "factory"));
        File.WriteAllText(System.IO.Path.Combine(source, "src", "Program.cs"), "source");
        File.WriteAllText(System.IO.Path.Combine(source, "src", "bin", "App.dll"), "binary");
        File.WriteAllText(System.IO.Path.Combine(source, ".idd", "factory", "state.json"), "state");
        var snapshot = WorkspaceManager.CreateAcceptanceSnapshot(source);
        try
        {
            Assert.StartsWith(System.IO.Path.GetTempPath(), snapshot, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(System.IO.Path.Combine(snapshot, "src", "Program.cs")));
            Assert.False(Directory.Exists(System.IO.Path.Combine(snapshot, "src", "bin")));
            Assert.False(Directory.Exists(System.IO.Path.Combine(snapshot, ".idd")));
        }
        finally { Directory.Delete(snapshot, recursive: true); }
    }

    private static InvocationMetrics Analyze(params string[] lines)
    {
        var path = System.IO.Path.GetTempFileName();
        try { File.WriteAllLines(path, lines); return CodexJsonlAnalyzer.Analyze(path, TimeSpan.FromSeconds(1), 0, "test"); }
        finally { File.Delete(path); }
    }

    private static string Start(string id) => $"{{\"type\":\"item.started\",\"item\":{{\"id\":\"{id}\",\"type\":\"command_execution\"}}}}";
    private static string Complete(string id) => $"{{\"type\":\"item.completed\",\"item\":{{\"id\":\"{id}\",\"type\":\"command_execution\",\"status\":\"completed\"}}}}";
    private static InvocationMetrics Invocation(long input, long cached, long output) => new(input, cached, input - cached, output, 1, 1, 0, 0, 0, 1, 0, "test", "events.jsonl");

    private static BenchmarkRunResult Run(bool successful, long input, int acceptanceExit) => new()
    {
        Mode = "direct", Iteration = 1, Status = successful ? "SUCCESS" : "FAILED", Successful = successful,
        Invocations = [Invocation(input, 0, 1)], Metrics = AggregateMetrics.From([Invocation(input, 0, 1)]),
        Acceptance = new(acceptanceExit, 1, "out", "err"), AgentDurationMilliseconds = 1, TotalDurationMilliseconds = 2, CodexProcessCount = 1,
        Environment = new("os", "dotnet", "codex", "factory", "plugin", "model", "high", null, "elevated", "git", false, "hash", DateTimeOffset.UnixEpoch, new Dictionary<string, string>())
    };

    private static ModeAggregate Mode(long gross)
    {
        var metrics = new AggregateMetrics(gross, 0, gross, 0, 0, 0, 0, 0, 0);
        return new(1, 1, 1, metrics, metrics, metrics, 1, 1, 1, 1);
    }

    private sealed class BenchmarkFixture(string path) : IDisposable
    {
        public string Path { get; } = path;
        public static BenchmarkFixture Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "factory-benchmark-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(System.IO.Path.Combine(path, "work-items"));
            File.WriteAllText(System.IO.Path.Combine(path, "task.md"), "task");
            File.WriteAllText(System.IO.Path.Combine(path, "work-items", "one.md"), "work");
            File.WriteAllText(System.IO.Path.Combine(path, "benchmark.yaml"), """
name: fixture
model: model
reasoning:
  effort: high
repeat: 1
timeoutMinutes: 1
windowsSandbox: elevated
task: task.md
idealWorkItems:
  - work-items/one.md
acceptance:
  command: test
modes:
  - direct
  - factory
""");
            return new(path);
        }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
