namespace Idd.Factory.LiveTests.Infrastructure;

public sealed record FactoryEvalWorkspace(string RunDirectory, string WorkspaceDirectory, string GeneratedMarketplaceDirectory, string VerificationDirectory, string CaseDirectory)
{
    public string EventsPath => Path.Combine(RunDirectory, "events.jsonl");
    public string StderrPath => Path.Combine(RunDirectory, "stderr.log");
    public string LastMessagePath => Path.Combine(RunDirectory, "last-message.json");
    public string AgentTracePath => Path.Combine(RunDirectory, "agent-trace.json");
}
