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
CheckRouteSkill("claude");
CheckRouteSkill("codex");
CheckCanonicalManifest();
CheckCanonicalSkillSemantics();
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
    if (root.TryGetProperty("skillReferences", out _))
    {
        failures.Add($"{platform} {pluginName} metadata exposes skillReferences.");
    }

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

void CheckRouteSkill(string platform)
{
    var intentRoot = Path.Combine(marketplaceRoot, "plugins", platform, "idd-intent");
    var factoryRoot = Path.Combine(marketplaceRoot, "plugins", platform, "idd-factory");
    var routeSkill = Path.Combine(intentRoot, "skills", "idd-route", "SKILL.md");
    var reference = Path.Combine(intentRoot, "skills", "idd-route", "references", "common-workflows.md");
    var canonicalReference = Path.Combine(repoRoot, "src", "canonical", "methodology", "common-workflows.md");
    var canonicalRoute = Path.Combine(repoRoot, "src", "canonical", "skills", "idd-route.md");

    ExpectFile(routeSkill);
    ExpectMissing(Path.Combine(factoryRoot, "skills", "idd-route"));
    ExpectFile(reference);
    ExpectMissing(Path.Combine(intentRoot, "assets", "bootstrap", "common-workflows.md"));
    ExpectMissing(Path.Combine(
        intentRoot,
        "skills",
        "idd-route",
        "assets",
        "bootstrap",
        "common-workflows.md"));
    ExpectMissing(Path.Combine(factoryRoot, "skills", "idd-route", "references", "common-workflows.md"));

    if (File.Exists(reference) && File.Exists(canonicalReference))
    {
        var generated = NormalizeText(File.ReadAllText(reference));
        var canonical = NormalizeText(File.ReadAllText(canonicalReference));
        if (!StringComparer.Ordinal.Equals(generated, canonical))
        {
            failures.Add($"{platform} idd-route common-workflows reference does not match canonical content.");
        }
    }

    if (File.Exists(canonicalReference))
    {
        var text = File.ReadAllText(canonicalReference);
        foreach (var expected in new[]
        {
            "Add Behavior",
            "Modify Behavior",
            "Remove Behavior",
            "Refactor While Preserving Behavior",
            "Normalize Current Intent",
            "idd-intent-change",
            "idd-code-implement",
            "idd-code-check-implementation",
            "idd-intent-audit",
            "idd-intent-normalize-current",
            "idd-intent-lint"
        })
        {
            ExpectContains(text, expected, $"canonical common workflows {expected}");
        }
    }

    if (File.Exists(canonicalRoute))
    {
        var routeText = File.ReadAllText(canonicalRoute);
        foreach (var expected in new[]
        {
            "read-only",
            "product-change",
            "add",
            "modify",
            "remove",
            "focused",
            "orchestrated",
            "preservation boundary"
        })
        {
            ExpectContains(routeText, expected, $"canonical idd-route content {expected}");
        }
    }

    if (File.Exists(routeSkill))
    {
        var routeSkillText = File.ReadAllText(routeSkill);
        ExpectContains(routeSkillText, "references/common-workflows.md", $"{platform} idd-route reference path");
        ExpectContains(routeSkillText, "canonical source", $"{platform} idd-route canonical reference declaration");
        if (platform == "claude")
        {
            if (routeSkillText.Contains("disable-model-invocation: true", StringComparison.Ordinal))
            {
                failures.Add("Claude idd-route disables implicit invocation.");
            }

            ExpectContains(routeSkillText, "allowed-tools: Read Glob Grep", "Claude idd-route read-only tool policy");
        }
    }

    if (StringComparer.Ordinal.Equals(platform, "codex"))
    {
        var codexPolicy = Path.Combine(
            intentRoot,
            "skills",
            "idd-route",
            "agents",
            "openai.yaml");
        if (File.Exists(codexPolicy) &&
            File.ReadAllText(codexPolicy).Contains("allow_implicit_invocation: false", StringComparison.Ordinal))
        {
            failures.Add("Codex idd-route disables implicit invocation.");
        }
    }
}

void CheckCanonicalManifest()
{
    var path = Path.Combine(repoRoot, "src", "canonical", "plugins", "plugin-manifest.json");
    using var document = ReadJson(path);
    if (document is null)
    {
        return;
    }

    var factory = document.RootElement
        .GetProperty("plugins")
        .GetProperty("idd-factory");
    if (factory.TryGetProperty("skillReferences", out _))
    {
        failures.Add("Canonical idd-factory manifest still declares skillReferences.");
    }
}

void CheckCanonicalSkillSemantics()
{
    ExpectCanonicalContains(
        Path.Combine("src", "canonical", "skills", "idd-intent-change.md"),
        [
            "not-applicable",
            "task-only-no-idd-intent-change"
        ],
        "canonical idd-intent-change");

    ExpectCanonicalContains(
        Path.Combine("src", "canonical", "skills", "idd-code-check-implementation.md"),
        ["current-requirement"],
        "canonical idd-code-check-implementation");

    ExpectCanonicalContains(
        Path.Combine("src", "canonical", "skills", "idd-code-implement.md"),
        [
            "satisfy-current-intent",
            "preserve-current-intent",
            "Mode:"
        ],
        "canonical idd-code-implement");
}

void ExpectCanonicalContains(string relativePath, string[] expectedValues, string context)
{
    var path = Path.Combine(repoRoot, relativePath);
    ExpectFile(path);
    if (!File.Exists(path))
    {
        return;
    }

    var text = File.ReadAllText(path);
    foreach (var expected in expectedValues)
    {
        ExpectContains(text, expected, $"{context} {expected}");
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

static string NormalizeText(string text) => text.ReplaceLineEndings("\n").TrimEnd() + "\n";

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
