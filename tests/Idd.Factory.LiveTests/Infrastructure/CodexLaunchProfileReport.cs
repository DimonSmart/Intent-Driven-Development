using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Idd.Factory.LiveTests.Environments;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed record CodexLaunchProfileAttempt(
    string ProfileName,
    string AttemptDirectory,
    string CommandLine,
    string CodexVersion,
    int? ExitCode,
    bool? TimedOut,
    string StderrPath,
    string EventsPath,
    bool CreatedFileExists,
    string? CreatedFileContent,
    bool ExistingFileExists,
    string? ExistingFileContent,
    bool Passed);

public static partial class CodexLaunchProfileReport
{
    public static string FormatCommandLine(string executable, IEnumerable<string> arguments)
        => RedactSecrets(string.Join(" ", new[] { executable }.Concat(arguments).Select(QuoteArgument)));

    public static async Task WriteAsync(string repositoryRoot, string discoveryId, CodexLaunchProfileAttempt attempt, CancellationToken cancellationToken)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var attemptPath = Path.Combine(attempt.AttemptDirectory, "launch-profile-attempt.json");
        await File.WriteAllTextAsync(attemptPath, JsonSerializer.Serialize(attempt, options) + Environment.NewLine, cancellationToken);

        var discoveryDirectory = Directory.GetParent(Directory.GetParent(attempt.AttemptDirectory)!.FullName)!.FullName;
        var attempts = Directory.EnumerateFiles(discoveryDirectory, "launch-profile-attempt.json", SearchOption.AllDirectories)
            .Select(path => JsonSerializer.Deserialize<CodexLaunchProfileAttempt>(File.ReadAllText(path)))
            .Where(result => result is not null)
            .Cast<CodexLaunchProfileAttempt>()
            .GroupBy(result => result.ProfileName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(result => result.AttemptDirectory, StringComparer.Ordinal).Last(), StringComparer.Ordinal);
        var selected = LocalFactoryEvalEnvironment.LaunchProfileDiscoveryOrder
            .Select(name => attempts.GetValueOrDefault(name))
            .FirstOrDefault(result => result?.Passed == true)?.ProfileName;

        var report = new StringBuilder()
            .AppendLine("# Codex launch profile report")
            .AppendLine()
            .AppendLine($"Discovery: `{discoveryId}`")
            .AppendLine($"Selected profile: {(selected is null ? "none" : $"`{selected}`")}")
            .AppendLine()
            .AppendLine("| Profile | Result | Exit code | Timeout |")
            .AppendLine("|---|---:|---:|---:|");
        foreach (var profileName in LocalFactoryEvalEnvironment.LaunchProfileDiscoveryOrder)
        {
            if (!attempts.TryGetValue(profileName, out var result)) continue;
            report.AppendLine($"| `{profileName}` | {(result.Passed ? "PASS" : "FAIL")} | {result.ExitCode?.ToString() ?? "unavailable"} | {result.TimedOut?.ToString() ?? "unavailable"} |");
        }

        foreach (var profileName in LocalFactoryEvalEnvironment.LaunchProfileDiscoveryOrder)
        {
            if (!attempts.TryGetValue(profileName, out var result)) continue;
            report.AppendLine()
                .AppendLine($"## {profileName}")
                .AppendLine()
                .AppendLine($"- Result: {(result.Passed ? "PASS" : "FAIL")}")
                .AppendLine($"- Codex version: `{EscapeMarkdown(result.CodexVersion)}`")
                .AppendLine($"- Exit code: {result.ExitCode?.ToString() ?? "unavailable"}")
                .AppendLine($"- Timeout: {result.TimedOut?.ToString() ?? "unavailable"}")
                .AppendLine($"- Command: `{EscapeMarkdown(result.CommandLine)}`")
                .AppendLine($"- Attempt directory: `{Path.GetRelativePath(repositoryRoot, result.AttemptDirectory)}`")
                .AppendLine($"- stderr: `{Path.GetRelativePath(repositoryRoot, result.StderrPath)}`")
                .AppendLine($"- events.jsonl: `{Path.GetRelativePath(repositoryRoot, result.EventsPath)}`")
                .AppendLine($"- codex-write-probe.txt: {(result.CreatedFileExists ? $"present, `{EscapeMarkdown(result.CreatedFileContent ?? string.Empty)}`" : "missing")}")
                .AppendLine($"- existing.txt: {(result.ExistingFileExists ? $"present, `{EscapeMarkdown(result.ExistingFileContent ?? string.Empty)}`" : "missing")}");
        }

        var reportPath = Path.Combine(repositoryRoot, "artifacts", "factory-evals", "codex-launch-profile-report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        await File.WriteAllTextAsync(reportPath, report.ToString(), cancellationToken);
    }

    private static string QuoteArgument(string argument)
        => argument.Length > 0 && !argument.Any(char.IsWhiteSpace) && !argument.Contains('"')
            ? argument
            : $"\"{argument.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    internal static string RedactSecrets(string value)
        => SecretPattern().Replace(value, match => match.Groups[1].Value + "=<redacted>");

    private static string EscapeMarkdown(string value) => value.Replace("`", "'").Replace("\r", "\\r").Replace("\n", "\\n");

    [GeneratedRegex("(?i)\\b([a-z0-9_-]*(?:api[_-]?key|token|authorization|password))=([^\\s]+)")]
    private static partial Regex SecretPattern();
}
