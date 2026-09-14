using Idd.Factory.Processes;

namespace Idd.Factory.Runtime;

internal static class WorkspaceSnapshotFileEnumerator
{
    private static readonly IProcessExecutor Executor = ProcessExecutor.Shared;
    private static readonly TimeSpan GitEnumerationTimeout = TimeSpan.FromSeconds(10);

    public static Task<IReadOnlyList<string>> EnumerateAsync(
        string workspace,
        CancellationToken cancellationToken) =>
        EnumerateAsync(workspace, cancellationToken, Executor, GitEnumerationTimeout);

    internal static async Task<IReadOnlyList<string>> EnumerateAsync(
        string workspace,
        CancellationToken cancellationToken,
        IProcessExecutor executor,
        TimeSpan gitEnumerationTimeout)
    {
        if (gitEnumerationTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(gitEnumerationTimeout));

        var gitFiles = await TryEnumerateGitVisibleFilesAsync(
            workspace,
            cancellationToken,
            executor,
            gitEnumerationTimeout);
        return gitFiles ?? Directory.EnumerateFiles(workspace, "*", SearchOption.AllDirectories)
            .Where(path => !ContainsDirectorySegment(workspace, path, ".vs"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool ContainsDirectorySegment(string workspace, string path, string segment) =>
        Path.GetRelativePath(workspace, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .SkipLast(1)
            .Any(value => value.Equals(segment, StringComparison.OrdinalIgnoreCase));

    private static async Task<IReadOnlyList<string>?> TryEnumerateGitVisibleFilesAsync(
        string workspace,
        CancellationToken cancellationToken,
        IProcessExecutor executor,
        TimeSpan gitEnumerationTimeout)
    {
        var result = await executor.RunAsync(
            new(
                "git",
                ["ls-files", "--cached", "--others", "--exclude-standard", "-z"],
                workspace)
            {
                Timeout = gitEnumerationTimeout
            },
            cancellationToken);

        if (result.CompletionReason == ProcessCompletionReason.Cancelled)
            throw new OperationCanceledException(cancellationToken);
        if (result.CompletionReason != ProcessCompletionReason.Exited || result.ExitCode != 0)
            return null;

        return result.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(relative => Path.GetFullPath(Path.Combine(workspace, relative)))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }
}
