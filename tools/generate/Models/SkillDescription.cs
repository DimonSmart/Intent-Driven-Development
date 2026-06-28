internal sealed record SkillDescription(
    string Description,
    IReadOnlyDictionary<string, AdapterSkillMetadata>? Adapters);
