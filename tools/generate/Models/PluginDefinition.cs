internal sealed record PluginDefinition(
    string Description,
    string[] Dependencies,
    string[] Skills,
    string[] Roles,
    Dictionary<string, string[]> SkillRoleReferences,
    AssetDefinition[] Assets,
    Dictionary<string, object>? Metadata);
