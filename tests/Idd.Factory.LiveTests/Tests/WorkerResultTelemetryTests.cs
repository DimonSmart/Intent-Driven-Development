using System.Text.Json;
using Idd.Factory.LiveTests.Infrastructure;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class WorkerResultTelemetryTests
{
    [Fact]
    public void AgentTrace_ExtractsImplementerNeedsReplanWithoutChangingFactoryProtocol()
    {
        using var fixture = new RolloutFixture();
        fixture.Write("root", Meta("root"));
        fixture.Write("worker",
            Meta("worker", "root"),
            Message("user", "Role:\nimplementer\nWork item: 003-catalog-integration"),
            Message("assistant", "NEEDS_REPLAN\n\nDependency:\nProductCode.cs is inherited from the completed first Subtask and must not expand the active verification scope."),
            Completed());

        var trace = new AgentTraceBuilder().Build(fixture.Directory, "root");
        var worker = trace.Agents.Single(agent => agent.ThreadId == "worker");

        Assert.Equal("implementer", worker.Role);
        Assert.Equal("003-catalog-integration", worker.WorkItem);
        Assert.NotNull(worker.TerminalResult);
        Assert.Equal("NEEDS_REPLAN", worker.TerminalResult!.Kind);
        Assert.Contains("ProductCode.cs", worker.TerminalResult.Dependency);
        Assert.Equal(worker.TerminalResult.Dependency, worker.TerminalResult.Detail);
    }

    [Fact]
    public void EfficiencyReport_ExposesReviewerVerdictAndDependency()
    {
        using var fixture = new RolloutFixture();
        fixture.Write("root", Meta("root"));
        fixture.Write("reviewer",
            Meta("reviewer", "root"),
            Message("user", "Role:\ncheckpoint-reviewer\nWork item: 002-product-code-review"),
            Message("assistant", "Verdict: needs-replan\n\nImplementation assessment:\nCannot establish the checkpoint boundary.\n\nVerification assessment:\nNot reached.\n\nDependency:\nCheckpoint coverage must name the completed ProductCode Subtask explicitly."),
            Completed());

        var trace = new AgentTraceBuilder().Build(fixture.Directory, "root");
        var telemetry = EfficiencyTelemetryBuilder.Build(trace, new() { WallTimeMs = 1000 });
        var reviewer = telemetry.Agents.Single(agent => agent.ThreadId == "reviewer");

        Assert.NotNull(reviewer.TerminalResult);
        Assert.Equal("NEEDS_REPLAN", reviewer.TerminalResult!.Kind);
        Assert.Contains("Checkpoint coverage", reviewer.TerminalResult.Dependency);

        var markdown = EfficiencyReportWriter.Write(telemetry);
        Assert.Contains("## Worker results", markdown);
        Assert.Contains("NEEDS_REPLAN", markdown);
        Assert.Contains("Checkpoint coverage", markdown);
    }

    [Theory]
    [InlineData("DONE\n\nImplementation:\ncomplete", "implementer", "DONE")]
    [InlineData("Verdict: approved", "final-reviewer", "APPROVED")]
    [InlineData("READY\n\nWork slug: sample", "task-decomposer", "READY")]
    public void Parser_NormalizesKnownTerminalResults(string text, string role, string expected)
    {
        Assert.Equal(expected, CodexWorkerResultReader.TryParse(text, role)!.Kind);
    }

    private static string Meta(string id, string? parent = null) => JsonSerializer.Serialize(new { type = "session_meta", payload = new { id, parent_thread_id = parent } });
    private static string Message(string role, string text) => JsonSerializer.Serialize(new { type = "response_item", payload = new { item = new { type = "message", role, content = new[] { new { type = "output_text", text } } } } });
    private static string Completed() => JsonSerializer.Serialize(new { type = "event_msg", payload = new { type = "task_complete" } });

    private sealed class RolloutFixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public RolloutFixture() => System.IO.Directory.CreateDirectory(Directory);
        public void Write(string name, params string[] lines) => File.WriteAllLines(Path.Combine(Directory, name + ".jsonl"), lines);
        public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }
}
