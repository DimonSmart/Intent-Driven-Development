internal sealed class FileInstaller
{
    public void Copy(IReadOnlyList<PlannedFile> files, string destinationRoot, bool force)
    {
        var conflicts = files
            .Where(file =>
            {
                var destination = Path.Combine(destinationRoot, file.RelativePath);
                return File.Exists(destination) &&
                    !StringComparer.Ordinal.Equals(FileHasher.Sha256(File.ReadAllBytes(destination)), file.Hash);
            })
            .Select(file => file.RelativePath)
            .ToArray();

        if (conflicts.Length > 0 && !force)
        {
            throw new ToolException(string.Join(Environment.NewLine, conflicts.Select(path => $"File already exists: {path}")) +
                Environment.NewLine +
                "Use --force to overwrite.");
        }

        foreach (var file in files)
        {
            var destination = Path.Combine(destinationRoot, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllBytes(destination, file.Content);
        }
    }
}
