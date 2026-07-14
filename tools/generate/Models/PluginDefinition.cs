internal sealed record PluginDefinition(
    string Description,
    string[] Dependencies,
    string[] Skills,
    string[] Roles,
    Dictionary<string, string[]> SkillRoleReferences,
    SkillReferenceDefinition[]? SkillReferences,
    AssetDefinition[] Assets,
    Dictionary<string, object>? Metadata)
{
    public IReadOnlyList<SkillReferenceDefinition> SkillReferencesOrEmpty => SkillReferences ?? [];
}
