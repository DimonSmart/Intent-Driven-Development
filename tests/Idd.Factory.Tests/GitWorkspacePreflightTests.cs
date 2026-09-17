using System.Diagnostics;
using Idd.Factory.Processes;
using Idd.Factory.Runtime;
using Idd.Factory.State;

namespace Idd.Factory.Tests;

public sealed class GitWorkspacePreflightTests
{
    [Fact]
    public async Task RepositoryRootIsAccepted()
    {
        using var repo = Repository();

        await new GitWorkspacePreflight().EnsureAsync(repo.Path, default);
    }

    [Fact]
    public async Task LinkedWorktreeRootIsAccepted()
    {
        using var repo = Repository();
        using var linked = new TestWorkspace();
        repo.Write("README.md", "test");
        CommitAll(repo.Path, "initial");
        Directory.Delete(linked.Path);
        Git(repo.Path, "worktree", "add", "--quiet", "-b", "linked-test", linked.Path);

        await new GitWorkspacePreflight().EnsureAsync(linked.Path, default);
    }

    [Fact]
    public async Task NonGitDirectoryIsRejectedWithoutInitializingRepository()
    {
        using var workspace = new TestWorkspace();

        var error = await Assert.ThrowsAsync<FactoryStateException>(() =>
            new GitWorkspacePreflight().EnsureAsync(workspace.Path, default));

        Assert.Equal("FACTORY_REQUIRES_GIT_REPOSITORY", error.Code);
        Assert.False(Directory.Exists(Path.Combine(workspace.Path, ".git")));
    }

    [Fact]
    public async Task NestedRepositoryDirectoryIsRejected()
    {
        using var repo = Repository();
        var nested = Directory.CreateDirectory(Path.Combine(repo.Path, "src")).FullName;

        var error = await Assert.ThrowsAsync<FactoryStateException>(() =>
            new GitWorkspacePreflight().EnsureAsync(nested, default));

        Assert.Equal("FACTORY_REQUIRES_GIT_REPOSITORY", error.Code);
    }

    [Fact]
    public async Task UnavailableGitHasExplicitFailureCode()
    {
        using var workspace = new TestWorkspace();
        var executor = new UnavailableGitExecutor();

        var error = await Assert.ThrowsAsync<FactoryStateException>(() =>
            new GitWorkspacePreflight(executor).EnsureAsync(workspace.Path, default));

        Assert.Equal("FACTORY_GIT_UNAVAILABLE", error.Code);
        Assert.Single(executor.Requests);
        Assert.Equal(["--version"], executor.Requests[0].Arguments);
    }

    private static TestWorkspace Repository()
    {
        var workspace = new TestWorkspace();
        Git(workspace.Path, "init", "--quiet");
        Git(workspace.Path, "config", "user.email", "factory-tests@example.invalid");
        Git(workspace.Path, "config", "user.name", "IDD Factory Tests");
        return workspace;
    }

    private static void CommitAll(string workspace, string message)
    {
        Git(workspace, "add", "-A");
        Git(workspace, "commit", "--quiet", "-m", message);
    }

    private static string Git(string workspace, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workspace,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {stderr}");
        return stdout;
    }

    private sealed class UnavailableGitExecutor : IProcessExecutor
    {
        public List<ProcessExecutionRequest> Requests { get; } = [];

        public Task<ProcessExecutionResult> RunAsync(
            ProcessExecutionRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new ProcessExecutionResult(
                null,
                null,
                ProcessCompletionReason.TimedOut,
                "",
                "git unavailable",
                new ProcessTerminationOutcome(true, true),
                []));
        }
    }
}
