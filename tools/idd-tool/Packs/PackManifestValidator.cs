internal static class PackManifestValidator
{
    public static void Validate(Manifest manifest)
    {
        foreach (var (packName, pack) in manifest.Packs)
        {
            foreach (var dependency in pack.Requires)
            {
                if (!manifest.Packs.ContainsKey(dependency))
                {
                    throw new ToolException($"Pack '{packName}' requires unknown pack '{dependency}'.");
                }
            }
        }

        foreach (var packName in manifest.Packs.Keys)
        {
            ValidatePackDependencyAcyclic(manifest, packName, [], []);
        }
    }

    private static void ValidatePackDependencyAcyclic(
        Manifest manifest,
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
            throw new ToolException($"Pack dependency cycle includes '{packName}'.");
        }

        foreach (var dependency in manifest.Packs[packName].Requires)
        {
            ValidatePackDependencyAcyclic(manifest, dependency, visiting, visited);
        }

        visiting.Remove(packName);
        visited.Add(packName);
    }
}
