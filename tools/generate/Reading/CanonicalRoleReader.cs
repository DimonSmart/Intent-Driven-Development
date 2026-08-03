using System.Text;

internal sealed class CanonicalRoleReader
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    public RoleDefinition Read(string name, string path)
    {
        string content;
        try
        {
            content = File.ReadAllText(path, StrictUtf8);
        }
        catch (Exception exception) when (exception is IOException or DecoderFallbackException)
        {
            throw Error(name, path, "could not be read as UTF-8 Markdown", exception);
        }

        var lines = content.ReplaceLineEndings("\n").Split('\n');
        if (lines.Length == 0 || !StringComparer.Ordinal.Equals(lines[0], "---"))
        {
            throw Error(name, path, "does not define YAML front matter");
        }

        var frontMatterEnd = Array.FindIndex(lines, 1, line => StringComparer.Ordinal.Equals(line, "---"));
        if (frontMatterEnd < 0)
        {
            throw Error(name, path, "has unterminated YAML front matter");
        }

        var tools = ReadTools(name, path, lines[1..frontMatterEnd]);
        var instructions = string.Join("\n", lines[(frontMatterEnd + 1)..]).Trim();
        if (instructions.Length == 0)
        {
            throw Error(name, path, "has an empty Markdown instruction");
        }

        return new RoleDefinition(name, tools, instructions);
    }

    private static IReadOnlyList<RoleTool> ReadTools(string name, string path, string[] frontMatter)
    {
        var toolsLine = Array.FindIndex(frontMatter, line => StringComparer.Ordinal.Equals(line.Trim(), "tools:"));
        if (toolsLine < 0)
        {
            throw Error(name, path, "does not define required 'tools' metadata");
        }

        var tools = new List<RoleTool>();
        for (var index = toolsLine + 1; index < frontMatter.Length; index++)
        {
            var line = frontMatter[index];
            if (line.Length == 0 || char.IsWhiteSpace(line[0]) && line.Trim().Length == 0)
            {
                continue;
            }

            var trimmed = line.TrimStart();
            if (!char.IsWhiteSpace(line[0]) || !trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                break;
            }

            var value = trimmed[2..].Trim();
            if (value.Length == 0 || value.Contains(' ') || value.Contains('\t'))
            {
                throw Error(name, path, "has invalid YAML 'tools' metadata");
            }

            if (!RoleToolNames.TryParse(value, out var tool))
            {
                throw Error(name, path, $"references unknown tool '{value}'");
            }

            if (tools.Contains(tool))
            {
                throw Error(name, path, $"contains duplicate tool '{value}'");
            }

            tools.Add(tool);
        }

        if (tools.Count == 0)
        {
            throw Error(name, path, "defines an empty 'tools' list");
        }

        return tools;
    }

    private static InvalidOperationException Error(string name, string path, string reason, Exception? innerException = null) =>
        new($"Role '{name}' at '{path}' {reason}.", innerException);
}
