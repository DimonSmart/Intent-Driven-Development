using Idd.Factory.State;

namespace Idd.Factory.Runtime;

internal static class WorkspacePathPolicy
{
    private static readonly HashSet<string> ExcludedSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        "bin",
        "obj",
        "node_modules",
        ".angular",
        ".cache",
        "dist",
        "TestResults"
    };

    public static string Canonicalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw Invalid(path);

        var normalized = path.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];

        if (normalized.Length == 0
            || normalized.StartsWith('/', StringComparison.Ordinal)
            || Path.IsPathRooted(path)
            || (normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':'))
        {
            throw Invalid(path);
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
            throw Invalid(path);

        return string.Join('/', segments);
    }

    public static IReadOnlyList<string> NormalizeChangedPaths(IEnumerable<string> paths) =>
        paths.Select(Canonicalize)
            .Where(path => !IsOperationalArtifact(path))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    public static bool IsOperationalArtifact(string canonicalPath)
    {
        var segments = canonicalPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2
            && segments[0].Equals(".idd", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals("factory", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return segments.Any(ExcludedSegments.Contains);
    }

    private static FactoryStateException Invalid(string path) =>
        new("CORRUPT_FACTORY_STATE", $"Invalid repository-relative changed path '{path}'.");
}
