internal sealed record PluginDefinition(
    string Description,
    string[] Dependencies,
    string[] Skills,
    string[] Roles,
    Dictionary<string, string[]> SkillRoleReferences,
    SkillReferenceDefinition[]? SkillReferences,
    AssetDefinition[] Assets,
    Dictionary<string, object>? Metadata,
    SkillAssetDefinition[]? SkillAssets = null)
{
    public IReadOnlyList<SkillReferenceDefinition> SkillReferencesOrEmpty => SkillReferences ?? [];
    public IReadOnlyList<SkillAssetDefinition> SkillAssetsOrEmpty => SkillAssets ?? [];
}
