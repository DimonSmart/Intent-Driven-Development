internal sealed record AdapterConfig(
    string CodingAgent,
    string EntryPoint,
    string? SkillsRoot,
    bool SupportsSkills,
    bool SupportsFrontMatter);
