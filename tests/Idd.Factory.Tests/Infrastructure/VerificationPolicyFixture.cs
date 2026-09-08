namespace Idd.Factory.Tests;

internal static class VerificationPolicyFixture
{
    public static string SingleCheck(
        string id,
        string command,
        string context = "subtask",
        string? timeout = null,
        bool confirmationRequired = false)
    {
        var lines = new List<string>
        {
            "version: 1",
            "checks:",
            $"  {id}:",
            "    run: >-"
        };
        lines.AddRange(command.Replace("\r\n", "\n").Split('\n').Select(line => $"      {line}"));
        if (timeout is not null) lines.Add($"    timeout: {timeout}");
        if (confirmationRequired) lines.Add("    confirmation: required");
        lines.Add("default:");
        lines.Add("  use: []");
        if (context != "default")
        {
            lines.Add($"{context}:");
            lines.Add("  use:");
            lines.Add($"    - {id}");
        }
        else
        {
            lines[^1] = "  use:";
            lines.Add($"    - {id}");
        }
        return string.Join('\n', lines) + "\n";
    }

    public static string Empty() => "version: 1\nchecks: {}\ndefault:\n  use: []\n";
}
