using Idd.Factory.LiveTests.Infrastructure;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class CodexJsonlAnalyzerTests
{
    [Fact]
    public void Analyze_CorrelatesStartedAndCompletedSpawnCalls()
    {
        var metrics = CodexJsonlAnalyzer.Analyze(FixturePath, TimeSpan.FromMilliseconds(321));

        Assert.Equal(4, metrics.ToolCallCount);
        Assert.Equal(3, metrics.SpawnAgentCallCount);
        Assert.Equal(2, metrics.SpawnedAgentCount);
        Assert.Equal(1, metrics.FailedSpawnAgentCallCount);
        Assert.Equal(130, metrics.InputTokens);
        Assert.Equal(90, metrics.CachedInputTokens);
        Assert.Equal(30, metrics.OutputTokens);
        Assert.Equal(160, metrics.TotalTokens);
        Assert.Equal(321, metrics.WallTimeMs);
    }

    [Fact]
    public void Analyze_RejectsUnrecognizedSpawnEventFormat()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{\"type\":\"item.completed\",\"item\":{\"id\":\"unknown\",\"type\":\"spawn_agent_result\",\"status\":\"completed\"}}\n");

            var exception = Assert.Throws<CodexJsonlAnalysisException>(() => CodexJsonlAnalyzer.Analyze(path, TimeSpan.Zero));

            Assert.Contains("Unsupported spawn_agent item type", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string FixturePath => Path.Combine(RepositoryRootFinder.Find(), "tests", "Idd.Factory.LiveTests", "Tests", "Fixtures", "codex-jsonl-spawn-events.jsonl");
}
