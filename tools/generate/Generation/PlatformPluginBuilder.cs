using System.Text.Json;
using System.Text.Json.Nodes;

internal abstract class PlatformPluginBuilder : IPlatformAdapter
{
    public abstract string Platform { get; }
    protected abstract string ManifestDirectory { get; }
    protected abstract string ManifestFileName { get; }
    protected const string RepositoryUrl = "https://github.com/DimonSmart/Intent-Driven-Development";
    protected const string AuthorName = "DimonSmart";

    public abstract GeneratedFile BuildMarketplaceFile(PluginManifest manifest, string version);

    public virtual IReadOnlyList<GeneratedFile> BuildPluginFiles(
        AdapterDefinition adapterDefinition,
        PluginManifest manifest,
        string pluginName,
        PluginDefinition plugin,
        IReadOnlyDictionary<string, RoleDefinition> roleDefinitions,
        IReadOnlyDictionary<string, SkillDescription> skillDescriptions,
        string version)
    {
        var adapter = adapterDefinition.Config;
        var files = new List<GeneratedFile>
        {
            new(Path.Combine(ManifestDirectory, ManifestFileName), BuildPluginManifest(adapter, pluginName, plugin, version)),
            new("idd-plugin.json", BuildIddPluginMetadata(adapter, plugin, roleDefinitions, version))
        };

        foreach (var skillName in plugin.Skills.OrderBy(name => name, StringComparer.Ordinal))
        {
            files.AddRange(BuildSkillFiles(adapter, plugin, roleDefinitions, skillName, skillDescriptions));
        }

        foreach (var asset in plugin.Assets)
        {
            files.AddRange(BuildAssetFiles(asset));
        }

        files.AddRange(BuildAdditionalPluginFiles(pluginName, version));

        return files;
    }

    protected virtual IReadOnlyList<GeneratedFile> BuildAdditionalPluginFiles(string pluginName, string version) => [];

    protected abstract string BuildPluginManifest(
        AdapterConfig adapter,
        string pluginName,
        PluginDefinition plugin,
        string version);

    protected virtual string BuildIddPluginMetadata(
        AdapterConfig adapter,
        PluginDefinition plugin,
        IReadOnlyDictionary<string, RoleDefinition> roleDefinitions,
        string version)
    {
        var metadata = new JsonObject
        {
            ["version"] = version,
            ["platform"] = adapter.CodingAgent,
            ["dependencies"] = JsonStringArray(plugin.Dependencies),
            ["roles"] = JsonStringArray(plugin.Roles),
            ["roleDefinitions"] = BuildRoleDefinitions(plugin, roleDefinitions),
            ["assets"] = BuildAssets(plugin),
            ["canonicalSource"] = "src/canonical"
        };

        return metadata.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    protected virtual IReadOnlyList<GeneratedFile> BuildSkillFiles(
        AdapterConfig adapter,
        PluginDefinition plugin,
        IReadOnlyDictionary<string, RoleDefinition> roleDefinitions,
        string skillName,
        IReadOnlyDictionary<string, SkillDescription> skillDescriptions)
    {
        if (!skillDescriptions.TryGetValue(skillName, out var skillDescription))
        {
            throw new InvalidOperationException($"Missing skill description for {skillName}.");
        }

        var sourcePath = Path.Combine("src", "canonical", "skills", skillName + ".md");
        var content = RequiredFileReader.Read(sourcePath);
        var roles = plugin.SkillRoleReferences.TryGetValue(skillName, out var roleNames)
            ? roleNames.Select(name => roleDefinitions[name]).ToArray()
            : [];
        content = ContentNormalizer.JoinBlocks(
            BuildSkillFrontMatter(skillName, skillDescription, adapter, roles),
            content);

        var files = new List<GeneratedFile>
        {
            new(Path.Combine("skills", skillName, "SKILL.md"), ContentNormalizer.NormalizeContent(content))
        };

        if (plugin.SkillRoleReferences.TryGetValue(skillName, out var rolePrompts))
        {
            foreach (var rolePrompt in rolePrompts)
            {
                files.Add(new GeneratedFile(
                    Path.Combine("skills", skillName, "references", "roles", rolePrompt + ".md"),
                    ContentNormalizer.NormalizeContent(BuildRole(roleDefinitions[rolePrompt]))));
            }
        }

        foreach (var reference in plugin.SkillReferencesOrEmpty
            .Where(reference => StringComparer.Ordinal.Equals(reference.Skill, skillName))
            .OrderBy(reference => NormalizeReferenceDestination(reference.Destination), StringComparer.Ordinal))
        {
            var referenceContent = RequiredFileReader.Read(reference.Source);
            files.Add(new GeneratedFile(
                Path.Combine(
                    "skills",
                    skillName,
                    "references",
                    NormalizeReferenceDestination(reference.Destination).Replace('/', Path.DirectorySeparatorChar)),
                ContentNormalizer.NormalizeContent(referenceContent)));
        }

        if (StringComparer.Ordinal.Equals(skillName, "idd-project-init"))
        {
            foreach (var asset in plugin.Assets.Where(asset => StringComparer.Ordinal.Equals(asset.Destination, ".idd/intent")))
            {
                files.AddRange(BuildSkillAssetFiles(skillName, asset));
            }
        }

        return files;
    }

    protected abstract string BuildSkillFrontMatter(
        string skillName,
        SkillDescription skillDescription,
        AdapterConfig adapter,
        IReadOnlyList<RoleDefinition> roles);

    protected virtual string BuildRole(RoleDefinition role) => ContentNormalizer.JoinBlocks(
        role.Instructions,
        """
        ## Available tools

        This role may use only:

        """ + string.Join("\n", role.Tools.Select(tool => $"- {RoleToolNames.GetName(tool)}")) +
        """

        Do not substitute unavailable tools with another mechanism.
        If the required operation cannot be completed with these tools, return the
        role-specific blocked result.
        """);

    protected static JsonArray JsonStringArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }

