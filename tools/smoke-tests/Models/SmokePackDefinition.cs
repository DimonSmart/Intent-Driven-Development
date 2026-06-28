internal sealed record SmokePackDefinition(
    string Description,
    bool Default,
    string[] Requires,
    string[] Skills,
    string[] RolePrompts,
    Dictionary<string, string[]> SkillRoleReferences,
    SmokeProjectFileDefinition[] ProjectFiles);
