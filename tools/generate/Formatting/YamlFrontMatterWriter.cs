using System.Text.Json;

internal static class YamlFrontMatterWriter
{
    public static string BuildClaudeSkillFrontMatter(
        string skillName,
        SkillDescription skillDescription,
        AdapterConfig adapter,
        IReadOnlyList<string> allowedTools)
    {
        var invocationFields = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["user-invocable"] = skillDescription.Exposure == SkillExposure.Command,
            ["disable-model-invocation"] = skillDescription.Invocation == SkillInvocation.Manual
        };
        var lines = new List<string>
        {
            "---",
            $"name: {ToYamlString(skillName)}",
            $"description: {ToYamlString(skillDescription.Description)}"
        };

        if (skillDescription.Adapters?.TryGetValue(adapter.CodingAgent, out var adapterMetadata) == true &&
            adapterMetadata.Frontmatter is not null)
        {
            if (allowedTools.Count > 0 && adapterMetadata.Frontmatter.ContainsKey("allowed-tools"))
            {
                throw new InvalidOperationException(
                    $"Skill '{skillName}' defines Claude allowed-tools outside its canonical role.");
            }

            foreach (var field in adapterMetadata.Frontmatter)
            {
                if (invocationFields.TryGetValue(field.Key, out var expectedValue))
                {
                    if (field.Value.ValueKind is not JsonValueKind.True and not JsonValueKind.False ||
                        field.Value.GetBoolean() != expectedValue)
                    {
                        throw new InvalidOperationException(
                            $"Skill '{skillName}' adapter '{adapter.CodingAgent}' frontmatter field '{field.Key}' has value {field.Value.GetRawText()}, expected {expectedValue.ToString().ToLowerInvariant()} from canonical invocation/exposure policy.");
                    }

                    continue;
                }

                lines.Add($"{field.Key}: {ToYamlValue(field.Value)}");
            }
        }

        foreach (var field in invocationFields)
        {
            lines.Add($"{field.Key}: {field.Value.ToString().ToLowerInvariant()}");
        }

        if (allowedTools.Count > 0)
        {
            lines.Add($"allowed-tools: [{string.Join(", ", allowedTools)}]");
        }

        lines.Add("---");
        return string.Join(Environment.NewLine, lines);
    }

    public static string BuildCodexSkillFrontMatter(string skillName, SkillDescription skillDescription)
    {
        var lines = new List<string>
        {
            "---",
            $"name: {ToYamlString(skillName)}",
            $"description: {ToYamlString(skillDescription.Description)}",
            "---"
        };

        return string.Join(Environment.NewLine, lines);
    }

    public static string QuoteYamlString(string value) => JsonSerializer.Serialize(value);

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
