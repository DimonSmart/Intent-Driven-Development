using System.Text;
using System.Text.Json;
using Idd.Factory.Report;
using Xunit;

namespace Idd.Factory.Report.Tests;

public sealed class InternalFactoryTraceTests
{
    [Fact]
    public void FullInternalProtocols_TwoWorkersAndLegacyFreeNativeTrace()
    {
        var root = Path.Combine(Path.GetTempPath(), "idd-internal-trace-" + Guid.NewGuid().ToString("N"));
        var repo = Path.Combine(root, "repo");
        var codex = Path.Combine(root, "codex");
        var sessions = Path.Combine(codex, "sessions");
        Directory.CreateDirectory(repo);
        Directory.CreateDirectory(sessions);

        try
        {
            var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "internal-factory");
            foreach (var path in Directory.EnumerateFiles(fixture, "*.jsonl"))
            {
                var content = File.ReadAllText(path, Encoding.UTF8);
                Assert.DoesNotContain("idd-factory-execute-subtask", content);
                File.WriteAllText(Path.Combine(sessions, Path.GetFileName(path)),
                    content.Replace(JsonSerializer.Serialize("__REPO__"),
                        JsonSerializer.Serialize(repo), StringComparison.Ordinal), Encoding.UTF8);
            }

            var report = Assert.Single(new FactoryReportEngine().FindRuns(repo, codex));
            Assert.Equal("completed", report.Run.Result);
            Assert.Equal(2, report.Metrics.PlannerInvocations);
            Assert.Equal(2, report.Metrics.WorkerInvocations);
            Assert.Equal(2, report.Tasks.Count);
            Assert.Contains("Catalog refresh", report.Tasks[0].Text);
            Assert.Contains("refresh verification", report.Tasks[1].Text);
            Assert.Equal(2, report.Agents.Count(x => x.Role == "worker"));
            Assert.Equal("Implement Catalog refresh.", report.Agents.First(x => x.Role == "worker").TaskTitle);
            Assert.DoesNotContain(report.Agents.Where(x => x.Role == "worker"),
                a => a.Task is null || a.Task.Contains("Engineering Guardrails", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
