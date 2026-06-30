using System.Text.Json.Serialization;

internal sealed class Manifest
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string CanonicalSource { get; init; }
    public required string GeneratedRoot { get; init; }
    public required Dictionary<string, string> EntryPoints { get; init; }
    public Dictionary<string, SkillMetadata> Skills { get; init; } = new(StringComparer.Ordinal);
    public required Dictionary<string, PackDefinition> Packs { get; init; }

    [JsonPropertyName("codingAgents")]
    public string[]? CodingAgentsField { get; init; }

    [JsonPropertyName("targets")]
    public string[]? Targets { get; init; }

    [JsonPropertyName("codingAgentCapabilities")]
    public Dictionary<string, CodingAgentCapabilities>? CodingAgentCapabilitiesField { get; init; }

    [JsonPropertyName("targetCapabilities")]
    public Dictionary<string, CodingAgentCapabilities>? TargetCapabilities { get; init; }

    // Compatibility: older release manifests used target/targetCapabilities.
    [JsonIgnore]
    public string[] CodingAgents => CodingAgentsField ?? Targets ?? [];

    [JsonIgnore]
    public Dictionary<string, CodingAgentCapabilities>? CodingAgentCapabilities =>
        CodingAgentCapabilitiesField ?? TargetCapabilities;
}
