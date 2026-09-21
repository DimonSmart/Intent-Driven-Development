using System.Diagnostics;
using System.Text.Json;
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
        var runId = $"{startedAtUtc:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}";
        var artifactRoot = Path.Combine(repo, "artifacts", "factory-evals", runId);
        Directory.CreateDirectory(artifactRoot);

        var caseRoot = Path.Combine(repo, "tests", "Idd.Factory.LiveTests", "Cases", "TwoStepCatalog");
        var tempRoot = Path.Combine(Path.GetTempPath(), "idd-factory-native-eval", Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(tempRoot, "workspace");
        var marketplace = Path.Combine(tempRoot, "marketplace");
        var codexHome = Path.Combine(tempRoot, "codex-home");
        var lastMessage = Path.Combine(tempRoot, "last-message.json");
        var model = Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_MODEL") ?? "gpt-5.6-luna";
        var reasoning = Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_REASONING_EFFORT") ?? "low";
        var timeoutMinutes =
            int.TryParse(Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_TIMEOUT_MINUTES"), out var configuredTimeout)
                ? configuredTimeout
                : 20;
        Exception? failure = null;

        try
        {
            CopyDirectory(Path.Combine(caseRoot, "Template"), workspace);
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
                    "exec", "--json", "--ephemeral", "--ignore-rules",
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
            Assert.Contains(traceItems, item => IsToolCall(item, "collab_tool_call", "spawn_agent"));
            Assert.DoesNotContain(traceItems, item => IsToolCall(item, "mcp_tool_call", "factory_run"));
            Assert.DoesNotContain(traceItems, item => IsToolCall(item, "mcp_tool_call", "factory_status"));
            Assert.DoesNotContain(traceItems, IsLegacyFactoryRuntimeCommand);

            var finalJson = JsonDocument.Parse(await File.ReadAllTextAsync(lastMessage));
            Assert.Equal("COMPLETED", finalJson.RootElement.GetProperty("status").GetString());

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
            await TrySaveLastMessageAsync(lastMessage, artifactRoot);
            await TryWriteSummaryAsync(
                artifactRoot,
                runId,
                startedAtUtc,
                model,
                reasoning,
                timeoutMinutes,
                failure);

            try { Directory.Delete(tempRoot, recursive: true); } catch { }
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

    private static async Task TryWriteSummaryAsync(
        string artifactRoot,
        string runId,
        DateTimeOffset startedAtUtc,
        string model,
        string reasoning,
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
                    reasoningEffort = reasoning,
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
