using System.Text.Json;
using System.Text.Json.Nodes;

internal abstract class PlatformPluginBuilder : IPlatformAdapter
{
    public abstract string Platform { get; }
    protected abstract string ManifestDirectory { get; }
    protected abstract string ManifestFileName { get; }

    public IReadOnlyList<GeneratedFile> BuildPluginFiles(
        AdapterDefinition adapterDefinition,
        PluginManifest manifest,
        string pluginName,
        PluginDefinition plugin,
        IReadOnlyDictionary<string, SkillDescription> skillDescriptions,
        string version)
    {
        var adapter = adapterDefinition.Config;
        var files = new List<GeneratedFile>
        {
            new(Path.Combine(ManifestDirectory, ManifestFileName), BuildPluginManifest(adapter, pluginName, plugin, version)),
            new("idd-plugin.json", BuildIddPluginMetadata(adapter, plugin, version))
        };

        if (plugin.Metadata?.TryGetValue("entryPoint", out var entryPointValue) == true &&
            entryPointValue is JsonElement { ValueKind: JsonValueKind.True })
        {
            files.Add(new GeneratedFile(adapter.EntryPoint, BuildEntryPoint(adapterDefinition.Directory, adapter)));
        }

        foreach (var skillName in plugin.Skills.OrderBy(name => name, StringComparer.Ordinal))
        {
            files.AddRange(BuildSkillFiles(adapter, plugin, skillName, skillDescriptions));
        }

        foreach (var asset in plugin.Assets)
        {
            files.AddRange(BuildAssetFiles(asset));
        }

        return files;
    }

    private string BuildPluginManifest(
        AdapterConfig adapter,
        string pluginName,
        PluginDefinition plugin,
        string version)
    {
        var pluginJson = new JsonObject
        {
            ["name"] = pluginName,
            ["version"] = version,
            ["description"] = plugin.Description,
            ["author"] = new JsonObject
            {
                ["name"] = "Intent-Driven Development"
            },
            ["license"] = "MIT",
            ["keywords"] = new JsonArray("intent-driven-development", "idd", adapter.CodingAgent),
            ["skills"] = "./skills/",
            ["interface"] = new JsonObject
            {
                ["displayName"] = DisplayName(pluginName),
                ["shortDescription"] = plugin.Description,
                ["longDescription"] = plugin.Description,
                ["developerName"] = "Intent-Driven Development",
                ["category"] = "Productivity",
                ["capabilities"] = new JsonArray("Skills", "Workflow"),
                ["defaultPrompt"] = new JsonArray(
                    "Initialize IDD in this project.",
                    "Update product intent for this change.",
                    "Check implementation against intent.")
            }
        };

        return pluginJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    private static string BuildIddPluginMetadata(AdapterConfig adapter, PluginDefinition plugin, string version)
    {
        var metadata = new JsonObject
        {
            ["version"] = version,
            ["platform"] = adapter.CodingAgent,
            ["dependencies"] = JsonStringArray(plugin.Dependencies),
            ["roles"] = JsonStringArray(plugin.Roles),
            ["assets"] = BuildAssets(plugin),
            ["canonicalSource"] = "src/canonical"
        };

        return metadata.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    private IReadOnlyList<GeneratedFile> BuildSkillFiles(
        AdapterConfig adapter,
        PluginDefinition plugin,
        string skillName,
        IReadOnlyDictionary<string, SkillDescription> skillDescriptions)
    {
        if (!skillDescriptions.TryGetValue(skillName, out var skillDescription))
        {
            throw new InvalidOperationException($"Missing skill description for {skillName}.");
        }

        var sourcePath = Path.Combine("src", "canonical", "skills", skillName + ".md");
        var content = RequiredFileReader.Read(sourcePath);
        if (adapter.SupportsFrontMatter)
        {
            content = ContentNormalizer.JoinBlocks(
                YamlFrontMatterWriter.BuildSkillFrontMatter(skillName, skillDescription, adapter),
                content);
        }

        var files = new List<GeneratedFile>
        {
            new(Path.Combine("skills", skillName, "SKILL.md"), ContentNormalizer.NormalizeContent(content))
        };

        if (plugin.SkillRoleReferences.TryGetValue(skillName, out var rolePrompts))
        {
            foreach (var rolePrompt in rolePrompts)
            {
                var roleContent = RequiredFileReader.Read(Path.Combine("src", "canonical", "factory", "roles", rolePrompt + ".md"));
                files.Add(new GeneratedFile(
                    Path.Combine("skills", skillName, "references", "roles", rolePrompt + ".md"),
                    ContentNormalizer.NormalizeContent(roleContent)));
            }
        }

        return files;
    }

    private static string BuildEntryPoint(string adapterDir, AdapterConfig adapter)
    {
        var entry = RequiredFileReader.Read(Path.Combine(adapterDir, "entry.md"));
        var guidance = """
                       IDD is installed as native plugins. Do not copy plugin skills into the user project.

                       Use `idd-project-init` as the only project initialization workflow. It creates `.idd/intent`
                       and minimal plugin declarations. Product intent remains in `.idd/intent`.
                       """;
        var entryPoint = ContentNormalizer.NormalizeContent(ContentNormalizer.JoinBlocks(entry, guidance));
        EntryPointSizeGuard.Guard(adapter.EntryPoint, entryPoint);
        return entryPoint;
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

    private static JsonArray BuildAssets(PluginDefinition plugin)
    {
        var assets = new JsonArray();
        foreach (var asset in plugin.Assets)
        {
            assets.Add(new JsonObject
            {
                ["source"] = asset.Source,
                ["destination"] = asset.Destination
            });
        }

        return assets;
    }

    private static string DisplayName(string pluginName) =>
        pluginName switch
        {
            "idd-core" => "IDD Core",
            "idd-factory" => "IDD Factory",
            _ => pluginName
        };

    private static IReadOnlyList<GeneratedFile> BuildAssetFiles(AssetDefinition asset)
    {
        var source = asset.Source.Replace('/', Path.DirectorySeparatorChar);
        if (File.Exists(source))
        {
            return [BuildAssetFile(source, Path.GetFileName(source), asset.Destination)];
        }

        return Directory.GetFiles(source, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => BuildAssetFile(path, Path.GetRelativePath(source, path), asset.Destination))
            .ToArray();
    }

    private static GeneratedFile BuildAssetFile(string sourceFile, string relativeSourceFile, string destination)
    {
        var normalizedDestination = destination is "." or ""
            ? ""
            : destination.Replace('\\', '/').Trim('/');
        var relativePath = Path.Combine("assets", "bootstrap", normalizedDestination, relativeSourceFile);
        return new GeneratedFile(relativePath, ContentNormalizer.NormalizeContent(File.ReadAllText(sourceFile)));
    }
}
