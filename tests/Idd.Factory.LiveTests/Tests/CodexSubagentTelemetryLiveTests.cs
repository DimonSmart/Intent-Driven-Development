using System.Text.Json;
using Idd.Factory.LiveTests.Environments;
using Idd.Factory.LiveTests.Infrastructure;
using Idd.Factory.LiveTests.Models;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

[Collection("Live Factory Evals")]
public sealed class CodexSubagentTelemetryLiveTests
{
    [LiveFactoryEvalFact]
    [Trait("Category", "LiveFactoryEval")]
    public async Task CodexExec_ReportsOneCompletedSubagent()
    {
        var repositoryRoot = RepositoryRootFinder.Find();
        var workspace = new FactoryEvalWorkspaceBuilder().CreateTelemetryProbe(repositoryRoot);
        var runner = new ProcessRunner();
        var environment = new LocalFactoryEvalEnvironment(runner);
        var options = FactoryEvalOptions.FromEnvironment("telemetry-probe");

        await InitializeGitAsync(runner, workspace);
        await environment.PrepareAsync(workspace, CancellationToken.None);
        var codex = await environment.RunCodexAsync(workspace, options, CancellationToken.None);

        Assert.False(codex.TimedOut);
        Assert.Equal(0, codex.ExitCode);
        var metrics = CodexJsonlAnalyzer.Analyze(workspace.EventsPath, codex.Duration);
        Assert.Equal(0, metrics.MalformedLineCount);
        Assert.Equal(1, metrics.SpawnAgentCallCount);
        Assert.Equal(1, metrics.SpawnedAgentCount);
        Assert.Equal(0, metrics.FailedSpawnAgentCallCount);
        Assert.Equal(1, metrics.CompletedChildAgentCount);

        using var response = JsonDocument.Parse(await File.ReadAllTextAsync(workspace.LastMessagePath));
        Assert.Equal("SUBAGENT_OK", response.RootElement.GetProperty("result").GetString());
    }

    private static async Task InitializeGitAsync(ProcessRunner runner, FactoryEvalWorkspace workspace)
    {
        var output = Path.Combine(workspace.VerificationDirectory, "git-init.log");
        var error = Path.Combine(workspace.VerificationDirectory, "git-init.stderr.log");
        var result = await runner.RunAsync("git", ["init"], workspace.WorkspaceDirectory, output, error, TimeSpan.FromMinutes(1), CancellationToken.None);
        if (result.ExitCode != 0) throw new InvalidOperationException($"Could not initialize the telemetry probe workspace. See {error}.");
    }
}
