using System.Text;

internal sealed class PluginManifestValidator(RepositoryLayout layout)
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

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

            ValidateSkillReferences(pluginName, plugin);

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
                destinations = new HashSet<string>(StringComparer.Ordinal);
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
            _ = File.ReadAllText(source, StrictUtf8);
        }
        catch (DecoderFallbackException exception)
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
        if (string.IsNullOrWhiteSpace(reference.Destination))
        {
            throw new InvalidOperationException(
                $"Plugin '{pluginName}' has an empty skill reference destination for skill '{reference.Skill}'.");
        }

        var normalized = reference.Destination.Replace('\\', '/');

        if (IsAbsoluteReferenceDestination(normalized))
        {
            throw new InvalidOperationException(
                $"Plugin '{pluginName}' has absolute skill reference destination '{reference.Destination}'.");
        }

        var segments = normalized.Split('/', StringSplitOptions.None);

        if (segments.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException(
                $"Plugin '{pluginName}' has invalid skill reference destination '{reference.Destination}'.");
        }

        if (segments.Any(segment => segment is "." or ".."))
        {
            throw new InvalidOperationException(
                $"Plugin '{pluginName}' has unsafe skill reference destination '{reference.Destination}'.");
        }

        if (StringComparer.Ordinal.Equals(segments[0], "roles"))
        {
            throw new InvalidOperationException(
                $"Plugin '{pluginName}' has skill reference destination '{reference.Destination}' inside reserved 'roles/' references.");
        }

        return string.Join('/', segments);
    }

    private static bool IsAbsoluteReferenceDestination(string destination)
    {
        if (destination.StartsWith("/", StringComparison.Ordinal))
        {
            return true;
        }

        return destination.Length >= 3 &&
            IsAsciiLetter(destination[0]) &&
            destination[1] == ':' &&
            destination[2] == '/';
    }

    private static bool IsAsciiLetter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
}
