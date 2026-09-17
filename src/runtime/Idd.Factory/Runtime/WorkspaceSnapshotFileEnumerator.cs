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
        {
            throw new FactoryStateException(
                "WORKSPACE_TRACKING_FAILED",
                $"Git workspace enumeration failed ({result.CompletionReason}, exit {result.ExitCode?.ToString() ?? "n/a"}). Filesystem fallback is intentionally disabled.");
        }

        return result.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(relative => Path.GetFullPath(Path.Combine(workspace, relative)))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }
}
