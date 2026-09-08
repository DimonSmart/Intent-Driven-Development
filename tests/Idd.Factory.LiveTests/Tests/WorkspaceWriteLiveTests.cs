using Idd.Factory.LiveTests.Infrastructure;
using Xunit;
using Xunit.Sdk;

namespace Idd.Factory.LiveTests.Tests;

[Collection("Live Factory Evals")]
public sealed class WorkspaceWriteLiveTests
{
    [LiveFactoryEvalFact]
    [Trait("Category", "LiveFactoryEval")]
    public async Task ProductionWorkspaceWriteProfile_CanCreateAndModifyFiles()
    {
        var repositoryRoot = LiveTestWorkspace.FindRepositoryRoot();
        var workspace = LiveTestWorkspace.CreateWorkspaceWriteProbe(repositoryRoot);
        var runner = new ProcessRunner();
        try
        {
            await InitializeGitAsync(runner, workspace);
            await workspace.LogAsync("Starting real Codex workspace-write smoke test.");
            var codex = await new CodexProcess(runner).RunAsync(workspace, "workspace-write", factoryEnvironment: false, CancellationToken.None);

            Assert.False(codex.Process.TimedOut);
            Assert.True(codex.Process.ExitCode == 0 || codex.Process.CompletionSignaled, $"Codex exit={codex.Process.ExitCode}. See {workspace.StderrPath}.");
            Assert.Equal("WORKSPACE_WRITE_OK", await ReadIfPresentAsync(Path.Combine(workspace.WorkspaceDirectory, "codex-write-probe.txt")));
            Assert.Equal("WORKSPACE_UPDATE_OK", await ReadIfPresentAsync(Path.Combine(workspace.WorkspaceDirectory, "existing.txt")));
            await workspace.LogAsync("Workspace-write smoke test passed.");
        }
        catch (XunitException exception)
        {
            throw new XunitException($"{exception.Message}{Environment.NewLine}Artifacts: {workspace.RunDirectory}");
        }
        catch (Exception exception)
        {
            throw new XunitException($"Workspace-write live test failed: {exception.Message}{Environment.NewLine}Artifacts: {workspace.RunDirectory}");
        }
        finally
        {
            await workspace.CaptureGitEvidenceAsync(runner);
        }
    }

    private static async Task InitializeGitAsync(ProcessRunner runner, LiveTestWorkspace workspace)
    {
        foreach (var arguments in new[]
        {
            new[] { "init" }, new[] { "config", "user.name", "IDD Workspace Probe" }, new[] { "config", "user.email", "idd-workspace-probe@local" },
            new[] { "add", "." }, new[] { "commit", "-m", "Workspace write baseline" }
        })
        {
            var result = await runner.RunAsync("git", arguments, workspace.WorkspaceDirectory,
                Path.Combine(workspace.VerificationDirectory, "git-" + arguments[0] + ".log"), Path.Combine(workspace.VerificationDirectory, "git-" + arguments[0] + ".stderr.log"), TimeSpan.FromMinutes(1), CancellationToken.None);
            Assert.Equal(0, result.ExitCode);
        }
    }

    private static async Task<string?> ReadIfPresentAsync(string path) => File.Exists(path) ? await File.ReadAllTextAsync(path) : null;
}
