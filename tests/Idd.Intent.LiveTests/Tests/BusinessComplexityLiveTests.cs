using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Idd.Intent.LiveTests.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class LiveEstimateEvalFactAttribute : FactAttribute
{
    public LiveEstimateEvalFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("IDD_RUN_LIVE_ESTIMATE_EVALS") != "1")
            Skip = "Set IDD_RUN_LIVE_ESTIMATE_EVALS=1 for token-consuming business complexity live evaluations.";
    }
}

public sealed class BusinessComplexityLiveTests
{
    private sealed record EstimateCase(
        string Id,
        string Request,
        Dictionary<string, string> Documents,
        string[] Expected,
        string? ExtraCode = null,
        string[]? AdditionalIndexIds = null,
        string? Source = null);
    private sealed record EstimateCases(int SchemaVersion, EstimateCase[] Scenarios);
    private sealed record ProcessResult(int? ExitCode, string Stdout, string Stderr, string? Error);
    private sealed record CaseResult(string Id, string Host, string Model, string PluginRevision,
        bool Passed, string Reason, string Response, string Trace, string JudgeResponse);
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true, WriteIndented = true
    };

    [LiveEstimateEvalFact]
    [Trait("Category", "LiveEstimateEval")]
    public async Task Codex_EstimatesCurrentIntentWithoutModifyingWorkspace()
    {
        var repository = FindRoot();
        var suitePath = Path.Combine(repository, "tests", "Idd.Intent.LiveTests",
            "Scenarios", "estimate-cases.json");
        var suite = JsonSerializer.Deserialize<EstimateCases>(
            await File.ReadAllTextAsync(suitePath), Options)
            ?? throw new InvalidOperationException("Missing estimate scenarios");
        Assert.Equal(1, suite.SchemaVersion);
        Assert.True(suite.Scenarios.Length >= 20);
        Assert.Equal(suite.Scenarios.Length,
            suite.Scenarios.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count());

        var selected = suite.Scenarios;
        var specified = Environment.GetEnvironmentVariable("IDD_ESTIMATE_EVAL_CASE_IDS");
        if (!string.IsNullOrWhiteSpace(specified))
        {
            var ids = specified.Split(',', StringSplitOptions.TrimEntries |
                StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
            var indexed = selected.ToDictionary(x => x.Id, StringComparer.Ordinal);
            foreach (var id in ids) Assert.True(indexed.ContainsKey(id), "Unknown ID: " + id);
            selected = ids.Select(id => indexed[id]).ToArray();
        }
        else if (int.TryParse(Environment.GetEnvironmentVariable(
            "IDD_ESTIMATE_EVAL_MAX_CASES"), out var count) && count > 0)
        {
            selected = selected.Take(count).ToArray();
        }

        var codex = Environment.GetEnvironmentVariable("IDD_ESTIMATE_EVAL_CODEX") ?? "codex";
        var model = Environment.GetEnvironmentVariable("IDD_ESTIMATE_EVAL_MODEL") ?? "gpt-6";
        var reasoning = Environment.GetEnvironmentVariable("IDD_ESTIMATE_EVAL_REASONING") ?? "medium";
        var timeout = int.TryParse(Environment.GetEnvironmentVariable(
            "IDD_ESTIMATE_EVAL_TIMEOUT_MINUTES"), out var minutes) && minutes > 0
            ? TimeSpan.FromMinutes(minutes) : TimeSpan.FromMinutes(15);
        var judgeEnabled = Environment.GetEnvironmentVariable("IDD_ESTIMATE_EVAL_SKIP_JUDGE") != "1";
        var outputRoot = Environment.GetEnvironmentVariable("IDD_ESTIMATE_EVAL_OUTPUT_DIR")
            ?? Path.Combine(Path.GetTempPath(), "idd-estimate-eval",
                Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputRoot);
        var revision = await EnsureRevision(repository);
        var home = await InstallPlugin(repository, outputRoot, codex, timeout);
        var env = new Dictionary<string, string?> { ["CODEX_HOME"] = home };
        var results = new List<CaseResult>();

        foreach (var scenario in selected)
        {
            var work = Path.Combine(outputRoot, "workspaces", scenario.Id);
            Directory.CreateDirectory(work);
            Seed(repository, work, scenario);
            var before = Snapshot(work);
            var replyPath = Path.Combine(outputRoot, scenario.Id + "-response.md");
            var prompt = "Use the installed $idd-intent-estimate skill explicitly. " +
                "Read the current Product Intent in this workspace and complete the user " +
                "request without changing any file. Do not inspect implementation:\n\n" +
                scenario.Request;
            var run = await Run(codex,
                ["exec", "--json", "--skip-git-repo-check", "--ignore-rules",
                 "--enable", "plugins", "--disable", "remote_plugin",
                 "-c", "approval_policy=never",
                 "-c", "model_reasoning_effort=" + reasoning,
                 "--model", model, "--sandbox", "read-only", "--cd", work,
                 "--output-last-message", replyPath, "-"],
                work, env, prompt, timeout);
            var response = File.Exists(replyPath) ?
                await File.ReadAllTextAsync(replyPath) : "";
            var unchanged = before.SequenceEqual(Snapshot(work));
            var passed = run.Error is null && run.ExitCode == 0 &&
                !string.IsNullOrWhiteSpace(response) && unchanged;
            var reason = !unchanged ? "Workspace modified" :
                run.Error ?? (run.ExitCode != 0 ? "Codex exit " + run.ExitCode :
                string.IsNullOrWhiteSpace(response) ? "Missing response" :
                "Semantic judge was not run");
            var judgeResponse = "";
            if (passed && judgeEnabled)
            {
                var judgeFile = Path.Combine(outputRoot, scenario.Id + "-judge.md");
                var rubric = string.Join("\n", scenario.Expected.Select(x => "- " + x));
                var judgePrompt = "Evaluate the following business complexity assessment. " +
                    "Judge semantically, not by exact wording or formatting. Only pass " +
                    "if every scenario-specific expectation is met and there is no " +
                    "fabricated Product Intent, false claim of reading source code, " +
                    "unverified citations, invented COSMIC CFP, or claim of completed " +
                    "work. The source material is in the original workspace. " +
                    "The request is not to perform additional product work. " +
                    "Reply ONLY as a JSON object with boolean pass and string reason.\n\n" +
                    "Original request:\n" + scenario.Request +
                    "\n\nExpected semantic behavior:\n" + rubric +
                    "\n\nAssessment:\n" + response;
                var judged = await Run(codex,
                    ["exec", "--skip-git-repo-check", "--ignore-rules",
                     "-c", "approval_policy=never",
                     "-c", "model_reasoning_effort=" + reasoning,
                     "--model", model, "--sandbox", "read-only", "--cd", work,
                     "--output-last-message", judgeFile, "-"],
                    work, env, judgePrompt, timeout);
                judgeResponse = File.Exists(judgeFile) ?
                    await File.ReadAllTextAsync(judgeFile) : "";
                if (judged.ExitCode != 0 || judged.Error is not null ||
                    !TryVerdict(judgeResponse, out var verdict, out var explanation))
                {
                    passed = false;
                    reason = "Judge failed: " + judged.Error + " " + judged.Stderr;
                }
                else
                {
                    passed = verdict;
                    reason = explanation;
                }
            }
            results.Add(new CaseResult(scenario.Id, "Codex", model, revision,
                passed, reason, response, run.Stdout + "\n" + run.Stderr,
                judgeResponse));
            await File.WriteAllTextAsync(Path.Combine(outputRoot, "report.json"),
                JsonSerializer.Serialize(results, Options));
        }

        Assert.True(results.All(x => x.Passed),
            "Business complexity live evaluation failed. See " +
            Path.Combine(outputRoot, "report.json") + "\n" +
            string.Join("\n", results.Where(x => !x.Passed)
                .Select(x => x.Id + ": " + x.Reason)));
    }

    private static void Seed(string repository, string work, EstimateCase scenario)
    {
        var directory = Path.Combine(work, ".idd", "intent");
        Directory.CreateDirectory(directory);
        if (scenario.Source == "repository-current-intent")
        {
            var original = Path.Combine(repository, ".idd", "intent");
            foreach (var source in Directory.GetFiles(original, "*.md",
                         SearchOption.TopDirectoryOnly))
            {
                File.Copy(source, Path.Combine(directory, Path.GetFileName(source)));
            }
            return;
        }

        File.WriteAllText(Path.Combine(directory, "README.md"),
            "# Current Product Intent\n\nCurrent requirements are defined by the numbered documents.\n");
        var index = new List<string> { "# Current Intent", "" };
        foreach (var (name, content) in scenario.Documents)
        {
            if (!name.StartsWith("IDD-", StringComparison.Ordinal) ||
                Path.GetFileName(name) != name || !name.EndsWith(".md", StringComparison.Ordinal))
                throw new InvalidOperationException("Invalid fixture filename " + name);
            File.WriteAllText(Path.Combine(directory, name), content);
            index.Add("- " + name);
        }
        foreach (var missing in scenario.AdditionalIndexIds ?? [])
            index.Add("- " + missing + ".spec-missing.md");
        File.WriteAllText(Path.Combine(directory, "INDEX.md"),
            string.Join("\n", index) + "\n");
        if (scenario.ExtraCode is not null)
            File.WriteAllText(Path.Combine(work, "Implementation.cs"), scenario.ExtraCode);
    }

    private static string[] Snapshot(string directory) =>
        Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/') +
                ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))
            .ToArray();

    private static bool TryVerdict(string response, out bool passed, out string reason)
    {
        passed = false;
        reason = "";
        try
        {
            var start = response.IndexOf('{');
            var end = response.LastIndexOf('}');
            if (start < 0 || end <= start) return false;
            using var document = JsonDocument.Parse(response.Substring(start, end - start + 1));
            var root = document.RootElement;
            if (!root.TryGetProperty("pass", out var verdict) ||
                verdict.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return false;
            passed = verdict.GetBoolean();
            reason = root.TryGetProperty("reason", out var r) &&
                r.ValueKind == JsonValueKind.String ? r.GetString() ?? "" : "";
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static async Task<string> InstallPlugin(string repo, string root,
        string codex, TimeSpan timeout)
    {
        var home = Path.Combine(root, "codex-home");
        var marketplace = Path.Combine(root, "marketplace");
        Directory.CreateDirectory(home);
        var currentHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(currentHome))
            currentHome = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile), ".codex");
        var auth = Path.Combine(currentHome, "auth.json");
        if (File.Exists(auth)) File.Copy(auth, Path.Combine(home, "auth.json"), true);

        await Ensure("dotnet", ["build", "tools/generate/Generate.csproj", "--nologo"],
            repo, null, timeout);
        var generator = Path.Combine(repo, "tools", "generate", "bin", "Debug",
            "net10.0", "Generate.dll");
        await Ensure("dotnet", ["exec", generator, "--version",
            "0.0.0-estimate-eval", "--output", marketplace], repo, null, timeout);
        var environment = new Dictionary<string, string?> { ["CODEX_HOME"] = home };
        await Ensure(codex, ["plugin", "marketplace", "add", marketplace, "--json"],
            repo, environment, timeout);
        await Ensure(codex, ["plugin", "add", "idd-intent@intent-driven-development",
            "--json"], repo, environment, timeout);
        return home;
    }

    private static async Task<string> EnsureRevision(string repo)
    {
        var revision = await Run("git", ["rev-parse", "HEAD"], repo, null,
            null, TimeSpan.FromMinutes(1));
        return revision.ExitCode == 0 ? revision.Stdout.Trim() : "unknown";
    }

    private static async Task Ensure(string program, IReadOnlyList<string> arguments,
        string cwd, IReadOnlyDictionary<string, string?>? environment, TimeSpan timeout)
    {
        var result = await Run(program, arguments, cwd, environment, null, timeout);
        if (result.ExitCode != 0 || result.Error is not null)
            throw new InvalidOperationException(program + " " +
                string.Join(" ", arguments) + "\n" + result.Error +
                "\n" + result.Stderr + "\n" + result.Stdout);
    }

    private static async Task<ProcessResult> Run(string program, IReadOnlyList<string> arguments,
        string cwd, IReadOnlyDictionary<string, string?>? environment,
        string? stdin, TimeSpan timeout)
    {
        Process? process = null;
        Task<string>? output = null, errors = null;
        try
        {
            var start = new ProcessStartInfo(program)
            {
                WorkingDirectory = cwd,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdin is not null
            };
            foreach (var arg in arguments) start.ArgumentList.Add(arg);
            if (environment is not null)
                foreach (var (name, value) in environment) start.Environment[name] = value;
            process = Process.Start(start) ??
                throw new InvalidOperationException("Unable to start " + program);
            output = process.StandardOutput.ReadToEndAsync();
            errors = process.StandardError.ReadToEndAsync();
            if (stdin is not null)
            {
                await process.StandardInput.WriteAsync(stdin);
                process.StandardInput.Close();
            }
            using var cancel = new CancellationTokenSource(timeout);
            await process.WaitForExitAsync(cancel.Token);
            return new ProcessResult(process.ExitCode, await output, await errors, null);
        }
        catch (Exception error)
        {
            if (process is { HasExited: false })
                try { process.Kill(entireProcessTree: true); } catch { }
            return new ProcessResult(process is { HasExited: true } ?
                process.ExitCode : null,
                output is { IsCompletedSuccessfully: true } ? await output : "",
                errors is { IsCompletedSuccessfully: true } ? await errors : "",
                error.ToString());
        }
        finally { process?.Dispose(); }
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName,
                "Intent-Driven-Development.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Cannot find IDD repository");
    }
}
