using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Idd.Intent.LiveTests.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class LiveCosmicEvalFactAttribute : FactAttribute
{
    public LiveCosmicEvalFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("IDD_RUN_LIVE_COSMIC_EVALS") != "1")
            Skip = "Set IDD_RUN_LIVE_COSMIC_EVALS=1 to run token-consuming COSMIC live evaluations.";
    }
}

public sealed class CosmicFunctionalSizeLiveTests
{
    private sealed record CosmicCase(string Id, string Request, Dictionary<string, string> Documents,
        string ExpectedStatus, string[] ExpectedProcesses, string[] ExpectedMovements,
        string[] ExpectedRestrictions, string[] AdditionalIndexIds, string? Source);
    private sealed record Corpus(int SchemaVersion, CosmicCase[] Scenarios);
    private sealed record RunResult(int? Code, string Output, string Error, string? Failure);
    private sealed record CaseResult(string Id, string Host, string Status, string Explanation,
        string Response, string Judge, bool WorkspaceUnchanged);
    private static readonly JsonSerializerOptions Json = new()
    { PropertyNameCaseInsensitive = true, WriteIndented = true };

    [LiveCosmicEvalFact]
    [Trait("Category", "LiveCosmicEval")]
    public Task Codex_InstalledPlugin_MeasuresFurWithoutWorkspaceWrites() => Evaluate("codex");

    [LiveCosmicEvalFact]
    [Trait("Category", "LiveCosmicEval")]
    public Task Claude_InstalledPlugin_MeasuresFurWithoutWorkspaceWrites() => Evaluate("claude");

