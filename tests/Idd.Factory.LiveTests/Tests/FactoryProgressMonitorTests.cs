using System.Text.Json;
using Idd.Factory.LiveTests.Infrastructure;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class FactoryProgressMonitorTests
{
    [Theory]
    [InlineData("agent-dispatching", "A000002 executor ST-001 started")]
    [InlineData("verification-completed", "verification subtask passed")]
    [InlineData("run-completed", "Factory completed")]
    public void ProjectsOnlyMeaningfulStateChanges(string type, string expected)
    {
        var data = type switch
        {
            "agent-dispatching" => new { attemptId = "A000002", role = "executor", workItemId = "ST-001" },
            "verification-completed" => (object)new { verificationContext = "subtask", verificationStatus = "passed" },
            _ => new { }
        };
        var line = JsonSerializer.Serialize(new { timestamp = "2026-08-12T18:19:56Z", type, data });
        Assert.Contains(expected, FactoryProgressMonitor.Project(line));
    }

    [Fact]
    public void IgnoresUnknownAndMalformedEvents()
    {
        Assert.Null(FactoryProgressMonitor.Project("not-json"));
        Assert.Null(FactoryProgressMonitor.Project(JsonSerializer.Serialize(new { type = "poll", data = new { } })));
    }
}
