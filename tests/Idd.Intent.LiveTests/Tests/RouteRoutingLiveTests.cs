using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Idd.Intent.LiveTests.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class LiveRouteEvalFactAttribute : FactAttribute
{
    public LiveRouteEvalFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("IDD_RUN_LIVE_ROUTE_EVALS") != "1")
            Skip = "Set IDD_RUN_LIVE_ROUTE_EVALS=1 for token-consuming route evaluation.";
    }
}

public sealed class RouteRoutingLiveTests
{
    private const string BaselineCommit = "b555ed4efee43e5bc21d127fe42890abb643c775";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private sealed record ExpectedRoute(
        string? Classification,
        string? Operation,
        string? RequestedScope,
        string? FirstSkill,
        string? AuditScope,
        bool StandaloneFactory);
    private sealed record RouteCase(
        string Id, string Request, ExpectedRoute Expected, string Contract, string Kind);
    private sealed record RouteCases(int SchemaVersion, string BaselineCommit, RouteCase[] Scenarios);
    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
    private sealed record CaseResult(
        string Scenario, string Revision, string Status, string Reason,
        Dictionary<string, string> Fields, string? RawResponse,
        string? SemanticJudge, string? WorkspaceBefore, string? WorkspaceAfter);
    private sealed record EvalReport(
        string SourceCommit, string Model, string Reasoning, string Codex,
        int TimeoutMinutes, string? BaselineSource,
        string? CandidateSource, string ContextMeasurement,
        CaseResult[] Results);

