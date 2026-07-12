internal interface IPlatformAdapter
{
    string Platform { get; }
    IReadOnlyList<GeneratedFile> BuildPluginFiles(
        AdapterDefinition adapterDefinition,
        PluginManifest manifest,
        string pluginName,
        PluginDefinition plugin,
        IReadOnlyDictionary<string, SkillDescription> skillDescriptions,
        string version);
}
