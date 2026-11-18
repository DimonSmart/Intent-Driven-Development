using System.Text.Json;
using System.Text.RegularExpressions;

namespace Idd.Factory.LiveTests.Tests;

/// <summary>
/// Proves that a child received the complete contents of an installed Skill.
/// A tool-call request on its own (including a rejected Get-Content) is not proof.
/// </summary>
internal static class NativeSkillReadEvidence
{
    internal sealed record Result(bool Confirmed, int ReadAttempts, int ToolResponses, int RejectedResponses);

    internal static Result Inspect(string rolloutPath, string installedSkillPath, string expectedContent)
    {
        if (string.IsNullOrWhiteSpace(expectedContent))
            return new Result(false, 0, 0, 0);

        var calls = new Dictionary<string, string>(StringComparer.Ordinal);
        var responses = new Dictionary<string, string>(StringComparer.Ordinal);
        var normalizedPath = NormalizePath(installedSkillPath);

        foreach (var line in File.ReadLines(rolloutPath))
        {
            try
            {
                using var json = JsonDocument.Parse(line);
                var root = json.RootElement;
                if (!GetString(root, "type").Equals("response_item", StringComparison.Ordinal) ||
                    !root.TryGetProperty("payload", out var item) ||
                    item.ValueKind != JsonValueKind.Object)
                    continue;

                var type = GetString(item, "type");
                var callId = GetString(item, "call_id");
                if (callId.Length == 0)
                    continue;

                if (type is "custom_tool_call" or "function_call")
                {
                    var command = GetString(item, "input");
                    if (command.Length == 0)
                        command = GetString(item, "arguments");
                    if (IsReadCommand(command, normalizedPath))
                        calls[callId] = command;
                }
                else if (type is "custom_tool_call_output" or "function_call_output" &&
                         item.TryGetProperty("output", out var output))
                {
                    responses[callId] = output.ValueKind switch
                    {
                        JsonValueKind.String => output.GetString() ?? "",
                        JsonValueKind.Array => string.Join("\n", output.EnumerateArray()
                            .Where(part => part.ValueKind == JsonValueKind.Object)
                            .Select(part => GetString(part, "text"))),
                        _ => ""
                    };
                }
            }
            catch (JsonException)
            {
                // Malformed or truncated native trace lines cannot prove a read.
            }
        }

        var completed = 0;
        var rejected = 0;
        var expected = expectedContent.ReplaceLineEndings("\n");
        foreach (var id in calls.Keys)
        {
            if (!responses.TryGetValue(id, out var response))
                continue;
            completed++;
            if (response.Contains("blocked by policy", StringComparison.OrdinalIgnoreCase) ||
                response.Contains("Script failed", StringComparison.OrdinalIgnoreCase) ||
                response.Contains("exec_command failed", StringComparison.OrdinalIgnoreCase))
            {
                rejected++;
                continue;
            }

            if (response.ReplaceLineEndings("\n").Contains(expected, StringComparison.Ordinal))
                return new Result(true, calls.Count, completed, rejected);
        }

        return new Result(false, calls.Count, completed, rejected);
    }

    private static bool IsReadCommand(string command, string normalizedPath) =>
        NormalizePath(command).Contains(normalizedPath, StringComparison.OrdinalIgnoreCase) &&
        Regex.IsMatch(command,
            @"(?<![\w-])(?:Get-Content|cat|type|more|ReadAllText|read_file|read_text_file)(?![\w-])",
            RegexOptions.IgnoreCase);

    private static string NormalizePath(string path) =>
        path.Replace(@"\\", "/", StringComparison.Ordinal).Replace('\\', '/');

    private static string GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
