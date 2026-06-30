using System.Text.Json;
using System.Text.Json.Nodes;

internal static class ManifestBuilder
{
    public static string Build(
        IReadOnlyList<AdapterDefinition> adapterDefinitions,
        PackManifest packManifest,
        IReadOnlyDictionary<string, SkillDescription> skillDescriptions,
        string manifestVersion)
    {
        var orderedAdapters = adapterDefinitions
            .OrderBy(definition => definition.Config.CodingAgent, StringComparer.Ordinal)
            .ToArray();
        var codingAgents = JsonStringArray(orderedAdapters.Select(definition => definition.Config.CodingAgent));
        var targetAliases = JsonStringArray(orderedAdapters.Select(definition => definition.Config.CodingAgent));
        var entryPoints = new JsonObject();
        var codingAgentCapabilities = new JsonObject();
        var targetCapabilities = new JsonObject();

        foreach (var adapterDefinition in orderedAdapters)
        {
            var adapter = adapterDefinition.Config;
            entryPoints.Add(adapter.CodingAgent, adapter.EntryPoint);
            codingAgentCapabilities.Add(adapter.CodingAgent, new JsonObject
            {
                ["supportsSkills"] = adapter.SupportsSkills,
                ["supportsManualOnlySkills"] = adapter.SupportsManualOnlySkills
            });
            targetCapabilities.Add(adapter.CodingAgent, new JsonObject
            {
                ["supportsSkills"] = adapter.SupportsSkills,
                ["supportsManualOnlySkills"] = adapter.SupportsManualOnlySkills
            });
        }

        var manifest = new JsonObject
        {
            ["name"] = "Intent-Driven Development",
            ["version"] = manifestVersion,
            ["canonicalSource"] = "src/canonical",
            ["generatedRoot"] = "generated",
            ["codingAgents"] = codingAgents,
            ["codingAgentCapabilities"] = codingAgentCapabilities,
            ["targets"] = targetAliases,
            ["entryPoints"] = entryPoints,
            ["targetCapabilities"] = targetCapabilities,
            ["skills"] = BuildSkillsNode(skillDescriptions),
            ["packs"] = BuildPacksNode(packManifest)
        };

        return manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    private static JsonObject BuildSkillsNode(IReadOnlyDictionary<string, SkillDescription> skillDescriptions)
    {
        var skills = new JsonObject();
        foreach (var (skillName, skillDescription) in skillDescriptions.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            skills.Add(skillName, new JsonObject
            {
                ["description"] = skillDescription.Description,
                ["invocation"] = skillDescription.Invocation == SkillInvocation.Manual ? "manual" : "auto"
            });
        }

        return skills;
    }

    private static JsonObject BuildPacksNode(PackManifest packManifest)
    {
        var packs = new JsonObject();
        foreach (var (packName, pack) in packManifest.Packs.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var skillRoleReferences = new JsonObject();
            foreach (var (skill, rolePrompts) in pack.SkillRoleReferences.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                skillRoleReferences.Add(skill, JsonStringArray(rolePrompts));
            }

            var projectFiles = new JsonArray();
            foreach (var projectFile in pack.ProjectFiles)
            {
                projectFiles.Add(new JsonObject
                {
                    ["source"] = projectFile.Source,
                    ["destination"] = projectFile.Destination
                });
            }

            packs.Add(packName, new JsonObject
            {
                ["description"] = pack.Description,
                ["default"] = pack.Default,
                ["requires"] = JsonStringArray(pack.Requires),
                ["skills"] = JsonStringArray(pack.Skills),
                ["rolePrompts"] = JsonStringArray(pack.RolePrompts),
                ["skillRoleReferences"] = skillRoleReferences,
                ["projectFiles"] = projectFiles
            });
        }

        return packs;
    }

    private static JsonArray JsonStringArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }
}
