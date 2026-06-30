internal sealed record SkillDescription(
    string Description,
    SkillInvocation Invocation,
    IReadOnlyDictionary<string, AdapterSkillMetadata>? Adapters);
