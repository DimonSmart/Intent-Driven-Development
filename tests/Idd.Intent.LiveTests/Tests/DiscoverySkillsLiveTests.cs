using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Idd.Intent.LiveTests.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class LiveDiscoveryEvalFactAttribute : FactAttribute
{
    public LiveDiscoveryEvalFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("IDD_RUN_LIVE_DISCOVERY_EVALS") != "1")
            Skip = "Set IDD_RUN_LIVE_DISCOVERY_EVALS=1 to run token-consuming Clarify/Brainstorm live evaluations.";
    }
}

public sealed class DiscoverySkillsLiveTests
{
    private sealed record DiscoveryCase(string Id, string Skill, string Request, string[] Expect);
    private sealed record DiscoveryCases(int SchemaVersion, DiscoveryCase[] Scenarios);
    private sealed record ProcessResult(int? ExitCode, string Stdout, string Stderr, string? Error);
    private sealed record DiscoveryResult(string Id, string Skill, bool Passed, string Reason,
        string Response, string ExecutionLog, string JudgeResponse);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true, WriteIndented = true
    };

    [LiveDiscoveryEvalFact]
    [Trait("Category", "LiveDiscoveryEval")]
    public async Task InstalledSkills_ProduceSemanticallyUsefulReadOnlyResults()
    {
        var repo = FindRepo();
        var suite = JsonSerializer.Deserialize<DiscoveryCases>(await File.ReadAllTextAsync(
            Path.Combine(repo, "tests", "Idd.Intent.LiveTests", "Scenarios",
                "discovery-cases.json")), JsonOptions)
            ?? throw new InvalidOperationException("Missing discovery suite.");
        Assert.Equal(1, suite.SchemaVersion);
        Assert.Equal(13, suite.Scenarios.Length);

        var selected = suite.Scenarios;
        var requested = Environment.GetEnvironmentVariable("IDD_DISCOVERY_EVAL_CASE_IDS");
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var ids = requested.Split(',', StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);
            Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
            var byId = selected.ToDictionary(s => s.Id, StringComparer.Ordinal);
            foreach (var id in ids) Assert.True(byId.ContainsKey(id), "Unknown scenario " + id);
            selected = ids.Select(id => byId[id]).ToArray();
        }
        else if (int.TryParse(Environment.GetEnvironmentVariable("IDD_DISCOVERY_EVAL_MAX_CASES"),
            out var limit) && limit > 0)
            selected = selected.Take(limit).ToArray();

        var root = Environment.GetEnvironmentVariable("IDD_DISCOVERY_EVAL_OUTPUT_DIR")
            ?? Path.Combine(Path.GetTempPath(), "idd-discovery-eval", Guid.NewGuid().ToString("N"));
        var codex = Environment.GetEnvironmentVariable("IDD_DISCOVERY_EVAL_CODEX") ?? "codex";
        var model = Environment.GetEnvironmentVariable("IDD_DISCOVERY_EVAL_MODEL") ?? "gpt-6";
        var effort = Environment.GetEnvironmentVariable("IDD_DISCOVERY_EVAL_REASONING") ?? "medium";
        var judgeEnabled = Environment.GetEnvironmentVariable("IDD_DISCOVERY_EVAL_SKIP_JUDGE") != "1";
        var timeout = int.TryParse(Environment.GetEnvironmentVariable("IDD_DISCOVERY_EVAL_TIMEOUT_MINUTES"),
            out var minutes) && minutes > 0 ? TimeSpan.FromMinutes(minutes) : TimeSpan.FromMinutes(15);

        Directory.CreateDirectory(root);
        var home = await PreparePlugin(repo, root, codex, timeout);
        var env = new Dictionary<string, string?> { ["CODEX_HOME"] = home };
        var results = new List<DiscoveryResult>();

        foreach (var scenario in selected)
        {
            var work = Path.Combine(root, "workspaces", scenario.Id);
            Directory.CreateDirectory(work);
            if (scenario.Id == "C4") SeedCurrentIntent(work);
            if (scenario.Id == "B7") SeedProductContext(work);
            var before = Snapshot(work);
            var responsePath = Path.Combine(root, scenario.Id + "-response.md");
            var run = await Run(codex,
                ["exec", "--json", "--skip-git-repo-check", "--ignore-rules",
                 "--enable", "plugins", "--disable", "remote_plugin",
                 "-c", "approval_policy=never",
                 "-c", $"model_reasoning_effort={effort}",
                 "--model", model, "--sandbox", "read-only",
                 "--cd", work, "--output-last-message", responsePath, "-"],
                work, env, "Use the installed $" + scenario.Skill +
                    " skill explicitly. Do not modify any project files. " +
                    "Respond to the following original user request as that skill:

" +
                    scenario.Request, timeout);
            var response = File.Exists(responsePath)
                ? await File.ReadAllTextAsync(responsePath) : "";
            var unchanged = before.SequenceEqual(Snapshot(work));
            var succeeded = run.Error is null && run.ExitCode == 0 &&
                !string.IsNullOrWhiteSpace(response) && unchanged;
            var reason = !unchanged ? "Workspace changed during a read-only skill" :
                run.Error ?? (run.ExitCode != 0 ? "Codex failed: " + run.ExitCode :
                string.IsNullOrWhiteSpace(response) ? "No response" : "Semantic judge not run");
            var judgeResponse = "";
            if (succeeded && judgeEnabled)
            {
                var judgePath = Path.Combine(root, scenario.Id + "-judge.json");
                var rubric = string.Join("
", scenario.Expect.Select(x => "- " + x));
                var prompt = "Evaluate the assistant response against the user's request and " +
                    "the scenario-specific behavioral expectations. Consider semantic substance, " +
                    "not exact phrasing, headings, number of options or questions. Treat unsupported " +
                    "claims of having inspected files, web or code as failures. Do not reward " +
                    "repetition of skill boilerplate without addressing the task. Distinguish " +
                    "read-only compliance, explicit approval and hypothetical suggestions. " +
                    "Respond ONLY in JSON with a boolean pass and a concise string reason. " +
                    "The evaluation is not a request to perform Product work.

" +
                    "Request:
" + scenario.Request + "

Expectations:
" + rubric +
                    "

Assistant response:
" + response;
                var judged = await Run(codex,
                    ["exec", "--skip-git-repo-check", "--ignore-rules",
                     "-c", "approval_policy=never",
                     "-c", $"model_reasoning_effort={effort}",
                     "--model", model, "--sandbox", "read-only",
                     "--cd", work, "--output-last-message", judgePath, "-"],
                    work, env, prompt, timeout);
                judgeResponse = File.Exists(judgePath)
                    ? await File.ReadAllTextAsync(judgePath) : "";
                if (judged.ExitCode != 0 || judged.Error is not null ||
                    !TryReadVerdict(judgeResponse, out var verdict, out var explanation))
                {
                    succeeded = false;
                    reason = "Judge execution or JSON parsing failed: " + judged.Error +
                        " " + judged.Stderr;
                }
                else
                {
                    succeeded = verdict;
                    reason = explanation;
                }
            }
            results.Add(new DiscoveryResult(scenario.Id, scenario.Skill, succeeded,
                reason, response, run.Stdout + "
" + run.Stderr, judgeResponse));
            await File.WriteAllTextAsync(Path.Combine(root, "report.json"),
                JsonSerializer.Serialize(results, JsonOptions));
        }

        Assert.True(results.All(r => r.Passed),
            "Discovery live evaluation failed. See " + Path.Combine(root, "report.json") +
            "
" + string.Join("
", results.Where(r => !r.Passed)
                .Select(r => r.Id + ": " + r.Reason)));
    }

    private static bool TryReadVerdict(string response, out bool pass, out string reason)
    {
        pass = false; reason = "";
        try
        {
            var start = response.IndexOf('{');
            var end = response.LastIndexOf('}');
            if (start < 0 || end <= start) return false;
            using var json = JsonDocument.Parse(response.Substring(start, end - start + 1));
            var root = json.RootElement;
            if (!root.TryGetProperty("pass", out var value) ||
                value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return false;
            pass = value.GetBoolean();
            reason = root.TryGetProperty("reason", out var text) && text.ValueKind == JsonValueKind.String
                ? text.GetString() ?? "" : "";
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static void SeedCurrentIntent(string work)
    {
        var directory = Path.Combine(work, ".idd", "intent");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "README.md"),
            "# Current Product Intent\n\nCurrent confirmed requirements are indexed below.\n");
        File.WriteAllText(Path.Combine(directory, "INDEX.md"),
            "# Index\n\n- IDD-0001.spec-export.md — export limits and behavior\n");
        File.WriteAllText(Path.Combine(directory, "IDD-0001.spec-export.md"),
            "# IDD-0001 — Report export\n\nThe report export produces a CSV file " +
            "for the current user. Exported files have an existing confirmed " +
            "maximum size of 5 MB. UTF-8 encoding is required.\n");
    }

    private static void SeedProductContext(string work)
    {
        var directory = Path.Combine(work, ".idd", "intent");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "README.md"),
            "# Offline terminal file manager\n\nThe application works entirely locally without a required internet connection.\n");
        File.WriteAllText(Path.Combine(directory, "INDEX.md"),
            "# Index\n\nNo current numbered specifications.\n");
    }

    private static string[] Snapshot(string directory) =>
        Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/') +
                ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))
            .ToArray();

    private static async Task<string> PreparePlugin(string repo, string root,
        string codex, TimeSpan timeout)
    {
        var home = Path.Combine(root, "codex-home");
        var marketplace = Path.Combine(root, "marketplace");
        Directory.CreateDirectory(home);
        var existing = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(existing))
            existing = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile), ".codex");
        var auth = Path.Combine(existing, "auth.json");
        if (File.Exists(auth)) File.Copy(auth, Path.Combine(home, "auth.json"), true);

        await Ensure("dotnet", ["build", "tools/generate/Generate.csproj",
            "--nologo"], repo, null, timeout);
        var generator = Path.Combine(repo, "tools", "generate", "bin",
            "Debug", "net10.0", "Generate.dll");
        await Ensure("dotnet", ["exec", generator, "--version", "0.0.0-discovery-eval",
            "--output", marketplace], repo, null, timeout);
        var env = new Dictionary<string, string?> { ["CODEX_HOME"] = home };
        await Ensure(codex, ["plugin", "marketplace", "add", marketplace, "--json"],
            repo, env, timeout);
        await Ensure(codex, ["plugin", "add", "idd-intent@intent-driven-development",
            "--json"], repo, env, timeout);
        return home;
    }

    private static async Task Ensure(string app, IReadOnlyList<string> args, string cwd,
        IReadOnlyDictionary<string, string?>? env, TimeSpan timeout)
    {
        var run = await Run(app, args, cwd, env, null, timeout);
        if (run.ExitCode != 0 || run.Error is not null)
            throw new InvalidOperationException(app + " " + string.Join(" ", args) +
                "
" + run.Error + "
" + run.Stderr + "
" + run.Stdout);
    }

    private static async Task<ProcessResult> Run(string app, IReadOnlyList<string> args,
        string cwd, IReadOnlyDictionary<string, string?>? env, string? input, TimeSpan timeout)
    {
        Process? process = null;
        Task<string>? stdout = null, stderr = null;
        try
        {
            var start = new ProcessStartInfo(app)
            {
                WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                RedirectStandardInput = input is not null
            };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            if (env is not null)
                foreach (var (name, value) in env) start.Environment[name] = value;
            process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start " + app);
            stdout = process.StandardOutput.ReadToEndAsync();
            stderr = process.StandardError.ReadToEndAsync();
            if (input is not null)
            {
                await process.StandardInput.WriteAsync(input);
                process.StandardInput.Close();
            }
            using var cancellation = new CancellationTokenSource(timeout);
            await process.WaitForExitAsync(cancellation.Token);
            return new ProcessResult(process.ExitCode, await stdout, await stderr, null);
        }
        catch (Exception error)
        {
            if (process is { HasExited: false })
                try { process.Kill(entireProcessTree: true); } catch { }
            return new ProcessResult(process is { HasExited: true } ? process.ExitCode : null,
                stdout is { IsCompletedSuccessfully: true } ? await stdout : "",
                stderr is { IsCompletedSuccessfully: true } ? await stderr : "",
                error.ToString());
        }
        finally { process?.Dispose(); }
    }

    private static string FindRepo()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName,
                "Intent-Driven-Development.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Unable to locate IDD repository root.");
    }
}
