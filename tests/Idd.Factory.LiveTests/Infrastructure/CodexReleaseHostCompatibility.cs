namespace Idd.Factory.LiveTests.Infrastructure;

public static class CodexReleaseHostCompatibility
{
    public static readonly Version MinimumVersion = new(0, 148, 0);

    public static void RequireSupported(string versionOutput)
    {
        const string prefix = "codex-cli ";
        var normalized = versionOutput.Trim();
        var versionText = normalized.StartsWith(prefix, StringComparison.Ordinal)
            ? normalized[prefix.Length..]
            : string.Empty;
        if (!normalized.StartsWith(prefix, StringComparison.Ordinal)
            || versionText.Split('.').Length != 3
            || versionText.Any(character => !char.IsAsciiDigit(character) && character != '.')
            || !Version.TryParse(versionText, out var actualVersion))
        {
            throw new InvalidOperationException(
                $"Release certification requires a stable Codex CLI version in the form '{prefix}MAJOR.MINOR.PATCH'; actual output was '{normalized}'.");
        }

        if (actualVersion < MinimumVersion)
        {
            throw new InvalidOperationException(
                $"Release certification requires Codex CLI {MinimumVersion} or newer because older hosts can leak descendants of a stopped local MCP server; actual version was {actualVersion}.");
        }
    }
}
