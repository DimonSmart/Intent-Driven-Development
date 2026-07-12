using System.Text.Json;
using System.Text.Json.Nodes;

internal static class MarketplaceBuilder
{
    public static string Build(string platform, PluginManifest manifest)
    {
        var plugins = new JsonArray();
        foreach (var pluginName in manifest.Plugins.Keys.OrderBy(name => name, StringComparer.Ordinal))
        {
            plugins.Add(new JsonObject
            {
                ["name"] = pluginName,
                ["source"] = new JsonObject
                {
                    ["source"] = "local",
                    ["path"] = $"./plugins/{pluginName}"
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
            ["name"] = $"idd-{platform}",
            ["interface"] = new JsonObject
            {
                ["displayName"] = $"Intent-Driven Development {DisplayPlatform(platform)}"
            },
            ["plugins"] = plugins
        };

        return marketplace.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    private static string DisplayPlatform(string platform) =>
        platform switch
        {
            "claude" => "Claude",
            "codex" => "Codex",
            _ => platform
        };
}
