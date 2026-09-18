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
        var caseRoot = Path.Combine(repo, "tests", "Idd.Factory.LiveTests", "Cases", "TwoStepCatalog");
        var tempRoot = Path.Combine(Path.GetTempPath(), "idd-factory-native-eval", Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(tempRoot, "workspace");
        var marketplace = Path.Combine(tempRoot, "marketplace");
        var codexHome = Path.Combine(tempRoot, "codex-home");
        var lastMessage = Path.Combine(tempRoot, "last-message.json");

        Directory.CreateDirectory(tempRoot);
        try
        {
            CopyDirectory(Path.Combine(caseRoot, "Template"), workspace);
            PrepareCodexHome(codexHome);

            await RunAsync("dotnet", ["build", "tools/generate/Generate.csproj", "--nologo"], repo, null, null);
            var generator = Path.Combine(repo, "tools", "generate", "bin", "Debug", "net10.0", "Generate.dll");
            await RunAsync("dotnet", ["exec", generator, "--version", "0.0.0-live", "--output", marketplace], repo, null, null);

            var generatedFactory = Path.Combine(marketplace, "plugins", "codex", "idd-factory");
            Assert.False(Directory.Exists(Path.Combine(generatedFactory, "runtime")));
            Assert.False(File.Exists(Path.Combine(generatedFactory, ".mcp.json")));

            var env = new Dictionary<string, string?> { ["CODEX_HOME"] = codexHome };
            var codex = Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_CODEX") ?? "codex";
            await RunAsync(codex, ["plugin", "marketplace", "add", marketplace, "--json"], repo, env, null);
            await RunAsync(codex, ["plugin", "add", "idd-factory@intent-driven-development", "--json"], repo, env, null);

            var task = await File.ReadAllTextAsync(Path.Combine(caseRoot, "task.md"));
            var schema = await File.ReadAllTextAsync(Path.Combine(caseRoot, "final-response.schema.json"));
            var prompt = task + "\n\nReturn only JSON matching this schema:\n" + schema;

            var model = Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_MODEL") ?? "gpt-5.6-luna";
            var reasoning = Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_REASONING_EFFORT") ?? "low";
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
                timeout: TimeSpan.FromMinutes(
                    int.TryParse(Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_TIMEOUT_MINUTES"), out var minutes)
                        ? minutes
                        : 20));

            Assert.Contains("spawn_agent", result.Stdout, StringComparison.Ordinal);
            Assert.DoesNotContain("factory_run", result.Stdout, StringComparison.Ordinal);
            Assert.DoesNotContain("factory_status", result.Stdout, StringComparison.Ordinal);
            Assert.DoesNotContain("idd-factory.dll", result.Stdout, StringComparison.Ordinal);

            var finalJson = JsonDocument.Parse(await File.ReadAllTextAsync(lastMessage));
            Assert.Equal("COMPLETED", finalJson.RootElement.GetProperty("status").GetString());

            var verification = await RunAsync("dotnet", ["test", "MiniCatalog.sln", "--nologo"], workspace, null, null);
            Assert.Equal(0, verification.ExitCode);

            Assert.True(File.Exists(Path.Combine(workspace, "src", "MiniCatalog", "ProductCode.cs")));
            var catalog = await File.ReadAllTextAsync(Path.Combine(workspace, "src", "MiniCatalog", "Catalog.cs"));
            Assert.Contains("ProductCode", catalog, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
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
        TimeSpan? timeout = null)
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

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
        }

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(3));
        await process.WaitForExitAsync(cts.Token);
        var result = new RunResult(process.ExitCode, await stdout, await stderr);
        Assert.True(result.ExitCode == 0,
            $"{executable} failed with exit code {result.ExitCode}.{Environment.NewLine}{result.Stderr}");
        return result;
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

    private sealed record RunResult(int ExitCode, string Stdout, string Stderr);
}
