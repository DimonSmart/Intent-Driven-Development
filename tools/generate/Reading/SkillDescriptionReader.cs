using System.Text.Json;

internal sealed class SkillDescriptionReader
{
    public IReadOnlyDictionary<string, SkillDescription> Read(string path, IReadOnlySet<string> knownAdapterNames)
    {
        var json = RequiredFileReader.Read(path);
        using var document = JsonDocument.Parse(json);
        SkillDescriptionValidator.GuardRootObject(path, document.RootElement);

        SkillDescriptionValidator.GuardUniqueProperties(path, "(root)", document.RootElement, "skill inventory");

        var descriptions = new Dictionary<string, SkillDescription>(StringComparer.Ordinal);
        foreach (var skillProperty in document.RootElement.EnumerateObject())
        {
            SkillDescriptionValidator.GuardPublicSkillName(path, skillProperty.Name);
            descriptions.Add(
                skillProperty.Name,
                ReadSkillDescription(path, skillProperty.Name, skillProperty.Value, knownAdapterNames));
        }

        return descriptions;
    }

    private static SkillDescription ReadSkillDescription(
        string path,
        string skillName,
        JsonElement value,
        IReadOnlySet<string> knownAdapterNames)
    {
        SkillDescriptionValidator.GuardDescriptionObject(path, skillName, value);

        var objectDescription = value.GetProperty("description").GetString();
        SkillDescriptionValidator.GuardDescription(path, skillName, objectDescription);
        SkillDescriptionValidator.GuardUniqueProperties(path, skillName, value, "skill description");
        SkillDescriptionValidator.GuardSkillFields(path, skillName, value);
        var invocation = ReadInvocation(skillName, value);
        var exposure = ReadExposure(path, skillName, value);
        SkillDescriptionValidator.GuardInvocationExposure(skillName, invocation, exposure);
        var skillInterface = ReadInterface(path, skillName, value);

        IReadOnlyDictionary<string, AdapterSkillMetadata>? adapters = null;
        if (value.TryGetProperty("adapters", out var adaptersElement))
        {
            SkillDescriptionValidator.GuardAdaptersObject(path, skillName, adaptersElement);

            SkillDescriptionValidator.GuardUniqueProperties(path, skillName, adaptersElement, "adapters");
            var adapterMetadata = new Dictionary<string, AdapterSkillMetadata>(StringComparer.Ordinal);
            foreach (var adapterProperty in adaptersElement.EnumerateObject())
            {
                SkillDescriptionValidator.GuardKnownAdapter(path, skillName, adapterProperty.Name, knownAdapterNames);
                adapterMetadata.Add(
                    adapterProperty.Name,
                    ReadAdapterSkillMetadata(path, skillName, adapterProperty.Name, adapterProperty.Value));
            }

            adapters = adapterMetadata;
        }

        return new SkillDescription(objectDescription!, invocation, exposure, skillInterface, adapters);
    }

    private static SkillInvocation ReadInvocation(string skillName, JsonElement value)
    {
        if (!value.TryGetProperty("invocation", out var invocationElement) ||
            invocationElement.ValueKind == JsonValueKind.Null)
        {
            return SkillInvocation.Auto;
        }

        if (invocationElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                $"Unsupported invocation value for skill '{skillName}': '{invocationElement.GetRawText()}'. Allowed values: auto, manual.");
        }

        return invocationElement.GetString() switch
        {
            "auto" => SkillInvocation.Auto,
            "manual" => SkillInvocation.Manual,
            var unsupported => throw new InvalidOperationException(
                $"Unsupported invocation value for skill '{skillName}': '{unsupported}'. Allowed values: auto, manual.")
        };
    }

    private static SkillExposure ReadExposure(string path, string skillName, JsonElement value)
    {
        if (!value.TryGetProperty("exposure", out var element))
            throw new InvalidOperationException(
                $"Invalid skill description for '{skillName}' in {path}: required property 'exposure' is missing. Allowed values: command, workflow.");

        if (element.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException(
                $"Invalid exposure for skill '{skillName}': {element.GetRawText()}. Allowed values: command, workflow.");

        return element.GetString() switch
        {
            "command" => SkillExposure.Command,
            "workflow" => SkillExposure.Workflow,
            var invalid => throw new InvalidOperationException(
                $"Invalid exposure for skill '{skillName}': '{invalid}'. Allowed values: command, workflow.")
        };
    }

    private static SkillInterfaceMetadata? ReadInterface(string path, string skillName, JsonElement value)
    {
        if (!value.TryGetProperty("interface", out var element))
            return null;

        SkillDescriptionValidator.GuardInterfaceObject(path, skillName, element);
        SkillDescriptionValidator.GuardUniqueProperties(path, skillName, element, "interface");
        SkillDescriptionValidator.GuardInterfaceFields(path, skillName, element);
        return new SkillInterfaceMetadata(
            element.GetProperty("displayName").GetString()!,
            element.GetProperty("shortDescription").GetString()!);
    }

    private static AdapterSkillMetadata ReadAdapterSkillMetadata(
        string path,
        string skillName,
        string adapterName,
        JsonElement value)
    {
        SkillDescriptionValidator.GuardAdapterMetadataObject(path, skillName, adapterName, value);
        SkillDescriptionValidator.GuardUniqueProperties(path, skillName, value, $"adapter '{adapterName}'");
        SkillDescriptionValidator.GuardAdapterMetadataFields(path, skillName, adapterName, value);

        IReadOnlyDictionary<string, JsonElement>? frontMatter = null;
        if (value.TryGetProperty("frontmatter", out var frontMatterElement))
        {
            SkillDescriptionValidator.GuardFrontMatterObject(path, skillName, adapterName, frontMatterElement);

            SkillDescriptionValidator.GuardUniqueProperties(path, skillName, frontMatterElement, $"adapter '{adapterName}' frontmatter");
            var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var field in frontMatterElement.EnumerateObject())
            {
                SkillDescriptionValidator.GuardFrontMatterField(path, skillName, adapterName, field.Name, field.Value);
                fields.Add(field.Name, field.Value.Clone());
            }

            frontMatter = fields;
        }

        return new AdapterSkillMetadata(frontMatter);
    }
}
