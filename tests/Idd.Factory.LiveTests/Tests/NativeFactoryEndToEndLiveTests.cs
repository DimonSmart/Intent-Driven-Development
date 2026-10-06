using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Idd.Factory.Report;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class NativeFactoryEndToEndLiveTests
{
    [LiveFactoryEvalFact]
    [Trait("Category", "LiveFactoryEval")]
    public async Task Factory_UsesNativeAgentsAndCompletesTwoStepCatalog()
    {
        var repo = FindRepositoryRoot();
        var startedAtUtc = DateTimeOffset.UtcNow;
        var generatedRunId = $"{startedAtUtc:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}";
        var configuredArtifactRoot = Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_ARTIFACT_DIR");
        var artifactRoot = string.IsNullOrWhiteSpace(configuredArtifactRoot)
            ? Path.Combine(repo, "artifacts", "factory-evals", generatedRunId)
            : Path.GetFullPath(configuredArtifactRoot);
        var runId = Path.GetFileName(Path.TrimEndingDirectorySeparator(artifactRoot));
        Directory.CreateDirectory(artifactRoot);

        var caseRoot = Path.Combine(repo, "tests", "Idd.Factory.LiveTests", "Cases", "TwoStepCatalog");
        var configuredTempRoot = Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_TEMP_ROOT");
        var tempRoot = string.IsNullOrWhiteSpace(configuredTempRoot)
            ? Path.Combine(Path.GetTempPath(), "idd-factory-native-eval", Guid.NewGuid().ToString("N"))
            : Path.GetFullPath(configuredTempRoot);
        var preserveTempRoot = string.Equals(
            Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_KEEP_TEMP"),
            "1",
            StringComparison.Ordinal);
        var workspace = Path.Combine(tempRoot, "workspace");
        var marketplace = Path.Combine(tempRoot, "marketplace");
        var codexHome = Path.Combine(tempRoot, "codex-home");
        var lastMessage = Path.Combine(tempRoot, "last-message.json");
        var settings = LiveEvalSettings.Resolve(
            repo,
            Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_MODEL"),
            Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_REASONING_EFFORT"),
            Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_MODEL_SOURCE"),
            Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_REASONING_EFFORT_SOURCE"),
            profileEnvironment: Environment.GetEnvironmentVariable);
        var model = settings.Model;
        var reasoning = settings.ReasoningEffort;
        var timeoutMinutes =
            int.TryParse(Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_TIMEOUT_MINUTES"), out var configuredTimeout)
                ? configuredTimeout
                : 20;
        Exception? failure = null;

        try
        {
            Console.WriteLine($"Live-eval model: {model} ({settings.ModelSource})");
            Console.WriteLine($"Live-eval reasoning effort: {reasoning} ({settings.ReasoningEffortSource})");
            foreach (var mapping in settings.ExecutionProfiles)
                Console.WriteLine($"Worker {mapping.Key}: model={mapping.Value.Model}, reasoning={mapping.Value.ReasoningEffort}");
            CopyDirectory(Path.Combine(caseRoot, "Template"), workspace);
            ConfigureExecutionPolicy(workspace, settings.ExecutionProfiles);
            await InitializeGitRepositoryAsync(workspace, artifactRoot);
            PrepareCodexHome(codexHome);

            await RunAsync(
                "dotnet",
                ["build", "tools/generate/Generate.csproj", "--nologo"],
                repo,
                null,
                null,
                artifactRoot,
                "06-generator-build");

            var generator = Path.Combine(repo, "tools", "generate", "bin", "Debug", "net10.0", "Generate.dll");
            await RunAsync(
                "dotnet",
                ["exec", generator, "--version", "0.0.0-live", "--output", marketplace],
                repo,
                null,
                null,
                artifactRoot,
                "07-generate-marketplace");

            var generatedFactory = Path.Combine(marketplace, "plugins", "codex", "idd-factory");
            Assert.False(Directory.Exists(Path.Combine(generatedFactory, "runtime")));
            Assert.False(File.Exists(Path.Combine(generatedFactory, ".mcp.json")));

            var env = new Dictionary<string, string?> { ["CODEX_HOME"] = codexHome };
            var codex = Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_CODEX") ?? "codex";
            await RunAsync(
                codex,
                ["plugin", "marketplace", "add", marketplace, "--json"],
                repo,
                env,
                null,
                artifactRoot,
                "08-codex-marketplace-add");

            await RunAsync(
                codex,
                ["plugin", "add", "idd-factory@intent-driven-development", "--json"],
                repo,
                env,
                null,
                artifactRoot,
                "09-codex-plugin-add");

            var task = await File.ReadAllTextAsync(Path.Combine(caseRoot, "task.md"));
            var schema = await File.ReadAllTextAsync(Path.Combine(caseRoot, "final-response.schema.json"));
            var prompt = task + "\n\nReturn only JSON matching this schema:\n" + schema;

            var result = await RunAsync(
                codex,
                [
                    "exec", "--json", "--ignore-rules",
                    "--enable", "multi_agent", "--disable", "multi_agent_v2",
                    "--enable", "plugins", "--disable", "remote_plugin",
                    "-c", "agents.max_depth=2",
                    "-c", "agents.max_threads=10",
                    "-c", "approval_policy=never",
                    "-c", $"model_reasoning_effort={reasoning}",
                    "--model", model,
                    "--sandbox", "danger-full-access",
                    "--cd", workspace,
                    "--output-last-message", lastMessage,
                    "-"
                ],
                workspace,
                env,
                prompt,
                artifactRoot,
                "10-codex-exec",
                timeout: TimeSpan.FromMinutes(timeoutMinutes));

            await File.WriteAllTextAsync(Path.Combine(artifactRoot, "codex-trace.jsonl"), result.Stdout);
            await File.WriteAllTextAsync(Path.Combine(artifactRoot, "codex-stderr.log"), result.Stderr);
            if (File.Exists(lastMessage))
                File.Copy(lastMessage, Path.Combine(artifactRoot, "last-message.json"), overwrite: true);

            var traceItems = ParseTraceItems(result.Stdout);
            var rootRollout = FindRootRollout(codexHome);
            Assert.True(
                rootRollout.SpawnRecords.Count >= 4,
                "Expected a planner and at least three workers in the native Codex session rollout.");
            Assert.DoesNotContain(traceItems, item => IsToolCall(item, "mcp_tool_call", "factory_run"));
            Assert.DoesNotContain(traceItems, item => IsToolCall(item, "mcp_tool_call", "factory_status"));
            Assert.DoesNotContain(traceItems, IsLegacyFactoryRuntimeCommand);
            var finalJson = JsonDocument.Parse(await File.ReadAllTextAsync(lastMessage));
            Assert.Equal("COMPLETED", finalJson.RootElement.GetProperty("status").GetString());

            var factoryReport = Assert.Single(new FactoryReportEngine().FindRuns(workspace, codexHome));
            Assert.NotEmpty(factoryReport.Tasks);
            FactoryRoutingAssertions.Verify(factoryReport, settings.ExecutionProfiles);
            var childPaths = Directory.EnumerateFiles(Path.Combine(codexHome, "sessions"), "*.jsonl", SearchOption.AllDirectories)
                .Select(path => new CodexRolloutReader().Read(path))
                .ToDictionary(rollout => rollout.ThreadId, rollout => rollout.AgentPath, StringComparer.Ordinal);
            foreach (var worker in factoryReport.Agents.Where(agent => agent.Role == "worker"))
            {
                var spawn = Assert.Single(rootRollout.SpawnRecords.Values, spawn =>
                    spawn.Children.Contains(worker.ThreadId, StringComparer.Ordinal) ||
                    spawn.ChildAgentPath is not null && spawn.ChildAgentPath == childPaths[worker.ThreadId]);
                var expected = settings.ExecutionProfiles[worker.ExecutionProfile!];
                Assert.Equal(expected.Model, spawn.RequestedModel);
                Assert.Equal(expected.ReasoningEffort, spawn.RequestedReasoningEffort);
            }
            Assert.Equal(new[] { "standard", "strong", "economy" },
                factoryReport.Tasks.Take(3).Select(task => task.ExecutionProfile).ToArray());
            Assert.Equal("completed", factoryReport.Run.Result);
            Assert.Equal("passed", factoryReport.Completion.ProjectVerification);
            Assert.False(factoryReport.FactoryProjectState.CurrentRequestPresent);
            Assert.All(factoryReport.Tasks, task => Assert.Equal("completed", task.Status));

            var verification = await RunAsync(
                "dotnet",
                ["test", "MiniCatalog.sln", "--nologo"],
                workspace,
                null,
                null,
                artifactRoot,
                "11-project-verification");
            Assert.Equal(0, verification.ExitCode);

            Assert.True(File.Exists(Path.Combine(workspace, "src", "MiniCatalog", "ProductCode.cs")));
            var catalog = await File.ReadAllTextAsync(Path.Combine(workspace, "src", "MiniCatalog", "Catalog.cs"));
            Assert.Contains("ProductCode", catalog, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            failure = ex;
            await TryWriteTextAsync(Path.Combine(artifactRoot, "exception.txt"), ex.ToString());
            throw;
        }
        finally
        {
            await TrySaveWorkspaceArtifactsAsync(workspace, artifactRoot);
            await TrySaveCodexAliasesAsync(artifactRoot);
            await TrySaveLastMessageAsync(lastMessage, artifactRoot);
            await TrySaveReportSourceAsync(codexHome, artifactRoot);
            await TryWriteSummaryAsync(
                artifactRoot,
                runId,
                startedAtUtc,
                model,
                settings.ModelSource,
                reasoning,
                settings.ReasoningEffortSource,
                settings.ExecutionProfiles,
                timeoutMinutes,
                failure);

            if (!preserveTempRoot)
            {
                try { Directory.Delete(tempRoot, recursive: true); } catch { }
            }
        }
    }

    private static IReadOnlyList<JsonElement> ParseTraceItems(string stdout)
    {
        var items = new List<JsonElement>();
        foreach (var line in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("item", out var item))
                items.Add(item.Clone());
        }

        return items;
    }

    private static CodexRollout FindRootRollout(string codexHome)
    {
        var sessions = Path.Combine(codexHome, "sessions");
        Assert.True(Directory.Exists(sessions), "Codex did not create a sessions directory.");

        var reader = new CodexRolloutReader();
        var rollouts = Directory.EnumerateFiles(sessions, "*.jsonl", SearchOption.AllDirectories)
            .Select(path => reader.Read(path))
            .ToArray();
        return Assert.Single(rollouts, rollout =>
            string.IsNullOrWhiteSpace(rollout.ParentThreadId) &&
            rollout.SpawnRecords.Count > 0);
    }

    private static bool IsToolCall(JsonElement item, string itemType, string tool)
    {
        return item.TryGetProperty("type", out var type)
            && type.ValueEquals(itemType)
            && item.TryGetProperty("tool", out var actualTool)
            && actualTool.ValueEquals(tool);
    }

    private static bool IsLegacyFactoryRuntimeCommand(JsonElement item)
    {
        return item.TryGetProperty("type", out var type)
            && type.ValueEquals("command_execution")
            && item.TryGetProperty("command", out var command)
            && command.GetString()?.Contains("idd-factory.dll", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static async Task InitializeGitRepositoryAsync(string workspace, string artifactRoot)
    {
        await RunAsync("git", ["init"], workspace, null, null, artifactRoot, "01-git-init");
        await RunAsync("git", ["config", "user.name", "IDD Factory Eval"], workspace, null, null, artifactRoot, "02-git-config-name");
        await RunAsync("git", ["config", "user.email", "idd-factory-eval@localhost"], workspace, null, null, artifactRoot, "03-git-config-email");
        await RunAsync("git", ["add", "--all"], workspace, null, null, artifactRoot, "04-git-add");
        await RunAsync("git", ["commit", "-m", "Initial eval workspace"], workspace, null, null, artifactRoot, "05-git-commit");
    }

    private static void ConfigureExecutionPolicy(string workspace,
        IReadOnlyDictionary<string, WorkerExecutionSettings> mappings)
    {
        LiveEvalSettings.ValidateProfiles(mappings);
        var idd = Path.Combine(workspace, ".idd");
        Directory.CreateDirectory(idd);
        var yaml = new StringBuilder("version: 1\n\nfactory:\n  executionProfiles:\n");
        foreach (var profile in LiveEvalSettings.Profiles)
        {
            var mapping = mappings[profile];
            var modelScalar = mapping.Model.Replace("'", "''", StringComparison.Ordinal);
            var reasoningScalar = mapping.ReasoningEffort.Replace("'", "''", StringComparison.Ordinal);
            yaml.AppendLine($"    {profile}:");
            yaml.AppendLine("      codex:");
            yaml.AppendLine($"        model: '{modelScalar}'");
            yaml.AppendLine($"        reasoningEffort: '{reasoningScalar}'");
        }
        File.WriteAllText(Path.Combine(idd, "execution.yaml"), yaml.ToString().ReplaceLineEndings("\n"));
    }

    private static void PrepareCodexHome(string target)
    {
        Directory.CreateDirectory(target);
        var source = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(source))
            source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        var auth = Path.Combine(source, "auth.json");
        if (File.Exists(auth))
            File.Copy(auth, Path.Combine(target, "auth.json"), overwrite: true);
    }

    private static async Task<RunResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environment,
        string? stdin,
        string artifactRoot,
        string artifactName,
        TimeSpan? timeout = null,
        bool requireSuccess = true)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (environment is not null)
            foreach (var (name, value) in environment)
                start.Environment[name] = value;

        await SaveCommandMetadataAsync(artifactRoot, artifactName, executable, arguments, workingDirectory);

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(3));

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException ex)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            try { await process.WaitForExitAsync(CancellationToken.None); } catch { }

            var timedOutResult = new RunResult(
                process.HasExited ? process.ExitCode : -1,
                await stdoutTask,
                await stderrTask,
                TimedOut: true);
            await SaveCommandResultAsync(artifactRoot, artifactName, timedOutResult);

            throw new TimeoutException(
                $"{executable} timed out after {(timeout ?? TimeSpan.FromMinutes(3)).TotalMinutes:0.##} minutes.",
                ex);
        }

        var result = new RunResult(process.ExitCode, await stdoutTask, await stderrTask);
        await SaveCommandResultAsync(artifactRoot, artifactName, result);

        if (requireSuccess)
        {
            Assert.True(
                result.ExitCode == 0,
                $"{executable} failed with exit code {result.ExitCode}.{Environment.NewLine}{result.Stderr}");
        }

        return result;
    }

    private static async Task SaveCommandMetadataAsync(
        string artifactRoot,
        string artifactName,
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        var json = JsonSerializer.Serialize(
            new
            {
                executable,
                arguments,
                workingDirectory
            },
            new JsonSerializerOptions { WriteIndented = true });

        await File.WriteAllTextAsync(Path.Combine(artifactRoot, $"{artifactName}.command.json"), json);
    }

    private static async Task SaveCommandResultAsync(
        string artifactRoot,
        string artifactName,
        RunResult result)
    {
        await File.WriteAllTextAsync(Path.Combine(artifactRoot, $"{artifactName}.stdout.log"), result.Stdout);
        await File.WriteAllTextAsync(Path.Combine(artifactRoot, $"{artifactName}.stderr.log"), result.Stderr);

        var json = JsonSerializer.Serialize(
            new
            {
                exitCode = result.ExitCode,
                timedOut = result.TimedOut
            },
            new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(artifactRoot, $"{artifactName}.result.json"), json);
    }

    private static async Task TrySaveWorkspaceArtifactsAsync(string workspace, string artifactRoot)
    {
        if (!Directory.Exists(workspace))
            return;

        try
        {
            CopyWorkspaceSnapshot(workspace, Path.Combine(artifactRoot, "workspace"));

            var status = await RunAsync(
                "git",
                ["status", "--short"],
                workspace,
                null,
                null,
                artifactRoot,
                "12-workspace-status",
                requireSuccess: false);
            await File.WriteAllTextAsync(Path.Combine(artifactRoot, "workspace.status.txt"), status.Stdout);

            var diff = await RunAsync(
                "git",
                ["diff", "--binary", "HEAD"],
                workspace,
                null,
                null,
                artifactRoot,
                "13-workspace-diff",
                requireSuccess: false);
            await File.WriteAllTextAsync(Path.Combine(artifactRoot, "workspace.diff"), diff.Stdout);
        }
        catch (Exception ex)
        {
            await TryWriteTextAsync(Path.Combine(artifactRoot, "artifact-capture-error.txt"), ex.ToString());
        }
    }

    private static async Task TrySaveCodexAliasesAsync(string artifactRoot)
    {
        try
        {
            var stdout = Path.Combine(artifactRoot, "10-codex-exec.stdout.log");
            if (File.Exists(stdout))
                File.Copy(stdout, Path.Combine(artifactRoot, "codex-trace.jsonl"), overwrite: true);

            var stderr = Path.Combine(artifactRoot, "10-codex-exec.stderr.log");
            if (File.Exists(stderr))
                File.Copy(stderr, Path.Combine(artifactRoot, "codex-stderr.log"), overwrite: true);
        }
        catch (Exception ex)
        {
            await TryWriteTextAsync(Path.Combine(artifactRoot, "codex-alias-copy-error.txt"), ex.ToString());
        }
    }

    private static async Task TrySaveLastMessageAsync(string lastMessage, string artifactRoot)
    {
        if (!File.Exists(lastMessage))
            return;

        try
        {
            File.Copy(lastMessage, Path.Combine(artifactRoot, "last-message.json"), overwrite: true);
        }
        catch (Exception ex)
        {
            await TryWriteTextAsync(Path.Combine(artifactRoot, "last-message-copy-error.txt"), ex.ToString());
        }
    }

    private static async Task TrySaveReportSourceAsync(string codexHome, string artifactRoot)
    {
        try
        {
            if (!Directory.Exists(codexHome))
                return;

            var destination = Path.Combine(artifactRoot, "report-source");
            Directory.CreateDirectory(destination);

            foreach (var directoryName in new[] { "sessions", "archived_sessions" })
            {
                var source = Path.Combine(codexHome, directoryName);
                if (Directory.Exists(source))
                    CopyDirectory(source, Path.Combine(destination, directoryName));
            }

            foreach (var stateFile in Directory.EnumerateFiles(codexHome, "state_*.sqlite", SearchOption.TopDirectoryOnly))
                File.Copy(stateFile, Path.Combine(destination, Path.GetFileName(stateFile)), overwrite: true);

            var manifest = JsonSerializer.Serialize(
                new
                {
                    source = "allow-list",
                    copied = new[] { "sessions/**", "archived_sessions/**", "state_*.sqlite" },
                    excluded = new[] { "auth.json", "credentials", "API keys", "plugin credentials" }
                },
                new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(destination, "manifest.json"), manifest);
        }
        catch (Exception ex)
        {
            await TryWriteTextAsync(Path.Combine(artifactRoot, "report-source-copy-error.txt"), ex.ToString());
        }
    }

    private static async Task TryWriteSummaryAsync(
        string artifactRoot,
        string runId,
        DateTimeOffset startedAtUtc,
        string model,
        string modelSource,
        string reasoning,
        string reasoningEffortSource,
        IReadOnlyDictionary<string, WorkerExecutionSettings> executionProfiles,
        int timeoutMinutes,
        Exception? failure)
    {
        try
        {
            var summary = JsonSerializer.Serialize(
                new
                {
                    runId,
                    status = failure is null ? "PASSED" : "FAILED",
                    startedAtUtc,
                    finishedAtUtc = DateTimeOffset.UtcNow,
                    model,
                    modelSource,
                    reasoningEffort = reasoning,
                    reasoningEffortSource,
                    executionProfiles,
                    timeoutMinutes,
                    evalVersion = Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_VERSION"),
                    failure = failure?.ToString()
                },
                new JsonSerializerOptions { WriteIndented = true });

            await File.WriteAllTextAsync(Path.Combine(artifactRoot, "summary.json"), summary);
        }
        catch
        {
        }
    }

    private static async Task TryWriteTextAsync(string path, string content)
    {
        try
        {
            await File.WriteAllTextAsync(path, content);
        }
        catch
        {
        }
    }

    private static void CopyWorkspaceSnapshot(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (IsInsideGitDirectory(relative))
                continue;

            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static bool IsInsideGitDirectory(string relativePath)
    {
        var firstSeparator = relativePath.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        var firstSegment = firstSeparator < 0 ? relativePath : relativePath[..firstSeparator];
        return string.Equals(firstSegment, ".git", StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Environment.CurrentDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Intent-Driven-Development.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed record RunResult(int ExitCode, string Stdout, string Stderr, bool TimedOut = false);
}
