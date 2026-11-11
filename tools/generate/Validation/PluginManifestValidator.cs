using System.Text;

internal sealed class PluginManifestValidator(RepositoryLayout layout)
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public void Validate(PluginManifest manifest, IReadOnlyDictionary<string, RoleDefinition> roleDefinitions,
        IReadOnlyDictionary<string, SkillDescription> skillDescriptions)
    {
        if (manifest.Plugins.Count == 0)
        {
            throw new InvalidOperationException("Plugin manifest must define at least one plugin.");
        }

        var knownSkills = Directory.GetFiles(layout.SkillsRoot, "*.md")
            .Select(path => Path.GetFileNameWithoutExtension(path)!)
            .ToHashSet(StringComparer.Ordinal);
        var knownRoles = Directory.Exists(layout.FactoryRolesRoot)
            ? Directory.GetFiles(layout.FactoryRolesRoot, "*.md")
                .Select(path => Path.GetFileNameWithoutExtension(path)!)
                .ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        var assignedSkills = new HashSet<string>(StringComparer.Ordinal);
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
                if (!assignedSkills.Add(skill))
                    throw new InvalidOperationException($"Duplicate skill ownership for '{skill}'.");
                if (!skillDescriptions.TryGetValue(skill, out var description))
                    throw new InvalidOperationException($"Plugin '{pluginName}' lacks description for skill '{skill}'.");
                var isInternal = plugin.SkillSourcesOrEmpty.ContainsKey(skill);
                if (description.Exposure == SkillExposure.Internal != isInternal)
                    throw new InvalidOperationException($"Skill '{skill}' internal exposure/source mismatch in plugin '{pluginName}'.");
                if (isInternal && (description.Invocation != SkillInvocation.Auto || description.Interface is not null))
                    throw new InvalidOperationException($"Internal skill '{skill}' cannot be manual or have public interface metadata.");
                if (!knownSkills.Contains(skill) && !isInternal)
                    throw new InvalidOperationException($"Plugin '{pluginName}' references unknown skill '{skill}'.");
            }

            foreach (var role in plugin.Roles)
            {
                if (!knownRoles.Contains(role))
                {
                    throw new InvalidOperationException($"Plugin '{pluginName}' references unknown role '{role}'.");
                }

                if (!roleDefinitions.ContainsKey(role))
                {
                    throw new InvalidOperationException($"Plugin '{pluginName}' role '{role}' could not be read.");
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

                if (StringComparer.Ordinal.Equals(pluginName, "idd-factory") && roles.Length != 1)
                {
                    throw new InvalidOperationException($"Codex Factory skill '{skill}' must reference exactly one runtime role.");
                }
            }

            ValidateSkillReferences(pluginName, plugin);
            ValidateInternalSkillSources(pluginName, plugin);

            foreach (var asset in plugin.SkillAssetsOrEmpty)
            {
                if (!plugin.Skills.Contains(asset.Skill, StringComparer.Ordinal))
                    throw new InvalidOperationException(
                        $"Plugin '{pluginName}' has assets for unowned skill '{asset.Skill}'.");

                var source = ResolveRepositoryPath(asset.Source);
                if (!Directory.Exists(source))
                    throw new InvalidOperationException(
                        $"Plugin '{pluginName}' skill '{asset.Skill}' asset directory is missing: '{asset.Source}'.");

                try
                {
                    _ = SkillReferencePathValidator.NormalizeDestination(asset.Destination);
                }
                catch (ArgumentException ex)
                {
                    throw new InvalidOperationException(
                        $"Plugin '{pluginName}' has invalid skill asset destination '{asset.Destination}'.", ex);
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

    private void ValidateInternalSkillSources(string pluginName, PluginDefinition plugin)
    {
        foreach (var (skill, sourcePath) in plugin.SkillSourcesOrEmpty)
        {
            if (!StringComparer.Ordinal.Equals(pluginName, "idd-factory") ||
                (skill is not ("idd-internal-factory-planner" or "idd-internal-factory-worker")) ||
                !plugin.Skills.Contains(skill, StringComparer.Ordinal))
                throw new InvalidOperationException($"Invalid internal skill ownership: '{pluginName}/{skill}'.");
            var role = skill["idd-internal-factory-".Length..];
            if (!StringComparer.Ordinal.Equals(sourcePath, $"src/canonical/factory/{role}.md"))
                throw new InvalidOperationException($"Invalid canonical source for internal skill '{skill}': {sourcePath}.");
            var source = ResolveRepositoryPath(sourcePath);
            if (!File.Exists(source))
                throw new InvalidOperationException($"Missing internal skill source: {sourcePath}.");
            try
            {
                var content = File.ReadAllText(source, StrictUtf8);
                if (string.IsNullOrWhiteSpace(content) || content.Contains('\0'))
                    throw new InvalidOperationException($"Empty or invalid internal skill source: {sourcePath}.");
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidOperationException($"Invalid UTF-8 internal skill source: {sourcePath}.", ex);
            }
        }
        if (StringComparer.Ordinal.Equals(pluginName, "idd-factory") &&
            (plugin.SkillSourcesOrEmpty.Count != 2 ||
             !plugin.SkillSourcesOrEmpty.ContainsKey("idd-internal-factory-planner") ||
             !plugin.SkillSourcesOrEmpty.ContainsKey("idd-internal-factory-worker")))
            throw new InvalidOperationException("Factory must package exactly the Planner and Worker internal Skills.");
    }

    private void ValidateSkillReferences(string pluginName, PluginDefinition plugin)
    {
        var destinationsBySkill = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var reference in plugin.SkillReferencesOrEmpty)
        {
            if (!plugin.Skills.Contains(reference.Skill, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Plugin '{pluginName}' has document reference for unowned skill '{reference.Skill}'.");
            }

            var source = ResolveRepositoryPath(reference.Source);
            if (!File.Exists(source))
            {
                if (Directory.Exists(source))
                {
                    throw new InvalidOperationException(
                        $"Plugin '{pluginName}' skill reference source '{reference.Source}' is a directory.");
                }

                throw new InvalidOperationException(
                    $"Plugin '{pluginName}' references missing skill reference source '{reference.Source}'.");
            }

            ValidateUtf8ReferenceSource(pluginName, reference, source);

            var destination = NormalizeSkillReferenceDestination(pluginName, reference);
            if (!destinationsBySkill.TryGetValue(reference.Skill, out var destinations))
            {
                destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                destinationsBySkill.Add(reference.Skill, destinations);
            }

            if (!destinations.Add(destination))
            {
                throw new InvalidOperationException(
                    $"Plugin '{pluginName}' has duplicate skill reference destination '{destination}' for skill '{reference.Skill}'.");
            }
        }
    }

    private string ResolveRepositoryPath(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new InvalidOperationException("Skill reference source is empty.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(
            layout.RepoRoot,
            source.Replace('/', Path.DirectorySeparatorChar)));
        var repoRoot = Path.GetFullPath(layout.RepoRoot);
        var repoRootWithSeparator = repoRoot.EndsWith(Path.DirectorySeparatorChar)
            ? repoRoot
            : repoRoot + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!fullPath.StartsWith(repoRootWithSeparator, comparison) &&
            !string.Equals(fullPath, repoRoot, comparison))
        {
            throw new InvalidOperationException(
                $"Skill reference source '{source}' resolves outside the repository root.");
        }

        return fullPath;
    }

    private static void ValidateUtf8ReferenceSource(
        string pluginName,
        SkillReferenceDefinition reference,
        string source)
    {
        try
        {
            var content = File.ReadAllText(source, StrictUtf8);
            if (content.Contains('\0'))
            {
                throw new InvalidOperationException("contains a NUL character");
            }
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidOperationException(
                $"Plugin '{pluginName}' skill '{reference.Skill}' reference source '{reference.Source}' is not valid UTF-8 text.",
                exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                $"Plugin '{pluginName}' skill '{reference.Skill}' reference source '{reference.Source}' is not valid UTF-8 text.",
                exception);
        }
    }

    private static string NormalizeSkillReferenceDestination(
        string pluginName,
        SkillReferenceDefinition reference)
    {
        try
        {
            return SkillReferencePathValidator.NormalizeDestination(reference.Destination);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"Plugin '{pluginName}' has invalid skill reference destination '{reference.Destination}' for skill '{reference.Skill}': {exception.Message}");
        }
    }
}
