using System.Text.Json;
using System.Text.Json.Nodes;

internal static class CodexMarketplaceBuilder
{
    public static GeneratedFile Build(PluginManifest manifest)
    {
        var plugins = new JsonArray();
        foreach (var (pluginName, _) in MarketplacePluginOrdering.OrderedPlugins(manifest))
        {
            plugins.Add(new JsonObject
            {
                ["name"] = pluginName,
                ["source"] = new JsonObject
                {
                    ["source"] = "local",
                    ["path"] = $"./plugins/codex/{pluginName}"
                },
                ["policy"] = new JsonObject
                {
                    ["installation"] = "AVAILABLE",
                    ["authentication"] = "ON_INSTALL"
                },
                ["category"] = "Productivity"
            });
        }

        var marketplace = new JsonObject
        {
            ["name"] = "intent-driven-development",
            ["interface"] = new JsonObject
            {
                ["displayName"] = "Intent-Driven Development"
            },
            ["plugins"] = plugins
        };

        return new GeneratedFile(
            Path.Combine(".agents", "plugins", "marketplace.json"),
            marketplace.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
}
