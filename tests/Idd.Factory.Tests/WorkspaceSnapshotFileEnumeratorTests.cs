using System.Diagnostics;
using Idd.Factory.Processes;
using Idd.Factory.Runtime;
using Idd.Factory.State;

namespace Idd.Factory.Tests;

public sealed class WorkspaceSnapshotFileEnumeratorTests
{
    [Fact]
    public async Task GitIgnoreExcludesUntrackedWorkspaceFilesWithoutSpecialDirectoryRules()
    {
        using var temp = new TestWorkspace();
        RunGit(temp.Path, "init", "--quiet");
        temp.Write(".gitignore", ".vs/\nignored/\n");
        temp.Write("visible.txt", "visible");
        temp.Write(".vs/session.bin", "ignored");
        temp.Write("ignored/cache.bin", "ignored");

        var files = await WorkspaceSnapshotFileEnumerator.EnumerateAsync(temp.Path, default);
        var relative = RelativePaths(temp.Path, files);

        Assert.Contains(".gitignore", relative);
        Assert.Contains("visible.txt", relative);
        Assert.DoesNotContain(".vs/session.bin", relative);
        Assert.DoesNotContain("ignored/cache.bin", relative);
    }

    [Fact]
    public async Task TrackedFileRemainsVisibleWhenItLaterMatchesGitIgnore()
    {
        using var temp = new TestWorkspace();
        RunGit(temp.Path, "init", "--quiet");
        temp.Write("tracked.tmp", "tracked");
        RunGit(temp.Path, "add", "tracked.tmp");
        temp.Write(".gitignore", "*.tmp\n");

        var files = await WorkspaceSnapshotFileEnumerator.EnumerateAsync(temp.Path, default);
        var relative = RelativePaths(temp.Path, files);

        Assert.Contains("tracked.tmp", relative);
    }

    [Fact]
    public async Task NonGitWorkspaceFailsInsteadOfUsingFilesystemFallback()
    {
        using var temp = new TestWorkspace();
        temp.Write("visible.txt", "visible");

        var error = await Assert.ThrowsAsync<FactoryStateException>(() =>
            WorkspaceSnapshotFileEnumerator.EnumerateAsync(temp.Path, default));

        Assert.Equal("WORKSPACE_TRACKING_FAILED", error.Code);
    }

    [Fact]
    public async Task TimedOutGitEnumerationFailsInsteadOfUsingManagedEnumeration()
    {
        using var temp = new TestWorkspace();
        temp.Write("visible.txt", "visible");
        var executor = new TimedOutExecutor();
        var timeout = TimeSpan.FromMilliseconds(25);

        var error = await Assert.ThrowsAsync<FactoryStateException>(() =>
            WorkspaceSnapshotFileEnumerator.EnumerateAsync(
                temp.Path,
                default,
                executor,
                timeout));

        Assert.Equal("WORKSPACE_TRACKING_FAILED", error.Code);
        Assert.Equal(timeout, executor.Request!.Timeout);
        Assert.Equal("git", executor.Request.Executable);
    }

    private static string[] RelativePaths(string workspace, IReadOnlyList<string> files) =>
        files.Select(path => Path.GetRelativePath(workspace, path).Replace('\\', '/')).ToArray();

    private static void RunGit(string workspace, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workspace,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {stderr}");
    }

    private sealed class TimedOutExecutor : IProcessExecutor
    {
        public ProcessExecutionRequest? Request { get; private set; }

        public Task<ProcessExecutionResult> RunAsync(
            ProcessExecutionRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new ProcessExecutionResult(
                null,
                null,
                ProcessCompletionReason.TimedOut,
                "",
                "",
                new ProcessTerminationOutcome(true, true),
                []));
        }
    }
}
