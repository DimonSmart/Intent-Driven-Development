using System.Text.Json;

internal sealed class SkillDescriptionReader
{
    public IReadOnlyDictionary<string, SkillDescription> Read(string path, IReadOnlySet<string> knownAdapterNames)
    {
        var json = RequiredFileReader.Read(path);
        using var document = JsonDocument.Parse(json);
        SkillDescriptionValidator.GuardRootObject(path, document.RootElement);

        var descriptions = new Dictionary<string, SkillDescription>(StringComparer.Ordinal);
        foreach (var skillProperty in document.RootElement.EnumerateObject())
        {
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
        if (value.ValueKind == JsonValueKind.String)
        {
            var description = value.GetString();
            SkillDescriptionValidator.GuardDescription(path, skillName, description);
            return new SkillDescription(description!, SkillInvocation.Auto, Adapters: null);
        }

        SkillDescriptionValidator.GuardDescriptionObject(path, skillName, value);

        var objectDescription = value.GetProperty("description").GetString();
        SkillDescriptionValidator.GuardDescription(path, skillName, objectDescription);
        var invocation = ReadInvocation(skillName, value);

        IReadOnlyDictionary<string, AdapterSkillMetadata>? adapters = null;
        if (value.TryGetProperty("adapters", out var adaptersElement))
        {
            SkillDescriptionValidator.GuardAdaptersObject(path, skillName, adaptersElement);

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

        return new SkillDescription(objectDescription!, invocation, adapters);
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

    private static AdapterSkillMetadata ReadAdapterSkillMetadata(
        string path,
        string skillName,
        string adapterName,
        JsonElement value)
    {
        SkillDescriptionValidator.GuardAdapterMetadataObject(path, skillName, adapterName, value);

        IReadOnlyDictionary<string, JsonElement>? frontMatter = null;
        if (value.TryGetProperty("frontmatter", out var frontMatterElement))
        {
            SkillDescriptionValidator.GuardFrontMatterObject(path, skillName, adapterName, frontMatterElement);

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
