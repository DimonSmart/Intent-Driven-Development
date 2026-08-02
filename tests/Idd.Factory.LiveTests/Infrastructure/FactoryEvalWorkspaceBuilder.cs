using System.Text.Json;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed class FactoryEvalWorkspaceBuilder
{
    public FactoryEvalWorkspace Create(string repositoryRoot)
    {
        var runId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..24];
        var runDirectory = Path.Combine(repositoryRoot, "artifacts", "factory-evals", runId);
        var caseDirectory = Path.Combine(repositoryRoot, "tests", "Idd.Factory.LiveTests", "Cases", "TwoStepCatalog");
        var workspace = new FactoryEvalWorkspace(runDirectory, Path.Combine(runDirectory, "workspace"), Path.Combine(runDirectory, "generated-marketplace"), Path.Combine(runDirectory, "verification"), caseDirectory);
        Directory.CreateDirectory(workspace.RunDirectory);
        Directory.CreateDirectory(workspace.VerificationDirectory);
        CopyDirectory(Path.Combine(caseDirectory, "Template"), workspace.WorkspaceDirectory);
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
}
