using System.Text.Json;
using System.Text.Json.Nodes;

internal static class ClaudeMarketplaceBuilder
{
    public static GeneratedFile Build(PluginManifest manifest, string version)
    {
        var plugins = new JsonArray();
        foreach (var (pluginName, plugin) in MarketplacePluginOrdering.OrderedPlugins(manifest))
        {
            plugins.Add(new JsonObject
            {
                ["name"] = pluginName,
                ["source"] = $"./plugins/claude/{pluginName}",
                ["description"] = plugin.Description,
                ["version"] = version,
                ["author"] = new JsonObject
                {
                    ["name"] = "DimonSmart"
                }
            });
        }

        var marketplace = new JsonObject
        {
            ["name"] = "intent-driven-development",
            ["owner"] = new JsonObject
            {
                ["name"] = "DimonSmart"
            },
            ["description"] = "Intent-Driven Development plugins for Claude Code.",
            ["version"] = version,
            ["renames"] = new JsonObject
            {
                ["idd"] = "idd-intent",
                ["idd-core"] = "idd-intent"
            },
            ["plugins"] = plugins
        };

        return new GeneratedFile(
            Path.Combine(".claude-plugin", "marketplace.json"),
            marketplace.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
}
