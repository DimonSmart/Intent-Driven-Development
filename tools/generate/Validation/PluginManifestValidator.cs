internal sealed class PluginManifestValidator(RepositoryLayout layout)
{
    public void Validate(PluginManifest manifest)
    {
        if (manifest.Plugins.Count == 0)
        {
            throw new InvalidOperationException("Plugin manifest must define at least one plugin.");
        }

        var knownSkills = Directory.GetFiles(layout.SkillsRoot, "*.md")
            .Select(Path.GetFileNameWithoutExtension)
            .ToHashSet(StringComparer.Ordinal);
        var knownRoles = Directory.GetFiles(layout.FactoryRolesRoot, "*.md")
            .Select(Path.GetFileNameWithoutExtension)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (pluginName, plugin) in manifest.Plugins)
        {
            if (!pluginName.StartsWith("idd-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Plugin '{pluginName}' must use the idd-* naming scheme.");
            }

            foreach (var dependency in plugin.Dependencies)
            {
                if (!manifest.Plugins.ContainsKey(dependency))
                {
                    throw new InvalidOperationException($"Plugin '{pluginName}' depends on unknown plugin '{dependency}'.");
                }
            }

            foreach (var skill in plugin.Skills)
            {
                if (!knownSkills.Contains(skill))
                {
                    throw new InvalidOperationException($"Plugin '{pluginName}' references unknown skill '{skill}'.");
                }
            }

            foreach (var role in plugin.Roles)
            {
                if (!knownRoles.Contains(role))
                {
                    throw new InvalidOperationException($"Plugin '{pluginName}' references unknown role '{role}'.");
                }
            }

            foreach (var (skill, roles) in plugin.SkillRoleReferences)
            {
                if (!plugin.Skills.Contains(skill, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException($"Plugin '{pluginName}' has role references for unowned skill '{skill}'.");
                }

                foreach (var role in roles)
                {
                    if (!plugin.Roles.Contains(role, StringComparer.Ordinal))
                    {
                        throw new InvalidOperationException($"Plugin '{pluginName}' references role '{role}' from skill '{skill}' without owning it.");
                    }
                }
            }

            foreach (var asset in plugin.Assets)
            {
                var source = Path.Combine(layout.RepoRoot, asset.Source.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(source) && !Directory.Exists(source))
                {
                    throw new InvalidOperationException($"Plugin '{pluginName}' references missing asset source '{asset.Source}'.");
                }
            }
        }
    }
}
