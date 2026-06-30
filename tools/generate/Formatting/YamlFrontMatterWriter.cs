using System.Text.Json;

internal static class YamlFrontMatterWriter
{
    public static string BuildSkillFrontMatter(string skillName, SkillDescription skillDescription, AdapterConfig adapter)
    {
        var generatedManualFields = adapter.SupportsManualOnlySkills &&
            skillDescription.Invocation == SkillInvocation.Manual
                ? new Dictionary<string, bool>(StringComparer.Ordinal)
                {
                    ["disable-model-invocation"] = true,
                    ["user-invocable"] = true
                }
                : null;
        var lines = new List<string>
        {
            "---",
            $"name: {ToYamlString(skillName)}",
            $"description: {ToYamlString(skillDescription.Description)}"
        };

        if (skillDescription.Adapters?.TryGetValue(adapter.CodingAgent, out var adapterMetadata) == true &&
            adapterMetadata.Frontmatter is not null)
        {
            foreach (var field in adapterMetadata.Frontmatter)
            {
                if (generatedManualFields is not null &&
                    generatedManualFields.TryGetValue(field.Key, out var expectedValue))
                {
                    if (field.Value.ValueKind is not JsonValueKind.True and not JsonValueKind.False ||
                        field.Value.GetBoolean() != expectedValue)
                    {
                        throw new InvalidOperationException(
                            $"Skill '{skillName}' frontmatter field '{field.Key}' conflicts with manual invocation policy for adapter '{adapter.CodingAgent}'.");
                    }

                    continue;
                }

                lines.Add($"{field.Key}: {ToYamlValue(field.Value)}");
            }
        }

        if (generatedManualFields is not null)
        {
            foreach (var field in generatedManualFields)
            {
                lines.Add($"{field.Key}: true");
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
