using System.Text;
using System.Text.Json;
using Idd.Factory.Report;

namespace Idd.Factory.Report.Tests;

public sealed class NativeCodexTraceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "idd-native-report-" + Guid.NewGuid().ToString("N"));

    public NativeCodexTraceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Reader_normalizes_current_native_wire_format()
    {
        var rootFixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "native-factory", "root.jsonl");
        var rollout = new CodexRolloutReader().Read(rootFixture);

        var spawn = rollout.Events.Single(x => x.ToolId == "spawn-planner");
        Assert.Equal("ItemCompleted", spawn.Lifecycle);
        Assert.Equal("CollabAgentToolCall", spawn.ItemKind);
        Assert.Equal("spawn_agent", spawn.ToolName);
        Assert.Equal("root-thread", spawn.SenderThreadId);
        Assert.Equal(new[] { "planner-thread" }, spawn.ReceiverThreadIds);

        var command = rollout.Events.First(x => x.ItemKind == "CommandExecution");
        Assert.Equal("command_execution", command.ToolName);

        var change = rollout.Events.First(x => x.ItemKind == "FileChange");
        Assert.Equal("file_change", change.ToolName);

        var usage = rollout.Events.Last(x => x.UsageScope == "thread");
        Assert.True(usage.UsageIsCumulative);
        Assert.Equal("turn-final", usage.TurnId);
        Assert.Equal("response-final", usage.ResponseId);
        Assert.Equal(669821, usage.Usage!.InputTokens);
    }

    [Fact]
    public void Live_native_fixture_reconstructs_factory_topology_metrics_and_completion()
    {
        var repository = Path.Combine(_root, "repo");
        var codex = Path.Combine(_root, "codex");
        var sessions = Path.Combine(codex, "sessions");
        Directory.CreateDirectory(repository);
        Directory.CreateDirectory(sessions);

        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "native-factory");
        foreach (var file in Directory.EnumerateFiles(fixture, "*.jsonl"))
        {
            var text = File.ReadAllText(file, Encoding.UTF8)
                .Replace(JsonSerializer.Serialize("__REPO__"), JsonSerializer.Serialize(repository), StringComparison.Ordinal);
            File.WriteAllText(Path.Combine(sessions, Path.GetFileName(file)), text, Encoding.UTF8);
        }

        var report = Assert.Single(new FactoryReportEngine().FindRuns(repository, codex));

        Assert.Equal(2, report.SchemaVersion);
        Assert.Equal(1, report.Metrics.PlannerInvocations);
        Assert.Equal(2, report.Metrics.WorkerInvocations);
        Assert.Equal(0, report.Metrics.OtherChildAgents);
        Assert.Equal(2, report.Tasks.Count);
        Assert.Contains("ProductCode", report.Tasks[0].Text);
        Assert.Contains("Catalog", report.Tasks[1].Text);

        Assert.Equal(27, report.Metrics.Tools.NativeOperations);
        Assert.Equal(3, report.Metrics.Tools.SpawnAgentCalls);
        Assert.Equal(3, report.Metrics.Tools.WaitAgentCalls);
        Assert.Equal(19, report.Metrics.Tools.Commands);
        Assert.Equal(2, report.Metrics.Tools.FileChanges);
        Assert.Equal(0, report.Metrics.Tools.FailedCommands);
        Assert.Null(report.Metrics.Tools.ToolBatches);

        var rootAgent = report.Agents.Single(x => x.Role == "root");
        Assert.Equal(668821, rootAgent.Tokens.InputTokens);
        Assert.Equal(606976, rootAgent.Tokens.CachedInputTokens);
        Assert.Equal(2438, rootAgent.Tokens.OutputTokens);
        Assert.True(rootAgent.TokensAuthoritative);
        Assert.Equal("per-thread", report.Metrics.TokenAggregationMethod);
        Assert.True(report.Metrics.TokenAggregationComplete);

        Assert.Equal(true, report.Completion.PlannerDone);
        Assert.Equal("passed", report.Completion.ProjectVerification);
        Assert.Equal("completed", report.Completion.DeclaredResult);
        Assert.Equal("ok", report.Completion.ProtocolValidation);
        Assert.DoesNotContain(report.Diagnostics, x => x.Code == "completed_without_planner_done");

        Assert.Equal(DateTimeOffset.Parse("2026-09-24T08:19:05Z"), report.Run.FinishedAt);

        using var writer = new StringWriter();
        ReportWriters.WriteConsole(report, writer, verbose: false);
        var console = writer.ToString();
        Assert.Contains("├─ Planner", console);
        Assert.Contains("Worker #1", console);
        Assert.Contains("ProductCode", console);
        Assert.Contains("Tool batches:      unavailable", console);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
