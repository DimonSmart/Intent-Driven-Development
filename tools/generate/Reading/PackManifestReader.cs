using System.Text.Json;

internal sealed class PackManifestReader(RepositoryLayout layout)
{
    public PackManifest Read()
    {
        var json = RequiredFileReader.Read(layout.PackManifestPath);
        return JsonSerializer.Deserialize<PackManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Invalid pack manifest.");
    }
}
