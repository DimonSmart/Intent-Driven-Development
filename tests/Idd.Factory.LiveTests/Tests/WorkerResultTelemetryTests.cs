using Idd.Factory.LiveTests.Infrastructure;
using Idd.Factory.LiveTests.Models;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class WorkerResultTelemetryTests
{
    [Fact]
    public void FreeFormSemanticTextIsNotClassifiedAsRuntimeControl()
    {
        var rollout = new CodexRollout("unused.jsonl", "unused.jsonl", "thread", null, "executor", null, null);
        var diagnostics = new List<AgentTraceDiagnostic>();

        var result = CodexWorkerResultReader.TryRead(rollout, "executor", diagnostics);

        Assert.Null(result);
        Assert.Empty(diagnostics);
    }
}
