internal static class DirectoryCopier
{
    public static void Copy(string source, string destination, bool force)
    {
        if (Directory.Exists(destination) && !force)
        {
            throw new ToolException($"File already exists: {PathNormalizer.Normalize(Path.GetRelativePath(Directory.GetCurrentDirectory(), destination))}" +
                Environment.NewLine +
                "Use --force to overwrite.");
        }

        foreach (var sourcePath in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, sourcePath);
            var destinationPath = Path.Combine(destination, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }
}