    private static async Task Evaluate(string host)
    {
        var root = FindRoot();
        var corpus = JsonSerializer.Deserialize<Corpus>(
            await File.ReadAllTextAsync(Path.Combine(root, "tests", "Idd.Intent.LiveTests",
                "Scenarios", "cosmic-cases.json")), Json)
            ?? throw new InvalidOperationException("Missing COSMIC scenarios");
        Assert.Equal(1, corpus.SchemaVersion);
        Assert.True(corpus.Scenarios.Length >= 20);
        Assert.Equal(corpus.Scenarios.Length,
            corpus.Scenarios.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count());

        var cases = corpus.Scenarios;
        var ids = Environment.GetEnvironmentVariable("IDD_COSMIC_EVAL_CASE_IDS");
        if (!string.IsNullOrWhiteSpace(ids))
        {
            var lookup = cases.ToDictionary(x => x.Id, StringComparer.Ordinal);
            cases = ids.Split(',', StringSplitOptions.TrimEntries |
                StringSplitOptions.RemoveEmptyEntries).Select(id => lookup[id]).ToArray();
        }
        else if (int.TryParse(Environment.GetEnvironmentVariable("IDD_COSMIC_EVAL_MAX_CASES"),
            out var maximum) && maximum > 0)
            cases = cases.Take(maximum).ToArray();

        var agent = Environment.GetEnvironmentVariable(host == "codex"
            ? "IDD_COSMIC_EVAL_CODEX" : "IDD_COSMIC_EVAL_CLAUDE") ?? host;
        var model = Environment.GetEnvironmentVariable("IDD_COSMIC_EVAL_MODEL") ?? "gpt-6";
        var minutes = int.TryParse(Environment.GetEnvironmentVariable("IDD_COSMIC_EVAL_TIMEOUT_MINUTES"),
            out var selected) && selected > 0 ? selected : 15;
        var timeout = TimeSpan.FromMinutes(minutes);
        var outputRoot = Environment.GetEnvironmentVariable("IDD_COSMIC_EVAL_OUTPUT_DIR") ??
            Path.Combine(Path.GetTempPath(), "idd-cosmic-live", Guid.NewGuid().ToString("N"));
        var hostRoot = Path.Combine(outputRoot, host);
        Directory.CreateDirectory(hostRoot);
        var installed = await Install(root, hostRoot, agent, host, timeout);
        var results = new List<CaseResult>();
        foreach (var scenario in cases)
        {
            var work = Path.Combine(hostRoot, "workspaces", scenario.Id);
            Directory.CreateDirectory(work);
            Seed(root, work, scenario);
            var before = Snapshot(work);
            var reply = Path.Combine(hostRoot, scenario.Id + "-response.md");
            var prompt = scenario.Id is "C11-business-only" or "C13-cost"
                ? scenario.Request
                : "Use the installed idd-intent-cosmic skill directly, read current " +
                  "FUR, and carry out this read-only measurement using isolated readers " +
                  "and bounded hierarchical reducers. Do not write any target-project " +
                  "file. Session scratch Markdown evidence/ledger packets are allowed " +
                  "only outside the target workspace. Do not execute project code, " +
                  "scripts or validators, or create intermediate JSON models. " +
                  "Do not invent CFP or claim independent mechanical validation:\n\n" + scenario.Request;
            var invoke = await Invoke(agent, host, model, installed, work, reply, prompt, timeout);
            var answer = File.Exists(reply) ? await File.ReadAllTextAsync(reply) :
                host == "claude" ? invoke.Output : "";
            var unchanged = before.SequenceEqual(Snapshot(work));
            var status = "pass";
            var reason = "";
            var judge = "";
            if (invoke.Failure is not null || invoke.Code != 0 || string.IsNullOrWhiteSpace(answer))
            {
                status = "host-unavailable";
                reason = invoke.Failure ?? ("Agent exited with " + invoke.Code +
                    ": " + invoke.Error);
            }
            else if (!unchanged)
            {
                status = "semantic-failure";
                reason = "The agent modified the target workspace";
            }
            else
            {
                var restrictions = string.Join("\n",
                    scenario.ExpectedRestrictions.Select(x => "- " + x));
                var expectation = "Expected coverage/status: " + scenario.ExpectedStatus +
                    "\nExpected functional process set (semantic names, not arbitrary IDs): " +
                    string.Join(", ", scenario.ExpectedProcesses) +
                    "\nExpected FUR-backed movement identities (normalize E/X/R/W, " +
                    "data group and functional user): " +
                    string.Join(", ", scenario.ExpectedMovements) +
                    "\nOther requirements:\n" + restrictions;
                var judgePrompt = "You are evaluating a COSMIC v5.0 response. Do not perform " +
                    "the user's task. Compare the COMPLETE normalized process inventory " +
                    "and per-process movement sets against the explicit scenario FUR and " +
                    "expected semantic facts, not just overall CFP. Every process has " +
                    "one normative owner; a connector LINK from another spec is not a " +
                    "second process or new movement. Do not perform semantic process " +
                    "deduplication across owners. Reject invented/duplicate owner IDs, " +
                    "inflated CFP from coupling, missing sources, wrong boundaries, " +
                    "claims of independent COSMIC certification, workspace writes, incorrect " +
                    "full CFP, or a numerical claim inconsistent with the required movements. " +
                    "Some negative cases correctly report diagnostic only. " +
                    "Reply ONLY with JSON {\"pass\":boolean,\"reason\":string," +
                    "\"processes\":string[],\"movements\":string[]}.\n\n" +
                    "Request:\n" + scenario.Request +
                    "\nFUR:\n" + JsonSerializer.Serialize(scenario.Documents, Json) +
                    "\nExpected:\n" + expectation + "\nResponse:\n" + answer;
                var judgedPath = Path.Combine(hostRoot, scenario.Id + "-judge.json");
                var judged = await Invoke(agent, host, model, installed, work,
                    judgedPath, judgePrompt, timeout, judgeMode: true);
                judge = File.Exists(judgedPath) ? await File.ReadAllTextAsync(judgedPath) :
                    host == "claude" ? judged.Output : "";
                if (judged.Failure is not null || judged.Code != 0)
                {
                    status = "host-unavailable";
                    reason = "Judge unavailable: " + judged.Failure + " " + judged.Error;
                }
                else if (!ReadVerdict(judge, out var passed, out reason) || !passed)
                    status = "semantic-failure";
            }
            results.Add(new CaseResult(scenario.Id, host, status, reason, answer, judge,
                unchanged));
            await File.WriteAllTextAsync(Path.Combine(hostRoot, "report.json"),
                JsonSerializer.Serialize(results, Json));
        }

        var failures = results.Where(x => x.Status == "semantic-failure").ToArray();
        var missing = results.Where(x => x.Status == "host-unavailable").ToArray();
        Assert.True(failures.Length == 0,
            "COSMIC semantic failures (see report.json): " +
            string.Join("\n", failures.Select(x => x.Id + ": " + x.Explanation)));
        Assert.True(missing.Length == 0,
            "COSMIC host unavailable, not a model semantic pass/fail (see report.json): " +
            string.Join("\n", missing.Select(x => x.Id + ": " + x.Explanation)));
    }

