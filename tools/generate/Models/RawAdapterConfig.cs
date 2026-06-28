internal sealed record RawAdapterConfig(
    string? CodingAgent,
    string? Agent,
    string EntryPoint,
    string? SkillsRoot,
    bool SupportsSkills,
    bool SupportsFrontMatter);
