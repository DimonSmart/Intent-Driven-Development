internal sealed class RolePromptReferenceBuilder(RepositoryLayout layout)
{
    public IReadOnlyList<GeneratedFile> Build(string skillsRoot, string skillName, PackManifest manifest)
    {
        var files = new List<GeneratedFile>();
        foreach (var pack in manifest.Packs.Values)
        {
            if (!pack.Skills.Contains(skillName, StringComparer.Ordinal) ||
                !pack.SkillRoleReferences.TryGetValue(skillName, out var rolePrompts))
            {
                continue;
            }

            foreach (var rolePrompt in rolePrompts)
            {
                var content = RequiredFileReader.Read(Path.Combine(layout.FactoryRolesRoot, rolePrompt + ".md"));
                var relativePath = Path.Combine(skillsRoot, skillName, "references", "roles", rolePrompt + ".md");
                files.Add(new GeneratedFile(relativePath, ContentNormalizer.NormalizeContent(content)));
            }
        }

        return files;
    }
}
