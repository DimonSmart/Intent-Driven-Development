internal sealed record PackDefinition(
    string Description,
    bool Default,
    string[] Requires,
    string[] Skills,
    string[] RolePrompts,
    Dictionary<string, string[]> SkillRoleReferences,
    ProjectFileDefinition[] ProjectFiles);
