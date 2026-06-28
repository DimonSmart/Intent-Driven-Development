internal sealed class RepositoryLayout(string repoRoot)
{
    public string RepoRoot { get; } = repoRoot;
    public string AdaptersRoot => Path.Combine(RepoRoot, "src", "adapters");
    public string CanonicalRoot => Path.Combine(RepoRoot, "src", "canonical");
    public string SkillsRoot => Path.Combine(CanonicalRoot, "skills");
    public string PacksRoot => Path.Combine(CanonicalRoot, "packs");
    public string FactoryRolesRoot => Path.Combine(CanonicalRoot, "factory", "roles");
    public string GeneratedRoot => Path.Combine(RepoRoot, "generated");
    public string ManifestPath => Path.Combine(RepoRoot, "manifest.json");
    public string PackManifestPath => Path.Combine(PacksRoot, "pack-manifest.json");
    public string SkillDescriptionsPath => Path.Combine(SkillsRoot, "skill-descriptions.json");
}
