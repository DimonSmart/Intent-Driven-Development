using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Idd.Factory.Benchmark;

public static class BenchmarkApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var options = BenchmarkCliParser.Parse(args);
            var definition = BenchmarkDefinitionLoader.Load(options.BenchmarkDirectory);
            var repositoryRoot = FindRepositoryRoot(options.BenchmarkDirectory);
            var repeat = options.Repeat ?? definition.Repeat;
            var modes = options.Modes ?? definition.Modes;
            var output = options.Output ?? Path.Combine(repositoryRoot, "artifacts", "factory-benchmarks", DateTimeOffset.Now.ToString("yyyy-MM-dd_HHmmss"));
            Directory.CreateDirectory(output);
            var environment = await ReadEnvironmentAsync(repositoryRoot, options.BenchmarkDirectory, definition, options.Model ?? definition.Model, options.WindowsSandbox ?? definition.WindowsSandbox);
            var runner = new BenchmarkRunner(repositoryRoot, options.BenchmarkDirectory, output, definition, options);
            var started = DateTimeOffset.UtcNow;
            var results = new Dictionary<string, IReadOnlyList<BenchmarkRunResult>>(StringComparer.Ordinal);
            foreach (var mode in modes)
            {
                var modeResults = new List<BenchmarkRunResult>();
                for (var iteration = 1; iteration <= repeat; iteration++)
                {
                    Console.WriteLine($"[{mode}] run {iteration}/{repeat}");
                    var result = await runner.RunAsync(mode, iteration, environment);
                    Console.WriteLine($"[{mode}] run {iteration}: {result.Status} ({result.Metrics.GrossInputTokens:N0} gross input tokens)");
                    modeResults.Add(result);
                }
                results.Add(mode, modeResults);
            }
            var aggregates = results.ToDictionary(pair => pair.Key, pair => BenchmarkStatistics.Aggregate(pair.Value), StringComparer.Ordinal);
            var report = new BenchmarkReport
            {
                Benchmark = definition.Name, Environment = environment, Repeats = repeat, Modes = results, Aggregates = aggregates,
                Comparisons = BenchmarkStatistics.Compare(aggregates), ComparabilityWarnings = ComparabilityWarnings(results),
                TotalBenchmarkDurationMilliseconds = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds
            };
            await ReportWriter.WriteAsync(output, report);
            Console.WriteLine($"Report: {Path.Combine(output, "report.md")}");
            return aggregates.Values.Any(value => value.SuccessfulRuns == 0) ? 2 : 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static async Task<EnvironmentRecord> ReadEnvironmentAsync(string repositoryRoot, string benchmarkDirectory, BenchmarkDefinition definition, string model, string windowsSandbox)
    {
        async Task<string> Version(string command, params string[] arguments)
        {
            try { var result = await ProcessExecution.RunAsync(command, arguments, repositoryRoot, TimeSpan.FromSeconds(30)); return (result.Stdout + result.Stderr).Trim(); }
            catch (Exception exception) { return "unavailable: " + exception.Message; }
        }
        var codex = CodexExecutableResolver.Resolve();
        var codexVersionResult = await ProcessExecution.RunAsync(codex.Executable, codex.PrefixArguments.Append("--version"), repositoryRoot, TimeSpan.FromSeconds(30));
        var pluginRoot = Path.Combine(repositoryRoot, "artifacts", "marketplace", "plugins", "codex", "idd-factory");
        var pluginVersion = ReadPluginVersion(pluginRoot);
        var runtime = Path.Combine(pluginRoot, "runtime", "idd-factory.dll");
        var factoryVersion = File.Exists(runtime) ? FileVersionInfo.GetVersionInfo(runtime).ProductVersion ?? "unknown" : "unavailable";
        var git = await Version("git", "rev-parse", "HEAD");
        var gitDirty = !string.IsNullOrWhiteSpace(await Version("git", "status", "--porcelain"));
        var hash = BenchmarkFixtureHash(benchmarkDirectory);
        return new(Environment.OSVersion.ToString(), await Version("dotnet", "--version"), (codexVersionResult.Stdout + codexVersionResult.Stderr).Trim(), factoryVersion,
            pluginVersion, model, definition.Reasoning.Effort, Environment.GetEnvironmentVariable("IDD_FACTORY_MODEL"), OperatingSystem.IsWindows() ? windowsSandbox : null,
            git, gitDirty, hash, DateTimeOffset.UtcNow, ReadSkillVersions(pluginRoot));
    }

    private static string ReadPluginVersion(string pluginRoot)
    {
        var path = Path.Combine(pluginRoot, ".codex-plugin", "plugin.json");
        if (!File.Exists(path)) return "unavailable";
        try { using var document = JsonDocument.Parse(File.ReadAllText(path)); return document.RootElement.TryGetProperty("version", out var version) ? version.GetString() ?? "unknown" : "unknown"; }
        catch (JsonException) { return "invalid"; }
    }

    private static IReadOnlyDictionary<string, string> ReadSkillVersions(string pluginRoot)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(codexHome)) codexHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        Read(Path.Combine(codexHome, "skills"), "user");
        Read(Path.Combine(pluginRoot, "skills"), "factory-plugin");
        return result;

        void Read(string root, string scope)
        {
            if (!Directory.Exists(root)) return;
            foreach (var file in Directory.EnumerateFiles(root, "SKILL.md", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(Path.GetDirectoryName(file)!);
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant()[..12];
                result[$"{scope}:{name}:{Path.GetRelativePath(root, file).Replace('\\', '/')}"] = hash;
            }
        }
    }

    private static string BenchmarkFixtureHash(string benchmarkDirectory)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.EnumerateFiles(benchmarkDirectory, "*", SearchOption.AllDirectories)
                     .Where(file => !Path.GetRelativePath(benchmarkDirectory, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(segment => segment is "bin" or "obj"))
                     .Order(StringComparer.Ordinal))
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(benchmarkDirectory, file).Replace('\\', '/')));
            hash.AppendData(File.ReadAllBytes(file));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static IReadOnlyList<string> ComparabilityWarnings(IReadOnlyDictionary<string, IReadOnlyList<BenchmarkRunResult>> results)
    {
        var environments = results.Values.SelectMany(x => x).Select(x => x.Environment).ToArray();
        if (environments.Length < 2) return [];
        var first = environments[0];
        foreach (var current in environments.Skip(1))
            if (current.CodexVersion != first.CodexVersion || current.Model != first.Model || current.ReasoningEffort != first.ReasoningEffort || current.WindowsSandbox != first.WindowsSandbox ||
                current.FactoryVersion != first.FactoryVersion || current.FactoryPluginVersion != first.FactoryPluginVersion || current.GitRevision != first.GitRevision ||
                current.GitDirty != first.GitDirty || current.BenchmarkDefinitionSha256 != first.BenchmarkDefinitionSha256)
                return ["Benchmark environments are not directly comparable."];
        return [];
    }

    private static string FindRepositoryRoot(string start)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(start)); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Intent-Driven-Development.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate Intent-Driven-Development.slnx from the benchmark directory.");
    }
}
