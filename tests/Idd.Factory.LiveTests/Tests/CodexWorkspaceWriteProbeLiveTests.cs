using Idd.Factory.LiveTests.Environments;
using Idd.Factory.LiveTests.Infrastructure;
using Idd.Factory.LiveTests.Models;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

[Collection("Live Factory Evals")]
public sealed class CodexWorkspaceWriteProbeLiveTests
{
    [LiveFactoryEvalFact]
    [Trait("Category", "LiveFactoryEval")]
    public async Task CodexExec_CreatesAndUpdatesWorkspaceFiles()
    {
        var cancellationToken = CancellationToken.None;
        var repositoryRoot = RepositoryRootFinder.Find();
        var profileName = Environment.GetEnvironmentVariable(LocalFactoryEvalEnvironment.LaunchProfileEnvironmentVariable)
            ?? LocalFactoryEvalEnvironment.LaunchProfileDiscoveryOrder[0];
        LocalFactoryEvalEnvironment.ResolveLaunchProfile(profileName);
        var discoveryId = Environment.GetEnvironmentVariable("IDD_CODEX_LAUNCH_DISCOVERY_ID")
            ?? $"manual-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var workspace = new FactoryEvalWorkspaceBuilder().CreateWorkspaceWriteProbe(repositoryRoot, discoveryId, profileName);
        var runner = new ProcessRunner();
        var environment = new LocalFactoryEvalEnvironment(runner);
        var options = CreateOptions();
        ProcessResult? codex = null;
        string codexVersion = "unavailable";
        var commandLine = CodexLaunchProfileReport.FormatCommandLine(
            environment.CodexCommand.Executable,
            environment.CodexCommand.PrefixArguments.Concat(LocalFactoryEvalEnvironment.BuildRunCodexArguments(workspace, options, profileName)));

        try
        {
            codexVersion = await ReadCodexVersionAsync(runner, environment.CodexCommand, workspace, cancellationToken);
            await InitializeGitAsync(runner, workspace, cancellationToken);
            await environment.PrepareAsync(workspace, cancellationToken);
            codex = await environment.RunCodexAsync(workspace, options, cancellationToken);
        }
        catch (Exception exception)
        {
            if (!File.Exists(workspace.StderrPath))
                await File.WriteAllTextAsync(workspace.StderrPath, CodexLaunchProfileReport.RedactSecrets($"Probe setup or Codex launch failed: {exception.Message}{Environment.NewLine}"), cancellationToken);
            if (!File.Exists(workspace.EventsPath))
                await File.WriteAllTextAsync(workspace.EventsPath, string.Empty, cancellationToken);
            throw;
        }
        finally
        {
            var createdPath = Path.Combine(workspace.WorkspaceDirectory, "codex-write-probe.txt");
            var existingPath = Path.Combine(workspace.WorkspaceDirectory, "existing.txt");
            var createdContent = File.Exists(createdPath) ? await File.ReadAllTextAsync(createdPath, cancellationToken) : null;
            var existingContent = File.Exists(existingPath) ? await File.ReadAllTextAsync(existingPath, cancellationToken) : null;
            var passed = createdContent == "WORKSPACE_WRITE_OK" && existingContent == "WORKSPACE_UPDATE_OK";
            var attempt = new CodexLaunchProfileAttempt(
                profileName,
                workspace.RunDirectory,
                commandLine,
                codexVersion,
                codex?.ExitCode,
                codex?.TimedOut,
                workspace.StderrPath,
                workspace.EventsPath,
                File.Exists(createdPath),
                createdContent,
                File.Exists(existingPath),
                existingContent,
                passed);
            await CodexLaunchProfileReport.WriteAsync(repositoryRoot, discoveryId, attempt, cancellationToken);
        }

        Assert.Equal("WORKSPACE_WRITE_OK", await ReadIfPresentAsync(Path.Combine(workspace.WorkspaceDirectory, "codex-write-probe.txt"), cancellationToken));
        Assert.Equal("WORKSPACE_UPDATE_OK", await ReadIfPresentAsync(Path.Combine(workspace.WorkspaceDirectory, "existing.txt"), cancellationToken));
    }

    private static FactoryEvalOptions CreateOptions()
    {
        var timeoutText = Environment.GetEnvironmentVariable("IDD_CODEX_WRITE_PROBE_TIMEOUT_MINUTES");
        var timeout = int.TryParse(timeoutText, out var minutes) && minutes > 0 ? TimeSpan.FromMinutes(minutes) : TimeSpan.FromMinutes(5);
        return new FactoryEvalOptions(
            Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_MODEL") ?? "gpt-5.6-luna",
            Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_REASONING_EFFORT") ?? "low",
            timeout,
            "workspace-write-probe");
    }

    private static async Task InitializeGitAsync(ProcessRunner runner, FactoryEvalWorkspace workspace, CancellationToken cancellationToken)
    {
        foreach (var arguments in new[]
        {
            new[] { "init" },
            new[] { "config", "user.name", "IDD Codex Launch Probe" },
            new[] { "config", "user.email", "idd-codex-launch-probe@local" },
            new[] { "add", "." },
            new[] { "commit", "-m", "Initial workspace write probe" }
        })
        {
            var name = "git-" + arguments[0];
            var result = await runner.RunAsync("git", arguments, workspace.WorkspaceDirectory, Path.Combine(workspace.VerificationDirectory, name + ".log"), Path.Combine(workspace.VerificationDirectory, name + ".stderr.log"), TimeSpan.FromMinutes(1), cancellationToken);
            if (result.ExitCode != 0) throw new InvalidOperationException($"git {arguments[0]} failed. See {result.StderrPath}.");
        }
    }

    private static async Task<string> ReadCodexVersionAsync(ProcessRunner runner, CodexCommand command, FactoryEvalWorkspace workspace, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(command.Executable, command.PrefixArguments.Concat(["--version"]).ToArray(), workspace.WorkspaceDirectory, Path.Combine(workspace.VerificationDirectory, "codex-version.log"), Path.Combine(workspace.VerificationDirectory, "codex-version.stderr.log"), TimeSpan.FromMinutes(1), cancellationToken);
        if (result.ExitCode != 0) return $"unavailable (exit code {result.ExitCode})";
        return (await File.ReadAllTextAsync(result.StdoutPath, cancellationToken)).Trim();
    }

    private static async Task<string?> ReadIfPresentAsync(string path, CancellationToken cancellationToken)
        => File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
}
