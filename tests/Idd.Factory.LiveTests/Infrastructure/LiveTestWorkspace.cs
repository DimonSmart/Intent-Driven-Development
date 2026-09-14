namespace Idd.Factory.LiveTests.Infrastructure;

public sealed record LiveTestWorkspace(
    string RunDirectory,
    string WorkspaceDirectory,
    string TemporaryDirectory,
    string VerificationDirectory,
    string CaseDirectory)
{
    public string CodexHomeDirectory => Path.Combine(TemporaryDirectory, "codex-home");
    public string GeneratedMarketplaceDirectory => Path.Combine(TemporaryDirectory, "generated-marketplace");
    public string EventsPath => Path.Combine(RunDirectory, "events.jsonl");
    public string StderrPath => Path.Combine(RunDirectory, "stderr.log");
    public string LastMessagePath => Path.Combine(RunDirectory, "last-message.json");
    public string ProgressPath => Path.Combine(RunDirectory, "progress.log");

    public static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "tools", "generate", "Generate.csproj"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the Intent-Driven-Development repository root.");
    }

    public static LiveTestWorkspace CreateTwoStepCatalog(string repositoryRoot)
    {
        var runId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..24];
        var evalsDirectory = Path.Combine(repositoryRoot, "artifacts", "factory-evals");
        var runDirectory = Path.Combine(evalsDirectory, runId);
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "idd-factory", "live-evals", runId);
        var caseDirectory = Path.Combine(repositoryRoot, "tests", "Idd.Factory.LiveTests", "Cases", "TwoStepCatalog");
        var workspace = new LiveTestWorkspace(
            runDirectory,
            Path.Combine(runDirectory, "workspace"),
            temporaryDirectory,
            Path.Combine(runDirectory, "verification"),
            caseDirectory);
        Directory.CreateDirectory(workspace.RunDirectory);
        Directory.CreateDirectory(workspace.WorkspaceDirectory);
        Directory.CreateDirectory(workspace.TemporaryDirectory);
        Directory.CreateDirectory(workspace.VerificationDirectory);
        CopyDirectory(Path.Combine(caseDirectory, "Template"), workspace.WorkspaceDirectory);
        File.Copy(Path.Combine(caseDirectory, "task.md"), Path.Combine(runDirectory, "task.md"));
        File.WriteAllText(Path.Combine(evalsDirectory, "current-workspace.txt"), workspace.WorkspaceDirectory + Environment.NewLine);
        return workspace;
    }

    public Task LogAsync(string message, CancellationToken cancellationToken = default) =>
        File.AppendAllTextAsync(ProgressPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}", cancellationToken);

    public async Task CleanupTemporaryDataAsync()
    {
        if (!Directory.Exists(TemporaryDirectory)) return;

        Exception? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                Directory.Delete(TemporaryDirectory, recursive: true);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                lastError = exception;
                if (attempt < 3) await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt));
            }
        }

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(RunDirectory, "temporary-cleanup-warning.txt"),
                $"Temporary live-eval data could not be deleted after 3 attempts.{Environment.NewLine}" +
                $"Path: {TemporaryDirectory}{Environment.NewLine}" +
                $"{lastError?.GetType().Name}: {lastError?.Message}{Environment.NewLine}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    public async Task CaptureGitEvidenceAsync(ProcessRunner runner)
    {
        try
        {
            await runner.RunAsync("git", ["status", "--porcelain=v1", "--untracked-files=all"], WorkspaceDirectory,
                Path.Combine(RunDirectory, "git-status.txt"), Path.Combine(RunDirectory, "git-status.stderr.log"), TimeSpan.FromMinutes(1), CancellationToken.None);
            await runner.RunAsync("git", ["diff", "--binary", "HEAD"], WorkspaceDirectory,
                Path.Combine(RunDirectory, "git-diff.patch"), Path.Combine(RunDirectory, "git-diff.stderr.log"), TimeSpan.FromMinutes(1), CancellationToken.None);
        }
        catch (Exception exception)
        {
            await File.WriteAllTextAsync(Path.Combine(RunDirectory, "git-evidence-error.txt"), exception.ToString());
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
