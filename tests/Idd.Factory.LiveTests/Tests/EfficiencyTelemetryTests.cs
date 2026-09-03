using System.Text;
using System.Text.Json;
using Idd.Factory.LiveTests.Infrastructure;
using Idd.Factory.LiveTests.Models;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class EfficiencyTelemetryTests
{
    [Fact]
    public void TokenSnapshots_PreserveAllUsageAndCalculateOnlyValidDeltas()
    {
        using var fixture = new RolloutFixture();
        fixture.Write("root",
            Meta("root"),
            Usage(100, 60, 20),
            Usage(150, 80, 35),
            Usage(140, 70, 40),
            "{\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":160,\"output_tokens\":45}}}}",
            "{\"type\":\"event_msg\",\"payload\":{\"type\":\"turn.completed\"}}");

        var trace = new AgentTraceBuilder().Build(fixture.Directory, "root");
        var agent = Assert.Single(trace.Agents);

        Assert.Equal(4, agent.TokenProgression!.Count);
        Assert.Equal(50, agent.TokenProgression[1].InputDelta);
        Assert.Equal(20, agent.TokenProgression[1].CachedInputDelta);
        Assert.Equal(30, agent.TokenProgression[1].FreshInputDelta);
        Assert.True(agent.TokenProgression[2].Discontinuity);
        Assert.Null(agent.TokenProgression[2].InputDelta);
        Assert.Null(agent.TokenProgression[3].CachedInputDelta);
        Assert.Null(agent.TokenProgression[3].FreshInputDelta);
        Assert.Contains(trace.Diagnostics, diagnostic => diagnostic.Code == "TOKEN_COUNTER_DISCONTINUITY");
        Assert.Equal(160, agent.InputTokens);
        Assert.Null(agent.CachedInputTokens);
        Assert.Null(agent.FreshInputTokens);
        Assert.Equal(1, agent.TurnCount);
    }

    [Fact]
    public void MissingTokenTelemetry_LeavesUsageUnknown()
    {
        using var fixture = new RolloutFixture(); fixture.Write("root", Meta("root"));
        var agent = Assert.Single(new AgentTraceBuilder().Build(fixture.Directory, "root").Agents);
        Assert.Null(agent.InputTokens);
        Assert.Empty(agent.TokenProgression!);
    }

    [Fact]
    public void ToolTimeline_PairsCallsClassifiesOutcomesAndCountsRepeatedReads()
    {
        using var fixture = new RolloutFixture();
        fixture.Write("root", Meta("root"),
            Tool("item.started", "read-1", "local_shell_call", "shell", "in_progress", "2026-01-01T00:00:01Z"),
            Tool("item.completed", "read-1", "local_shell_call", "shell", "completed", "2026-01-01T00:00:03Z", "Get-Content 'refs\\SKILL.md'", "alpha"),
            Tool("item.completed", "read-2", "local_shell_call", "shell", "completed", "2026-01-01T00:00:04Z", "Get-Content 'refs/SKILL.md'", "beta"),
            Tool("item.completed", "failed", "local_shell_call", "shell", "failed", "2026-01-01T00:00:05Z", "rg target source", "", 1),
            Tool("item.completed", "retry", "local_shell_call", "shell", "completed", "2026-01-01T00:00:06Z", "rg target source", "match", 0),
            "{\"timestamp\":\"2026-01-01T00:00:07Z\",\"type\":\"item.completed\",\"item\":{\"id\":\"denied\",\"type\":\"custom_tool_call\",\"name\":\"delete\",\"status\":\"rejected\",\"error\":{\"message\":\"policy rejected\"}}}");

        var agent = Assert.Single(new AgentTraceBuilder().Build(fixture.Directory, "root").Agents);
        Assert.Equal(5, agent.ToolCallCount);
        Assert.Equal(2, agent.FailedToolCallCount);
        Assert.Equal(1, agent.RejectedToolCallCount);
        Assert.Equal(1, agent.RetryOrFallbackCallCount);
        Assert.Equal(2, agent.FileReadCount);
        Assert.Equal(1, agent.RepeatedFileReadCount);
        Assert.Equal(Encoding.UTF8.GetByteCount("alpha") + Encoding.UTF8.GetByteCount("beta"), agent.FileReadBytes);
        var read = Assert.Single(agent.ToolCalls!, call => call.CallId == "read-1");
        Assert.Equal(2000, read.DurationMs);
        Assert.Equal(Encoding.UTF8.GetByteCount("alpha"), read.ResultBytes);
        Assert.DoesNotContain("alpha", JsonSerializer.Serialize(read));
        Assert.Single(agent.FileReads!.Select(file => file.Path).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void DispatchAndWaitTelemetry_CapturesChildRoleSizesAndRepeatedWaits()
    {
        using var fixture = new RolloutFixture();
        var prompt = "Role:\nexecutor\nWork item: 001-code";
        fixture.Write("root", Meta("root", timestamp: "2026-01-01T00:00:00Z"),
            Tool("item.started", "spawn", "collab_tool_call", "spawn_agent", "in_progress", "2026-01-01T00:00:01Z", prompt: prompt),
            Tool("item.completed", "spawn", "collab_tool_call", "spawn_agent", "completed", "2026-01-01T00:00:02Z", children: ["child"]),
            Wait("wait-1", "2026-01-01T00:00:03Z", "2026-01-01T00:00:05Z", terminal: false),
            Wait("wait-2", "2026-01-01T00:00:06Z", "2026-01-01T00:00:09Z", terminal: true));
        fixture.Write("child", Meta("child", "root", "2026-01-01T00:00:02Z"), JsonSerializer.Serialize(new { type = "response_item", payload = new { item = new { type = "message", text = prompt } } }));

        var trace = new AgentTraceBuilder().Build(fixture.Directory, "root");
        var child = trace.Agents.Single(agent => agent.ThreadId == "child");
        var root = trace.Agents.Single(agent => agent.ThreadId == "root");
        Assert.Equal("executor", child.Role);
        Assert.Equal("001-code", child.WorkItem);
        Assert.Equal(prompt.Length, child.DispatchCharacters);
        Assert.Equal(Encoding.UTF8.GetByteCount(prompt), child.DispatchUtf8Bytes);
        var spawn = Assert.Single(root.ToolCalls!, call => call.Tool == "spawn_agent");
        Assert.Equal("executor", spawn.ChildRole);
        Assert.Equal(prompt.Length, spawn.DispatchCharacters);
        Assert.Equal(Encoding.UTF8.GetByteCount(prompt), spawn.DispatchUtf8Bytes);
        var waits = root.ToolCalls!.Where(call => call.Tool == "wait_agent").ToArray();
        Assert.Equal([1, 2], waits.Select(wait => wait.RepeatedWaitNumber));
        Assert.False(waits[0].IsTerminalWait);
        Assert.True(waits[1].IsTerminalWait);
        Assert.Equal(5000, root.WaitAgentMs);
    }

    [Fact]
    public void AggregationAndReport_ExposeRolesGroupsHotspotsAndRequiredSections()
    {
        var root = Node("root", "factory-root", 100, 60, 2, reads: [new("refs/SKILL.md", 1, 10, 10), new("refs/SKILL.md", 2, 20, 20)]);
        var worker = Node("worker", "executor", 200, 50, 4, reads: [new("refs/SKILL.md", 1, 30, 30)]);
        var telemetry = EfficiencyTelemetryBuilder.Build(new(2, "root", [root, worker], []), new() { WallTimeMs = 9000 });

        Assert.Equal(300, telemetry.Summary.InputTokens);
        Assert.Equal(110, telemetry.Summary.CachedInputTokens);
        Assert.Equal(190, telemetry.Summary.FreshInputTokens);
        Assert.Equal(100, telemetry.RootLauncher.InputTokens);
        Assert.Equal(200, telemetry.SemanticWorkers.InputTokens);
        Assert.Equal(300, telemetry.EndToEndFactory.InputTokens);
        Assert.Equal(2, telemetry.Roles.Count);
        Assert.Equal(100, telemetry.Groups.Single(group => group.Group == "orchestration").InputTokens);
        Assert.Equal(200, telemetry.Groups.Single(group => group.Group == "execution").InputTokens);
        Assert.Equal(0, telemetry.Groups.Single(group => group.Group == "planning").Agents);
        var file = Assert.Single(telemetry.FileAccess);
        Assert.Equal(3, file.ReadCount);
        Assert.Equal(2, file.DistinctAgentCount);
        Assert.Equal("worker", telemetry.Hotspots.TopAgentsByInput[0]);

        var markdown = EfficiencyReportWriter.Write(telemetry);
        foreach (var heading in new[] { "Factory token scopes", "Root launcher", "Semantic workers", "End-to-end Factory", "Total Factory tokens", "Token usage by role", "Token usage by agent", "Token progression", "Tool-call hotspots", "Repeated file reads", "Dispatch/reference sizes", "Failures and retries", "Wait-agent telemetry", "Diagnostics" }) Assert.Contains(heading, markdown);
        Assert.DoesNotContain(Path.GetTempPath(), markdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SyntheticRootUsesOuterMetricsForEndToEndTotals()
    {
        var root = new AgentTraceNode("root", null, "factory-root", null, null, "completed", null, null, null, 0, 0, null, null, null, null, null);
        var worker = Node("worker", "executor", 200, 50, 4, reads: []);
        var telemetry = EfficiencyTelemetryBuilder.Build(new(2, "root", [root, worker], []), new() { InputTokens = 100, CachedInputTokens = 60, OutputTokens = 20, ReasoningOutputTokens = 5, TotalTokens = 120 });

        Assert.Equal(100, telemetry.RootLauncher.InputTokens);
        Assert.Equal(200, telemetry.SemanticWorkers.InputTokens);
        Assert.Equal(300, telemetry.EndToEndFactory.InputTokens);
        Assert.Equal(340, telemetry.EndToEndFactory.TotalTokens);
    }

    [Theory]
    [InlineData("refs\\roles\\executor.md", "refs/roles/executor.md")]
    [InlineData("./refs/SKILL.md", "refs/SKILL.md")]
    public void NormalizePath_UnifiesSeparators(string input, string expected) => Assert.Equal(expected, CodexRolloutReader.NormalizePath(input));

    private static AgentTraceNode Node(string id, string role, long input, long cached, int tools, IReadOnlyList<AgentFileRead> reads) => new(id, null, role, null, null, "completed", null, null, 1000, 1, tools, input, cached, 20, 5, input + 20, input - cached, 100d * cached / input, FileReadCount: reads.Count, UniqueFileReadCount: reads.Select(read => read.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count(), RepeatedFileReadCount: reads.Count - reads.Select(read => read.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count(), FileReadBytes: reads.Sum(read => read.ReturnedBytes), TokenProgression: [], ToolCalls: [], FileReads: reads, DispatchReferences: []);
    private static string Meta(string id, string? parent = null, string? timestamp = null) => JsonSerializer.Serialize(new Dictionary<string, object?> { ["type"] = "session_meta", ["payload"] = new Dictionary<string, object?> { ["id"] = id, ["parent_thread_id"] = parent, ["timestamp"] = timestamp } });
    private static string Usage(long input, long cached, long output) => JsonSerializer.Serialize(new { type = "event_msg", payload = new { type = "token_count", info = new { total_token_usage = new { input_tokens = input, cached_input_tokens = cached, output_tokens = output } } } });
    private static string Tool(string eventType, string id, string itemType, string tool, string status, string timestamp, string? command = null, string? output = null, int? exitCode = null, string? prompt = null, IReadOnlyList<string>? children = null) => JsonSerializer.Serialize(new Dictionary<string, object?> { ["timestamp"] = timestamp, ["type"] = eventType, ["item"] = new Dictionary<string, object?> { ["id"] = id, ["type"] = itemType, [itemType == "custom_tool_call" ? "name" : "tool"] = tool, ["status"] = status, ["command"] = command, ["aggregated_output"] = output, ["exit_code"] = exitCode, ["prompt"] = prompt, ["receiver_thread_ids"] = children } });
    private static string[] Wait(string id, string started, string completed, bool terminal) =>
    [
        Tool("item.started", id, "collab_tool_call", "wait_agent", "in_progress", started, children: ["child"]),
        JsonSerializer.Serialize(new { timestamp = completed, type = "item.completed", item = new { id, type = "collab_tool_call", tool = "wait_agent", status = "completed", receiver_thread_ids = new[] { "child" }, agents_states = new Dictionary<string, object> { ["child"] = new { status = terminal ? "completed" : "running" } } } })
    ];

    private sealed class RolloutFixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public RolloutFixture() => System.IO.Directory.CreateDirectory(Directory);
        public void Write(string name, params object[] lines) => File.WriteAllLines(Path.Combine(Directory, name + ".jsonl"), lines.SelectMany(line => line is string[] array ? array : [line.ToString()!]));
        public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }
}