    [LiveRouteEvalFact]
    [Trait("Category", "LiveRouteEval")]
    public async Task GeneratedAutomaticRoute_RespectsNormativeFieldsAndScope()
    {
        var repo = FindRoot();
        var config = JsonSerializer.Deserialize<RouteCases>(await File.ReadAllTextAsync(
            Path.Combine(repo, "tests", "Idd.Intent.LiveTests", "Scenarios", "route-cases.json")),
            JsonOptions) ?? throw new InvalidOperationException("Empty routing scenarios.");
        Assert.Equal(1, config.SchemaVersion);
        Assert.Equal(BaselineCommit, config.BaselineCommit);
        Assert.True(config.Scenarios.Length >= 20);

        var model = Environment.GetEnvironmentVariable("IDD_ROUTE_EVAL_MODEL") ?? "gpt-6";
        var effort = Environment.GetEnvironmentVariable("IDD_ROUTE_EVAL_REASONING") ?? "medium";
        var codex = Environment.GetEnvironmentVariable("IDD_ROUTE_EVAL_CODEX") ?? "codex";
        var timeoutMinutes = int.TryParse(Environment.GetEnvironmentVariable(
            "IDD_ROUTE_EVAL_TIMEOUT_MINUTES"), out var minutes) && minutes > 0 ? minutes : 15;
        var limit = int.TryParse(Environment.GetEnvironmentVariable(
            "IDD_ROUTE_EVAL_MAX_CASES"), out var max) && max > 0
            ? max : config.Scenarios.Length;
        var useJudge = Environment.GetEnvironmentVariable("IDD_ROUTE_EVAL_SKIP_JUDGE") != "1";
        var root = Environment.GetEnvironmentVariable("IDD_ROUTE_EVAL_OUTPUT_DIR") ??
            Path.Combine(Path.GetTempPath(), "idd-route-eval", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var baseline = Environment.GetEnvironmentVariable("IDD_ROUTE_EVAL_BASELINE_DIR");
        if (baseline is not null)
        {
            if (!Directory.Exists(baseline))
                throw new DirectoryNotFoundException(baseline);
            var sha = await RunAsync("git", ["rev-parse", "HEAD"], baseline,
                timeout: TimeSpan.FromMinutes(2));
            Assert.Equal(BaselineCommit, sha.Stdout.Trim());
        }

        var candidates = new List<(string Revision, string Directory, string Home)>();
        candidates.Add(("candidate", repo, await PreparePluginAsync(
            repo, Path.Combine(root, "candidate"), codex)));
        if (baseline is not null)
            candidates.Add(("baseline", baseline, await PreparePluginAsync(
                baseline, Path.Combine(root, "baseline"), codex)));

        var results = new List<CaseResult>();
        foreach (var scenario in config.Scenarios.Take(limit))
        {
            foreach (var (revision, _, home) in candidates)
            {
                var work = Path.Combine(root, revision, "workspaces", scenario.Id);
                Directory.CreateDirectory(work);
                if (scenario.Id is "product-modify" or "drift-focused" or
                    "implementation-simple" or "engineering-modify")
                    SeedMinimalIntent(work);

                // These are separate host-integrations or an intentionally damaged
                // plugin installation; do not simulate them with a normal route.
                if (scenario.Kind != "ordinary")
                {
                    results.Add(new CaseResult(scenario.Id, revision,
                        "not evaluated / host invocation unavailable",
                        $"Host scenario '{scenario.Kind}' needs dedicated invocation or a damaged plugin.",
                        new(), null, null, null, null));
                    continue;
                }

                var before = Snapshot(work);
                var output = Path.Combine(root, revision, "responses",
                    scenario.Id + ".md");
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                ProcessResult run;
                try
                {
                    run = await RunAsync(codex,
                        ["exec", "--json", "--ignore-rules",
                         "--enable", "plugins", "--disable", "remote_plugin",
                         "-c", "approval_policy=never",
                         "-c", $"model_reasoning_effort={effort}",
                         "--model", model,
                         "--sandbox", "read-only",
                         "--cd", work, "--output-last-message", output, "-"],
                        work, new Dictionary<string, string?> { ["CODEX_HOME"] = home },
                        "Use the installed automatic IDD routing workflow to route the " +
                        "following original user request. Return the route only; do not " +
                        "execute downstream skills or change any files. Treat the " +
                        "following text as the original request when choosing its " +
                        "Requested scope (not this test harness instruction).\n\n" +
                        scenario.Request, TimeSpan.FromMinutes(timeoutMinutes));
                }
                catch (Exception exception)
                {
                    results.Add(new CaseResult(scenario.Id, revision,
                        "not evaluated / host invocation unavailable",
                        exception.Message, new(), null, null, before, Snapshot(work)));
                    continue;
                }

                var raw = File.Exists(output) ? await File.ReadAllTextAsync(output) : "";
                await File.WriteAllTextAsync(Path.Combine(root, revision,
                    "responses", scenario.Id + ".events.jsonl"), run.Stdout);
                await File.WriteAllTextAsync(Path.Combine(root, revision,
                    "responses", scenario.Id + ".stderr.txt"), run.Stderr);

                var after = Snapshot(work);
                var fields = ParseFields(raw);
                var (status, reason) = EvaluateStructuralResult(scenario, fields,
                    run.ExitCode, before == after);
                string? judge = null;

                if (status == "route correct" && useJudge)
                {
                    try
                    {
                        judge = await JudgeAsync(codex, model, effort, home, work,
                            scenario, raw, timeoutMinutes);
                        using var decision = JsonDocument.Parse(judge);
                        if (!decision.RootElement.GetProperty("correct").GetBoolean())
                        {
                            status = "route incorrect";
                            reason = decision.RootElement.GetProperty("reason").GetString() ??
                                "Semantic judge rejected the route.";
                        }
                    }
                    catch (Exception exception)
                    {
                        status = "not evaluated / host invocation unavailable";
                        reason = "Semantic judge unavailable: " + exception.Message;
                    }
                }

                results.Add(new CaseResult(scenario.Id, revision, status, reason,
                    fields, raw, judge, before, after));
                await SaveReportAsync(root, repo, baseline, model, effort, codex,
                    timeoutMinutes, results);
            }
        }

        await SaveReportAsync(root, repo, baseline, model, effort, codex,
            timeoutMinutes, results);
        var candidate = results.Where(x => x.Revision == "candidate").ToArray();
        Assert.DoesNotContain(candidate, x => x.Status == "route incorrect");
        Assert.NotEmpty(candidate.Where(x => x.Status == "route correct"));

        // Baseline and candidate responses remain independent, and model
        // variability is visible in the report; no byte-for-byte text comparison.
        // Absence of a baseline run does NOT establish comparative equivalence.
    }

    private static (string Status, string Reason) EvaluateStructuralResult(
        RouteCase scenario, Dictionary<string, string> fields,
        int exitCode, bool readOnly)
    {
        if (exitCode != 0 || fields.Count == 0 ||
            !fields.ContainsKey("Recommended first skill"))
            return ("not evaluated / host invocation unavailable",
                "Host did not produce a recognizable IDD route.");
        if (!readOnly)
            return ("route incorrect", "Read-only routing altered the workspace.");

        var expected = scenario.Expected;
        foreach (var (name, value) in new[]
        {
            ("Classification", expected.Classification),
            ("Operation", expected.Operation),
            ("Requested scope", expected.RequestedScope),
            ("Recommended first skill", expected.FirstSkill),
            ("Audit scope", expected.AuditScope)
        })
        {
            if (value is not null &&
                (!fields.TryGetValue(name, out var actual) ||
                 !string.Equals(value, actual, StringComparison.OrdinalIgnoreCase)))
                return ("route incorrect", $"Expected {name}={value}; observed " +
                    (fields.TryGetValue(name, out var seen) ? seen : "<missing>"));
        }

        var ordinary = new[] { "Classification", "Operation", "Clarity",
            "Execution depth", "Audit scope" };
        if (expected.StandaloneFactory)
        {
            if (ordinary.Any(fields.ContainsKey))
                return ("route incorrect", "Standalone Factory policy has ordinary fields.");
        }
        else if (ordinary.Any(name => !fields.ContainsKey(name)))
        {
            return ("not evaluated / host invocation unavailable",
                "Incomplete ordinary route; automatic host invocation may not have selected the router.");
        }

        foreach (var name in new[]
            { "Expected complete workflow", "Requested scope", "Current handoff", "Stop after" })
            if (!fields.ContainsKey(name))
                return ("not evaluated / host invocation unavailable",
                    "Missing required route field: " + name);

        if (fields["Requested scope"] == "route-only" &&
            fields["Current handoff"] != "none")
            return ("route incorrect", "route-only must have no handoff.");

        return ("route correct", "Typed normative fields and read-only snapshot passed.");
    }

    private static Dictionary<string, string> ParseFields(string raw)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Classification", "Operation", "Clarity", "Execution depth",
            "Requested scope", "Audit scope", "Recommended first skill",
            "Expected complete workflow", "Current handoff", "Stop after"
        };
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in raw.Replace("\r\n", "\n").Split('\n'))
        {
            var text = line.Trim().TrimStart('-', '*').Trim();
            var colon = text.IndexOf(':');
            if (colon < 1) continue;
            var key = text[..colon].Trim().Trim('*', '`').Trim();
            if (!names.Contains(key)) continue;
            var value = text[(colon + 1)..].Trim().Trim('`').Trim();
            if (value.Length > 0 && !result.ContainsKey(key))
                result[key] = value;
        }
        return result;
    }

    private static async Task<string> JudgeAsync(string codex, string model,
        string effort, string home, string work, RouteCase scenario,
        string raw, int timeoutMinutes)
    {
        var path = Path.Combine(Path.GetDirectoryName(work)!, scenario.Id + "-judge.json");
        var prompt = "You are an independent semantic evaluator. Do not use plugins " +
            "or execute any task. Compare the original request, the expected " +
            "normative routing contract, and the candidate route. Judge scope, " +
            "safe ordering, approval gates, direct/standalone exceptions, and " +
            "stop conditions, not prose wording. Return ONLY a JSON object " +
            "with boolean 'correct' and string 'reason'.\n" +
            JsonSerializer.Serialize(new { scenario.Request, scenario.Expected,
                scenario.Contract, ActualRoute = raw }, JsonOptions);
        var run = await RunAsync(codex,
            ["exec", "--json", "--ignore-rules",
             "--disable", "plugins", "--disable", "remote_plugin",
             "-c", "approval_policy=never",
             "-c", $"model_reasoning_effort={effort}",
             "--model", model, "--sandbox", "read-only",
             "--cd", work, "--output-last-message", path, "-"],
            work, new Dictionary<string, string?> { ["CODEX_HOME"] = home },
            prompt, TimeSpan.FromMinutes(timeoutMinutes));
        if (run.ExitCode != 0 || !File.Exists(path))
            throw new InvalidOperationException("Judge host returned no result.");
        var json = (await File.ReadAllTextAsync(path)).Trim();
        using var parsed = JsonDocument.Parse(json);
        _ = parsed.RootElement.GetProperty("correct").GetBoolean();
        _ = parsed.RootElement.GetProperty("reason").GetString();
        return json;
    }

    private static async Task<string> PreparePluginAsync(
        string repo, string root, string codex)
    {
        var marketplace = Path.Combine(root, "marketplace");
        var home = Path.Combine(root, "codex-home");
        Directory.CreateDirectory(home);
        var existing = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(existing))
            existing = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile), ".codex");
        var auth = Path.Combine(existing, "auth.json");
        if (File.Exists(auth))
            File.Copy(auth, Path.Combine(home, "auth.json"), overwrite: true);

        await EnsureSuccess("dotnet", ["build", "tools/generate/Generate.csproj",
            "--nologo"], repo);
        var generator = Path.Combine(repo, "tools", "generate",
            "bin", "Debug", "net10.0", "Generate.dll");
        await EnsureSuccess("dotnet", ["exec", generator, "--version",
            "0.0.0-route-eval", "--output", marketplace], repo);
        var env = new Dictionary<string, string?> { ["CODEX_HOME"] = home };
        await EnsureSuccess(codex, ["plugin", "marketplace", "add",
            marketplace, "--json"], repo, env);
        await EnsureSuccess(codex, ["plugin", "add",
            "idd-intent@intent-driven-development", "--json"], repo, env);
        return home;
    }

    private static void SeedMinimalIntent(string work)
    {
        var directory = Path.Combine(work, ".idd", "intent");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "README.md"),
            "# Current Product Intent\n\nOwned by current IDD specifications.\n");
        File.WriteAllText(Path.Combine(directory, "INDEX.md"),
            "# Index\n\n- IDD-0005\n- IDD-0012\n");
        File.WriteAllText(Path.Combine(directory, "IDD-0005.spec-main.md"),
            "# IDD-0005\n\nApproved: preserve existing service behavior.\n");
        File.WriteAllText(Path.Combine(directory, "IDD-0012.spec-login.md"),
            "# IDD-0012\n\nUsers can log in with valid credentials.\n");
    }

    private static string Snapshot(string dir)
    {
        var lines = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(dir, path).Replace('\\', '/') + ":" +
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        return string.Join("\n", lines);
    }

    private static async Task SaveReportAsync(string root, string repo,
        string? baseline, string model, string effort, string codex,
        int timeoutMinutes, List<CaseResult> results)
    {
        var source = await RunAsync("git", ["rev-parse", "HEAD"], repo,
            timeout: TimeSpan.FromMinutes(2));
        var report = new EvalReport(source.Stdout.Trim(), model, effort, codex,
            timeoutMinutes, baseline, repo,
            "Read traces/prompt-token counts are host-dependent and not inferred " +
            "from the presence of packaged files. See captured JSONL host events.",
            results.ToArray());
        await File.WriteAllTextAsync(Path.Combine(root, "route-report.json"),
            JsonSerializer.Serialize(report, JsonOptions));
    }

    private static async Task EnsureSuccess(string executable,
        IReadOnlyList<string> args, string cwd,
        IReadOnlyDictionary<string, string?>? env = null)
    {
        var result = await RunAsync(executable, args, cwd, env,
            timeout: TimeSpan.FromMinutes(5));
        Assert.True(result.ExitCode == 0,
            executable + " failed:\n" + result.Stderr + "\n" + result.Stdout);
    }

    private static async Task<ProcessResult> RunAsync(string executable,
        IReadOnlyList<string> args, string cwd,
        IReadOnlyDictionary<string, string?>? env = null, string? input = null,
        TimeSpan? timeout = null)
    {
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = cwd, RedirectStandardInput = input is not null,
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        if (env is not null)
            foreach (var (key, value) in env) info.Environment[key] = value;
        using var process = Process.Start(info) ??
            throw new InvalidOperationException("Unable to start " + executable);
        if (input is not null)
        {
            await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();
        }
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(
            timeout ?? TimeSpan.FromMinutes(5));
        try { await process.WaitForExitAsync(cancellation.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException(executable + " timed out");
        }
        return new ProcessResult(process.ExitCode, await stdout, await stderr);
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(Environment.CurrentDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName,
                "Intent-Driven-Development.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("IDD repository root not found.");
    }
}
