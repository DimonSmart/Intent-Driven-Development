using System.Diagnostics;
using Idd.Factory.Runtime;

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
}
