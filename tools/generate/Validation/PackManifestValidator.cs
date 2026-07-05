internal sealed class PackManifestValidator(RepositoryLayout layout)
{
    public void Validate(PackManifest manifest)
    {
        if (manifest.Packs.Count == 0)
        {
            throw new InvalidOperationException("Pack manifest must define at least one pack.");
        }

        var canonicalSkills = Directory
            .GetFiles(layout.SkillsRoot, "*.md")
            .Select(path => Path.GetFileNameWithoutExtension(path)!)
            .ToHashSet(StringComparer.Ordinal);
        var skillOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var skill in canonicalSkills)
        {
            SkillDescriptionValidator.GuardPublicSkillName(layout.SkillsRoot, skill);
        }

        foreach (var (packName, pack) in manifest.Packs)
        {
            foreach (var requiredPack in pack.Requires)
            {
                if (!manifest.Packs.ContainsKey(requiredPack))
                {
                    throw new InvalidOperationException($"Pack '{packName}' requires unknown pack '{requiredPack}'.");
                }
            }

            foreach (var skill in pack.Skills)
            {
                if (!canonicalSkills.Contains(skill))
                {
                    throw new InvalidOperationException($"Pack '{packName}' references missing skill '{skill}'.");
                }

                if (skillOwners.TryGetValue(skill, out var existingOwner))
                {
                    throw new InvalidOperationException($"Skill '{skill}' is owned by both '{existingOwner}' and '{packName}'.");
                }

                skillOwners.Add(skill, packName);
            }

            foreach (var rolePrompt in pack.RolePrompts)
            {
                var path = Path.Combine(layout.FactoryRolesRoot, rolePrompt + ".md");
                if (!File.Exists(path))
                {
                    throw new InvalidOperationException($"Pack '{packName}' references missing role prompt '{rolePrompt}'.");
                }
            }

            foreach (var (skill, rolePrompts) in pack.SkillRoleReferences)
            {
                if (!pack.Skills.Contains(skill, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException($"Pack '{packName}' has skillRoleReferences for skill '{skill}' that is not owned by that pack.");
                }

                foreach (var rolePrompt in rolePrompts)
                {
                    if (!pack.RolePrompts.Contains(rolePrompt, StringComparer.Ordinal))
                    {
                        throw new InvalidOperationException($"Pack '{packName}' skill '{skill}' references undeclared role prompt '{rolePrompt}'.");
                    }
                }
            }
        }

        var declaredRolePrompts = manifest.Packs.Values
            .SelectMany(pack => pack.RolePrompts)
            .ToHashSet(StringComparer.Ordinal);
        if (Directory.Exists(layout.FactoryRolesRoot))
        {
            foreach (var path in Directory.GetFiles(layout.FactoryRolesRoot, "*.md"))
            {
                var rolePrompt = Path.GetFileNameWithoutExtension(path);
                if (!declaredRolePrompts.Contains(rolePrompt))
                {
                    throw new InvalidOperationException($"Factory role prompt file is not declared by a pack: {rolePrompt}.");
                }
            }
        }

        foreach (var skill in canonicalSkills)
        {
            if (!skillOwners.ContainsKey(skill))
            {
                throw new InvalidOperationException($"Canonical skill is not owned by a pack: {skill}.");
            }
        }

        foreach (var packName in manifest.Packs.Keys)
        {
            ValidatePackDependencyAcyclic(manifest, packName, [], []);
        }
    }

    private static void ValidatePackDependencyAcyclic(
        PackManifest manifest,
        string packName,
        HashSet<string> visiting,
        HashSet<string> visited)
    {
        if (visited.Contains(packName))
        {
            return;
        }

        if (!visiting.Add(packName))
        {
            throw new InvalidOperationException($"Pack dependency cycle includes '{packName}'.");
        }

        foreach (var dependency in manifest.Packs[packName].Requires)
        {
            ValidatePackDependencyAcyclic(manifest, dependency, visiting, visited);
        }

        visiting.Remove(packName);
        visited.Add(packName);
    }
}
