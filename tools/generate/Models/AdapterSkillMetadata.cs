using System.Text.Json;

internal sealed record AdapterSkillMetadata(
    IReadOnlyDictionary<string, JsonElement>? Frontmatter);
