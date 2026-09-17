using System.Diagnostics;
using System.Text.Json;
using Idd.Factory.Runtime;

namespace Idd.Factory.Tests;

public sealed class GitWorkspaceTrackingTests
{
    [Fact]
    public async Task CleanTrackedModificationIsDetected()
    {
        using var repo = Repository();
        repo.Write("src/App.cs", "before");
        CommitAll(repo.Path, "initial");
        var (tracker, attempt) = Attempt(repo.Path, "A000001");

        await tracker.PersistBaselineAsync(attempt, default);
        repo.Write("src/App.cs", "after");
        var result = await tracker.MaterializeChangesAsync(attempt, default);

        Assert.Equal(["src/App.cs"], result.ChangedPaths);
    }

    [Fact]
    public async Task PreExistingDirtyAndUntrackedFilesAreAttemptRelative()
    {
        using var repo = Repository();
        repo.Write("tracked.txt", "committed");
        CommitAll(repo.Path, "initial");
        repo.Write("tracked.txt", "already dirty");
        repo.Write("existing-untracked.txt", "already untracked");
        var (tracker, attempt) = Attempt(repo.Path, "A000002");

        await tracker.PersistBaselineAsync(attempt, default);
        var unchanged = await tracker.MaterializeChangesAsync(attempt, default);

        Assert.Empty(unchanged.ChangedPaths);

        var (tracker2, attempt2) = Attempt(repo.Path, "A000003");
        await tracker2.PersistBaselineAsync(attempt2, default);
        repo.Write("tracked.txt", "changed again");
        repo.Write("existing-untracked.txt", "changed again");
        var changed = await tracker2.MaterializeChangesAsync(attempt2, default);

        Assert.Equal(["existing-untracked.txt", "tracked.txt"], changed.ChangedPaths);
    }

    [Fact]
    public async Task CommitInsideExecutorDoesNotHideChange()
    {
        using var repo = Repository();
        repo.Write("src/A.cs", "before");
        CommitAll(repo.Path, "initial");
        var (tracker, attempt) = Attempt(repo.Path, "A000004");

        await tracker.PersistBaselineAsync(attempt, default);
        repo.Write("src/A.cs", "after");
        CommitAll(repo.Path, "executor commit");
        var result = await tracker.MaterializeChangesAsync(attempt, default);

        Assert.Equal(["src/A.cs"], result.ChangedPaths);
    }

    [Fact]
    public async Task CommittingPreExistingDirtyContentWithoutEditingIsNotAttemptChange()
    {
        using var repo = Repository();
        repo.Write("foo.txt", "committed");
        CommitAll(repo.Path, "initial");
        repo.Write("foo.txt", "already dirty");
        var (tracker, attempt) = Attempt(repo.Path, "A000005");

        await tracker.PersistBaselineAsync(attempt, default);
        CommitAll(repo.Path, "commit existing dirty state");
        var result = await tracker.MaterializeChangesAsync(attempt, default);

        Assert.Empty(result.ChangedPaths);
    }

    [Fact]
    public async Task PureIndexChangeAndRestoreToBaselineAreNotChanges()
    {
        using var repo = Repository();
        repo.Write("foo.txt", "baseline");
        CommitAll(repo.Path, "initial");
        var (tracker, attempt) = Attempt(repo.Path, "A000006");

        await tracker.PersistBaselineAsync(attempt, default);
        Git(repo.Path, "rm", "--cached", "foo.txt");
        var result = await tracker.MaterializeChangesAsync(attempt, default);
        Assert.Empty(result.ChangedPaths);

        Git(repo.Path, "reset", "--hard", "HEAD");
        var (tracker2, attempt2) = Attempt(repo.Path, "A000007");
        await tracker2.PersistBaselineAsync(attempt2, default);
        repo.Write("foo.txt", "temporary");
        Git(repo.Path, "restore", "foo.txt");
        var restored = await tracker2.MaterializeChangesAsync(attempt2, default);
        Assert.Empty(restored.ChangedPaths);
    }

    [Fact]
    public async Task CommittedRenameIsOldPlusNew()
    {
        using var repo = Repository();
        repo.Write("old/path.cs", "content");
        CommitAll(repo.Path, "initial");
        var (tracker, attempt) = Attempt(repo.Path, "A000008");

        await tracker.PersistBaselineAsync(attempt, default);
        Directory.CreateDirectory(Path.Combine(repo.Path, "new"));
        Git(repo.Path, "mv", "old/path.cs", "new/path.cs");
        CommitAll(repo.Path, "rename");
        var result = await tracker.MaterializeChangesAsync(attempt, default);

        Assert.Equal(["new/path.cs", "old/path.cs"], result.ChangedPaths);
    }

    [Fact]
    public async Task UnbornRepositorySupportsFirstCommitWithoutMisattributingExistingUntrackedFile()
    {
        using var repo = Repository();
        repo.Write("existing.txt", "already here");
        var (tracker, attempt) = Attempt(repo.Path, "A000009");

        await tracker.PersistBaselineAsync(attempt, default);
        repo.Write("new.txt", "created by executor");
        CommitAll(repo.Path, "first commit");
        var result = await tracker.MaterializeChangesAsync(attempt, default);

        Assert.Equal(["new.txt"], result.ChangedPaths);
    }

    [Fact]
    public async Task BaselineDoesNotCreatePerFileEntriesForCleanTrackedFiles()
    {
        using var repo = Repository();
        for (var index = 0; index < 200; index++)
            repo.Write($"src/File{index:D4}.cs", $"content {index}");
        CommitAll(repo.Path, "many clean files");
        var (tracker, attempt) = Attempt(repo.Path, "A000010");

        var stats = await tracker.PersistBaselineAsync(attempt, default);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(attempt, "workspace-before.json")));

        Assert.Equal(0, stats.BaselineCandidateCount);
        Assert.Empty(document.RootElement.GetProperty("baselineEntries").EnumerateArray());
        Assert.Equal(2, document.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public async Task ModeOnlyChangeIsNotChangedPath()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var repo = Repository();
        var path = repo.Write("script.sh", "#!/bin/sh\necho ok\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        CommitAll(repo.Path, "initial");
        Git(repo.Path, "config", "core.filemode", "true");
        var (tracker, attempt) = Attempt(repo.Path, "A000011");

        await tracker.PersistBaselineAsync(attempt, default);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var result = await tracker.MaterializeChangesAsync(attempt, default);

        Assert.Empty(result.ChangedPaths);
    }

    private static TestWorkspace Repository()
    {
        var workspace = new TestWorkspace();
        Git(workspace.Path, "init", "--quiet");
        Git(workspace.Path, "config", "user.email", "factory-tests@example.invalid");
        Git(workspace.Path, "config", "user.name", "IDD Factory Tests");
        return workspace;
    }

    private static (GitWorkspaceChangeTracker Tracker, string Directory) Attempt(string workspace, string id)
    {
        var directory = Path.Combine(workspace, ".idd", "factory", "current", "attempts", id);
        Directory.CreateDirectory(directory);
        return (new GitWorkspaceChangeTracker(workspace), directory);
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
}
