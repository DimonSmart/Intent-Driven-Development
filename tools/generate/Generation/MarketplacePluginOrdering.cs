internal static class MarketplacePluginOrdering
{
    public static IEnumerable<KeyValuePair<string, PluginDefinition>> OrderedPlugins(PluginManifest manifest)
    {
        var ordered = new List<KeyValuePair<string, PluginDefinition>>();
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var pluginName in manifest.Plugins.Keys.OrderBy(name => name, StringComparer.Ordinal))
        {
            Visit(pluginName);
        }

        return ordered;

        void Visit(string pluginName)
        {
            if (visited.Contains(pluginName))
            {
                return;
            }

            if (!visiting.Add(pluginName))
            {
                throw new InvalidOperationException($"Plugin dependency cycle includes '{pluginName}'.");
            }

            var plugin = manifest.Plugins[pluginName];
            foreach (var dependency in plugin.Dependencies.OrderBy(name => name, StringComparer.Ordinal))
            {
                Visit(dependency);
            }

            visiting.Remove(pluginName);
            visited.Add(pluginName);
            ordered.Add(new KeyValuePair<string, PluginDefinition>(pluginName, plugin));
        }
    }
}
