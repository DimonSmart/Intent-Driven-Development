using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

var repoRoot = FindRepoRoot();
var marketplaceRoot = Path.Combine(repoRoot, "artifacts", "marketplace");
var generatorDll = Path.Combine(repoRoot, "tools", "generate", "bin", "Debug", "net10.0", "Generate.dll");
var version = ParseVersion(args);
var failures = new List<string>();

RunGenerator();
CheckClaudeMarketplace();
CheckCodexMarketplace();
CheckPlatformPlugins("claude", ".claude-plugin");
CheckPlatformPlugins("codex", ".codex-plugin");
CheckManualSkillPolicies();
CheckPublishedLayout();
CheckGeneratorCheckMode();
CheckSecondRunIsStable();

if (failures.Count > 0)
{
    foreach (var failure in failures)
    {
        Console.Error.WriteLine(failure);
    }

    return 1;
}

Console.WriteLine("Smoke tests passed.");
return 0;

void RunGenerator()
{
    if (RunProcess("dotnet", $"exec \"{generatorDll}\" --version {version}") != 0)
    {
        failures.Add("Generator failed.");
    }
}

void CheckClaudeMarketplace()
{
    var path = Path.Combine(marketplaceRoot, ".claude-plugin", "marketplace.json");
    using var document = ReadJson(path);
    if (document is null)
    {
        return;
    }

    var root = document.RootElement;
    ExpectString(root, "name", "intent-driven-development", "Claude marketplace name");
    ExpectString(root, "version", version, "Claude marketplace version");

    if (!root.TryGetProperty("renames", out var renames))
    {
        failures.Add("Claude marketplace is missing rename metadata.");
    }
    else
    {
        ExpectString(renames, "idd", "idd-intent", "Claude unified-plugin migration");
        ExpectString(renames, "idd-core", "idd-intent", "Claude core-plugin migration");
    }

    var plugins = root.GetProperty("plugins").EnumerateArray().ToArray();
    ExpectPluginNames(plugins, "Claude marketplace");

    foreach (var plugin in plugins)
    {
        var name = plugin.GetProperty("name").GetString() ?? "";
        ExpectString(plugin, "version", version, $"Claude marketplace {name} version");
        if (plugin.TryGetProperty("policy", out _))
        {
            failures.Add($"Claude marketplace plugin {name} contains Codex-only policy.");
        }

        var source = plugin.GetProperty("source").GetString();
        ExpectMarketplacePath(source, $"Claude marketplace {name} source");
    }
}

void CheckCodexMarketplace()
{
    var path = Path.Combine(marketplaceRoot, ".agents", "plugins", "marketplace.json");
    using var document = ReadJson(path);
    if (document is null)
    {
        return;
    }

    var root = document.RootElement;
    ExpectString(root, "name", "intent-driven-development", "Codex marketplace name");
    ExpectString(root.GetProperty("interface"), "displayName", "Intent-Driven Development", "Codex marketplace display name");

    var plugins = root.GetProperty("plugins").EnumerateArray().ToArray();
    ExpectPluginNames(plugins, "Codex marketplace");

    foreach (var plugin in plugins)
    {
        var name = plugin.GetProperty("name").GetString() ?? "";
        var source = plugin.GetProperty("source");
        ExpectString(source, "source", "local", $"Codex marketplace {name} source type");
        ExpectMarketplacePath(source.GetProperty("path").GetString(), $"Codex marketplace {name} source path");

        if (!plugin.TryGetProperty("policy", out var policy))
        {
            failures.Add($"Codex marketplace plugin {name} is missing policy.");
            continue;
        }

        ExpectString(policy, "installation", "AVAILABLE", $"Codex marketplace {name} installation policy");
        ExpectString(policy, "authentication", "ON_INSTALL", $"Codex marketplace {name} authentication policy");
    }
}

