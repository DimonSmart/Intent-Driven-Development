using System.Text.Json;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed class FactoryEvalWorkspaceBuilder
{
    public FactoryEvalWorkspace Create(string repositoryRoot)
        => Create(repositoryRoot, "TwoStepCatalog", copyTemplate: true);

    public FactoryEvalWorkspace CreateWorkspaceWriteProbe(string repositoryRoot, string discoveryId, string launchProfile)
    {
        var attemptId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..24];
        var runDirectory = Path.Combine(repositoryRoot, "artifacts", "factory-evals", "codex-launch-profiles", SanitizePathSegment(discoveryId), SanitizePathSegment(launchProfile), attemptId);
        var caseDirectory = Path.Combine(repositoryRoot, "tests", "Idd.Factory.LiveTests", "Cases", "CodexWorkspaceWriteProbe");
        var workspace = new FactoryEvalWorkspace(runDirectory, Path.Combine(runDirectory, "workspace"), Path.Combine(runDirectory, "generated-marketplace"), Path.Combine(runDirectory, "verification"), caseDirectory);
        Directory.CreateDirectory(workspace.RunDirectory);
        Directory.CreateDirectory(workspace.VerificationDirectory);
        Directory.CreateDirectory(workspace.WorkspaceDirectory);
        File.WriteAllText(Path.Combine(workspace.WorkspaceDirectory, "existing.txt"), "WORKSPACE_UPDATE_PENDING");
        File.Copy(Path.Combine(caseDirectory, "task.md"), Path.Combine(workspace.RunDirectory, "task.md"), overwrite: true);
        return workspace;
    }

    private static FactoryEvalWorkspace Create(string repositoryRoot, string caseName, bool copyTemplate)
    {
        var runId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..24];
        var runDirectory = Path.Combine(repositoryRoot, "artifacts", "factory-evals", runId);
        var caseDirectory = Path.Combine(repositoryRoot, "tests", "Idd.Factory.LiveTests", "Cases", caseName);
        var workspace = new FactoryEvalWorkspace(runDirectory, Path.Combine(runDirectory, "workspace"), Path.Combine(runDirectory, "generated-marketplace"), Path.Combine(runDirectory, "verification"), caseDirectory);
        Directory.CreateDirectory(workspace.RunDirectory);
        Directory.CreateDirectory(workspace.VerificationDirectory);
        Directory.CreateDirectory(workspace.WorkspaceDirectory);
        if (copyTemplate) CopyDirectory(Path.Combine(caseDirectory, "Template"), workspace.WorkspaceDirectory);
        File.Copy(Path.Combine(caseDirectory, "task.md"), Path.Combine(workspace.RunDirectory, "task.md"));
        return workspace;
    }

    public static void CopyDirectory(string source, string destination)
    {
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }
}
