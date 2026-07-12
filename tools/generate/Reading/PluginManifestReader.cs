using System.Text.Json;

internal sealed class PluginManifestReader(RepositoryLayout layout)
{
    public PluginManifest Read()
    {
        var json = RequiredFileReader.Read(layout.PluginManifestPath);
        return JsonSerializer.Deserialize<PluginManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Invalid plugin manifest.");
    }
}
