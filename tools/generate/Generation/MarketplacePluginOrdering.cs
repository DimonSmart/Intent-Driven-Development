internal static class MarketplacePluginOrdering
{
    public static IEnumerable<KeyValuePair<string, PluginDefinition>> OrderedPlugins(PluginManifest manifest) =>
        manifest.Plugins.OrderBy(item => item.Key, StringComparer.Ordinal);
}
