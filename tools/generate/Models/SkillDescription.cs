internal sealed record SkillDescription(
    string Description,
    SkillInvocation Invocation,
    SkillExposure Exposure,
    SkillInterfaceMetadata? Interface,
    IReadOnlyDictionary<string, AdapterSkillMetadata>? Adapters);
