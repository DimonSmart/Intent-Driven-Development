internal sealed class SkillFileBuilder(RepositoryLayout layout)
{
    public IReadOnlyList<GeneratedFile> Build(
        AdapterConfig adapter,
        IReadOnlySet<string> knownAdapterNames,
        PackManifest packManifest)
    {
        if (string.IsNullOrWhiteSpace(adapter.SkillsRoot))
        {
            throw new InvalidOperationException($"{adapter.CodingAgent} supports skills but has no skillsRoot.");
        }

        var files = new List<GeneratedFile>();
        var skillDescriptions = new SkillDescriptionReader().Read(layout.SkillDescriptionsPath, knownAdapterNames);
        var skillPaths = Directory.GetFiles(layout.SkillsRoot, "*.md").OrderBy(Path.GetFileName).ToArray();
        var skillNames = skillPaths.Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.Ordinal);

        foreach (var skillName in skillDescriptions.Keys.OrderBy(name => name, StringComparer.Ordinal))
        {
            if (!skillNames.Contains(skillName))
            {
                throw new InvalidOperationException($"Unused skill description: {skillName}.");
            }
        }

        foreach (var skillPath in skillPaths)
        {
            var skillName = Path.GetFileNameWithoutExtension(skillPath);
            if (!skillDescriptions.TryGetValue(skillName, out var skillDescription))
            {
                throw new InvalidOperationException($"Missing skill description for {skillName} in src/canonical/skills/skill-descriptions.json.");
            }

            var content = RequiredFileReader.Read(skillPath);
            if (adapter.SupportsFrontMatter)
            {
                content = ContentNormalizer.JoinBlocks(
                    YamlFrontMatterWriter.BuildSkillFrontMatter(skillName, skillDescription, adapter.CodingAgent),
                    content);
            }

            var relativePath = Path.Combine(adapter.SkillsRoot!, skillName, "SKILL.md");
            files.Add(new GeneratedFile(relativePath, ContentNormalizer.NormalizeContent(content)));
            files.AddRange(new RolePromptReferenceBuilder(layout).Build(adapter.SkillsRoot!, skillName, packManifest));
        }

        return files;
    }
}
