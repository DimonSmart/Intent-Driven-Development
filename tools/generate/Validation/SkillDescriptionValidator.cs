using System.Text.Json;
using System.Text.RegularExpressions;

internal static partial class SkillDescriptionValidator
{
    public static void GuardRootObject(string path, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Invalid skill descriptions in {path}: root must be a JSON object.");
        }
    }

    public static void GuardDescriptionObject(string path, string skillName, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Invalid skill description for {skillName} in {path}: expected string or object.");
        }

        if (!value.TryGetProperty("description", out var descriptionElement) ||
            descriptionElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Invalid skill description for {skillName} in {path}: description is required.");
        }
    }

    public static void GuardPublicSkillName(string path, string skillName)
    {
        if (!PublicSkillNamePattern().IsMatch(skillName))
        {
            throw new InvalidOperationException(
                $"Invalid public skill name '{skillName}' in {path}: expected idd-skip or idd-<area>-<action> with area intent, code, or factory.");
        }
    }

    public static void GuardDescription(string path, string skillName, string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new InvalidOperationException($"Invalid skill description for {skillName} in {path}: description cannot be empty.");
        }
    }

    public static void GuardAdaptersObject(string path, string skillName, JsonElement adaptersElement)
    {
        if (adaptersElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Invalid skill description for {skillName} in {path}: adapters must be an object.");
        }
    }

    public static void GuardKnownAdapter(string path, string skillName, string adapterName, IReadOnlySet<string> knownAdapterNames)
    {
        if (!knownAdapterNames.Contains(adapterName))
        {
            throw new InvalidOperationException(
                $"Invalid skill description for {skillName} in {path}: unknown adapter '{adapterName}'.");
        }
    }

    public static void GuardAdapterMetadataObject(string path, string skillName, string adapterName, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"Invalid metadata for {skillName}/{adapterName} in {path}: adapter metadata must be an object.");
        }
    }

    public static void GuardFrontMatterObject(string path, string skillName, string adapterName, JsonElement frontMatterElement)
    {
        if (frontMatterElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"Invalid frontmatter for {skillName}/{adapterName} in {path}: frontmatter must be an object.");
        }
    }

    public static void GuardFrontMatterField(
        string path,
        string skillName,
        string adapterName,
        string fieldName,
        JsonElement value)
    {
        if (StringComparer.Ordinal.Equals(fieldName, "name") ||
            StringComparer.Ordinal.Equals(fieldName, "description"))
        {
            throw new InvalidOperationException(
                $"Invalid frontmatter for {skillName}/{adapterName} in {path}: '{fieldName}' is generated automatically and cannot be overridden.");
        }

        GuardSupportedFrontMatterValue(path, skillName, adapterName, fieldName, value);
    }

    private static void GuardSupportedFrontMatterValue(
        string path,
        string skillName,
        string adapterName,
        string fieldName,
        JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.String or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Number)
        {
            return;
        }

        if (value.ValueKind == JsonValueKind.Array &&
            value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Invalid frontmatter for {skillName}/{adapterName} in {path}: '{fieldName}' must be a string, bool, number, or string array.");
    }

    [GeneratedRegex("^(?:idd-skip|idd-(intent|code|factory)-[a-z0-9]+(?:-[a-z0-9]+)*)$", RegexOptions.CultureInvariant)]
    private static partial Regex PublicSkillNamePattern();
}
