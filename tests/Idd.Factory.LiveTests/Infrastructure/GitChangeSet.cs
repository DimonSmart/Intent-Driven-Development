namespace Idd.Factory.LiveTests.Infrastructure;

public sealed record GitChangeSet(IReadOnlyList<string> Paths)
{
    public static GitChangeSet Parse(string porcelain)
    {
        var entries = porcelain.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            if (entry.Length < 4 || entry[2] != ' ')
                throw new InvalidOperationException($"Invalid git status --porcelain=v1 -z entry: '{entry}'.");

            var status = entry[..2];
            paths.Add(Normalize(entry[3..]));
            if (status.Contains('R') || status.Contains('C'))
            {
                if (++index >= entries.Length)
                    throw new InvalidOperationException($"Git status rename/copy entry '{entry}' has no source path.");
                paths.Add(Normalize(entries[index]));
            }
        }

        return new(paths.OrderBy(path => path, StringComparer.Ordinal).ToArray());
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
