internal interface IPlatformAdapter
{
    string Platform { get; }
    GeneratedFile BuildMarketplaceFile(PluginManifest manifest, string version);
    IReadOnlyList<GeneratedFile> BuildPluginFiles(
        AdapterDefinition adapterDefinition,
        PluginManifest manifest,
        string pluginName,
        PluginDefinition plugin,
        IReadOnlyDictionary<string, RoleDefinition> roleDefinitions,
        IReadOnlyDictionary<string, SkillDescription> skillDescriptions,
        string version);
}
