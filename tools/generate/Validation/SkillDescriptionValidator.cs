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
            throw new InvalidOperationException($"Invalid skill description for '{skillName}' in {path}: expected an object with 'description' and required 'exposure' (command, workflow); string entries are not supported.");
        }

        if (!value.TryGetProperty("description", out var descriptionElement) ||
            descriptionElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Invalid skill description for {skillName} in {path}: description is required.");
        }
    }

    public static void GuardUniqueProperties(string path, string skillName, JsonElement element, string context)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name))
                throw new InvalidOperationException(
                    $"Duplicate JSON property '{property.Name}' in {context} for skill '{skillName}' in {path}.");
        }
    }

    public static void GuardSkillFields(string path, string skillName, JsonElement element)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name is not ("description" or "invocation" or "exposure" or "interface" or "adapters"))
                throw new InvalidOperationException(
                    $"Invalid skill description for '{skillName}' in {path}: unknown property '{property.Name}'. Allowed properties: description, invocation, exposure, interface, adapters.");
        }
    }

    public static void GuardAdapterMetadataFields(string path, string skillName, string adapterName, JsonElement element)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name != "frontmatter")
                throw new InvalidOperationException(
                    $"Invalid adapter metadata for skill '{skillName}' in {path}: unknown property 'adapters.{adapterName}.{property.Name}'. Allowed property: frontmatter.");
        }
    }

    public static void GuardInvocationExposure(string skillName, SkillInvocation invocation, SkillExposure exposure)
    {
        if (invocation == SkillInvocation.Manual && exposure == SkillExposure.Workflow)
            throw new InvalidOperationException(
                $"Invalid invocation/exposure for skill '{skillName}': manual + workflow has no activation path.");
    }

    public static void GuardInterfaceObject(string path, string skillName, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException(
                $"Invalid interface for skill '{skillName}' in {path}: expected an object.");
    }

    public static void GuardInterfaceFields(string path, string skillName, JsonElement element)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name is not ("displayName" or "shortDescription"))
                throw new InvalidOperationException(
                    $"Invalid interface property '{property.Name}' for skill '{skillName}' in {path}.");
        }

        foreach (var name in new[] { "displayName", "shortDescription" })
        {
            if (!element.TryGetProperty(name, out var field) ||
                field.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(field.GetString()))
            {
                throw new InvalidOperationException(
                    $"Invalid interface.{name} for skill '{skillName}' in {path}: a non-empty string is required.");
            }
        }
    }

    public static void GuardPublicSkillName(string path, string skillName)
    {
        if (!PublicSkillNamePattern().IsMatch(skillName))
        {
            throw new InvalidOperationException(
                $"Invalid public skill name '{skillName}' in {path}: expected idd-help, idd-skip, idd-route, idd-project-init, idd-glossary-build, idd-verification-configure, idd-engineering-change, or idd-<area>-<action> with area intent, code, or factory.");
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

    [GeneratedRegex("^(?:idd-help|idd-skip|idd-route|idd-project-init|idd-glossary-build|idd-verification-configure|idd-engineering-change|idd-(intent|code|factory)-[a-z0-9]+(?:-[a-z0-9]+)*)$", RegexOptions.CultureInvariant)]
    private static partial Regex PublicSkillNamePattern();
}
