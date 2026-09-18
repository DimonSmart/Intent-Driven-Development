using System.Diagnostics;
using System.Text.Json;
using Idd.Factory.Agents;
using Idd.Factory.Processes;
using Idd.Factory.Runtime;
using Idd.Factory.State;

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


    [Fact]
    public async Task DeletedCleanTrackedFileIsDetected()
    {
        using var repo = Repository();
        repo.Write("deleted.txt", "baseline");
        CommitAll(repo.Path, "initial");
        var (tracker, attempt) = Attempt(repo.Path, "A000012");

        await tracker.PersistBaselineAsync(attempt, default);
        File.Delete(Path.Combine(repo.Path, "deleted.txt"));
        var result = await tracker.MaterializeChangesAsync(attempt, default);

        Assert.Equal(["deleted.txt"], result.ChangedPaths);
    }

    [Fact]
    public async Task NewUntrackedIsDetectedButIgnoredUntrackedIsNot()
    {
        using var repo = Repository();
        repo.Write(".gitignore", "ignored.tmp\n");
        CommitAll(repo.Path, "initial");
        var (tracker, attempt) = Attempt(repo.Path, "A000013");

        await tracker.PersistBaselineAsync(attempt, default);
        repo.Write("new.txt", "new");
        repo.Write("ignored.tmp", "ignored");
        var result = await tracker.MaterializeChangesAsync(attempt, default);

        Assert.Equal(["new.txt"], result.ChangedPaths);
    }

    [Fact]
    public async Task TrackedFileRemainsTrackedWhenGitIgnoreLaterMatchesIt()
    {
        using var repo = Repository();
        repo.Write("tracked.log", "before");
        CommitAll(repo.Path, "initial");
        repo.Write(".gitignore", "*.log\n");
        CommitAll(repo.Path, "ignore logs");
        var (tracker, attempt) = Attempt(repo.Path, "A000014");

        await tracker.PersistBaselineAsync(attempt, default);
        repo.Write("tracked.log", "after");
        var result = await tracker.MaterializeChangesAsync(attempt, default);

        Assert.Equal(["tracked.log"], result.ChangedPaths);
    }

    [Fact]
    public async Task CommittedAddAndDeleteAreDetected()
    {
        using var repo = Repository();
        repo.Write("delete-me.txt", "baseline");
        CommitAll(repo.Path, "initial");

        var (addTracker, addAttempt) = Attempt(repo.Path, "A000015");
        await addTracker.PersistBaselineAsync(addAttempt, default);
        repo.Write("added.txt", "new");
        CommitAll(repo.Path, "add");
        var added = await addTracker.MaterializeChangesAsync(addAttempt, default);
        Assert.Equal(["added.txt"], added.ChangedPaths);

        var (deleteTracker, deleteAttempt) = Attempt(repo.Path, "A000016");
        await deleteTracker.PersistBaselineAsync(deleteAttempt, default);
        File.Delete(Path.Combine(repo.Path, "delete-me.txt"));
        CommitAll(repo.Path, "delete");
        var deleted = await deleteTracker.MaterializeChangesAsync(deleteAttempt, default);
        Assert.Equal(["delete-me.txt"], deleted.ChangedPaths);
    }

    [Fact]
    public async Task RestoringPreExistingDeletedTrackedFileIsAttemptChange()
    {
        using var repo = Repository();
        repo.Write("restore.txt", "committed");
        CommitAll(repo.Path, "initial");
        File.Delete(Path.Combine(repo.Path, "restore.txt"));
        var (tracker, attempt) = Attempt(repo.Path, "A000017");

        await tracker.PersistBaselineAsync(attempt, default);
        repo.Write("restore.txt", "committed");
        var result = await tracker.MaterializeChangesAsync(attempt, default);

        Assert.Equal(["restore.txt"], result.ChangedPaths);
    }

    [Fact]
    public async Task RestoringPreExistingDirtyFileToCommittedContentIsAttemptChange()
    {
        using var repo = Repository();
        repo.Write("dirty.txt", "committed");
        CommitAll(repo.Path, "initial");
        repo.Write("dirty.txt", "dirty baseline");
        var (tracker, attempt) = Attempt(repo.Path, "A000018");

        await tracker.PersistBaselineAsync(attempt, default);
        repo.Write("dirty.txt", "committed");
        var result = await tracker.MaterializeChangesAsync(attempt, default);

        Assert.Equal(["dirty.txt"], result.ChangedPaths);
    }

    [Fact]
    public async Task GitAttributesCleanNormalizationUsesGitBlobSemantics()
    {
        using var repo = Repository();
        repo.Write(".gitattributes", "*.txt text eol=lf\n");
        var path = repo.Write("filtered.txt", "same\n");
        CommitAll(repo.Path, "initial");

        var (tracker, attempt) = Attempt(repo.Path, "A000019");
        await tracker.PersistBaselineAsync(attempt, default);
        await File.WriteAllTextAsync(path, "same\r\n");
        var normalized = await tracker.MaterializeChangesAsync(attempt, default);
        Assert.Empty(normalized.ChangedPaths);

        var (tracker2, attempt2) = Attempt(repo.Path, "A000020");
        await tracker2.PersistBaselineAsync(attempt2, default);
        await File.WriteAllTextAsync(path, "different\r\n");
        var changed = await tracker2.MaterializeChangesAsync(attempt2, default);
        Assert.Equal(["filtered.txt"], changed.ChangedPaths);
    }

    [Fact]
    public async Task UnixUnusualFilenamesArePreservedExactly()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var repo = Repository();
        var names = new[]
        {
            "a:b.txt",
            "foo\\bar.txt",
            "space name.txt",
            "unicode-имя.txt",
            "line\nbreak.txt"
        };
        foreach (var name in names)
            repo.Write(name, "before");
        CommitAll(repo.Path, "initial");
        var (tracker, attempt) = Attempt(repo.Path, "A000021");

        await tracker.PersistBaselineAsync(attempt, default);
        foreach (var name in names)
            repo.Write(name, "after");
        var result = await tracker.MaterializeChangesAsync(attempt, default);

        Assert.Equal(names.OrderBy(x => x, StringComparer.Ordinal), result.ChangedPaths);
        using var document = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(attempt, "workspace-changes.json")));
        var persisted = document.RootElement.GetProperty("changedPaths")
            .EnumerateArray()
            .Select(node => node.GetString())
            .ToArray();
        Assert.Equal(names.OrderBy(x => x, StringComparer.Ordinal), persisted);
    }

    [Fact]
    public void GitPathsAndArtifactReferencesUseDifferentRules()
    {
        Assert.Throws<FactoryStateException>(() => WorkspacePathPolicy.ValidateGitPath("/absolute/path"));
        Assert.Throws<FactoryStateException>(() => WorkspacePathPolicy.ValidateGitPath("../escape"));
        Assert.Throws<FactoryStateException>(() => WorkspacePathPolicy.ValidateGitPath("dir/../../escape"));
        Assert.Throws<FactoryStateException>(() =>
            WorkspacePathPolicy.ResolveGitPath(Path.GetTempPath(), "../escape"));

        Assert.Equal("changes/run.json", WorkspacePathPolicy.ValidateArtifactReference("changes/run.json"));
        Assert.Throws<FactoryStateException>(() => WorkspacePathPolicy.ValidateArtifactReference("changes\\run.json"));
        Assert.Throws<FactoryStateException>(() => WorkspacePathPolicy.ValidateArtifactReference("../changes/run.json"));
        Assert.Throws<FactoryStateException>(() => WorkspacePathPolicy.ValidateArtifactReference("C:/changes/run.json"));

        if (OperatingSystem.IsWindows())
        {
            Assert.Throws<FactoryStateException>(() => WorkspacePathPolicy.ValidateGitPath("C:/absolute.txt"));
            Assert.Throws<FactoryStateException>(() => WorkspacePathPolicy.ValidateGitPath("C:relative.txt"));
            Assert.Throws<FactoryStateException>(() => WorkspacePathPolicy.ValidateGitPath("foo\\bar.txt"));
        }
        else
        {
            Assert.Equal("a:b.txt", WorkspacePathPolicy.ValidateGitPath("a:b.txt"));
            Assert.Equal("foo\\bar.txt", WorkspacePathPolicy.ValidateGitPath("foo\\bar.txt"));
        }
    }

    [Fact]
    public async Task ThousandCleanTrackedChangesUseBoundedGitProcessCount()
    {
        using var repo = Repository();
        for (var index = 0; index < 1000; index++)
            repo.Write($"src/File{index:D4}.cs", $"before {index}");
        CommitAll(repo.Path, "initial");

        var executor = new CountingExecutor();
        var attempt = Path.Combine(
            repo.Path, ".idd", "factory", "current", "attempts", "A000022");
        Directory.CreateDirectory(attempt);
        var tracker = new GitWorkspaceChangeTracker(repo.Path, executor);

        await tracker.PersistBaselineAsync(attempt, default);
        var beforeMaterialization = executor.GitInvocations;
        for (var index = 0; index < 1000; index++)
            repo.Write($"src/File{index:D4}.cs", $"after {index}");

        var result = await tracker.MaterializeChangesAsync(attempt, default);
        var materializationInvocations = executor.GitInvocations - beforeMaterialization;

        Assert.Equal(1000, result.ChangedPaths.Count);
        Assert.True(
            materializationInvocations < 20,
            $"Expected bounded Git process count, got {materializationInvocations}.");
        Assert.DoesNotContain(
            executor.Requests,
            request => request.Arguments.Contains("hash-object", StringComparer.Ordinal));
    }

    [Fact]
    public async Task ExistingImmutableAttemptArtifactIsNotRecalculated()
    {
        using var repo = Repository();
        repo.Write("a.txt", "before");
        repo.Write("b.txt", "before");
        CommitAll(repo.Path, "initial");
        var (tracker, attempt) = Attempt(repo.Path, "A000023");

        await tracker.PersistBaselineAsync(attempt, default);
        repo.Write("a.txt", "after");
        var first = await tracker.MaterializeChangesAsync(attempt, default);
        var artifactPath = Path.Combine(attempt, "workspace-changes.json");
        var original = await File.ReadAllTextAsync(artifactPath);

        repo.Write("b.txt", "later");
        var second = await tracker.MaterializeChangesAsync(attempt, default);

        Assert.Equal(["a.txt"], first.ChangedPaths);
        Assert.Equal(["a.txt"], second.ChangedPaths);
        Assert.False(second.WasMaterialized);
        Assert.Equal(original, await File.ReadAllTextAsync(artifactPath));
    }

    [Fact]
    public async Task CorruptImmutableAttemptArtifactIsNotRecalculated()
    {
        using var repo = Repository();
        repo.Write("a.txt", "before");
        CommitAll(repo.Path, "initial");
        var (tracker, attempt) = Attempt(repo.Path, "A000024");

        await tracker.PersistBaselineAsync(attempt, default);
        repo.Write("a.txt", "after");
        await tracker.MaterializeChangesAsync(attempt, default);
        await File.WriteAllTextAsync(Path.Combine(attempt, "workspace-changes.json"), "{ broken");

        var error = await Assert.ThrowsAsync<AgentProtocolException>(() =>
            tracker.MaterializeChangesAsync(attempt, default));

        Assert.Equal("WORKSPACE_TRACKING_FAILED", error.Code);
    }

    private static TestWorkspace Repository()
    {
        var workspace = new TestWorkspace();
        Git(workspace.Path, "init", "--quiet");
        Git(workspace.Path, "config", "user.email", "factory-tests@example.invalid");
        Git(workspace.Path, "config", "user.name", "IDD Factory Tests");
        Git(workspace.Path, "config", "core.autocrlf", "false");
        Git(workspace.Path, "config", "core.safecrlf", "false");
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

    private sealed class CountingExecutor : IProcessExecutor
    {
        public int GitInvocations { get; private set; }
        public List<ProcessExecutionRequest> Requests { get; } = [];

        public async Task<ProcessExecutionResult> RunAsync(
            ProcessExecutionRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (string.Equals(request.Executable, "git", StringComparison.OrdinalIgnoreCase))
                GitInvocations++;
            return await ProcessExecutor.Shared.RunAsync(request, cancellationToken);
        }
    }
}
