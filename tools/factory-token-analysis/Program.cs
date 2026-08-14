using System.Globalization;
using System.Text.Json;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return 0;
        }

        try
        {
            if (string.Equals(args[0], "baseline", StringComparison.OrdinalIgnoreCase))
                return await CreateBaselineAsync(args[1..]);

            return await AnalyzeAsync(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> AnalyzeAsync(string[] args)
    {
        var index = string.Equals(args[0], "analyze", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (index >= args.Length) throw new ArgumentException("Run directory or workspace path is required.");

        var runPath = args[index++];
        string? baselinePath = null;
        string? jsonPath = null;
        var failOnRegression = false;

        while (index < args.Length)
        {
            switch (args[index])
            {
                case "--baseline":
                    baselinePath = RequireValue(args, ref index);
                    break;
                case "--json":
                    jsonPath = RequireValue(args, ref index);
                    break;
                case "--fail-on-regression":
                    failOnRegression = true;
                    index++;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {args[index]}");
            }
        }

        var report = AnalyzeRun(ResolveRunDirectory(runPath));
        PrintReport(report);

        if (jsonPath is not null)
        {
            await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(report, JsonOptions));
            Console.WriteLine($"\nJSON report written to {Path.GetFullPath(jsonPath)}");
        }

        if (baselinePath is null) return 0;

        var baseline = JsonSerializer.Deserialize<BaselineDocument>(
            await File.ReadAllTextAsync(baselinePath), JsonOptions)
            ?? throw new InvalidDataException($"Cannot parse baseline: {baselinePath}");

        if (baseline.SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported baseline schema version: {baseline.SchemaVersion}");

        var comparison = Compare(report, baseline);
        PrintComparison(comparison, baseline);

        return failOnRegression && comparison.Critical.Count > 0 ? 2 : 0;
    }

    private static async Task<int> CreateBaselineAsync(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException("Usage: baseline <output.json> <run-or-workspace> [run-or-workspace ...]");

        var output = args[0];
        var reports = args[1..].Select(path => AnalyzeRun(ResolveRunDirectory(path))).ToArray();
        var baseline = new BaselineDocument(
            1,
            DateTimeOffset.UtcNow,
            reports.Length,
            reports.Select(x => x.RunDirectory).ToArray(),
            reports.SelectMany(x => x.Models).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            reports.SelectMany(x => x.SkillVersions).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            MedianMetrics(reports.Select(x => x.Metrics).ToArray()));

        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(baseline, JsonOptions));
        Console.WriteLine($"Baseline written to {Path.GetFullPath(output)} from {reports.Length} run(s).");
        Console.WriteLine("Use 3-5 comparable known-good runs when possible; each metric is stored as the median.");
        return 0;
    }

    private static RunReport AnalyzeRun(string runDirectory)
    {
        var attemptsDirectory = Path.Combine(runDirectory, "attempts");
        var eventsPath = Path.Combine(runDirectory, "events.jsonl");
        if (!Directory.Exists(attemptsDirectory)) throw new DirectoryNotFoundException($"Missing attempts directory: {attemptsDirectory}");
        if (!File.Exists(eventsPath)) throw new FileNotFoundException("Missing events.jsonl.", eventsPath);

        var eventTimes = ReadEventTimes(eventsPath);
        var attempts = Directory.EnumerateDirectories(attemptsDirectory)
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .Select(path => AnalyzeAttempt(path, eventTimes))
            .ToArray();

        var metrics = new RunMetrics(
            attempts.Length,
            attempts.Sum(x => x.ToolBatches),
            attempts.Sum(x => x.Commands),
            attempts.Sum(x => x.ToolOutputChars),
            attempts.Sum(x => x.FailedCommands),
            attempts.Sum(x => x.FailedToolOutputChars),
            attempts.Sum(x => x.InputTokens),
            attempts.Sum(x => x.CachedInputTokens),
            attempts.Sum(x => x.NewInputTokens),
            attempts.Sum(x => x.OutputTokens));

        var anomalies = DetectRunAnomalies(attempts, metrics);
        return new RunReport(
            Path.GetFullPath(runDirectory),
            attempts,
            metrics,
            attempts.Select(x => x.Model).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            attempts.Select(x => x.SkillVersion).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            anomalies);
    }

    private static AttemptReport AnalyzeAttempt(string attemptDirectory, IReadOnlyDictionary<string, EventTimes> eventTimes)
    {
        var attemptId = Path.GetFileName(attemptDirectory);
        var telemetry = ReadJsonObject(Path.Combine(attemptDirectory, "attempt-telemetry.json"));
        var invocation = ReadJsonObject(Path.Combine(attemptDirectory, "invocation.json"));

        var role = String(telemetry, "role");
        var skill = String(telemetry, "skillName");
        var model = String(telemetry, "requestedModel");
        var skillVersion = String(telemetry, "skillSourceVersion");
        var inputChars = Int64(telemetry, "inputChars");
        var userSkills = Int64(telemetry, "inheritedUserSkillCount");
        var projectSkills = Int64(telemetry, "projectLocalSkillCount");
        var workItem = String(invocation, "workItemId");
        var invocationInput = String(invocation, "input");
        var launchReason = LaunchReason(role, workItem, invocationInput);

        long inputTokens = 0;
        long cachedInputTokens = 0;
        long outputTokens = 0;
        var commands = 0;
        var toolBatches = 0;
        var activeTools = 0;
        long toolOutputChars = 0;
        var failedCommands = 0;
        long failedToolOutputChars = 0;
        var readsOwnSkill = false;
        var broadWorkspaceInventory = false;
        var gitFailure = false;

        var stdoutPath = Path.Combine(attemptDirectory, "stdout.log");
        if (File.Exists(stdoutPath))
        {
            foreach (var line in File.ReadLines(stdoutPath))
            {
                if (!TryParse(line, out var document)) continue;
                using (document)
                {
                    var element = document.RootElement;
                    var type = String(element, "type");

                    if (type == "turn.completed" && element.TryGetProperty("usage", out var usage))
                    {
                        inputTokens = Int64(usage, "input_tokens");
                        cachedInputTokens = Int64(usage, "cached_input_tokens");
                        outputTokens = Int64(usage, "output_tokens");
                        continue;
                    }

                    if (!element.TryGetProperty("item", out var item)) continue;
                    var itemType = String(item, "type");
                    var isToolItem = itemType is "command_execution" or "file_change";

                    if (type == "item.started" && isToolItem)
                    {
                        if (activeTools == 0) toolBatches++;
                        activeTools++;
                        continue;
                    }

                    if (type != "item.completed" || !isToolItem) continue;
                    activeTools = Math.Max(0, activeTools - 1);
                    if (itemType != "command_execution") continue;

                    commands++;
                    var output = String(item, "aggregated_output");
                    var command = String(item, "command");
                    var exitCode = Int64(item, "exit_code");
                    toolOutputChars += output.Length;

                    if (exitCode != 0)
                    {
                        failedCommands++;
                        failedToolOutputChars += output.Length;
                    }

                    readsOwnSkill |= command.Contains("SKILL.md", StringComparison.OrdinalIgnoreCase)
                        && (string.IsNullOrWhiteSpace(skill) || command.Contains(skill, StringComparison.OrdinalIgnoreCase));
                    broadWorkspaceInventory |= command.Contains("Get-ChildItem -Recurse", StringComparison.OrdinalIgnoreCase)
                        || command.Contains("Get-ChildItem -Force; Get-ChildItem -Recurse", StringComparison.OrdinalIgnoreCase)
                        || command.Contains("rg --files", StringComparison.OrdinalIgnoreCase);
                    gitFailure |= exitCode != 0 && output.Contains("not a git repository", StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        eventTimes.TryGetValue(attemptId, out var times);
        var seconds = times?.Started is { } started && times.Completed is { } completed
            ? Math.Round((completed - started).TotalSeconds, 1)
            : (double?)null;

        var newInputTokens = Math.Max(0, inputTokens - cachedInputTokens);
        var anomalies = new List<string>();
        if (readsOwnSkill) anomalies.Add("worker re-read its active SKILL.md");
        if (broadWorkspaceInventory) anomalies.Add("broad workspace inventory detected");
        if (gitFailure) anomalies.Add("Git command failed because the workspace is not a repository");
        if (failedToolOutputChars >= 10_000) anomalies.Add($"failed commands produced {failedToolOutputChars:N0} chars");
        if (toolBatches >= 5) anomalies.Add($"{toolBatches} sequential tool batches");

        return new AttemptReport(
            attemptId,
            role,
            skill,
            model,
            skillVersion,
            workItem,
            launchReason,
            inputChars,
            userSkills,
            projectSkills,
            toolBatches,
            commands,
            toolOutputChars,
            failedCommands,
            failedToolOutputChars,
            inputTokens,
            cachedInputTokens,
            newInputTokens,
            outputTokens,
            inputTokens + outputTokens,
            seconds,
            anomalies.ToArray());
    }

    private static Dictionary<string, EventTimes> ReadEventTimes(string path)
    {
        var result = new Dictionary<string, EventTimes>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path))
        {
            if (!TryParse(line, out var document)) continue;
            using (document)
            {
                var element = document.RootElement;
                var type = String(element, "type");
                if (type is not ("agent-dispatching" or "agent-completed")) continue;
                if (!element.TryGetProperty("data", out var data)) continue;

                var attemptId = String(data, "attemptId");
                if (string.IsNullOrWhiteSpace(attemptId)) continue;
                if (!DateTimeOffset.TryParse(String(element, "timestamp"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
                    continue;

                result.TryGetValue(attemptId, out var existing);
                result[attemptId] = type == "agent-dispatching"
                    ? new EventTimes(timestamp, existing?.Completed)
                    : new EventTimes(existing?.Started, timestamp);
            }
        }

        return result;
    }

    private static string ResolveRunDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException(fullPath);

        if (Directory.Exists(Path.Combine(fullPath, "attempts")) && File.Exists(Path.Combine(fullPath, "events.jsonl")))
            return fullPath;

        var resultsDirectory = Directory.Exists(Path.Combine(fullPath, ".idd", "factory", "results"))
            ? Path.Combine(fullPath, ".idd", "factory", "results")
            : string.Equals(Path.GetFileName(fullPath), "results", StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : null;

        if (resultsDirectory is null)
            throw new DirectoryNotFoundException($"Cannot find a completed Factory result under {fullPath}.");

        return Directory.EnumerateDirectories(resultsDirectory)
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .FirstOrDefault()
            ?? throw new DirectoryNotFoundException($"No completed Factory runs found under {resultsDirectory}.");
    }

    private static string LaunchReason(string role, string workItem, string input)
    {
        if (role == "task-decomposer") return "decomposition";
        if (role == "final-reviewer") return "final review";
        if (role == "checkpoint-reviewer") return "checkpoint review";
        if (role == "factory-replanner") return "replan";
        if (role == "implementer" && input.StartsWith("Mode:\nverification-fix", StringComparison.Ordinal))
            return string.IsNullOrWhiteSpace(workItem) ? "verification fix" : $"verification fix {workItem}";
        if (role == "implementer")
            return string.IsNullOrWhiteSpace(workItem) ? "implementation" : $"work item {workItem}";
        return role;
    }

    private static string[] DetectRunAnomalies(AttemptReport[] attempts, RunMetrics metrics)
    {
        var anomalies = new List<string>();
        if (metrics.FailedToolOutputChars > 0)
            anomalies.Add($"Failed commands produced {metrics.FailedToolOutputChars:N0} chars of context pollution.");

        var finalReviewer = attempts.Where(x => x.Role == "final-reviewer").Sum(x => x.InputTokens);
        if (metrics.InputTokens > 0 && finalReviewer > metrics.InputTokens * 0.30)
            anomalies.Add($"Final reviewer consumed {finalReviewer * 100.0 / metrics.InputTokens:F1}% of gross input.");

        return anomalies.ToArray();
    }

    private static void PrintReport(RunReport report)
    {
        Console.WriteLine($"Factory token analysis: {report.RunDirectory}");
        Console.WriteLine();
        Console.WriteLine("Attempt  Role             Reason                    InChars Batches Cmds ToolChars FailChars    Input   Cached      New Output Seconds");
        Console.WriteLine("-------  ---------------  ------------------------ ------- ------- ---- --------- --------- -------- -------- -------- ------ -------");

        foreach (var item in report.Attempts)
        {
            Console.WriteLine(
                $"{item.AttemptId,-7}  {Trim(item.Role, 15),-15}  {Trim(item.LaunchReason, 24),-24} " +
                $"{item.InputChars,7} {item.ToolBatches,7} {item.Commands,4} {item.ToolOutputChars,9} {item.FailedToolOutputChars,9} " +
                $"{item.InputTokens,8} {item.CachedInputTokens,8} {item.NewInputTokens,8} {item.OutputTokens,6} {FormatSeconds(item.Seconds),7}");
        }

        var metrics = report.Metrics;
        var cacheRatio = metrics.InputTokens == 0 ? 0 : metrics.CachedInputTokens * 100.0 / metrics.InputTokens;
        Console.WriteLine();
        Console.WriteLine($"Semantic attempts : {metrics.SemanticAttempts}");
        Console.WriteLine($"Tool batches      : {metrics.ToolBatches}");
        Console.WriteLine($"Commands          : {metrics.Commands}");
        Console.WriteLine($"Gross input       : {metrics.InputTokens:N0}");
        Console.WriteLine($"Cached input      : {metrics.CachedInputTokens:N0} ({cacheRatio:F1}%)");
        Console.WriteLine($"New input         : {metrics.NewInputTokens:N0}");
        Console.WriteLine($"Output            : {metrics.OutputTokens:N0}");
        Console.WriteLine($"Tool output chars : {metrics.ToolOutputChars:N0}");
        Console.WriteLine($"Failed tool chars : {metrics.FailedToolOutputChars:N0}");

        Console.WriteLine();
        Console.WriteLine("By role:");
        foreach (var group in report.Attempts.GroupBy(x => x.Role).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {group.Key,-18} attempts={group.Count(),2} input={group.Sum(x => x.InputTokens),8:N0} new={group.Sum(x => x.NewInputTokens),7:N0} batches={group.Sum(x => x.ToolBatches),2}");
        }

        var attemptAnomalies = report.Attempts.SelectMany(x => x.Anomalies.Select(a => $"{x.AttemptId}: {a}")).ToArray();
        if (report.Anomalies.Length > 0 || attemptAnomalies.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Anomalies:");
            foreach (var anomaly in report.Anomalies) Console.WriteLine($"  WARN {anomaly}");
            foreach (var anomaly in attemptAnomalies) Console.WriteLine($"  WARN {anomaly}");
        }
    }

    private static ComparisonReport Compare(RunReport current, BaselineDocument baseline)
    {
        var warnings = new List<string>();
        var critical = new List<string>();
        var b = baseline.Metrics;
        var c = current.Metrics;

        WarnRatio("gross input", c.InputTokens, b.InputTokens, 1.25, 1.50, warnings, critical);
        WarnRatio("new input", c.NewInputTokens, b.NewInputTokens, 1.25, 1.50, warnings, critical);
        WarnRatio("tool output chars", c.ToolOutputChars, b.ToolOutputChars, 1.50, 2.00, warnings, critical);

        if (c.SemanticAttempts > b.SemanticAttempts + 2)
            critical.Add($"semantic attempts: {c.SemanticAttempts} vs baseline {b.SemanticAttempts}");
        else if (c.SemanticAttempts > b.SemanticAttempts + 1)
            warnings.Add($"semantic attempts: {c.SemanticAttempts} vs baseline {b.SemanticAttempts}");

        if (c.ToolBatches > b.ToolBatches + 5)
            critical.Add($"tool batches: {c.ToolBatches} vs baseline {b.ToolBatches}");
        else if (c.ToolBatches > b.ToolBatches + 2)
            warnings.Add($"tool batches: {c.ToolBatches} vs baseline {b.ToolBatches}");

        if (c.FailedToolOutputChars >= Math.Max(10_000, b.FailedToolOutputChars + 10_000))
            critical.Add($"failed tool output: {c.FailedToolOutputChars:N0} chars vs baseline {b.FailedToolOutputChars:N0}");

        var comparability = new List<string>();
        if (!current.Models.SequenceEqual(baseline.Models, StringComparer.Ordinal))
            comparability.Add($"requested model set differs: current=[{string.Join(", ", current.Models)}], baseline=[{string.Join(", ", baseline.Models)}]");
        if (!current.SkillVersions.SequenceEqual(baseline.SkillVersions, StringComparer.Ordinal))
            comparability.Add($"Factory skill version set differs: current=[{string.Join(", ", current.SkillVersions)}], baseline=[{string.Join(", ", baseline.SkillVersions)}]");

        return new ComparisonReport(warnings.ToArray(), critical.ToArray(), comparability.ToArray());
    }

    private static void WarnRatio(
        string name,
        long current,
        long baseline,
        double warningRatio,
        double criticalRatio,
        List<string> warnings,
        List<string> critical)
    {
        if (baseline <= 0) return;
        var ratio = current / (double)baseline;
        if (ratio > criticalRatio)
            critical.Add($"{name}: {current:N0} is {ratio:F2}x baseline {baseline:N0}");
        else if (ratio > warningRatio)
            warnings.Add($"{name}: {current:N0} is {ratio:F2}x baseline {baseline:N0}");
    }

    private static void PrintComparison(ComparisonReport comparison, BaselineDocument baseline)
    {
        Console.WriteLine();
        Console.WriteLine($"Baseline comparison ({baseline.SampleCount} sample(s), created {baseline.CreatedAt:O}):");

        foreach (var item in comparison.ComparabilityWarnings)
            Console.WriteLine($"  NOTE {item}");

        if (comparison.Warnings.Count == 0 && comparison.Critical.Count == 0)
        {
            Console.WriteLine("  OK No token-efficiency regression detected.");
            return;
        }

        foreach (var item in comparison.Warnings) Console.WriteLine($"  WARN {item}");
        foreach (var item in comparison.Critical) Console.WriteLine($"  CRITICAL {item}");
    }

    private static RunMetrics MedianMetrics(RunMetrics[] values) => new(
        Median(values.Select(x => x.SemanticAttempts)),
        Median(values.Select(x => x.ToolBatches)),
        Median(values.Select(x => x.Commands)),
        Median(values.Select(x => x.ToolOutputChars)),
        Median(values.Select(x => x.FailedCommands)),
        Median(values.Select(x => x.FailedToolOutputChars)),
        Median(values.Select(x => x.InputTokens)),
        Median(values.Select(x => x.CachedInputTokens)),
        Median(values.Select(x => x.NewInputTokens)),
        Median(values.Select(x => x.OutputTokens)));

    private static long Median(IEnumerable<long> values)
    {
        var ordered = values.OrderBy(x => x).ToArray();
        if (ordered.Length == 0) return 0;
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 1
            ? ordered[middle]
            : (long)Math.Round((ordered[middle - 1] + ordered[middle]) / 2.0, MidpointRounding.AwayFromZero);
    }

    private static JsonElement ReadJsonObject(string path)
    {
        if (!File.Exists(path)) return default;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    private static bool TryParse(string line, out JsonDocument document)
    {
        try
        {
            document = JsonDocument.Parse(line);
            return true;
        }
        catch (JsonException)
        {
            document = null!;
            return false;
        }
    }

    private static string String(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value)) return "";
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Null => "",
            _ => value.ToString()
        };
    }

    private static long Int64(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        return long.TryParse(value.ToString(), CultureInfo.InvariantCulture, out number) ? number : 0;
    }

    private static string RequireValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length) throw new ArgumentException($"Missing value for {args[index]}.");
        var value = args[index + 1];
        index += 2;
        return value;
    }

    private static string Trim(string value, int length) => value.Length <= length ? value : value[..(length - 1)] + "…";

    private static string FormatSeconds(double? value) => value is null ? "-" : value.Value.ToString("0.0", CultureInfo.InvariantCulture);

    private static void PrintUsage()
    {
        Console.WriteLine("""
Factory token analysis

Analyze the latest completed Factory run in a workspace:
  dotnet run --project tools/factory-token-analysis -- analyze <workspace-or-result-dir>

Compare with a known-good baseline:
  dotnet run --project tools/factory-token-analysis -- analyze <workspace-or-result-dir> --baseline <baseline.json>

Fail with exit code 2 on a critical regression:
  dotnet run --project tools/factory-token-analysis -- analyze <workspace-or-result-dir> --baseline <baseline.json> --fail-on-regression

Create a median baseline from 3-5 comparable known-good runs:
  dotnet run --project tools/factory-token-analysis -- baseline <baseline.json> <run1> <run2> <run3>

Optional machine-readable report:
  --json <report.json>
""");
    }
}

internal sealed record EventTimes(DateTimeOffset? Started, DateTimeOffset? Completed);

internal sealed record AttemptReport(
    string AttemptId,
    string Role,
    string Skill,
    string Model,
    string SkillVersion,
    string WorkItem,
    string LaunchReason,
    long InputChars,
    long InheritedUserSkills,
    long ProjectLocalSkills,
    long ToolBatches,
    long Commands,
    long ToolOutputChars,
    long FailedCommands,
    long FailedToolOutputChars,
    long InputTokens,
    long CachedInputTokens,
    long NewInputTokens,
    long OutputTokens,
    long TotalTokens,
    double? Seconds,
    string[] Anomalies);

internal sealed record RunMetrics(
    long SemanticAttempts,
    long ToolBatches,
    long Commands,
    long ToolOutputChars,
    long FailedCommands,
    long FailedToolOutputChars,
    long InputTokens,
    long CachedInputTokens,
    long NewInputTokens,
    long OutputTokens);

internal sealed record RunReport(
    string RunDirectory,
    AttemptReport[] Attempts,
    RunMetrics Metrics,
    string[] Models,
    string[] SkillVersions,
    string[] Anomalies);

internal sealed record BaselineDocument(
    int SchemaVersion,
    DateTimeOffset CreatedAt,
    int SampleCount,
    string[] SourceRuns,
    string[] Models,
    string[] SkillVersions,
    RunMetrics Metrics);

internal sealed record ComparisonReport(
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Critical,
    IReadOnlyList<string> ComparabilityWarnings);
