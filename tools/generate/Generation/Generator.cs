using System.Text;

internal sealed class Generator(RepositoryLayout layout)
{
    public IReadOnlyList<string> Run(bool checkOnly, string manifestVersion)
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
        new PluginManifestValidator(layout).Validate(pluginManifest);

        foreach (var adapterDefinition in adapterDefinitions)
        {
            var platformAdapter = PlatformAdapterFactory.Create(adapterDefinition.Config.CodingAgent);
            var expectedFiles = BuildMarketplaceFiles(platformAdapter, adapterDefinition, pluginManifest, skillDescriptions, manifestVersion);
            var outputRoot = Path.Combine(layout.MarketplaceRoot, adapterDefinition.Config.CodingAgent);

            if (checkOnly)
            {
                errors.AddRange(GeneratedOutputChecker.CheckFiles(outputRoot, expectedFiles));
                continue;
            }

            GeneratedOutputWriter.Write(outputRoot, expectedFiles);
        }

        return errors;
    }

    private static IReadOnlyList<GeneratedFile> BuildMarketplaceFiles(
        IPlatformAdapter platformAdapter,
        AdapterDefinition adapterDefinition,
        PluginManifest pluginManifest,
        IReadOnlyDictionary<string, SkillDescription> skillDescriptions,
        string version)
    {
        var files = new List<GeneratedFile>
        {
            new("marketplace.json", MarketplaceBuilder.Build(platformAdapter.Platform, pluginManifest))
        };

        foreach (var (pluginName, plugin) in pluginManifest.Plugins.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            foreach (var file in platformAdapter.BuildPluginFiles(adapterDefinition, pluginManifest, pluginName, plugin, skillDescriptions, version))
            {
                files.Add(new GeneratedFile(Path.Combine("plugins", pluginName, file.RelativePath), file.Content));
            }
        }

        return files;
    }
}
