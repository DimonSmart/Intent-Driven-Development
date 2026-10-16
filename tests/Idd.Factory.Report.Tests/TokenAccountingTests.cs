using System.Text;
using System.Text.Json;
using Idd.Factory.Report;
using Xunit;

namespace Idd.Factory.Report.Tests;

public sealed class TokenAccountingTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "idd-token-accounting-" + Guid.NewGuid().ToString("N"));
    private const string RootId = "11111111-1111-1111-1111-111111111111";
    private const string RunTurn = "run-turn";

    public TokenAccountingTests() => Directory.CreateDirectory(_temp);

    [Theory]
    [InlineData("main", 908468L, 852608L, 55860L, 5490L)]
    [InlineData("model-unavailable", 237205L, 153344L, 83861L, 2262L)]
    [InlineData("verification-recovery", 1353510L, 1275392L, 78118L, 10329L)]
    public void SanitizedLiveEval_UsesFinalNativeCounterAfterStructuredReply(
        string scenario, long input, long cached, long newInput, long output)
    {
        var (repo, codex, dir) = Layout(scenario);
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "token-accounting", scenario + ".jsonl");
        File.WriteAllText(Path.Combine(dir, "root.jsonl"),
            File.ReadAllText(fixture, Encoding.UTF8).Replace("__REPO__", JsonEscaped(repo)),
            Encoding.UTF8);

        var report = Assert.Single(new FactoryReportEngine().FindRuns(repo, codex));
        var root = Assert.Single(report.Agents);
        Assert.True(root.TokensAuthoritative);
        Assert.Equal(input, root.Tokens.InputTokens);
        Assert.Equal(cached, root.Tokens.CachedInputTokens);
        Assert.Equal(newInput, root.Tokens.NewInputTokens);
        Assert.Equal(output, root.Tokens.OutputTokens);
        Assert.Equal(input, report.Metrics.RootReportedTokens.InputTokens);
        Assert.Equal(cached, report.Metrics.RootReportedTokens.CachedInputTokens);
        Assert.Equal(newInput, report.Metrics.RootReportedTokens.NewInputTokens);
        Assert.Equal(output, report.Metrics.RootReportedTokens.OutputTokens);
        Assert.Equal("per-thread", report.Metrics.TokenAggregationMethod);
        Assert.Equal("complete", report.Metrics.TokenAggregationStatus);
        Assert.Equal(input, report.Metrics.Tokens.InputTokens);
        Assert.Equal(DateTimeOffset.Parse("2026-10-16T15:00:03Z"), report.Run.FinishedAt);
        Assert.DoesNotContain(report.Diagnostics, x => x.Code == "root_start_baseline_unavailable");

        var markdown = Path.Combine(_temp, scenario + ".md");
        var json = Path.Combine(_temp, scenario + ".json");
        ReportWriters.WriteMarkdown(report, markdown, verbose: false);
        ReportWriters.WriteJson(report, json);
        using var writer = new StringWriter();
        ReportWriters.WriteConsole(report, writer, verbose: false);
        Assert.Contains("Aggregation: complete", writer.ToString());
        Assert.Contains("Aggregate status: complete", File.ReadAllText(markdown));
        using var parsed = JsonDocument.Parse(File.ReadAllText(json));
        Assert.Equal(input, parsed.RootElement.GetProperty("metrics").GetProperty("rootReportedTokens")
            .GetProperty("inputTokens").GetInt64());
        Assert.Equal(input, parsed.RootElement.GetProperty("metrics").GetProperty("tokens")
            .GetProperty("inputTokens").GetInt64());
    }

    [Fact]
    public void ExistingThread_SubtractsLatestCounterBeforeFactory()
    {
        var report = Report("previous",
            User("00", "ordinary earlier request", "prior-turn"),
            Usage("01", "prior-turn", 100, 30, 10),
            User("02", "Use idd-factory-run", RunTurn),
            Usage("03", RunTurn, 160, 70, 15),
            Assistant("04", RunTurn));
        var root = Assert.Single(report.Agents);
        Assert.Equal(60, root.Tokens.InputTokens);
        Assert.Equal(40, root.Tokens.CachedInputTokens);
        Assert.Equal(20, root.Tokens.NewInputTokens);
        Assert.Equal(5, root.Tokens.OutputTokens);
        Assert.Equal(160, report.Metrics.RootReportedTokens.InputTokens);
    }

    [Fact]
    public void PreviousModelWorkWithMissingBaseline_IsUnavailable()
    {
        var report = Report("missing-baseline",
            User("00", "an earlier request", "prior-turn"),
            Assistant("01", "prior-turn", "Completed previous task."),
            User("02", "Use idd-factory-run", RunTurn),
            Usage("03", RunTurn, 160, 70, 15),
            Assistant("04", RunTurn));
        Assert.Null(Assert.Single(report.Agents).Tokens.InputTokens);
        Assert.Equal("unavailable", report.Metrics.TokenAggregationStatus);
        Assert.Contains(report.Diagnostics, x => x.Code == "root_start_baseline_unavailable");
    }

    [Fact]
    public void UnknownPreludeEvent_DoesNotProveZeroBaseline()
    {
        var report = Report("unknown-prelude",
            new { timestamp = Time("00"), type = "unknown_host_event", payload = new { } },
            User("01", "Use idd-factory-run", RunTurn),
            Usage("02", RunTurn, 50, 20, 5),
            Assistant("03", RunTurn));
        Assert.Null(Assert.Single(report.Agents).Tokens.InputTokens);
        Assert.Contains(report.Diagnostics, x => x.Code == "root_start_baseline_unavailable");
    }

    [Fact]
    public void LaterRecordFromDifferentTurnOrThread_IsNeverIncluded()
    {
        var report = Report("foreign-final",
            User("00", "Use idd-factory-run", RunTurn),
            Usage("01", RunTurn, 40, 10, 4),
            Assistant("02", RunTurn),
            Usage("03", "other-turn", 90, 30, 8),
            Usage("04", RunTurn, 120, 45, 12, "22222222-2222-2222-2222-222222222222"));
        Assert.Equal(40, report.Metrics.RootReportedTokens.InputTokens);
        Assert.Equal(40, Assert.Single(report.Agents).Tokens.InputTokens);
        Assert.Equal(DateTimeOffset.Parse(Time("02")), report.Run.FinishedAt);
    }

    [Fact]
    public void ConflictingTurnIds_EmitDiagnosticsAndNeverCorrelate()
    {
        var message = new
        {
            timestamp = Time("02"), type = "response_item",
            payload = new
            {
                type = "message", role = "assistant", turn_id = "other-turn",
                content = new[] { new { type = "output_text", text = ResultText() } },
                internal_chat_message_metadata_passthrough = new { turn_id = RunTurn }
            }
        };
        var report = Report("turn-conflict",
            User("00", "Use idd-factory-run", RunTurn),
            Usage("01", RunTurn, 40, 10, 4),
            message,
            Usage("03", RunTurn, 90, 30, 8));
        Assert.Contains(report.Diagnostics, x => x.Code == "conflicting_turn_ids");
        Assert.Equal(40, report.Metrics.RootReportedTokens.InputTokens);
    }

    [Fact]
    public void IndependentPlannerAndWorkers_AggregateOncePerThread()
    {
        var (repo, codex, dir) = Layout("agents");
        var events = new List<object>
        {
            Session(RootId, repo), User("00", "Use idd-factory-run", RunTurn),
            Usage("01", RunTurn, 100, 40, 10),
            Assistant("09", RunTurn)
        };
        Write(dir, "root.jsonl", events);
        WriteChild(dir, repo, "planner-1", "factory-planner", 60, 25, 6);
        WriteChild(dir, repo, "planner-2", "factory-planner", 70, 30, 7);
        WriteChild(dir, repo, "worker-1", "factory-worker", 80, 35, 8);
        WriteChild(dir, repo, "worker-2", "factory-worker", 90, 40, 9);
        WriteChild(dir, repo, "worker-3", "factory-worker", 100, 50, 10);
        var report = Assert.Single(new FactoryReportEngine().FindRuns(repo, codex));
        Assert.Equal(2, report.Metrics.PlannerInvocations);
        Assert.Equal(3, report.Metrics.WorkerInvocations);
        Assert.True(report.Metrics.TokenAggregationComplete);
        Assert.Equal("per-thread", report.Metrics.TokenAggregationMethod);
        Assert.Equal(500, report.Metrics.Tokens.InputTokens);
        Assert.Equal(220, report.Metrics.Tokens.CachedInputTokens);
        Assert.Equal(280, report.Metrics.Tokens.NewInputTokens);
        Assert.Equal(50, report.Metrics.Tokens.OutputTokens);
    }

    [Fact]
    public void MissingChildCounter_PreventsExactTotalWithoutLosingRoot()
    {
        var (repo, codex, dir) = Layout("incomplete-child");
        Write(dir, "root.jsonl",
        [
            Session(RootId, repo), User("00", "Use idd-factory-run", RunTurn),
            Usage("01", RunTurn, 100, 40, 10), Assistant("09", RunTurn)
        ]);
        Write(dir, "planner.jsonl",
        [
            Session("planner-1", repo, RootId, "factory-planner"),
            User("02", "delegated planner task", "child-turn"),
            new { timestamp = Time("08"), type = "event_msg", payload = new { type = "task_complete" } }
        ]);
        var report = Assert.Single(new FactoryReportEngine().FindRuns(repo, codex));
        Assert.Equal(100, Assert.Single(report.Agents, x => x.Role == "root").Tokens.InputTokens);
        Assert.False(report.Metrics.TokenAggregationComplete);
        Assert.Equal("overlap-unknown", report.Metrics.TokenAggregationStatus);
        Assert.Null(report.Metrics.Tokens.InputTokens);
        Assert.Contains(report.Diagnostics, x => x.Code == "agent_data_unavailable");
    }

    [Fact]
    public void MultipleRunsInSameRootThread_UseSeparateBaselines()
    {
        var (repo, codex, dir) = Layout("multiple");
        Write(dir, "root.jsonl",
        [
            Session(RootId, repo),
            User("00", "Use idd-factory-run first", "turn-one"),
            Usage("01", "turn-one", 100, 40, 10),
            Assistant("02", "turn-one"),
            Usage("03", "turn-one", 120, 50, 12),
            User("04", "other intermediate activity", "between"),
            Usage("05", "between", 140, 60, 14),
            User("06", "Use idd-factory-run second", "turn-two"),
            Usage("07", "turn-two", 200, 100, 20),
            Assistant("08", "turn-two"),
            Usage("09", "turn-two", 220, 110, 22)
        ]);
        var reports = new FactoryReportEngine().FindRuns(repo, codex);
        Assert.Equal(2, reports.Count);
        Assert.Equal(120, reports[0].Agents[0].Tokens.InputTokens);
        Assert.Equal(80, reports[1].Agents[0].Tokens.InputTokens);
        Assert.Equal(120, reports[0].Metrics.RootReportedTokens.InputTokens);
        Assert.Equal(220, reports[1].Metrics.RootReportedTokens.InputTokens);
    }

    private void WriteChild(string dir, string repo, string id, string role, long input, long cached, long output)
    {
        Write(dir, id + ".jsonl",
        [
            Session(id, repo, RootId, role),
            User("02", "delegated work", "child-turn"),
            Usage("03", "child-turn", input, cached, output, id),
            Usage("04", "child-turn", input, cached, output, id),
            new { timestamp = Time("08"), type = "event_msg", payload = new { type = "task_complete" } }
        ]);
    }

    private FactoryRunReport Report(string key, params object[] events)
    {
        var (repo, codex, dir) = Layout(key);
        Write(dir, "root.jsonl", new object[] { Session(RootId, repo) }.Concat(events));
        return Assert.Single(new FactoryReportEngine().FindRuns(repo, codex));
    }

    private (string Repo, string Codex, string Directory) Layout(string key)
    {
        var repo = Path.Combine(_temp, key, "repo");
        var codex = Path.Combine(_temp, key, "codex");
        var dir = Path.Combine(codex, "sessions");
        Directory.CreateDirectory(repo);
        Directory.CreateDirectory(dir);
        return (repo, codex, dir);
    }

    private static void Write(string dir, string name, IEnumerable<object> events) =>
        File.WriteAllLines(Path.Combine(dir, name), events.Select(e => JsonSerializer.Serialize(e)), Encoding.UTF8);

    private static string JsonEscaped(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    private static string Time(string seconds) => "2026-10-16T15:00:" + seconds + ".000Z";
    private static string ResultText() => JsonSerializer.Serialize(
        new { schemaVersion = 2, status = "COMPLETED", reason = "Sanitized test run." });

    private static object Session(string id, string repo, string? parent = null, string? role = null) =>
        new { timestamp = Time("00"), type = "session_meta",
            payload = new { id, cwd = repo, parent_thread_id = parent, agent_role = role } };

    private static object User(string second, string text, string turn) =>
        Message(second, "user", text, turn);

    private static object Assistant(string second, string turn, string? text = null) =>
        Message(second, "assistant", text ?? ResultText(), turn);

    private static object Message(string second, string role, string text, string turn) =>
        new { timestamp = Time(second), type = "response_item",
            payload = new { type = "message", role,
                content = new[] { new { type = role == "assistant" ? "output_text" : "input_text", text } },
                internal_chat_message_metadata_passthrough = new { turn_id = turn } } };

    private static object Usage(string second, string turn, long input, long cached, long output,
        string thread = RootId) =>
        new { timestamp = Time(second), type = "token_usage_record",
            payload = new { thread_id = thread, turn_id = turn, root_turn_id = turn,
                response_id = "response-" + second,
                turn_token_usage = new { input_tokens = 1L, cached_input_tokens = 0L, output_tokens = 1L },
                thread_token_usage = new { input_tokens = input, cached_input_tokens = cached, output_tokens = output } } };

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch { }
    }
}