void CheckPlatformPlugins(string platform, string manifestDirectory)
{
    var intentRoot = Path.Combine(marketplaceRoot, "plugins", platform, "idd-intent");
    var factoryRoot = Path.Combine(marketplaceRoot, "plugins", platform, "idd-factory");

    CheckPluginManifest(intentRoot, manifestDirectory, "idd-intent", platform);
    CheckPluginManifest(factoryRoot, manifestDirectory, "idd-factory", platform);

    foreach (var skill in new[]
    {
        "idd-project-init",
        "idd-intent-change",
        "idd-code-implement",
        "idd-code-check-implementation"
    })
    {
        ExpectFile(Path.Combine(intentRoot, "skills", skill, "SKILL.md"));
    }

    ExpectMissing(Path.Combine(intentRoot, "skills", "idd-factory-create-work-plan"));
    ExpectFile(Path.Combine(intentRoot, "assets", "bootstrap", ".idd", "intent", "README.md"));
    ExpectFile(Path.Combine(
        intentRoot,
        "skills",
        "idd-project-init",
        "assets",
        "bootstrap",
        ".idd",
        "intent",
        "README.md"));

    var initSkillPath = Path.Combine(intentRoot, "skills", "idd-project-init", "SKILL.md");
    if (File.Exists(initSkillPath))
    {
        var initText = File.ReadAllText(initSkillPath);
        ExpectContains(initText, "idd-intent", $"{platform} project initialization plugin declaration");
        if (initText.Contains("\"idd\"", StringComparison.Ordinal))
        {
            failures.Add($"{platform} project initialization still declares the unified idd plugin.");
        }
    }

    foreach (var skill in new[]
    {
        "idd-factory-create-work-plan",
        "idd-factory-execute-work-plan",
        "idd-factory-review-work-result",
        "idd-factory-finish-work"
    })
    {
        ExpectFile(Path.Combine(factoryRoot, "skills", skill, "SKILL.md"));
    }

    ExpectMissing(Path.Combine(factoryRoot, "skills", "idd-project-init"));
    ExpectMissing(Path.Combine(factoryRoot, "skills", "idd-intent-change"));
    ExpectDirectory(Path.Combine(factoryRoot, "assets", "bootstrap", ".idd", "factory"));
    ExpectFile(Path.Combine(
        factoryRoot,
        "skills",
        "idd-factory-execute-work-plan",
        "references",
        "roles",
        "implementer.md"));

    CheckIddMetadata(intentRoot, [], ".idd/intent", platform, "idd-intent");
    CheckIddMetadata(factoryRoot, ["idd-intent"], ".idd/factory", platform, "idd-factory");
}

void CheckPluginManifest(string pluginRoot, string manifestDirectory, string pluginName, string platform)
{
    var path = Path.Combine(pluginRoot, manifestDirectory, "plugin.json");
    using var document = ReadJson(path);
    if (document is null)
    {
        return;
    }

    ExpectString(document.RootElement, "name", pluginName, $"{platform} {pluginName} manifest name");
    ExpectString(document.RootElement, "version", version, $"{platform} {pluginName} manifest version");

    if (platform == "codex")
    {
        var expectedDisplayName = pluginName == "idd-intent" ? "IDD Intent" : "IDD Factory";
        ExpectString(
            document.RootElement.GetProperty("interface"),
            "displayName",
            expectedDisplayName,
            $"Codex {pluginName} display name");
        ExpectString(document.RootElement, "skills", "./skills/", $"Codex {pluginName} skills path");
    }
}

void CheckIddMetadata(
    string pluginRoot,
    string[] expectedDependencies,
    string expectedAssetDestination,
    string platform,
    string pluginName)
{
    using var document = ReadJson(Path.Combine(pluginRoot, "idd-plugin.json"));
    if (document is null)
    {
        return;
    }

    var root = document.RootElement;
    ExpectString(root, "version", version, $"{platform} {pluginName} metadata version");

    var dependencies = root.GetProperty("dependencies")
        .EnumerateArray()
        .Select(value => value.GetString() ?? "")
        .ToArray();
    if (!dependencies.SequenceEqual(expectedDependencies))
    {
        failures.Add($"{platform} {pluginName} dependencies are [{string.Join(", ", dependencies)}], expected [{string.Join(", ", expectedDependencies)}].");
    }

    var destinations = root.GetProperty("assets")
        .EnumerateArray()
        .Select(asset => asset.GetProperty("destination").GetString() ?? "")
        .ToArray();
    if (!destinations.SequenceEqual([expectedAssetDestination]))
    {
        failures.Add($"{platform} {pluginName} asset destinations are [{string.Join(", ", destinations)}], expected {expectedAssetDestination}.");
    }
}

void CheckManualSkillPolicies()
{
    foreach (var skillName in new[] { "idd-skip", "idd-project-init" })
    {
        var claudeSkill = Path.Combine(
            marketplaceRoot,
            "plugins",
            "claude",
            "idd-intent",
            "skills",
            skillName,
            "SKILL.md");
        ExpectFile(claudeSkill);
        if (File.Exists(claudeSkill))
        {
            var text = File.ReadAllText(claudeSkill);
            ExpectContains(text, "disable-model-invocation: true", $"Claude {skillName} manual policy");
            ExpectContains(text, "user-invocable: true", $"Claude {skillName} user policy");
        }

        var codexPolicy = Path.Combine(
            marketplaceRoot,
            "plugins",
            "codex",
            "idd-intent",
            "skills",
            skillName,
            "agents",
            "openai.yaml");
        ExpectFile(codexPolicy);
        if (File.Exists(codexPolicy))
        {
            ExpectContains(File.ReadAllText(codexPolicy), "allow_implicit_invocation: false", $"Codex {skillName} manual policy");
        }
    }
}

