internal static class SkillReferencePathValidator
{
    public static string NormalizeDestination(string destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            throw new ArgumentException("Skill reference destination is empty.", nameof(destination));
        }

        var normalized = destination.Replace('\\', '/');

        if (IsRootedOrDriveQualifiedDestination(normalized))
        {
            throw new ArgumentException(
                "Skill reference destination is rooted or drive-qualified.",
                nameof(destination));
        }

        var segments = normalized.Split('/', StringSplitOptions.None);

        if (segments.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Skill reference destination contains an empty segment.", nameof(destination));
        }

        if (segments.Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("Skill reference destination contains an unsafe segment.", nameof(destination));
        }

        if (StringComparer.OrdinalIgnoreCase.Equals(segments[0], "roles"))
        {
            throw new ArgumentException(
                "Skill reference destination is inside reserved 'roles/' references.",
                nameof(destination));
        }

        return string.Join('/', segments);
    }

    private static bool IsRootedOrDriveQualifiedDestination(string destination)
    {
        if (destination.StartsWith("/", StringComparison.Ordinal))
        {
            return true;
        }

        return destination.Length >= 2 &&
            IsAsciiLetter(destination[0]) &&
            destination[1] == ':';
    }

    private static bool IsAsciiLetter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
}
