namespace Idd.Factory.LiveTests.Infrastructure;

public sealed record FactoryEvalWorkspace(string RunDirectory, string WorkspaceDirectory, string GeneratedMarketplaceDirectory, string VerificationDirectory, string CaseDirectory)
{
    public string CodexHomeDirectory => Path.Combine(RunDirectory, "codex-home");
    public string ProgressPath => Path.Combine(RunDirectory, "progress.log");
    public string EventsPath => Path.Combine(RunDirectory, "events.jsonl");
    public string StderrPath => Path.Combine(RunDirectory, "stderr.log");
    public string LastMessagePath => Path.Combine(RunDirectory, "last-message.json");
    public string AgentTracePath => Path.Combine(RunDirectory, "agent-trace.json");
    public string EfficiencyJsonPath => Path.Combine(RunDirectory, "efficiency.json");
    public string EfficiencyMarkdownPath => Path.Combine(RunDirectory, "efficiency.md");
}