void CheckPublishedLayout()
{
    foreach (var path in new[]
    {
        ".claude-plugin/marketplace.json",
        ".agents/plugins/marketplace.json",
        "plugins/claude/idd-intent",
        "plugins/claude/idd-factory",
        "plugins/codex/idd-intent",
        "plugins/codex/idd-factory"
    })
    {
        var fullPath = Path.Combine(marketplaceRoot, path.Replace('/', Path.DirectorySeparatorChar));
        if (Path.HasExtension(fullPath))
        {
            ExpectFile(fullPath);
        }
        else
        {
            ExpectDirectory(fullPath);
        }
    }

    foreach (var obsoletePath in new[]
    {
        "plugins/claude/idd",
        "plugins/codex/idd",
        "plugins/claude/idd-core",
        "plugins/codex/idd-core"
    })
    {
        ExpectMissing(Path.Combine(marketplaceRoot, obsoletePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}

void CheckGeneratorCheckMode()
{
    if (RunProcess("dotnet", $"exec \"{generatorDll}\" --check --version {version}") != 0)
    {
        failures.Add("Generator --check failed.");
    }
}

void CheckSecondRunIsStable()
{
    var before = SnapshotMarketplace();
    RunGenerator();
    var after = SnapshotMarketplace();
    if (!before.SequenceEqual(after))
    {
        failures.Add("Running the generator twice changed marketplace output.");
    }
}

void ExpectPluginNames(JsonElement[] plugins, string context)
{
    var names = plugins.Select(plugin => plugin.GetProperty("name").GetString()).ToArray();
    if (!names.SequenceEqual(["idd-intent", "idd-factory"]))
    {
        failures.Add($"{context} plugins are [{string.Join(", ", names)}], expected idd-intent and idd-factory.");
    }
}

void ExpectMarketplacePath(string? relativePath, string context)
{
    if (string.IsNullOrWhiteSpace(relativePath))
    {
        failures.Add($"{context} is empty.");
        return;
    }

    var fullPath = Path.GetFullPath(Path.Combine(
        marketplaceRoot,
        relativePath.Replace('/', Path.DirectorySeparatorChar)));
    if (!fullPath.StartsWith(Path.GetFullPath(marketplaceRoot), StringComparison.OrdinalIgnoreCase))
    {
        failures.Add($"{context} resolves outside the marketplace root.");
        return;
    }

    if (!Directory.Exists(fullPath) && !File.Exists(fullPath))
    {
        failures.Add($"{context} does not exist: {relativePath}");
    }
}

JsonDocument? ReadJson(string path)
{
    ExpectFile(path);
    if (!File.Exists(path))
    {
        return null;
    }

    try
    {
        return JsonDocument.Parse(File.ReadAllText(path));
    }
    catch (Exception exception)
    {
        failures.Add($"Invalid JSON in {Relative(path)}: {exception.Message}");
        return null;
    }
}

void ExpectString(JsonElement element, string propertyName, string expected, string context)
{
    if (!element.TryGetProperty(propertyName, out var property) ||
        property.ValueKind != JsonValueKind.String ||
        !StringComparer.Ordinal.Equals(property.GetString(), expected))
    {
        failures.Add($"{context} is not '{expected}'.");
    }
}

void ExpectContains(string text, string expected, string context)
{
    if (!text.Contains(expected, StringComparison.Ordinal))
    {
        failures.Add($"{context} does not contain '{expected}'.");
    }
}

void ExpectFile(string path)
{
    if (!File.Exists(path))
    {
        failures.Add($"Missing file: {Relative(path)}");
    }
}

void ExpectDirectory(string path)
{
    if (!Directory.Exists(path))
    {
        failures.Add($"Missing directory: {Relative(path)}");
    }
}

void ExpectMissing(string path)
{
    if (File.Exists(path) || Directory.Exists(path))
    {
        failures.Add($"Obsolete path exists: {Relative(path)}");
    }
}

string[] SnapshotMarketplace() => Directory.Exists(marketplaceRoot)
    ? Directory.GetFiles(marketplaceRoot, "*", SearchOption.AllDirectories)
        .OrderBy(path => path, StringComparer.Ordinal)
        .Select(path => $"{Relative(path)}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}")
        .ToArray()
    : [];

string Relative(string path) => Path.GetRelativePath(repoRoot, path).Replace('\\', '/');

int RunProcess(string fileName, string arguments)
{
    using var process = Process.Start(new ProcessStartInfo
    {
        FileName = fileName,
        Arguments = arguments,
        WorkingDirectory = repoRoot,
        UseShellExecute = false
    });

    if (process is null)
    {
        return -1;
    }

    process.WaitForExit();
    return process.ExitCode;
}

static string FindRepoRoot()
{
    var current = new DirectoryInfo(Environment.CurrentDirectory);
    while (current is not null)
    {
        if (Directory.Exists(Path.Combine(current.FullName, "src", "canonical")) &&
            Directory.Exists(Path.Combine(current.FullName, "tools", "generate")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new InvalidOperationException("Could not locate repository root.");
}

static string ParseVersion(string[] arguments)
{
    for (var index = 0; index < arguments.Length; index++)
    {
        if (!StringComparer.Ordinal.Equals(arguments[index], "--version"))
        {
            continue;
        }

        if (index + 1 >= arguments.Length)
        {
            throw new InvalidOperationException("Missing value for --version.");
        }

        return arguments[index + 1];
    }

    throw new InvalidOperationException("Missing required --version MAJOR.MINOR.PATCH option.");
}
