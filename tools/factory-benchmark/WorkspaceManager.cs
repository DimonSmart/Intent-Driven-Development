namespace Idd.Factory.Benchmark;

public static class WorkspaceManager
{
    public static string Create(string benchmarkDirectory, string runDirectory)
    {
        var workspace = Path.Combine(runDirectory, "workspace");
        if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true);
        Directory.CreateDirectory(workspace);
        var template = Path.Combine(benchmarkDirectory, "workspace");
        if (Directory.Exists(template)) CopyDirectory(template, workspace);
        return workspace;
    }

    public static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }

    public static string CreateAcceptanceSnapshot(string sourceWorkspace)
    {
        var destination = Directory.CreateTempSubdirectory("idd-factory-benchmark-acceptance-").FullName;
        foreach (var file in Directory.EnumerateFiles(sourceWorkspace, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceWorkspace, file);
            if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(segment => segment is ".git" or ".idd" or "bin" or "obj")) continue;
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
        return destination;
    }
}
