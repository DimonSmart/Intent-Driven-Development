using System.Text.Json;

internal static class YamlFrontMatterWriter
{
    public static string BuildSkillFrontMatter(string skillName, SkillDescription skillDescription, string adapterName)
    {
        var lines = new List<string>
        {
            "---",
            $"name: {ToYamlString(skillName)}",
            $"description: {ToYamlString(skillDescription.Description)}"
        };

        if (skillDescription.Adapters?.TryGetValue(adapterName, out var adapterMetadata) == true &&
            adapterMetadata.Frontmatter is not null)
        {
            foreach (var field in adapterMetadata.Frontmatter)
            {
                lines.Add($"{field.Key}: {ToYamlValue(field.Value)}");
            }
        }

        lines.Add("---");
        return string.Join(Environment.NewLine, lines);
    }

    private static string ToYamlValue(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => ToYamlString(value.GetString() ?? ""),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.Array => "[" + string.Join(", ", value.EnumerateArray().Select(item => ToYamlString(item.GetString() ?? ""))) + "]",
            _ => throw new InvalidOperationException($"Unsupported YAML frontmatter value: {value.ValueKind}.")
        };

    private static string ToYamlString(string value)
    {
        if (NeedsQuotedYamlString(value))
        {
            return JsonSerializer.Serialize(value);
        }

        return value;
    }

    private static bool NeedsQuotedYamlString(string value)
    {
        if (value.Length == 0 || !StringComparer.Ordinal.Equals(value, value.Trim()))
        {
            return true;
        }

        return value.Any(character => character is ':' or '[' or ']' or '{' or '}' or '#' or '\r' or '\n' or '"' or '\'');
    }
}
