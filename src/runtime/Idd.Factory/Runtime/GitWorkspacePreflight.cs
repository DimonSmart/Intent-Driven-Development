using Idd.Factory.Processes;
using Idd.Factory.State;

namespace Idd.Factory.Runtime;

internal sealed class GitWorkspacePreflight(IProcessExecutor? executor = null)
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);
    private readonly IProcessExecutor processExecutor = executor ?? ProcessExecutor.Shared;

    public async Task EnsureAsync(string workspace, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(workspace))
        {
            throw new FactoryStateException(
                "FACTORY_REQUIRES_GIT_REPOSITORY",
                "Factory requires an existing Git worktree root.");
        }

        var version = await RunAsync(workspace, ["--version"], cancellationToken);
        if (!Succeeded(version))
        {
            throw new FactoryStateException(
                "FACTORY_GIT_UNAVAILABLE",
                "Factory requires a working Git CLI, but 'git --version' could not be executed successfully.");
        }

        var inside = await RunAsync(
            workspace,
            ["rev-parse", "--is-inside-work-tree"],
            cancellationToken);
        if (!Succeeded(inside)
            || !string.Equals(inside.StandardOutput.Trim(), "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new FactoryStateException(
                "FACTORY_REQUIRES_GIT_REPOSITORY",
                "Factory requires a Git repository and must be started from the repository/worktree root.");
        }

        var topLevel = await RunAsync(
            workspace,
            ["rev-parse", "--show-toplevel"],
            cancellationToken);
        if (!Succeeded(topLevel) || string.IsNullOrWhiteSpace(topLevel.StandardOutput))
        {
            throw new FactoryStateException(
                "FACTORY_REQUIRES_GIT_REPOSITORY",
                "Factory requires a Git repository and must be started from the repository/worktree root.");
        }

        if (!SamePath(workspace, topLevel.StandardOutput.Trim()))
        {
            throw new FactoryStateException(
                "FACTORY_REQUIRES_GIT_REPOSITORY",
                "Factory workspace must be the Git repository/worktree root; nested repository directories are not supported.");
        }
    }

    private Task<ProcessExecutionResult> RunAsync(
        string workspace,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken) =>
        processExecutor.RunAsync(
            new ProcessExecutionRequest("git", arguments, workspace)
            {
                Timeout = CommandTimeout
            },
            cancellationToken);

    private static bool Succeeded(ProcessExecutionResult result)
    {
        if (result.CompletionReason == ProcessCompletionReason.Cancelled)
            throw new OperationCanceledException();
        return result.CompletionReason == ProcessCompletionReason.Exited && result.ExitCode == 0;
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(
            NormalizePath(left),
            NormalizePath(right),
            FileSystemPathComparison);

    private static StringComparison FileSystemPathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
