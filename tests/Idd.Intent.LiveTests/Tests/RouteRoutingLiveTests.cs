using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit.Abstractions;
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
        bool StandaloneFactory,
        string? CurrentHandoff = null,
        string? Clarity = null,
        string? ExecutionDepth = null);
    private sealed record RouteCase(
        string Id, string Request, ExpectedRoute Expected, string Contract, string Kind);
    private sealed record RouteCases(int SchemaVersion, string BaselineCommit, RouteCase[] Scenarios);
    private sealed record ProcessResult(
        int? ExitCode, string Stdout, string Stderr, string? ExecutionError);
    private sealed record InstructionTrace(
        bool Available, bool RouteRead, bool CoreRead, string Summary);
    private sealed record CaseResult(
        string Scenario, string Revision, string Status, string Reason,
        int? ExitCode, string? ExecutionError, string[] ExecCommandErrors,
        string InstructionTrace,
        Dictionary<string, string> Fields, string? RawResponse,
        string? SemanticJudge, string? WorkspaceBefore, string? WorkspaceAfter);
    private sealed record EvalReport(
        string SourceCommit, string Model, string Reasoning, string Codex,
        string Sandbox, int TimeoutMinutes, string? BaselineSource,
        string? CandidateSource, string ContextMeasurement,
        CaseResult[] Results);

    private readonly ITestOutputHelper output;

    public RouteRoutingLiveTests(ITestOutputHelper output) => this.output = output;

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

        var requestedIds = Environment.GetEnvironmentVariable(
            "IDD_ROUTE_EVAL_CASE_IDS");
        RouteCase[] selectedScenarios;
        if (string.IsNullOrWhiteSpace(requestedIds))
        {
            selectedScenarios = config.Scenarios;
        }
        else
        {
            var ids = requestedIds.Split(',', StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);
            if (ids.Length == 0 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
                throw new InvalidOperationException(
                    "IDD_ROUTE_EVAL_CASE_IDS must contain unique comma-separated scenario IDs.");
            var byId = config.Scenarios.ToDictionary(x => x.Id, StringComparer.Ordinal);
            var unknownIds = ids.Where(id => !byId.ContainsKey(id)).ToArray();
            if (unknownIds.Length > 0)
                throw new InvalidOperationException("Unknown route scenario ID(s): " +
                    string.Join(", ", unknownIds));
            selectedScenarios = ids.Select(id => byId[id]).ToArray();
        }

        var model = Environment.GetEnvironmentVariable("IDD_ROUTE_EVAL_MODEL") ?? "gpt-6";
        var effort = Environment.GetEnvironmentVariable("IDD_ROUTE_EVAL_REASONING") ?? "medium";
        var sandbox = ResolveSandbox(Environment.GetEnvironmentVariable(
            "IDD_ROUTE_EVAL_SANDBOX"));
        var codex = Environment.GetEnvironmentVariable("IDD_ROUTE_EVAL_CODEX") ?? "codex";
        var timeoutMinutes = int.TryParse(Environment.GetEnvironmentVariable(
            "IDD_ROUTE_EVAL_TIMEOUT_MINUTES"), out var minutes) && minutes > 0 ? minutes : 15;
        var limit = int.TryParse(Environment.GetEnvironmentVariable(
            "IDD_ROUTE_EVAL_MAX_CASES"), out var max) && max > 0
            ? max : config.Scenarios.Length;
        if (!string.IsNullOrWhiteSpace(requestedIds) && limit < selectedScenarios.Length)
            throw new InvalidOperationException(
                "IDD_ROUTE_EVAL_MAX_CASES is smaller than the explicitly selected scenario count.");
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
            Assert.Equal(0, sha.ExitCode);
            Assert.Equal(BaselineCommit, sha.Stdout.Trim());
        }

        var candidates = new List<(string Revision, string Directory, string Home)>();
        candidates.Add(("candidate", repo, await PreparePluginAsync(
            repo, Path.Combine(root, "candidate"), codex)));
        if (baseline is not null)
            candidates.Add(("baseline", baseline, await PreparePluginAsync(
                baseline, Path.Combine(root, "baseline"), codex)));

        var results = new List<CaseResult>();
        foreach (var scenario in selectedScenarios.Take(limit))
        {
            foreach (var (revision, _, home) in candidates)
            {
                var work = Path.Combine(root, revision, "workspaces", scenario.Id);
                Directory.CreateDirectory(work);
                if (scenario.Id is "product-modify" or "drift-focused" or
                    "implementation-simple" or "engineering-modify")
                    SeedMinimalIntent(work);
                if (scenario.Id == "import-present")
                    await File.WriteAllTextAsync(Path.Combine(work,
                        "approved-requirements.md"),
                        "# Approved requirements\n\nThe report must be exportable as CSV.\n");

                // These are separate host-integrations or an intentionally damaged
                // plugin installation; do not simulate them with a normal route.
                if (scenario.Kind != "ordinary")
                {
                    results.Add(new CaseResult(scenario.Id, revision,
                        "not evaluated / host invocation unavailable",
                        $"Host scenario '{scenario.Kind}' needs dedicated invocation or a damaged plugin.",
                        null, null, [], "not available", new(), null, null, null, null));
                    continue;
                }

                var before = Snapshot(work);
                var responsePath = Path.Combine(root, revision, "responses",
                    scenario.Id + ".md");
                Directory.CreateDirectory(Path.GetDirectoryName(responsePath)!);
                var run = await RunAsync(codex,
                    ["exec", "--json", "--skip-git-repo-check", "--ignore-rules",
                     "--enable", "plugins", "--disable", "remote_plugin",
                     "-c", "approval_policy=never",
                     "-c", $"model_reasoning_effort={effort}",
                     "--model", model,
                     "--sandbox", sandbox,
                     "--cd", work, "--output-last-message", responsePath, "-"],
                    work, new Dictionary<string, string?> { ["CODEX_HOME"] = home },
                    "Use the installed automatic IDD routing workflow to classify " +
                    "the following original user request. Output only its normative " +
                    "route format; do not invoke the recommended skill or change files. " +
                    "Choose Requested scope solely from the original request below. " +
                    "The instruction to output only a route is a formatting constraint, " +
                    "not a request for `route-only`; choose `route-only` only when the " +
                    "original user requests a routing recommendation without a handoff.\n\n" +
                    scenario.Request, TimeSpan.FromMinutes(timeoutMinutes));

                var raw = File.Exists(responsePath)
                    ? await File.ReadAllTextAsync(responsePath) : "";
                var responseDirectory = Path.Combine(root, revision, "responses");
                await File.WriteAllTextAsync(Path.Combine(responseDirectory,
                    scenario.Id + ".events.jsonl"), run.Stdout);
                await File.WriteAllTextAsync(Path.Combine(responseDirectory,
                    scenario.Id + ".stderr.txt"), run.Stderr);
                if (run.ExecutionError is not null)
                    await File.WriteAllTextAsync(Path.Combine(responseDirectory,
                        scenario.Id + ".execution-error.txt"), run.ExecutionError);

                var after = Snapshot(work);
                var fields = ParseFields(raw);
                var trace = InspectInstructionTrace(run.Stdout, run.Stderr);
                var execCommandErrors = ExtractExecCommandErrors(run.Stdout, run.Stderr);
                var (status, reason) = EvaluateStructuralResult(scenario, fields,
                    run.ExitCode, before == after, raw, trace, run.ExecutionError,
                    execCommandErrors);
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
                    run.ExitCode, run.ExecutionError, execCommandErrors, trace.Summary,
                    fields, raw, judge, before, after));
                this.output.WriteLine(FormatCaseResult(results[^1]));
                await SaveReportAsync(root, repo, baseline, model, effort, codex,
                    sandbox, timeoutMinutes, results);
                if (status == "not evaluated / host invocation unavailable")
                    break;
            }
            if (results.Any(result => result.Scenario == scenario.Id &&
                result.Status == "not evaluated / host invocation unavailable"))
                break;
        }

        await SaveReportAsync(root, repo, baseline, model, effort, codex,
            sandbox, timeoutMinutes, results);
        var candidate = results.Where(x => x.Revision == "candidate").ToArray();
        Assert.DoesNotContain(candidate, x => x.Status == "route incorrect");
        Assert.True(candidate.Any(x => x.Status == "route correct"),
            "No complete normative route was obtained:\n" +
            string.Join("\n", candidate.Select(FormatCaseResult)) +
            $"\nReport: {Path.Combine(root, "route-report.json")}");

        // Baseline and candidate responses remain independent, and model
        // variability is visible in the report; no byte-for-byte text comparison.
        // Absence of a baseline run does NOT establish comparative equivalence.
    }

    private static (string Status, string Reason) EvaluateStructuralResult(
        RouteCase scenario, Dictionary<string, string> fields,
        int? exitCode, bool workspaceUnchanged, string raw,
        InstructionTrace trace, string? executionError,
        string[] execCommandErrors)
    {
        if (!workspaceUnchanged)
            return ("route incorrect", "Routing altered files in the test workspace.");

        if (!trace.RouteRead || !trace.CoreRead)
            return ("not evaluated / host invocation unavailable",
                "The installed idd-route invocation was not confirmed by captured reads " +
                "of both SKILL.md and references/routing-core.md: " + trace.Summary);

        if (exitCode != 0 || executionError is not null || fields.Count == 0 ||
            !fields.ContainsKey("Recommended first skill"))
            return ("route incorrect",
                DescribeMissingRoute(exitCode, executionError, execCommandErrors, raw));

        if (!Regex.IsMatch(raw, @"(?m)^\s*#\s+IDD Route\s*$"))
            return ("route incorrect",
                "The response did not contain the required '# IDD Route' heading.");

        var expected = scenario.Expected;
        foreach (var (name, value) in new[]
        {
            ("Classification", expected.Classification),
            ("Operation", expected.Operation),
            ("Requested scope", expected.RequestedScope),
            ("Clarity", expected.Clarity),
            ("Execution depth", expected.ExecutionDepth),
            ("Recommended first skill", expected.FirstSkill),
            ("Audit scope", expected.AuditScope),
            ("Current handoff", expected.CurrentHandoff)
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
            return ("route incorrect", "Incomplete ordinary route; missing " +
                string.Join(", ", ordinary.Where(name => !fields.ContainsKey(name))));
        }

        foreach (var name in new[]
            { "Expected complete workflow", "Requested scope", "Current handoff", "Stop after" })
            if (!fields.ContainsKey(name))
                return ("route incorrect",
                    "Missing required route field: " + name);

        if (fields["Requested scope"] == "route-only" &&
            fields["Current handoff"] != "none")
            return ("route incorrect", "route-only must have no handoff.");
        if (expected.CurrentHandoff is not null &&
            fields["Stop after"].Equals("routing recommendation",
                StringComparison.OrdinalIgnoreCase))
            return ("route incorrect",
                "An expected handoff cannot stop at the routing recommendation.");

        return ("route correct", "Typed structural fields and unchanged workspace passed; " +
            "semantic workflow and free-text stop boundaries require judge or manual review.");
    }

    private static string ResolveSandbox(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return "read-only";
        return configured.Trim().ToLowerInvariant() switch
        {
            "read-only" => "read-only",
            "workspace-write" => "workspace-write",
            _ => throw new InvalidOperationException(
                "IDD_ROUTE_EVAL_SANDBOX must be 'read-only' or 'workspace-write'.")
        };
    }

    private static string DescribeMissingRoute(int? exitCode,
        string? executionError, string[] execCommandErrors, string raw)
    {
        var details = new List<string>();
        details.Add(exitCode is null ? "exit code unavailable" : $"exit code {exitCode}");
        if (!string.IsNullOrWhiteSpace(executionError))
            details.Add("process error: " + executionError.Trim());
        if (execCommandErrors.Length > 0)
            details.Add("exec_command errors: " + string.Join(" | ", execCommandErrors));
        if (raw.Length == 0)
            details.Add("--output-last-message was missing or empty");
        else if (ParseFields(raw).Count == 0)
            details.Add("response contained no recognizable IDD route fields");
        return "No complete IDD route was obtained (" + string.Join("; ", details) + ").";
    }

    private static InstructionTrace InspectInstructionTrace(string stdout, string stderr)
    {
        var traceEvents = new List<(string Text, bool Succeeded)>();
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                CollectToolTrace(document.RootElement, traceEvents);
            }
            catch (JsonException)
            {
                // Non-JSON output is not a structured tool trace.
            }
        }

        foreach (var line in stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            if (line.Contains("exec_command", StringComparison.OrdinalIgnoreCase))
                traceEvents.Add((line, false));

        var traceAvailable = traceEvents.Count > 0;
        var routeRead = traceEvents.Any(entry => MentionsRouteInstruction(entry.Text) &&
            (entry.Succeeded || ContainsRouteInstructionContent(entry.Text)));
        var coreRead = traceEvents.Any(entry => MentionsCoreInstruction(entry.Text) &&
            (entry.Succeeded || ContainsCoreInstructionContent(entry.Text)));
        var summary = traceAvailable
            ? "available; idd-route/SKILL.md=" + (routeRead ? "read" : "not confirmed") +
              ", routing-core.md=" + (coreRead ? "read" : "not confirmed")
            : "not exposed by the Codex event stream";
        return new InstructionTrace(traceAvailable, routeRead, coreRead, summary);
    }

    private static void CollectToolTrace(JsonElement element,
        List<(string Text, bool Succeeded)> events)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var values = element.EnumerateObject().ToArray();
            var type = values.FirstOrDefault(property =>
                property.Name.Equals("type", StringComparison.OrdinalIgnoreCase)).Value;
            var name = values.FirstOrDefault(property =>
                property.Name.Equals("name", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("tool_name", StringComparison.OrdinalIgnoreCase)).Value;
            var typeText = type.ValueKind == JsonValueKind.String ? type.GetString() : null;
            var nameText = name.ValueKind == JsonValueKind.String ? name.GetString() : null;
            var text = element.GetRawText();
            var isToolTrace = IsToolTrace(typeText, nameText, text);
            if (isToolTrace)
                events.Add((text, IsSuccessfulToolTrace(element, text)));

            foreach (var property in values)
                CollectToolTrace(property.Value, events);
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
                CollectToolTrace(child, events);
        }
    }

    private static bool IsToolTrace(string? type, string? name, string json)
    {
        static bool IsToolName(string? value) => value is not null &&
            (value.Contains("command_execution", StringComparison.OrdinalIgnoreCase) ||
             value.Contains("exec_command", StringComparison.OrdinalIgnoreCase) ||
             value.Contains("file_read", StringComparison.OrdinalIgnoreCase) ||
             value.Contains("read_file", StringComparison.OrdinalIgnoreCase));

        return IsToolName(type) || IsToolName(name) ||
            json.Contains("\"exec_command\"", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSuccessfulToolTrace(JsonElement element, string text)
    {
        if (text.Contains("blocked by policy", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Rejected(", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("\"isError\":true", StringComparison.OrdinalIgnoreCase))
            return false;

        if (TryGetNumber(element, "exit_code", out var exitCode) ||
            TryGetNumber(element, "exitCode", out exitCode))
            return exitCode == 0;
        if (TryGetBoolean(element, "success", out var success)) return success;
        return false;
    }

    private static bool TryGetNumber(JsonElement element, string name, out int value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.Number &&
                    property.Value.TryGetInt32(out value)) return true;
                if (TryGetNumber(property.Value, name, out value)) return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
                if (TryGetNumber(child, name, out value)) return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetBoolean(JsonElement element, string name, out bool value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    value = property.Value.GetBoolean();
                    return true;
                }
                if (TryGetBoolean(property.Value, name, out value)) return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
                if (TryGetBoolean(child, name, out value)) return true;
        }

        value = default;
        return false;
    }

    private static bool MentionsRouteInstruction(string text) =>
        text.Replace('\\', '/').Contains("idd-route/skill.md",
            StringComparison.OrdinalIgnoreCase);

    private static bool MentionsCoreInstruction(string text) =>
        text.Replace('\\', '/').Contains("routing-core.md",
            StringComparison.OrdinalIgnoreCase);

    private static bool ContainsRouteInstructionContent(string text) =>
        text.Contains("name: idd-route", StringComparison.OrdinalIgnoreCase) &&
        text.Contains("Required references", StringComparison.OrdinalIgnoreCase) &&
        text.Contains("Output Format", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsCoreInstructionContent(string text) =>
        text.Contains("# IDD Routing Core", StringComparison.OrdinalIgnoreCase) &&
        text.Contains("## Decision priority", StringComparison.OrdinalIgnoreCase) &&
        text.Contains("## First Skill", StringComparison.OrdinalIgnoreCase);

    private static string[] ExtractExecCommandErrors(string stdout, string stderr) =>
        (stdout + "\n" + stderr).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains("exec_command", StringComparison.OrdinalIgnoreCase) &&
                (line.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("Rejected(", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("blocked by policy", StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string FormatCaseResult(CaseResult result)
    {
        var exitCode = result.ExitCode?.ToString() ?? "unavailable";
        var errors = result.ExecCommandErrors.Length == 0
            ? "none" : string.Join(" | ", result.ExecCommandErrors);
        return $"{result.Scenario}/{result.Revision}: {result.Status}; exit code={exitCode}; " +
            $"reason={result.Reason}; execution error={result.ExecutionError ?? "none"}; " +
            $"exec_command errors={errors}; instruction trace={result.InstructionTrace}";
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
            "stop conditions. Explicitly check that the full expected workflow " +
            "includes every required owner, conflict/validation/lint stage, " +
            "and that the current handoff and stop boundary respect authorization. " +
            "Evaluate meaning, not prose wording. Return ONLY a JSON object " +
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
        string sandbox, int timeoutMinutes, List<CaseResult> results)
    {
        var source = await RunAsync("git", ["rev-parse", "HEAD"], repo,
            timeout: TimeSpan.FromMinutes(2));
        var report = new EvalReport(source.Stdout.Trim(), model, effort, codex,
            sandbox, timeoutMinutes, baseline, repo,
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
            executable + $" failed with exit code {result.ExitCode?.ToString() ?? "unavailable"}:\n" +
            (result.ExecutionError ?? "") + "\n" + result.Stderr + "\n" + result.Stdout);
    }

    private static async Task<ProcessResult> RunAsync(string executable,
        IReadOnlyList<string> args, string cwd,
        IReadOnlyDictionary<string, string?>? env = null, string? input = null,
        TimeSpan? timeout = null)
    {
        Process? process = null;
        Task<string>? stdout = null;
        Task<string>? stderr = null;
        try
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
            process = Process.Start(info) ??
                throw new InvalidOperationException("Unable to start " + executable);
            stdout = process.StandardOutput.ReadToEndAsync();
            stderr = process.StandardError.ReadToEndAsync();
            if (input is not null)
            {
                await process.StandardInput.WriteAsync(input);
                process.StandardInput.Close();
            }

            using var cancellation = new CancellationTokenSource(
                timeout ?? TimeSpan.FromMinutes(5));
            await process.WaitForExitAsync(cancellation.Token);
            return new ProcessResult(process.ExitCode, await stdout, await stderr, null);
        }
        catch (Exception exception)
        {
            if (process is { HasExited: false })
                try { process.Kill(entireProcessTree: true); } catch { }
            return new ProcessResult(process is { HasExited: true }
                    ? process.ExitCode : null,
                await ReadCompletedStreamAsync(stdout),
                await ReadCompletedStreamAsync(stderr),
                exception.ToString());
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static async Task<string> ReadCompletedStreamAsync(Task<string>? stream)
    {
        if (stream is null) return "";
        try { return await stream; }
        catch { return ""; }
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
