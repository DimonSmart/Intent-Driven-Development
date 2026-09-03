using Idd.Factory.LiveTests.Models;

namespace Idd.Factory.LiveTests.Infrastructure;

public static class CodexWorkerResultReader
{
    public static AgentTerminalResult? TryRead(CodexRollout rollout, string role, ICollection<AgentTraceDiagnostic> diagnostics)
    {
        // Factory semantic output is intentionally free-form Markdown. Telemetry
        // must not reinterpret it as a workflow outcome.
        return null;
    }
}
