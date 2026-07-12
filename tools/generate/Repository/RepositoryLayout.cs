internal sealed class RepositoryLayout(string repoRoot)
{
    public string RepoRoot { get; } = repoRoot;
    public string AdaptersRoot => Path.Combine(RepoRoot, "src", "adapters");
    public string CanonicalRoot => Path.Combine(RepoRoot, "src", "canonical");
    public string SkillsRoot => Path.Combine(CanonicalRoot, "skills");
    public string PluginsRoot => Path.Combine(CanonicalRoot, "plugins");
    public string FactoryRolesRoot => Path.Combine(CanonicalRoot, "factory", "roles");
    public string MarketplaceRoot => Path.Combine(RepoRoot, "artifacts", "marketplace");
    public string VersionPath => Path.Combine(RepoRoot, "VERSION");
    public string PluginManifestPath => Path.Combine(PluginsRoot, "plugin-manifest.json");
    public string SkillDescriptionsPath => Path.Combine(SkillsRoot, "skill-descriptions.json");
}
