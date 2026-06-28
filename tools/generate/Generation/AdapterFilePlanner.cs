internal sealed class AdapterFilePlanner(RepositoryLayout layout)
{
    public IReadOnlyList<GeneratedFile> BuildFiles(
        AdapterDefinition adapterDefinition,
        IReadOnlySet<string> knownAdapterNames,
        PackManifest packManifest)
    {
        var adapter = adapterDefinition.Config;
        var files = new List<GeneratedFile>();
        var entryPoint = new EntryPointBuilder(layout).Build(adapterDefinition.Directory, adapter);
        files.Add(new GeneratedFile(adapter.EntryPoint, entryPoint));

        if (adapter.SupportsSkills)
        {
            files.AddRange(new SkillFileBuilder(layout).Build(adapter, knownAdapterNames, packManifest));
        }

        return files;
    }
}
