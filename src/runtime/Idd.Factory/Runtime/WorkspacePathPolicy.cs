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

    public static string ValidateGitPath(string path)
    {
        if (string.IsNullOrEmpty(path)
            || path.Contains('\0')
            || path.StartsWith("/", StringComparison.Ordinal))
        {
            throw InvalidGitPath(path);
        }

        var segments = path.Split('/');
        if (segments.Length == 0
            || segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw InvalidGitPath(path);
        }

        if (OperatingSystem.IsWindows())
        {
            if (path.Contains('\\'))
                throw InvalidGitPath(path);

            var osPath = path.Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(osPath))
                throw InvalidGitPath(path);
        }

        return path;
    }

    public static string ResolveGitPath(string workspaceRoot, string gitPath)
    {
        var validated = ValidateGitPath(gitPath);
        var relative = validated.Replace('/', Path.DirectorySeparatorChar);
        var root = Path.GetFullPath(workspaceRoot);
        var full = Path.GetFullPath(Path.Combine(root, relative));

        if (!IsInside(root, full))
            throw InvalidGitPath(gitPath);

        return full;
    }

    public static string ValidateArtifactReference(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)
            || reference.Contains('\0')
            || reference.Contains('\\')
            || reference.StartsWith("/", StringComparison.Ordinal)
            || Path.IsPathRooted(reference)
            || (reference.Length >= 2 && char.IsLetter(reference[0]) && reference[1] == ':'))
        {
            throw InvalidArtifactReference(reference);
        }

        var segments = reference.Split('/');
        if (segments.Length == 0
            || segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw InvalidArtifactReference(reference);
        }

        return reference;
    }

    public static string ResolveArtifactReference(string rootDirectory, string reference)
    {
        var validated = ValidateArtifactReference(reference);
        var root = Path.GetFullPath(rootDirectory);
        var full = Path.GetFullPath(Path.Combine(
            root,
            validated.Replace('/', Path.DirectorySeparatorChar)));

        if (!IsInside(root, full))
            throw InvalidArtifactReference(reference);

        return full;
    }

    public static IReadOnlyList<string> NormalizeChangedPaths(IEnumerable<string> paths) =>
        paths.Select(ValidateGitPath)
            .Where(path => !IsOperationalArtifact(path))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    public static bool IsOperationalArtifact(string gitPath)
    {
        var segments = gitPath.Split('/');
        if (segments.Length >= 2
            && segments[0].Equals(".idd", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals("factory", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return segments.Any(ExcludedSegments.Contains);
    }

    private static bool IsInside(string root, string fullPath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            || root.EndsWith(Path.AltDirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(rootWithSeparator, comparison);
    }

    private static FactoryStateException InvalidGitPath(string path) =>
        new("CORRUPT_FACTORY_STATE", $"Invalid Git repository-relative path '{path}'.");

    private static FactoryStateException InvalidArtifactReference(string reference) =>
        new("CORRUPT_FACTORY_STATE", $"Invalid Factory artifact reference '{reference}'.");
}
