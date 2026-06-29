internal sealed class ContentLayout(string contentRoot)
{
    public string ContentRoot { get; } = contentRoot;
    public string ManifestPath => Path.Combine(ContentRoot, "manifest.json");
    public string GeneratedRoot => Path.Combine(ContentRoot, "generated");
    public string AdaptersRoot => Path.Combine(ContentRoot, "src", "adapters");
    public string PacksRoot => Path.Combine(ContentRoot, "src", "canonical", "packs");
    public string IntentRoot => Path.Combine(ContentRoot, "src", "canonical", "project-files", "intent");
    public string MethodologyRoot => Path.Combine(ContentRoot, "src", "canonical", "methodology");
}
