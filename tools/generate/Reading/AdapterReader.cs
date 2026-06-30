using System.Text.Json;

internal sealed class AdapterReader
{
    public AdapterConfig Read(string adapterDir)
    {
        var json = File.ReadAllText(Path.Combine(adapterDir, "adapter.json"));
        var rawConfig = JsonSerializer.Deserialize<RawAdapterConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException($"Invalid adapter config in {adapterDir}.");

        var codingAgent = rawConfig.CodingAgent ?? rawConfig.Agent;
        if (string.IsNullOrWhiteSpace(codingAgent))
        {
            throw new InvalidOperationException($"Invalid adapter config in {adapterDir}: codingAgent is required.");
        }

        return new AdapterConfig(
            codingAgent,
            rawConfig.EntryPoint,
            rawConfig.SkillsRoot,
            rawConfig.SupportsSkills,
            rawConfig.SupportsFrontMatter,
            rawConfig.SupportsSkills && rawConfig.SupportsManualOnlySkills);
    }
}
