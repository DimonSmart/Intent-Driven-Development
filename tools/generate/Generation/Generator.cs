using System.Text;

internal sealed class Generator(RepositoryLayout layout)
{
    public IReadOnlyList<string> Run(bool checkOnly, string manifestVersion, string? outputDirectory = null)
    {
        if (Environment.ExitCode != 0)
        {
            return ["Invalid generator arguments."];
        }

        var errors = new List<string>();
        var adapterReader = new AdapterReader();
        var adapterDefinitions = Directory
            .GetDirectories(layout.AdaptersRoot)
            .OrderBy(Path.GetFileName)
            .Select(adapterDir => new AdapterDefinition(adapterDir, adapterReader.Read(adapterDir)))
            .ToArray();
        var supportedCodingAgents = adapterDefinitions
            .Select(definition => definition.Config.CodingAgent)
            .ToHashSet(StringComparer.Ordinal);
        var skillDescriptions = new SkillDescriptionReader().Read(layout.SkillDescriptionsPath, supportedCodingAgents);
        var pluginManifest = new PluginManifestReader(layout).Read();
        var roleDefinitions = ReadRoleDefinitions(pluginManifest);
        new PluginManifestValidator(layout).Validate(pluginManifest, roleDefinitions);

        var expectedFiles = new List<GeneratedFile>();
        foreach (var adapterDefinition in adapterDefinitions)
        {
            var platformAdapter = PlatformAdapterFactory.Create(adapterDefinition.Config.CodingAgent);
            expectedFiles.AddRange(BuildMarketplaceFiles(platformAdapter, adapterDefinition, pluginManifest, roleDefinitions, skillDescriptions, manifestVersion));
        }

        if (checkOnly)
        {
            errors.AddRange(GeneratedOutputChecker.CheckFiles(outputDirectory ?? layout.MarketplaceRoot, expectedFiles));
            return errors;
        }

        GeneratedOutputWriter.Write(outputDirectory ?? layout.MarketplaceRoot, expectedFiles);
        return errors;
    }

    private IReadOnlyDictionary<string, RoleDefinition> ReadRoleDefinitions(PluginManifest manifest)
    {
        var reader = new CanonicalRoleReader();
        return manifest.Plugins.Values
            .SelectMany(plugin => plugin.Roles)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToDictionary(
                name => name,
                name => reader.Read(name, Path.Combine(layout.FactoryRolesRoot, name + ".md")),
                StringComparer.Ordinal);
    }

    private static IReadOnlyList<GeneratedFile> BuildMarketplaceFiles(
        IPlatformAdapter platformAdapter,
        AdapterDefinition adapterDefinition,
        PluginManifest pluginManifest,
        IReadOnlyDictionary<string, RoleDefinition> roleDefinitions,
        IReadOnlyDictionary<string, SkillDescription> skillDescriptions,
        string version)
    {
        var files = new List<GeneratedFile> { platformAdapter.BuildMarketplaceFile(pluginManifest, version) };

        foreach (var (pluginName, plugin) in pluginManifest.Plugins.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            foreach (var file in platformAdapter.BuildPluginFiles(adapterDefinition, pluginManifest, pluginName, plugin, roleDefinitions, skillDescriptions, version))
            {
                files.Add(new GeneratedFile(Path.Combine("plugins", platformAdapter.Platform, pluginName, file.RelativePath), file.Content));
            }
        }

        return files;
    }
}
