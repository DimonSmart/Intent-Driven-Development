using System.ComponentModel;
using System.Diagnostics;

namespace Idd.Factory.Runtime;

internal static class WorkspaceSnapshotFileEnumerator
{
    public static async Task<IReadOnlyList<string>> EnumerateAsync(string workspace, CancellationToken cancellationToken)
    {
        var gitFiles = await TryEnumerateGitVisibleFilesAsync(workspace, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        Process? process;
        try { process = Process.Start(BuildGitStartInfo(workspace)); }
        catch (Win32Exception) { return null; }
        if (process is null) return null;

        using (process)
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                await Task.WhenAll(stdoutTask, stderrTask);
                throw;
            }

            var stdout = await stdoutTask;
            _ = await stderrTask;
            if (process.ExitCode != 0) return null;

            return stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Select(relative => Path.GetFullPath(Path.Combine(workspace, relative)))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }
    }

    private static ProcessStartInfo BuildGitStartInfo(string workspace)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workspace,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("ls-files");
        start.ArgumentList.Add("--cached");
        start.ArgumentList.Add("--others");
        start.ArgumentList.Add("--exclude-standard");
        start.ArgumentList.Add("-z");
        return start;
    }
}