    protected virtual JsonArray BuildRoleDefinitions(
        PluginDefinition plugin,
        IReadOnlyDictionary<string, RoleDefinition> roleDefinitions)
    {
        var roles = new JsonArray();
        foreach (var roleName in plugin.Roles)
        {
            var role = roleDefinitions[roleName];
            roles.Add(new JsonObject
            {
                ["name"] = role.Name,
                ["tools"] = JsonStringArray(role.Tools.Select(RoleToolNames.GetName))
            });
        }

        return roles;
    }

    protected static JsonArray BuildAssets(PluginDefinition plugin)
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

    protected static string DisplayName(string pluginName) =>
        pluginName switch
        {
            "idd-core" => "IDD Core",
            "idd-factory" => "IDD Factory",
            _ => pluginName
        };

    protected virtual IReadOnlyList<GeneratedFile> BuildAssetFiles(AssetDefinition asset)
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

    private static IReadOnlyList<GeneratedFile> BuildSkillAssetFiles(string skillName, AssetDefinition asset)
    {
        var source = asset.Source.Replace('/', Path.DirectorySeparatorChar);
        if (File.Exists(source))
        {
            return [BuildSkillAssetFile(skillName, source, Path.GetFileName(source), asset.Destination)];
        }

        return Directory.GetFiles(source, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => BuildSkillAssetFile(skillName, path, Path.GetRelativePath(source, path), asset.Destination))
            .ToArray();
    }

    private static GeneratedFile BuildSkillAssetFile(
        string skillName,
        string sourceFile,
        string relativeSourceFile,
        string destination)
    {
        var normalizedDestination = destination.Replace('\\', '/').Trim('/');
        var relativePath = Path.Combine(
            "skills",
            skillName,
            "assets",
            "bootstrap",
            normalizedDestination,
            relativeSourceFile);
        return new GeneratedFile(relativePath, ContentNormalizer.NormalizeContent(File.ReadAllText(sourceFile)));
    }

    private static GeneratedFile BuildAssetFile(string sourceFile, string relativeSourceFile, string destination)
    {
        var normalizedDestination = destination is "." or ""
            ? ""
            : destination.Replace('\\', '/').Trim('/');
        relativeSourceFile = NormalizeAssetFileName(relativeSourceFile);
        var relativePath = Path.Combine("assets", "bootstrap", normalizedDestination, relativeSourceFile);
        return new GeneratedFile(relativePath, ContentNormalizer.NormalizeContent(File.ReadAllText(sourceFile)));
    }

    private static string NormalizeAssetFileName(string relativeSourceFile) =>
        StringComparer.Ordinal.Equals(relativeSourceFile, "gitignore.template")
            ? ".gitignore"
            : relativeSourceFile;

    private static string NormalizeReferenceDestination(string destination) =>
        string.Join('/', destination.Replace('\\', '/').Split('/'));
}
