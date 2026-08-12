using System.Text.Json;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed class CurrentIddArtifactBuilder(ProcessRunner processRunner)
{
    public async Task BuildAsync(string repositoryRoot, FactoryEvalWorkspace workspace, string version, CancellationToken cancellationToken)
    {
        var generator = Path.Combine(repositoryRoot, "tools", "generate", "bin", "Debug", "net10.0", "Generate.dll");
        if (!File.Exists(generator)) throw new FileNotFoundException("Current generator assembly was not built.", generator);
        var result = await processRunner.RunAsync("dotnet", ["exec", generator, "--version", version, "--output", workspace.GeneratedMarketplaceDirectory], repositoryRoot,
            Path.Combine(workspace.VerificationDirectory, "generator.stdout.log"), Path.Combine(workspace.VerificationDirectory, "generator.stderr.log"), TimeSpan.FromMinutes(2), cancellationToken);
        if (result.ExitCode != 0) throw new InvalidOperationException($"Generator failed with exit code {result.ExitCode}. See {result.StderrPath}.");
        var factory = Path.Combine(workspace.GeneratedMarketplaceDirectory, "plugins", "codex", "idd-factory");
        var intent = Path.Combine(workspace.GeneratedMarketplaceDirectory, "plugins", "codex", "idd-intent");
        CopySkills(factory, workspace.WorkspaceDirectory);
        CopySkills(intent, workspace.WorkspaceDirectory);
        CopyRuntime(factory, workspace.WorkspaceDirectory);
        MergeBootstrap(Path.Combine(factory, "assets", "bootstrap"), workspace.WorkspaceDirectory);
        var reference = Path.Combine(workspace.WorkspaceDirectory, ".agents", "skills", "idd-factory-run", "references", "methodology-version.json");
        var generated = JsonDocument.Parse(File.ReadAllText(reference)).RootElement.GetProperty("methodologyVersion").GetString();
        if (generated != version) throw new InvalidOperationException("Generated methodology-version.json does not match the requested methodology version.");
    }

    private static void CopySkills(string plugin, string workspace)
    {
        var source = Path.Combine(plugin, "skills");
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException($"Generated skills are missing: {source}");
        CopyDirectory(source, Path.Combine(workspace, ".agents", "skills"), allowExisting: false);
    }

    private static void CopyRuntime(string plugin, string workspace)
    {
        var source = Path.Combine(plugin, "runtime");
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException($"Generated Factory runtime is missing: {source}");
        CopyDirectory(source, Path.Combine(workspace, ".agents", "runtime"), allowExisting: false);
    }

    private static void MergeBootstrap(string source, string workspace)
    {
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException($"Generated Factory bootstrap assets are missing: {source}");
        CopyDirectory(source, workspace, allowExisting: true, preserveExistingIntent: true);
    }

    private static void CopyDirectory(string source, string destination, bool allowExisting, bool preserveExistingIntent = false)
    {
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            if (File.Exists(target))
            {
                if (preserveExistingIntent && relative.Replace('\\', '/').StartsWith(".idd/intent/", StringComparison.Ordinal)) continue;
                if (!allowExisting) throw new InvalidOperationException($"Skill copy conflict: {target}");
                if (File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(target))) continue;
                throw new InvalidOperationException($"Unexpected bootstrap asset conflict: {target}");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