    private static bool ReadVerdict(string data, out bool pass, out string reason)
    {
        pass = false; reason = "Malformed semantic judge output";
        try
        {
            var start = data.IndexOf('{');
            var end = data.LastIndexOf('}');
            if (start < 0 || end < start) return false;
            using var json = JsonDocument.Parse(data[start..(end + 1)]);
            var root = json.RootElement;
            if (!root.TryGetProperty("pass", out var value) ||
                value.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !root.TryGetProperty("processes", out var processes) ||
                !root.TryGetProperty("movements", out var movements) ||
                processes.ValueKind != JsonValueKind.Array ||
                movements.ValueKind != JsonValueKind.Array)
                return false;
            pass = value.GetBoolean();
            reason = root.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static void Seed(string repo, string work, CosmicCase scenario)
    {
        var dest = Path.Combine(work, ".idd", "intent");
        Directory.CreateDirectory(dest);
        if (scenario.Source == "repository-current-intent")
        {
            foreach (var file in Directory.GetFiles(Path.Combine(repo, ".idd", "intent"), "*.md"))
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)));
            return;
        }
        File.WriteAllText(Path.Combine(dest, "README.md"), "# Current Product Intent\n");
        var index = new List<string> { "# Current Intent\n" };
        foreach (var (name, document) in scenario.Documents)
        {
            if (name != Path.GetFileName(name) || !name.StartsWith("IDD-", StringComparison.Ordinal) ||
                !name.EndsWith(".md", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe fixture path " + name);
            File.WriteAllText(Path.Combine(dest, name), document);
            index.Add("- " + name);
        }
        foreach (var missing in scenario.AdditionalIndexIds)
            index.Add("- " + missing + ".spec-unavailable.md");
        File.WriteAllText(Path.Combine(dest, "INDEX.md"), string.Join("\n", index) + "\n");
    }

    private static string[] Snapshot(string path) => Directory.GetFiles(path, "*",
            SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal)
        .Select(file => Path.GetRelativePath(path, file).Replace('\\', '/') + ":" +
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))).ToArray();

    private static async Task<string> Install(string root, string output, string agent,
        string host, TimeSpan timeout)
    {
        var marketplace = Path.Combine(output, "marketplace");
        await Ensure("dotnet", ["build", "tools/generate/Generate.csproj", "--nologo"],
            root, null, timeout);
        var dll = Path.Combine(root, "tools", "generate", "bin", "Debug",
            "net10.0", "Generate.dll");
        await Ensure("dotnet", ["exec", dll, "--version", "0.0.0-cosmic-eval",
            "--output", marketplace], root, null, timeout);
        if (host == "claude")
        {
            var plugin = Path.Combine(marketplace, "plugins", "claude", "idd-intent");
            if (!File.Exists(Path.Combine(plugin, "skills", "idd-intent-cosmic", "SKILL.md")))
                throw new InvalidOperationException("Claude skill not packaged");
            return plugin;
        }
        var home = Path.Combine(output, "codex-home");
        Directory.CreateDirectory(home);
        var originalHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(originalHome))
            originalHome = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile), ".codex");
        var auth = Path.Combine(originalHome, "auth.json");
        if (File.Exists(auth)) File.Copy(auth, Path.Combine(home, "auth.json"));
        var environment = new Dictionary<string, string?> { ["CODEX_HOME"] = home };
        await Ensure(agent, ["plugin", "marketplace", "add", marketplace, "--json"],
            root, environment, timeout);
        await Ensure(agent, ["plugin", "add", "idd-intent@intent-driven-development", "--json"],
            root, environment, timeout);
        return home;
    }

    private static async Task<RunResult> Invoke(string agent, string host, string model,
        string installed, string cwd, string output, string prompt, TimeSpan timeout,
        bool judgeMode = false)
    {
        if (host == "claude")
        {
            return await Run(agent,
                ["-p", "--plugin-dir", installed, "--output-format", "text"],
                cwd, null, prompt, timeout);
        }
        return await Run(agent,
            ["exec", "--json", "--skip-git-repo-check", "--ignore-rules",
             "--enable", "plugins", "--disable", "remote_plugin",
             "-c", "approval_policy=never",
             "-c", "model_reasoning_effort=medium",
             "--model", model, "--sandbox", "workspace-write",
             "--cd", cwd, "--output-last-message", output, "-"],
            cwd, new Dictionary<string, string?> { ["CODEX_HOME"] = installed },
            prompt, timeout);
    }

    private static Task Ensure(string program, IReadOnlyList<string> args,
        string cwd, IReadOnlyDictionary<string, string?>? env, TimeSpan timeout) =>
        EnsureCore(program, args, cwd, env, timeout);

    private static async Task EnsureCore(string program, IReadOnlyList<string> args,
        string cwd, IReadOnlyDictionary<string, string?>? env, TimeSpan timeout)
    {
        var result = await Run(program, args, cwd, env, null, timeout);
        if (result.Code != 0 || result.Failure is not null)
            throw new InvalidOperationException(program + " " + string.Join(" ", args) +
                "\n" + result.Failure + "\n" + result.Error);
    }

    private static async Task<RunResult> Run(string executable, IReadOnlyList<string> args,
        string cwd, IReadOnlyDictionary<string, string?>? env, string? stdin,
        TimeSpan timeout)
    {
        Process? process = null;
        try
        {
            var start = new ProcessStartInfo(executable)
            {
                WorkingDirectory = cwd, RedirectStandardOutput = true,
                RedirectStandardError = true, RedirectStandardInput = stdin is not null,
                UseShellExecute = false, CreateNoWindow = true
            };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            if (env is not null)
                foreach (var (name, value) in env) start.Environment[name] = value;
            process = Process.Start(start) ??
                throw new InvalidOperationException("Cannot launch " + executable);
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (stdin is not null) { await process.StandardInput.WriteAsync(stdin); process.StandardInput.Close(); }
            using var cancel = new CancellationTokenSource(timeout);
            await process.WaitForExitAsync(cancel.Token);
            return new RunResult(process.ExitCode, await stdout, await stderr, null);
        }
        catch (Exception error)
        {
            if (process is { HasExited: false })
                try { process.Kill(entireProcessTree: true); } catch { }
            return new RunResult(process is { HasExited: true } ? process.ExitCode : null,
                "", "", error.Message);
        }
        finally { process?.Dispose(); }
    }

    private static string FindRoot()
    {
        var current = new DirectoryInfo(Environment.CurrentDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName,
                "Intent-Driven-Development.slnx"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Cannot find IDD repository");
    }
}
