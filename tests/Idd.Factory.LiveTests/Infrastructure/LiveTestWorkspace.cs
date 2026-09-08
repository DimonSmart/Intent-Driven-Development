namespace Idd.Factory.LiveTests.Infrastructure;

public sealed record LiveTestWorkspace(
    string RunDirectory,
    string WorkspaceDirectory,
    string GeneratedMarketplaceDirectory,
    string VerificationDirectory,
    string CaseDirectory)
{
    public string CodexHomeDirectory => Path.Combine(RunDirectory, "codex-home");
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

    public static LiveTestWorkspace CreateCase(string repositoryRoot, string caseName, bool copyTemplate)
    {
        var runId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..24];
        var runDirectory = Path.Combine(repositoryRoot, "artifacts", "factory-evals", runId);
        var caseDirectory = Path.Combine(repositoryRoot, "tests", "Idd.Factory.LiveTests", "Cases", caseName);
        var workspace = new LiveTestWorkspace(
            runDirectory,
            Path.Combine(runDirectory, "workspace"),
            Path.Combine(runDirectory, "generated-marketplace"),
            Path.Combine(runDirectory, "verification"),
            caseDirectory);
        Directory.CreateDirectory(workspace.RunDirectory);
        Directory.CreateDirectory(workspace.WorkspaceDirectory);
        Directory.CreateDirectory(workspace.VerificationDirectory);
        if (copyTemplate) CopyDirectory(Path.Combine(caseDirectory, "Template"), workspace.WorkspaceDirectory);
        File.Copy(Path.Combine(caseDirectory, "task.md"), Path.Combine(runDirectory, "task.md"));
        return workspace;
    }

    public static LiveTestWorkspace CreateWorkspaceWriteProbe(string repositoryRoot)
    {
        var workspace = CreateCase(repositoryRoot, "CodexWorkspaceWriteProbe", copyTemplate: false);
        File.WriteAllText(Path.Combine(workspace.WorkspaceDirectory, "existing.txt"), "WORKSPACE_UPDATE_PENDING");
        return workspace;
    }

    public Task LogAsync(string message, CancellationToken cancellationToken = default) =>
        File.AppendAllTextAsync(ProgressPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}", cancellationToken);

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
