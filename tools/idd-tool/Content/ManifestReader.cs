using System.Text.Json;

internal sealed class ManifestReader
{
    public Manifest Read()
    {
        var manifestPath = new ContentLayout(ContentRootLocator.Find()).ManifestPath;
        if (!File.Exists(manifestPath))
        {
            throw new ToolException($"Bundled manifest not found: {manifestPath}");
        }

        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (manifest is null)
        {
            throw new ToolException($"Invalid bundled manifest: {manifestPath}");
        }

        return manifest;
    }
}
