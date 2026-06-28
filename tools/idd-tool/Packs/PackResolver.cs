internal sealed class PackResolver
{
    public IReadOnlyList<string> Resolve(Manifest manifest, IReadOnlyList<string> requestedPacks)
    {
        PackManifestValidator.Validate(manifest);
        var selected = new HashSet<string>(StringComparer.Ordinal);

        if (requestedPacks.Count == 0)
        {
            foreach (var (packName, pack) in manifest.Packs)
            {
                if (pack.Default)
                {
                    AddPackWithDependencies(manifest, packName, selected);
                }
            }
        }
        else
        {
            foreach (var packName in requestedPacks.Distinct(StringComparer.Ordinal))
            {
                if (!manifest.Packs.ContainsKey(packName))
                {
                    throw new ToolException($"Unknown pack: {packName}" + Environment.NewLine + $"Available packs: {string.Join(", ", manifest.Packs.Keys.OrderBy(name => name, StringComparer.Ordinal))}");
                }

                AddPackWithDependencies(manifest, packName, selected);
            }
        }

        return selected.OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    public static bool IsDefaultPackSelection(Manifest manifest, IReadOnlyList<string> selectedPacks)
    {
        var defaultPacks = manifest.Packs
            .Where(item => item.Value.Default)
            .Select(item => item.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        return defaultPacks.SequenceEqual(selectedPacks.OrderBy(name => name, StringComparer.Ordinal), StringComparer.Ordinal);
    }

    private static void AddPackWithDependencies(Manifest manifest, string packName, HashSet<string> selected)
    {
        foreach (var dependency in manifest.Packs[packName].Requires)
        {
            AddPackWithDependencies(manifest, dependency, selected);
        }

        selected.Add(packName);
    }
}
