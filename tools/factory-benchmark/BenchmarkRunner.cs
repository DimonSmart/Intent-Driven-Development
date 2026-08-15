using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Idd.Factory.Benchmark;

public sealed class BenchmarkRunner(string repositoryRoot, string benchmarkDirectory, string outputDirectory, BenchmarkDefinition definition, BenchmarkOptions options)
{
    private readonly CodexCommand codex = CodexExecutableResolver.Resolve();
    private readonly string task = File.ReadAllText(Path.Combine(benchmarkDirectory, definition.Task));
    private readonly TimeSpan timeout = TimeSpan.FromMinutes(options.TimeoutMinutes ?? definition.TimeoutMinutes);
    private readonly string windowsSandbox = options.WindowsSandbox ?? definition.WindowsSandbox;
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<BenchmarkRunResult> RunAsync(string mode, int iteration, EnvironmentRecord environment)
    {
        var runDirectory = Path.Combine(outputDirectory, mode, $"run-{iteration:00}");
        var resultPath = Path.Combine(runDirectory, "result.json");
        if (!options.Force && File.Exists(resultPath))
        {
            var existing = JsonSerializer.Deserialize<BenchmarkRunResult>(File.ReadAllText(resultPath), json);
            if (existing?.Successful == true && Comparable(existing.Environment, environment)) return existing;
        }
        if (Directory.Exists(runDirectory)) Directory.Delete(runDirectory, recursive: true);
        Directory.CreateDirectory(runDirectory);
        var stopwatch = Stopwatch.StartNew();
        string? workspace = null;
        try
        {
            workspace = WorkspaceManager.Create(benchmarkDirectory, runDirectory);
            var telemetry = Path.Combine(runDirectory, "telemetry");
            Directory.CreateDirectory(telemetry);
            var invocations = new List<InvocationMetrics>();
            FactoryDecompositionRecord? decomposition = null;
            switch (mode)
            {
                case BenchmarkModes.Direct:
                    invocations.Add(await RunCodexAsync(workspace, telemetry, "direct", DirectPrompt()));
                    break;
                case BenchmarkModes.StructuredSingle:
                    invocations.Add(await RunCodexAsync(workspace, telemetry, "structured-single", StructuredPrompt()));
                    break;
                case BenchmarkModes.ManualIsolated:
                    foreach (var item in ReadIdealWorkItems()) invocations.Add(await RunCodexAsync(workspace, telemetry, item.Name, WorkerPrompt(item.Content)));
                    break;
                case BenchmarkModes.FactorySplitReplay:
                    (decomposition, var splitMetrics, var contracts) = await DecomposeAsync(runDirectory, telemetry);
                    invocations.Add(splitMetrics);
                    foreach (var item in contracts.Where(x => x.Kind == "subtask").OrderBy(x => x.Sequence))
                        invocations.Add(await RunCodexAsync(workspace, telemetry, item.Id, WorkerPrompt(item.ContractMarkdown)));
                    break;
                case BenchmarkModes.Factory:
                    (decomposition, var factoryMetrics) = await RunFactoryAsync(workspace, runDirectory, telemetry);
                    invocations.AddRange(factoryMetrics);
                    break;
                default: throw new InvalidOperationException($"Unsupported mode '{mode}'.");
            }

            var acceptanceWorkspace = WorkspaceManager.CreateAcceptanceSnapshot(workspace);
            var acceptance = await RunAcceptanceAsync(acceptanceWorkspace, runDirectory);
            stopwatch.Stop();
            var successful = acceptance.ExitCode == 0 && invocations.All(x => x.ExitCode == 0);
            var acceptanceRetained = options.KeepWorkspaces || !await TryDeleteWorkspaceAsync(acceptanceWorkspace);
            var productRetained = options.KeepWorkspaces || !successful || !await TryDeleteWorkspaceAsync(workspace);
            var workspaceRetained = acceptanceRetained || productRetained;
            var result = new BenchmarkRunResult
            {
                Mode = mode, Iteration = iteration, Status = successful ? "SUCCESS" : "FAILED", Successful = successful,
                Failure = successful ? null : FailureSummary(invocations, acceptance), Invocations = invocations,
                Metrics = AggregateMetrics.From(invocations), Acceptance = acceptance,
                AgentDurationMilliseconds = invocations.Sum(x => x.DurationMilliseconds), TotalDurationMilliseconds = (long)stopwatch.Elapsed.TotalMilliseconds,
                CodexProcessCount = invocations.Count(x => x.Role != "factory-runtime"), WorkspaceRetained = workspaceRetained,
                Environment = environment, FactoryDecomposition = decomposition
            };
            await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(result, json));
            return result;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            var acceptance = new AcceptanceResult(-1, 0, Path.Combine(runDirectory, "acceptance.stdout.log"), Path.Combine(runDirectory, "acceptance.stderr.log"));
            await File.WriteAllTextAsync(acceptance.StderrPath, exception.ToString());
            var result = new BenchmarkRunResult
            {
                Mode = mode, Iteration = iteration, Status = "FAILED", Successful = false, Failure = exception.Message,
                Invocations = [], Metrics = AggregateMetrics.From([]), Acceptance = acceptance, AgentDurationMilliseconds = 0,
                TotalDurationMilliseconds = (long)stopwatch.Elapsed.TotalMilliseconds, CodexProcessCount = 0, Environment = environment
            };
            await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(result, json));
            return result;
        }
    }

    private async Task<InvocationMetrics> RunCodexAsync(string workspace, string telemetryDirectory, string role, string prompt, string? lastMessagePath = null, string sandbox = "workspace-write")
    {
        var safeRole = Sanitize(role);
        var eventsPath = UniquePath(telemetryDirectory, safeRole, ".jsonl");
        lastMessagePath ??= Path.ChangeExtension(eventsPath, ".last-message.txt");
        var arguments = new List<string>(codex.PrefixArguments);
        arguments.AddRange(new[]
        {
            "exec", "--json", "--ephemeral", "--ignore-user-config", "--model", options.Model ?? definition.Model,
            "-c", $"model_reasoning_effort={definition.Reasoning.Effort}", "--sandbox", sandbox,
            "-c", "approval_policy=\"never\"", "-c", "mcp_servers={}"
        });
        if (OperatingSystem.IsWindows()) arguments.AddRange(["-c", $"windows.sandbox=\"{windowsSandbox}\""]);
        arguments.AddRange(["--skip-git-repo-check", "-C", workspace, "--output-last-message", lastMessagePath, "-"]);
        var result = await ProcessExecution.RunAsync(codex.Executable, arguments, workspace, timeout, prompt);
        await File.WriteAllTextAsync(eventsPath, result.Stdout);
        await File.WriteAllTextAsync(Path.ChangeExtension(eventsPath, ".stderr.log"), result.Stderr);
        return CodexJsonlAnalyzer.Analyze(eventsPath, result.Duration, result.ExitCode, role);
    }

    private async Task<(FactoryDecompositionRecord, InvocationMetrics, IReadOnlyList<GeneratedWorkItem>)> DecomposeAsync(string runDirectory, string telemetry)
    {
        var decompositionWorkspace = Path.Combine(runDirectory, "decomposition-workspace");
        Directory.CreateDirectory(decompositionWorkspace);
        var skillDirectory = Path.Combine(decompositionWorkspace, ".agents", "skills", "idd-factory-decompose-task");
        Directory.CreateDirectory(skillDirectory);
        File.Copy(Path.Combine(repositoryRoot, "src", "canonical", "skills", "idd-factory-decompose-task.md"), Path.Combine(skillDirectory, "SKILL.md"), overwrite: true);
        var lastMessage = Path.Combine(telemetry, "factory-decomposer.result.json");
        var prompt = $"""
Use $idd-factory-decompose-task to decompose this benchmark task. This is an independent decomposition for replay, not a Factory runtime run.

Run id: benchmark-{Guid.NewGuid():N}
Attempt id: decomposition-1
Role: task-decomposer

Original request:
{task}

Return only the version 1 worker envelope required by the skill. Use outcome ready and payload.workItems when decomposition succeeds.
""";
        var metrics = await RunCodexAsync(decompositionWorkspace, telemetry, "factory-decomposer", prompt, lastMessage, sandbox: "read-only");
        if (metrics.ExitCode != 0) throw new InvalidOperationException("Factory decomposer Codex invocation failed.");
        var workItems = ParseDecomposition(lastMessage);
        var capture = Path.Combine(runDirectory, "factory-decomposition");
        Directory.CreateDirectory(capture);
        foreach (var item in workItems) await File.WriteAllTextAsync(Path.Combine(capture, $"{Sanitize(item.Id)}.md"), item.ContractMarkdown);
        var record = new FactoryDecompositionRecord(false, metrics.InputTokens, metrics.CachedInputTokens, metrics.OutputTokens,
            workItems.Select(x => new FactoryWorkItemRecord(x.Id, x.Kind, Title(x.ContractMarkdown), $"factory-decomposition/{Sanitize(x.Id)}.md")).ToArray());
        if (!options.KeepWorkspaces) Directory.Delete(decompositionWorkspace, recursive: true);
        return (record, metrics, workItems);
    }

    private async Task<(FactoryDecompositionRecord, IReadOnlyList<InvocationMetrics>)> RunFactoryAsync(string workspace, string runDirectory, string telemetry)
    {
        var pluginRoot = Path.Combine(repositoryRoot, "artifacts", "marketplace", "plugins", "codex", "idd-factory");
        var runtime = Path.Combine(pluginRoot, "runtime", "idd-factory.dll");
        if (!File.Exists(runtime)) throw new FileNotFoundException("Generated production Factory runtime is missing. Run scripts/Check.ps1 first.", runtime);
        var requestPath = Path.Combine(runDirectory, "task.md");
        await File.WriteAllTextAsync(requestPath, task);
        var environment = new Dictionary<string, string?>
        {
            ["IDD_FACTORY_MODEL"] = options.Model ?? definition.Model,
            ["IDD_FACTORY_REASONING_EFFORT"] = definition.Reasoning.Effort,
            ["IDD_FACTORY_WINDOWS_SANDBOX"] = OperatingSystem.IsWindows() ? windowsSandbox : null
        };
        var result = await ProcessExecution.RunAsync("dotnet", [runtime, "run", "--workspace", workspace, "--request-file", requestPath, "--plugin-root", pluginRoot], repositoryRoot, timeout, environment: environment);
        await File.WriteAllTextAsync(Path.Combine(telemetry, "factory-runtime.stdout.log"), result.Stdout);
        await File.WriteAllTextAsync(Path.Combine(telemetry, "factory-runtime.stderr.log"), result.Stderr);
        var source = FindFactoryRunDirectory(workspace);
        var capture = Path.Combine(runDirectory, "factory-decomposition");
        Directory.CreateDirectory(capture);
        var workItems = new List<FactoryWorkItemRecord>();
        if (source is not null)
        {
            var contracts = Directory.Exists(Path.Combine(source, "decomposition", "contracts"))
                ? Path.Combine(source, "decomposition", "contracts")
                : Path.Combine(source, "work-items");
            var kinds = ReadFactoryKinds(source);
            if (Directory.Exists(contracts))
                foreach (var file in Directory.EnumerateFiles(contracts, "*.md").Order(StringComparer.Ordinal))
                {
                    var target = Path.Combine(capture, Path.GetFileName(file)); File.Copy(file, target, overwrite: true);
                    var content = File.ReadAllText(file); var id = Path.GetFileNameWithoutExtension(file);
                    var typed = kinds.Values.FirstOrDefault(x => Path.GetFileName(x.ContractPath).Equals(Path.GetFileName(file), StringComparison.OrdinalIgnoreCase));
                    workItems.Add(new(typed?.Id ?? id, typed?.Kind ?? "unknown", Title(content), Path.GetRelativePath(runDirectory, target).Replace('\\', '/')));
                }
        }
        var metrics = new List<InvocationMetrics>();
        var durations = source is null ? new Dictionary<string, TimeSpan>(StringComparer.Ordinal) : ReadFactoryAttemptDurations(source);
        var attemptsDirectory = source is null ? null : Path.Combine(source, "attempts");
        if (attemptsDirectory is not null && Directory.Exists(attemptsDirectory))
            foreach (var stdout in Directory.EnumerateFiles(attemptsDirectory, "stdout.log", SearchOption.AllDirectories))
            {
                var destination = UniquePath(telemetry, "factory-worker", ".jsonl"); File.Copy(stdout, destination, overwrite: true);
                var stderr = Path.Combine(Path.GetDirectoryName(stdout)!, "stderr.log");
                if (File.Exists(stderr)) File.Copy(stderr, Path.ChangeExtension(destination, ".stderr.log"), overwrite: true);
                var attemptId = Path.GetFileName(Path.GetDirectoryName(stdout)!);
                metrics.Add(CodexJsonlAnalyzer.Analyze(destination, durations.GetValueOrDefault(attemptId), 0, ReadFactoryRole(Path.GetDirectoryName(stdout)!)));
            }
        if (metrics.Count == 0)
            metrics.Add(new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, result.ExitCode, "factory-runtime", Path.Combine(telemetry, "factory-runtime.stdout.log"), result.ExitCode == 0 ? null : (result.Stderr + result.Stdout).Trim()));
        else
        {
            var last = metrics[^1]; metrics[^1] = last with { ExitCode = result.ExitCode };
        }
        var decompositionMetrics = metrics.Where(x => x.Role == "task-decomposer").ToArray();
        return (new FactoryDecompositionRecord(true, decompositionMetrics.Sum(x => x.InputTokens), decompositionMetrics.Sum(x => x.CachedInputTokens), decompositionMetrics.Sum(x => x.OutputTokens), workItems), metrics);
    }

    private async Task<AcceptanceResult> RunAcceptanceAsync(string workspace, string runDirectory)
    {
        var args = definition.Acceptance.Arguments.Select(argument =>
        {
            var fixturePath = Path.Combine(benchmarkDirectory, argument);
            return File.Exists(fixturePath) ? Path.GetFullPath(fixturePath) : argument;
        }).ToArray();
        var result = await ProcessExecution.RunAsync(definition.Acceptance.Command, args, workspace, timeout);
        var stdout = Path.Combine(runDirectory, "acceptance.stdout.log"); var stderr = Path.Combine(runDirectory, "acceptance.stderr.log");
        await File.WriteAllTextAsync(stdout, result.Stdout); await File.WriteAllTextAsync(stderr, result.Stderr);
        return new(result.ExitCode, (long)result.Duration.TotalMilliseconds, stdout, stderr);
    }

    private string DirectPrompt() => $"""
Complete the following task in this workspace.

Do not use IDD Factory, Factory skills, decomposition agents, reviewers, or the Factory runtime. Work directly in this single Codex session.

{task}
""";

    private string StructuredPrompt() => $"""
Complete the following task in this workspace. Do not use IDD Factory, Factory skills, child agents, or fresh contexts.

Original task:
{task}

The work is structured into these ordered work items:

{string.Join("\n\n", ReadIdealWorkItems().Select((item, index) => $"{index + 1}. {item.Content}"))}

Complete all work items in order in this same session.
""";

    private static string WorkerPrompt(string contract) => $"""
Complete only the following work-item contract in the current workspace. You have no prior worker conversation. Inspect the current workspace as needed. Do not use IDD Factory, Factory skills, child agents, reviewers, or the Factory runtime.

{contract}
""";

    private IReadOnlyList<(string Name, string Content)> ReadIdealWorkItems() => definition.IdealWorkItems.Select(path => (Path.GetFileNameWithoutExtension(path), File.ReadAllText(Path.Combine(benchmarkDirectory, path)))).ToArray();

    public static IReadOnlyList<GeneratedWorkItem> ParseDecomposition(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.TryGetProperty("payload", out var payload)) root = payload;
        if (!root.TryGetProperty("workItems", out var items) || items.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Decomposer result does not contain payload.workItems.");
        return items.EnumerateArray().Select((item, index) => new GeneratedWorkItem(
            item.GetProperty("id").GetString() ?? $"WI-{index + 1:000}",
            item.TryGetProperty("sequence", out var sequence) ? sequence.GetInt32() : index + 1,
            item.GetProperty("kind").GetString() ?? "subtask",
            item.GetProperty("contractMarkdown").GetString() ?? throw new InvalidDataException("Work item contractMarkdown is missing."))).ToArray();
    }

    private static string? FindFactoryRunDirectory(string workspace)
    {
        var current = Path.Combine(workspace, ".idd", "factory", "current");
        if (File.Exists(Path.Combine(current, "events.jsonl"))) return current;
        var results = Path.Combine(workspace, ".idd", "factory", "results");
        return Directory.Exists(results) ? Directory.EnumerateFiles(results, "events.jsonl", SearchOption.AllDirectories).Select(Path.GetDirectoryName).Where(x => x is not null).OrderByDescending(x => x, StringComparer.Ordinal).FirstOrDefault() : null;
    }

    private static IReadOnlyDictionary<string, FactoryManifestItem> ReadFactoryKinds(string source)
    {
        var path = Path.Combine(source, "decomposition", "decomposition.json");
        if (!File.Exists(path)) return new Dictionary<string, FactoryManifestItem>();
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("workItems").EnumerateArray().Select(item => new FactoryManifestItem(
            item.GetProperty("id").GetString() ?? "unknown",
            item.GetProperty("kind").GetString() ?? "unknown",
            item.GetProperty("contractPath").GetString() ?? "unknown"))
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
    }

    private static string ReadFactoryRole(string attemptDirectory)
    {
        var path = Path.Combine(attemptDirectory, "invocation.json");
        if (!File.Exists(path)) return "factory-worker";
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("role", out var role) ? role.GetString() ?? "factory-worker" : "factory-worker";
        }
        catch (JsonException) { return "factory-worker"; }
    }

    private static Dictionary<string, TimeSpan> ReadFactoryAttemptDurations(string source)
    {
        var eventsPath = Path.Combine(source, "events.jsonl");
        var starts = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var durations = new Dictionary<string, TimeSpan>(StringComparer.Ordinal);
        if (!File.Exists(eventsPath)) return durations;
        foreach (var line in File.ReadLines(eventsPath))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("attemptId", out var idNode)) continue;
                var id = idNode.GetString();
                if (string.IsNullOrWhiteSpace(id) || !root.TryGetProperty("timestamp", out var timestampNode)) continue;
                var timestamp = timestampNode.GetDateTimeOffset();
                var type = root.GetProperty("type").GetString();
                if (type == "agent-dispatching") starts[id] = timestamp;
                if (type is "agent-completed" or "agent-result-reused" && starts.TryGetValue(id, out var started) && timestamp >= started) durations[id] = timestamp - started;
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException) { }
        }
        return durations;
    }

    private static bool Comparable(EnvironmentRecord left, EnvironmentRecord right) =>
        left.CodexVersion == right.CodexVersion && left.Model == right.Model && left.ReasoningEffort == right.ReasoningEffort && left.WindowsSandbox == right.WindowsSandbox &&
        left.FactoryVersion == right.FactoryVersion && left.FactoryPluginVersion == right.FactoryPluginVersion &&
        left.GitRevision == right.GitRevision && left.GitDirty == right.GitDirty && left.BenchmarkDefinitionSha256 == right.BenchmarkDefinitionSha256 &&
        left.SkillVersions.OrderBy(x => x.Key).SequenceEqual(right.SkillVersions.OrderBy(x => x.Key));

    private static async Task<bool> TryDeleteWorkspaceAsync(string workspace)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try { Directory.Delete(workspace, recursive: true); return true; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt == 5) return false;
                await Task.Delay(500);
            }
        }
        return false;
    }

    private static string UniquePath(string directory, string prefix, string extension)
    {
        var path = Path.Combine(directory, prefix + extension); var index = 1;
        while (File.Exists(path)) path = Path.Combine(directory, $"{prefix}-{++index:00}{extension}");
        return path;
    }

    private static string Sanitize(string value) => string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '-' : character));
    private static string Title(string markdown)
    {
        var lines = markdown.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
        var heading = lines.Select(line => line.StartsWith('#') ? line.TrimStart('#', ' ') : null)
            .FirstOrDefault(line => line is not null && line is not ("Goal" or "Context" or "Scope" or "Requirements"));
        var title = heading ?? lines.SkipWhile(line => !line.Equals("## Goal", StringComparison.OrdinalIgnoreCase))
            .Skip(1).FirstOrDefault(line => !line.StartsWith('#')) ?? lines.FirstOrDefault(line => !line.StartsWith('#')) ?? "untitled";
        return title.Length <= 160 ? title : title[..157] + "...";
    }
    private static string FailureSummary(IEnumerable<InvocationMetrics> invocations, AcceptanceResult acceptance)
    {
        var failed = invocations.FirstOrDefault(x => x.ExitCode != 0);
        return failed is null ? $"Acceptance failed with exit code {acceptance.ExitCode}." : failed.Error ?? $"Codex invocation '{failed.Role}' failed with exit code {failed.ExitCode}.";
    }
}

public sealed record GeneratedWorkItem(string Id, int Sequence, string Kind, string ContractMarkdown);
public sealed record FactoryManifestItem(string Id, string Kind, string ContractPath);
