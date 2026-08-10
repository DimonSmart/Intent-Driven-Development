using System.Text.Json;
using Idd.Factory.LiveTests.Infrastructure;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class CodeModeTelemetryTests
{
    [Fact]
    public void CodeMode_InferTurnsFromChangedTokenSnapshotsAndIgnoreDuplicates()
    {
        using var fixture = new RolloutFixture();
        fixture.Write("root",
            Meta("root"),
            Usage(100, 60, 20),
            Usage(100, 60, 20),
            Usage(150, 80, 35));

        var agent = Assert.Single(new AgentTraceBuilder().Build(fixture.SessionsDirectory, "root").Agents);

        Assert.Equal(2, agent.TurnCount);
    }

    [Fact]
    public void CodeMode_ExtractsRepeatedFileReadsFromExecSource()
    {
        using var fixture = new RolloutFixture();
        fixture.Write("root",
            Meta("root"),
            Exec("one", "const r = await tools.exec_command({cmd: \"Get-Content -Raw 'refs\\\\SKILL.md'\"}); text(r);"),
            Exec("two", "const r = await tools.exec_command({cmd: \"Get-Content 'refs/SKILL.md'\"}); text(r);"));

        var agent = Assert.Single(new AgentTraceBuilder().Build(fixture.SessionsDirectory, "root").Agents);

        Assert.Equal(2, agent.FileReadCount);
        Assert.Equal(1, agent.UniqueFileReadCount);
        Assert.Equal(1, agent.RepeatedFileReadCount);
        Assert.All(agent.FileReads!, read => Assert.Equal("refs/SKILL.md", read.Path));
    }

    [Fact]
    public void LiveEvalProcessStderr_SupplementsNestedFailuresAndRejections()
    {
        using var fixture = new RolloutFixture(liveEvalLayout: true);
        fixture.Write("root", Meta("root", cwd: fixture.WorkspaceDirectory));
        File.WriteAllLines(fixture.StderrPath!,
        [
            "2026-01-01T00:00:00Z ERROR codex_core::tools::router: error=Exit code: 1",
            "2026-01-01T00:00:01Z ERROR codex_core::tools::router: error=`Remove-Item foo` rejected: blocked by policy"
        ]);

        var trace = new AgentTraceBuilder().Build(fixture.SessionsDirectory, "root");
        var agent = Assert.Single(trace.Agents);
        var efficiency = EfficiencyTelemetryBuilder.Build(trace, new());

        Assert.Equal(2, agent.FailedToolCallCount);
        Assert.Equal(1, agent.RejectedToolCallCount);
        Assert.Equal(2, efficiency.Summary.FailedToolCalls);
        Assert.Equal(1, efficiency.Summary.RejectedToolCalls);
        Assert.Contains(efficiency.Hotspots.FailedOrRejectedCalls, item => item.Contains("process-log", StringComparison.Ordinal));
        Assert.Contains(trace.Diagnostics, diagnostic => diagnostic.Code == "PROCESS_TOOL_FAILURE_FALLBACK");
    }

    [Fact]
    public void CodeModeWait_IsNotReportedAsFactoryWaitAgent()
    {
        using var fixture = new RolloutFixture();
        fixture.Write("root",
            Meta("root"),
            CustomTool("wait-1", "wait", "await new Promise(resolve => setTimeout(resolve, 100));"));

        var trace = new AgentTraceBuilder().Build(fixture.SessionsDirectory, "root");
        var agent = Assert.Single(trace.Agents);
        var efficiency = EfficiencyTelemetryBuilder.Build(trace, new());
        var markdown = EfficiencyReportWriter.Write(efficiency);

        Assert.Equal(0, agent.WaitAgentMs);
        Assert.Empty(efficiency.Hotspots.LongestWaits);
        Assert.Contains("No structured `wait_agent` calls", markdown);
    }

    private static string Meta(string id, string? cwd = null) => JsonSerializer.Serialize(new
    {
        type = "session_meta",
        payload = new { id, cwd }
    });

    private static string Usage(long input, long cached, long output) => JsonSerializer.Serialize(new
    {
        type = "event_msg",
        payload = new
        {
            type = "token_count",
            info = new
            {
                total_token_usage = new
                {
                    input_tokens = input,
                    cached_input_tokens = cached,
                    output_tokens = output
                }
            }
        }
    });

    private static string Exec(string id, string input) => CustomTool(id, "exec", input);

    private static string CustomTool(string id, string name, string input) => JsonSerializer.Serialize(new
    {
        type = "response_item",
        payload = new
        {
            type = "custom_tool_call",
            call_id = id,
            name,
            input,
            status = "completed"
        }
    });

    private sealed class RolloutFixture : IDisposable
    {
        private readonly string root;
        public string SessionsDirectory { get; }
        public string WorkspaceDirectory { get; }
        public string? StderrPath { get; }

        public RolloutFixture(bool liveEvalLayout = false)
        {
            root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            if (liveEvalLayout)
            {
                SessionsDirectory = Path.Combine(root, "sessions");
                WorkspaceDirectory = Path.Combine(root, "run", "workspace");
                StderrPath = Path.Combine(root, "run", "stderr.log");
                Directory.CreateDirectory(SessionsDirectory);
                Directory.CreateDirectory(WorkspaceDirectory);
                File.WriteAllText(Path.Combine(root, "run", "run-manifest.json"), "{}");
                File.WriteAllText(StderrPath, string.Empty);
            }
            else
            {
                SessionsDirectory = Path.Combine(root, "sessions");
                WorkspaceDirectory = Path.Combine(root, "workspace");
                Directory.CreateDirectory(SessionsDirectory);
                Directory.CreateDirectory(WorkspaceDirectory);
            }
        }

        public void Write(string name, params string[] lines) =>
            File.WriteAllLines(Path.Combine(SessionsDirectory, name + ".jsonl"), lines);

        public void Dispose() => Directory.Delete(root, true);
    }
}
