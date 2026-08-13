namespace Idd.Factory.Agents;

internal static class CodexProcessEnvironment
{
    public static CodexPathPreparation PrepareSandboxCompatiblePath(string path, bool isWindows)
    {
        if (!isWindows)
            return new(path, 0);

        var retainedEntries = new List<string>();
        var removedEntries = 0;
        foreach (var entry in path.Split(';'))
        {
            var trimmed = entry.Trim();
            if (trimmed.Length == 0)
                continue;
            if (trimmed.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase))
            {
                removedEntries++;
                continue;
            }
            retainedEntries.Add(trimmed);
        }

        return new(string.Join(';', retainedEntries), removedEntries);
    }
}

internal sealed record CodexPathPreparation(string Path, int WindowsAppsPathEntriesRemoved);
