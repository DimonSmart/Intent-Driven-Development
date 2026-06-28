internal sealed class InstallPlanner(ContentLayout layout)
{
    public IReadOnlyList<PlannedFile> Collect(
        Manifest manifest,
        IEnumerable<string> codingAgents,
        EntryMode entryMode,
        IReadOnlyList<string> selectedPacks)
    {
        var byRelativePath = new Dictionary<string, PlannedFile>(StringComparer.Ordinal);
        var selectedSkills = ManifestSkillSelector.SelectedSkills(manifest, selectedPacks);

        foreach (var codingAgent in codingAgents)
        {
            var sourceRoot = Path.Combine(layout.GeneratedRoot, codingAgent);
            if (!Directory.Exists(sourceRoot))
            {
                throw new ToolException($"Bundled generated CodingAgent not found: {codingAgent}");
            }

            foreach (var sourcePath in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                var relativePath = PathNormalizer.Normalize(Path.GetRelativePath(sourceRoot, sourcePath));
                if (manifest.EntryPoints.TryGetValue(codingAgent, out var entryPoint) &&
                    StringComparer.Ordinal.Equals(relativePath, PathNormalizer.Normalize(entryPoint)))
                {
                    continue;
                }

                if (TryGetGeneratedSkillName(relativePath, out var skillName) &&
                    !selectedSkills.Contains(skillName))
                {
                    continue;
                }

                AddFile(byRelativePath, relativePath, File.ReadAllBytes(sourcePath));
            }

            if (entryMode != EntryMode.None)
            {
                AddFile(byRelativePath, new EntryBuilder(layout).Build(manifest, codingAgent, entryMode, selectedPacks));
            }
        }

        foreach (var projectFile in selectedPacks.SelectMany(pack => manifest.Packs[pack].ProjectFiles))
        {
            var projectFilesRoot = Path.Combine(layout.ContentRoot, projectFile.Source);
            if (!Directory.Exists(projectFilesRoot))
            {
                throw new ToolException($"Bundled project files not found: {projectFile.Source}");
            }

            foreach (var sourcePath in Directory.GetFiles(projectFilesRoot, "*", SearchOption.AllDirectories))
            {
                var relativePath = PathNormalizer.Normalize(Path.Combine(projectFile.Destination, Path.GetRelativePath(projectFilesRoot, sourcePath)));
                AddFile(byRelativePath, relativePath, File.ReadAllBytes(sourcePath));
            }
        }

        return byRelativePath.Values.ToArray();
    }

    private static bool TryGetGeneratedSkillName(string relativePath, out string skillName)
    {
        var parts = PathNormalizer.Normalize(relativePath).Split('/');
        for (var index = 0; index < parts.Length - 1; index++)
        {
            if (StringComparer.Ordinal.Equals(parts[index], "skills") && index + 1 < parts.Length)
            {
                skillName = parts[index + 1];
                return true;
            }
        }

        skillName = "";
        return false;
    }

    private static void AddFile(Dictionary<string, PlannedFile> byRelativePath, string relativePath, byte[] content) =>
        AddFile(byRelativePath, new PlannedFile(relativePath, content, FileHasher.Sha256(content)));

    private static void AddFile(Dictionary<string, PlannedFile> byRelativePath, PlannedFile plannedFile)
    {
        if (byRelativePath.TryGetValue(plannedFile.RelativePath, out var existing))
        {
            if (!StringComparer.Ordinal.Equals(existing.Hash, plannedFile.Hash))
            {
                throw new ToolException($"Conflicting bundled files for path: {plannedFile.RelativePath}");
            }

            return;
        }

        byRelativePath.Add(plannedFile.RelativePath, plannedFile);
    }
}
