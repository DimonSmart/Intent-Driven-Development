using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

internal sealed partial class SmokeTestSuite
{
    void ExpectPackManifestShape()
    {
        const string manifestPath = "src/canonical/packs/pack-manifest.json";
        var fullManifestPath = Path.Combine(repoRoot, manifestPath);
        var manifest = ReadPackManifest();
        if (manifest?.Packs is null || manifest.Packs.Count == 0)
        {
            failures.Add("Pack manifest could not be parsed.");
            return;
        }

        var canonicalSkills = Directory.GetFiles(Path.Combine(repoRoot, "src", "canonical", "skills"), "*.md")
            .Select(path => Path.GetFileNameWithoutExtension(path)!)
            .ToHashSet(StringComparer.Ordinal);
        var skillOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var skillPath in Directory.GetFiles(Path.Combine(repoRoot, "src", "canonical", "skills"), "*.md"))
        {
            var skillName = Path.GetFileNameWithoutExtension(skillPath);
            var owners = manifest.Packs
                .Where(item => item.Value.Skills.Contains(skillName, StringComparer.Ordinal))
                .Select(item => item.Key)
                .ToArray();
            if (owners.Length != 1)
            {
                failures.Add($"Canonical skill is not owned by exactly one pack: {skillName}");
            }
            else
            {
                skillOwners[skillName!] = owners[0];
            }
        }

        foreach (var (packName, pack) in manifest.Packs)
        {
            foreach (var skill in pack.Skills)
            {
                if (!canonicalSkills.Contains(skill))
                {
                    failures.Add($"Pack '{packName}' lists missing canonical skill: {skill}");
                }
            }

            foreach (var (skill, rolePrompts) in pack.SkillRoleReferences)
            {
                if (!pack.Skills.Contains(skill, StringComparer.Ordinal))
                {
                    failures.Add($"Pack '{packName}' has role references for non-owned skill: {skill}");
                }

                foreach (var rolePrompt in rolePrompts)
                {
                    if (!pack.RolePrompts.Contains(rolePrompt, StringComparer.Ordinal))
                    {
                        failures.Add($"Pack '{packName}' skill '{skill}' references undeclared role prompt: {rolePrompt}");
                    }
                }
            }
        }

        foreach (var skill in canonicalSkills)
        {
            if (!skillOwners.ContainsKey(skill))
            {
                failures.Add($"Canonical skill file is not listed in exactly one pack: {skill}");
            }
        }

        foreach (var rolePrompt in manifest.Packs.Values.SelectMany(pack => pack.RolePrompts).Distinct(StringComparer.Ordinal))
        {
            ExpectFile($"src/canonical/factory/roles/{rolePrompt}.md");
        }

        ExpectNoDirectory("src/canonical/agents");
    }

    void ExpectManifestShape()
    {
        const string manifestPath = "manifest.json";
        var fullManifestPath = Path.Combine(repoRoot, manifestPath);
        if (!File.Exists(fullManifestPath))
        {
            failures.Add("Generator did not create manifest.json.");
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(fullManifestPath));
        var root = document.RootElement;
        ExpectJsonProperty(root, "codingAgents", manifestPath);
        ExpectJsonProperty(root, "targets", manifestPath);
        ExpectJsonProperty(root, "codingAgentCapabilities", manifestPath);
        ExpectJsonProperty(root, "targetCapabilities", manifestPath);
        ExpectJsonProperty(root, "entryPoints", manifestPath);
        ExpectJsonProperty(root, "skills", manifestPath);
        ExpectJsonProperty(root, "packs", manifestPath);

        var codingAgents = JsonStringArray(root, "codingAgents");
        var targets = JsonStringArray(root, "targets");
        if (!codingAgents.SequenceEqual(targets, StringComparer.Ordinal))
        {
            failures.Add("manifest.json codingAgents and targets differ.");
        }

        var codingAgentCapabilityKeys = JsonObjectKeys(root, "codingAgentCapabilities");
        var targetCapabilityKeys = JsonObjectKeys(root, "targetCapabilities");
        if (!codingAgentCapabilityKeys.SequenceEqual(targetCapabilityKeys, StringComparer.Ordinal))
        {
            failures.Add("manifest.json codingAgentCapabilities and targetCapabilities keys differ.");
        }

        if (!root.TryGetProperty("packs", out var packs) || packs.ValueKind != JsonValueKind.Object)
        {
            failures.Add("manifest.json packs must be an object.");
            return;
        }

        if (!packs.TryGetProperty("core", out _))
        {
            failures.Add("manifest.json is missing packs.core.");
        }

        var coreSkills = packs.GetProperty("core").GetProperty("skills").EnumerateArray().Select(item => item.GetString() ?? "").ToArray();
        if (!coreSkills.Contains("idd-skip", StringComparer.Ordinal))
        {
            failures.Add("idd-skip is missing from the core pack.");
        }
        if (packs.TryGetProperty("factory", out var manifestFactory) &&
            manifestFactory.GetProperty("skills").EnumerateArray().Any(item => StringComparer.Ordinal.Equals(item.GetString(), "idd-skip")))
        {
            failures.Add("idd-skip must not be listed in the factory pack.");
        }

        if (!packs.TryGetProperty("factory", out var factoryPack))
        {
            failures.Add("manifest.json is missing packs.factory.");
        }
        else
        {
            ExpectJsonProperty(factoryPack, "rolePrompts", manifestPath);
            ExpectJsonProperty(factoryPack, "skillRoleReferences", manifestPath);
        }
    }

    void ExpectJsonProperty(JsonElement element, string propertyName, string relativePath)
    {
        if (!element.TryGetProperty(propertyName, out _))
        {
            failures.Add($"{relativePath} is missing '{propertyName}'.");
        }
    }

    string[] JsonStringArray(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Array
            ? property.EnumerateArray().Select(item => item.GetString() ?? "").ToArray()
            : [];

    string[] JsonObjectKeys(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Object
            ? property.EnumerateObject().Select(item => item.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray()
            : [];

    SmokePackManifest? ReadPackManifest()
    {
        var path = Path.Combine(repoRoot, "src", "canonical", "packs", "pack-manifest.json");
        if (!File.Exists(path))
        {
            failures.Add("Missing pack manifest: src/canonical/packs/pack-manifest.json");
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SmokePackManifest>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException exception)
        {
            failures.Add($"Pack manifest could not be parsed: {exception.Message}");
            return null;
        }
    }

}
