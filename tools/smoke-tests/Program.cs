using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

var repoRoot = FindRepoRoot();
var marketplaceRoot = Path.Combine(repoRoot, "artifacts", "marketplace");
var failures = new List<string>();
var version = ParseVersion(args);
var generatorDll = Path.Combine(repoRoot, "tools", "generate", "bin", "Debug", "net10.0", "Generate.dll");

RunGenerator();
ClaudeMarketplaceSmokeTests();
CodexMarketplaceSmokeTests();
ClaudePluginSmokeTests();
CodexPluginSmokeTests();
SkillPolicySmokeTests();
ReleaseVersionSmokeTests();
PublishedLayoutSmokeTests();
NativeValidatorSmokeTests();
ExpectNoPath("generated");
ExpectNoPath("tools/idd-tool");
ExpectNoPath("src/adapters/gemini");
ExpectNoPath("src/adapters/copilot");
ExpectNoText("NuGet", "dotnet tool", "list-packs", "--pack", "Gemini", "Copilot", "Cursor", "Windsurf");
ExpectGeneratorCheckPasses();
ExpectSecondRunStable();

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

void ClaudeMarketplaceSmokeTests()
{
    var marketplacePath = Path.Combine(marketplaceRoot, ".claude-plugin", "marketplace.json");
    ExpectFile(marketplacePath);
    using var marketplace = ReadJson(marketplacePath);
    ExpectString(marketplace.RootElement, "name", "intent-driven-development", "Claude marketplace name");
    ExpectString(marketplace.RootElement.GetProperty("owner"), "name", "DimonSmart", "Claude marketplace owner.name");
    ExpectString(marketplace.RootElement, "version", version, "Claude marketplace version");
    var renames = marketplace.RootElement.GetProperty("renames");
    ExpectString(renames, "idd-core", "idd", "Claude marketplace idd-core migration");
    ExpectString(renames, "idd-factory", "idd", "Claude marketplace idd-factory migration");

    var plugins = marketplace.RootElement.GetProperty("plugins").EnumerateArray().ToArray();
    ExpectPluginCount(plugins, "Claude marketplace");
    var plugin = plugins.Single();
    if (plugin.TryGetProperty("policy", out _))
    {
        failures.Add("Claude marketplace plugin contains Codex-only policy.");
    }

    if (plugin.GetProperty("source").ValueKind != JsonValueKind.String)
    {
        failures.Add("Claude marketplace plugin source is not a local string path.");
    }
    else
    {
        ExpectExistingMarketplacePath(plugin.GetProperty("source").GetString(), "Claude marketplace plugin source");
    }

    ExpectString(plugin, "version", version, "Claude marketplace plugin version");
}

void CodexMarketplaceSmokeTests()
{
    var marketplacePath = Path.Combine(marketplaceRoot, ".agents", "plugins", "marketplace.json");
    ExpectFile(marketplacePath);
    using var marketplace = ReadJson(marketplacePath);
    ExpectString(
        marketplace.RootElement.GetProperty("interface"),
        "displayName",
        "Intent-Driven Development",
        "Codex marketplace interface.displayName");

    var plugins = marketplace.RootElement.GetProperty("plugins").EnumerateArray().ToArray();
    ExpectPluginCount(plugins, "Codex marketplace");
    var plugin = plugins.Single();
    var source = plugin.GetProperty("source");
    ExpectString(source, "source", "local", "Codex marketplace plugin source.source");

    var sourcePath = source.GetProperty("path").GetString() ?? "";
    if (!sourcePath.StartsWith("./", StringComparison.Ordinal))
    {
        failures.Add("Codex marketplace plugin source.path does not start with ./.");
    }

    ExpectExistingMarketplacePath(sourcePath, "Codex marketplace plugin source.path");

    if (!plugin.TryGetProperty("policy", out var policy))
    {
        failures.Add("Codex marketplace plugin is missing policy.");
        return;
    }

    ExpectString(policy, "installation", "AVAILABLE", "Codex marketplace plugin policy.installation");
    ExpectString(policy, "authentication", "ON_INSTALL", "Codex marketplace plugin policy.authentication");
}

void ClaudePluginSmokeTests()
{
    var root = Path.Combine(marketplaceRoot, "plugins", "claude", "idd");
    ExpectDirectory(root);
    var manifestPath = Path.Combine(root, ".claude-plugin", "plugin.json");
    ExpectFile(manifestPath);
    using var manifest = ReadJson(manifestPath);
    ExpectString(manifest.RootElement, "name", "idd", "Claude plugin manifest name");
    ExpectString(manifest.RootElement, "version", version, "Claude plugin manifest version");
    ExpectMissing(Path.Combine(root, "CLAUDE.md"));

    foreach (var field in new[] { "skills", "interface", "capabilities", "defaultPrompt" })
    {
        if (manifest.RootElement.TryGetProperty(field, out _))
        {
            failures.Add($"Claude plugin manifest contains unsupported field {field}.");
        }
    }

    ExpectIntentAndFactorySkills(root, "Claude");
}

void CodexPluginSmokeTests()
{
    var root = Path.Combine(marketplaceRoot, "plugins", "codex", "idd");
    ExpectDirectory(root);
    var manifestPath = Path.Combine(root, ".codex-plugin", "plugin.json");
    ExpectFile(manifestPath);
    using var manifest = ReadJson(manifestPath);
    ExpectString(manifest.RootElement, "name", "idd", "Codex plugin manifest name");
    ExpectString(manifest.RootElement, "version", version, "Codex plugin manifest version");
    ExpectString(manifest.RootElement, "skills", "./skills/", "Codex plugin skills path");
    ExpectString(
        manifest.RootElement.GetProperty("interface"),
        "displayName",
        "Intent-Driven Development",
        "Codex plugin interface.displayName");
    ExpectMissing(Path.Combine(root, "AGENTS.md"));

    ExpectIntentAndFactorySkills(root, "Codex");
}

void ExpectIntentAndFactorySkills(string pluginRoot, string platform)
{
    foreach (var skillName in new[]
    {
        "idd-project-init",
        "idd-intent-change",
        "idd-code-implement",
        "idd-factory-create-work-plan",
        "idd-factory-execute-work-plan",
        "idd-factory-finish-work"
    })
    {
        ExpectFile(Path.Combine(pluginRoot, "skills", skillName, "SKILL.md"));
    }

    var roleReference = Path.Combine(
        pluginRoot,
        "skills",
        "idd-factory-execute-work-plan",
        "references",
        "roles",
        "implementer.md");
    ExpectFile(roleReference);

    if (!File.Exists(roleReference))
    {
        failures.Add($"{platform} plugin does not package Factory role references.");
    }
}

void SkillPolicySmokeTests()
{
    foreach (var skillName in new[] { "idd-skip", "idd-project-init" })
    {
        var claudeSkill = Path.Combine(
            marketplaceRoot,
            "plugins",
            "claude",
            "idd",
            "skills",
            skillName,
            "SKILL.md");
        ExpectFile(claudeSkill);
        var claudeText = File.Exists(claudeSkill) ? File.ReadAllText(claudeSkill) : "";
        ExpectContains(claudeText, "disable-model-invocation: true", $"Claude {skillName} manual policy");
        ExpectContains(claudeText, "user-invocable: true", $"Claude {skillName} user invocable policy");

        var codexSkill = Path.Combine(
            marketplaceRoot,
            "plugins",
            "codex",
            "idd",
            "skills",
            skillName,
            "SKILL.md");
        ExpectFile(codexSkill);
        var codexText = File.Exists(codexSkill) ? ReadFrontMatter(codexSkill) : "";
        foreach (var field in new[]
        {
            "disable-model-invocation",
            "user-invocable",
            "context",
            "agent",
            "allowed-tools",
            "argument-hint"
        })
        {
            if (ContainsYamlKey(codexText, field))
            {
                failures.Add($"Codex {skillName} SKILL.md contains Claude-only field {field}.");
            }
        }

        var codexPolicy = Path.Combine(
            marketplaceRoot,
            "plugins",
            "codex",
            "idd",
            "skills",
            skillName,
            "agents",
            "openai.yaml");
        ExpectFile(codexPolicy);
        ExpectContains(
            File.Exists(codexPolicy) ? File.ReadAllText(codexPolicy) : "",
            "allow_implicit_invocation: false",
            $"Codex {skillName} manual policy");
    }

    foreach (var skillPath in Directory.GetFiles(
                 Path.Combine(marketplaceRoot, "plugins", "codex"),
                 "SKILL.md",
                 SearchOption.AllDirectories))
    {
        var text = ReadFrontMatter(skillPath);
        foreach (var field in new[]
        {
            "disable-model-invocation",
            "user-invocable",
            "context",
            "agent",
            "allowed-tools",
            "argument-hint"
        })
        {
            if (ContainsYamlKey(text, field))
            {
                failures.Add($"Codex skill {Relative(skillPath)} contains Claude-only field {field}.");
            }
        }
    }

    var projectInitAsset = Path.Combine(
        marketplaceRoot,
        "plugins",
        "codex",
        "idd",
        "skills",
        "idd-project-init",
        "assets",
        "bootstrap",
        ".idd",
        "intent",
        "README.md");
    ExpectFile(projectInitAsset);
}

void ReleaseVersionSmokeTests()
{
    foreach (var manifestPath in Directory.GetFiles(marketplaceRoot, "plugin.json", SearchOption.AllDirectories))
    {
        using var manifest = ReadJson(manifestPath);
        ExpectString(manifest.RootElement, "version", version, $"Plugin manifest {Relative(manifestPath)} version");
    }

    using var marketplace = ReadJson(Path.Combine(marketplaceRoot, ".claude-plugin", "marketplace.json"));
    ExpectString(marketplace.RootElement, "version", version, "Claude marketplace release version");
}

void PublishedLayoutSmokeTests()
{
    foreach (var path in new[]
    {
        ".claude-plugin/marketplace.json",
        ".agents/plugins/marketplace.json",
        "plugins/claude/idd",
        "plugins/codex/idd"
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

    foreach (var legacyPath in new[]
    {
        "plugins/claude/idd-core",
        "plugins/claude/idd-factory",
        "plugins/codex/idd-core",
        "plugins/codex/idd-factory"
    })
    {
        ExpectMissing(Path.Combine(marketplaceRoot, legacyPath.Replace('/', Path.DirectorySeparatorChar)));
    }

    ExpectMissing(Path.Combine(marketplaceRoot, "claude", "marketplace.json"));
    ExpectMissing(Path.Combine(marketplaceRoot, "codex", "marketplace.json"));
}

void NativeValidatorSmokeTests()
{
    if (!IsCommandAvailable("claude"))
    {
        Console.WriteLine("Claude CLI not available; skipping native validator smoke tests.");
        return;
    }

    foreach (var path in new[]
    {
        marketplaceRoot,
        Path.Combine(marketplaceRoot, "plugins", "claude", "idd")
    })
    {
        if (RunProcess("claude", $"plugin validate \"{path}\"") != 0)
        {
            failures.Add($"Claude validator failed for {Relative(path)}.");
        }
    }
}

void ExpectGeneratorCheckPasses()
{
    if (RunProcess("dotnet", $"exec \"{generatorDll}\" --check --version {version}") != 0)
    {
        failures.Add("Generator --check failed.");
    }
}

void ExpectSecondRunStable()
{
    var before = SnapshotMarketplace();
    RunGenerator();
    var after = SnapshotMarketplace();
    if (!before.SequenceEqual(after))
    {
        failures.Add("Running generator twice changed marketplace output.");
    }
}

void ExpectNoText(params string[] forbidden)
{
    var files = Directory.GetFiles(repoRoot, "*", SearchOption.AllDirectories)
        .Where(path => !path.Contains(
            $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}",
            StringComparison.Ordinal))
        .Where(path => !path.Contains(
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            StringComparison.Ordinal))
        .Where(path => !path.Contains(
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            StringComparison.Ordinal))
        .Where(path => !path.Contains(
            $"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}",
            StringComparison.Ordinal))
        .Where(path => !path.Contains(
            $"{Path.DirectorySeparatorChar}tools{Path.DirectorySeparatorChar}smoke-tests{Path.DirectorySeparatorChar}",
            StringComparison.Ordinal))
        .Where(path => Path.GetExtension(path) is ".md" or ".json" or ".yml" or ".ps1" or ".cs" or ".csproj")
        .ToArray();

    foreach (var file in files)
    {
        var text = File.ReadAllText(file);
        foreach (var value in forbidden)
        {
            if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"Forbidden legacy text '{value}' remains in {Relative(file)}.");
            }
        }
    }
}

string[] SnapshotMarketplace() =>
    Directory.GetFiles(marketplaceRoot, "*", SearchOption.AllDirectories)
        .Select(path => $"{Relative(path)}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}")
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

void ExpectPluginCount(JsonElement[] plugins, string context)
{
    var names = plugins.Select(plugin => plugin.GetProperty("name").GetString()).ToArray();
    if (!names.SequenceEqual(["idd"]))
    {
        failures.Add($"{context} plugins are not exactly idd.");
    }
}

void ExpectExistingMarketplacePath(string? relativePath, string context)
{
    if (string.IsNullOrWhiteSpace(relativePath))
    {
        failures.Add($"{context} is empty.");
        return;
    }

    var fullPath = Path.GetFullPath(
        Path.Combine(marketplaceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    if (!fullPath.StartsWith(Path.GetFullPath(marketplaceRoot), StringComparison.OrdinalIgnoreCase))
    {
        failures.Add($"{context} resolves outside marketplace root.");
        return;
    }

    if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
    {
        failures.Add($"{context} does not exist: {relativePath}");
    }
}

void ExpectString(JsonElement element, string property, string expected, string context)
{
    if (!element.TryGetProperty(property, out var value) ||
        value.ValueKind != JsonValueKind.String ||
        !StringComparer.Ordinal.Equals(value.GetString(), expected))
    {
        failures.Add($"{context} is not '{expected}'.");
    }
}

void ExpectContains(string text, string expected, string context)
{
    if (!text.Contains(expected, StringComparison.Ordinal))
    {
        failures.Add($"{context} is missing '{expected}'.");
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
        failures.Add($"Unexpected path: {Relative(path)}");
    }
}

void ExpectNoPath(string relativePath)
{
    var path = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    if (File.Exists(path) || Directory.Exists(path))
    {
        failures.Add($"Legacy path remains: {relativePath}");
    }
}

JsonDocument ReadJson(string path) => JsonDocument.Parse(File.ReadAllText(path));

string ReadFrontMatter(string path)
{
    var text = File.ReadAllText(path);
    if (!text.StartsWith("---", StringComparison.Ordinal))
    {
        return "";
    }

    var end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
    return end < 0 ? text : text[..end];
}

bool ContainsYamlKey(string frontMatter, string key) =>
    frontMatter.Split('\n').Any(line => line.TrimStart().StartsWith($"{key}:", StringComparison.Ordinal));

bool IsCommandAvailable(string command)
{
    var path = Environment.GetEnvironmentVariable("PATH") ?? "";
    var extensions = OperatingSystem.IsWindows()
        ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
        : [""];

    return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .SelectMany(directory => extensions.Select(extension => Path.Combine(directory, command + extension)))
        .Any(File.Exists);
}

int RunProcess(string fileName, string arguments)
{
    using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
    {
        WorkingDirectory = repoRoot,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    });

    if (process is null)
    {
        return 1;
    }

    Console.Write(process.StandardOutput.ReadToEnd());
    Console.Error.Write(process.StandardError.ReadToEnd());
    process.WaitForExit();
    return process.ExitCode;
}

string Relative(string path) => Path.GetRelativePath(repoRoot, path).Replace('\\', '/');

static string FindRepoRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
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

static string ParseVersion(string[] args)
{
    string? version = null;
    for (var index = 0; index < args.Length; index++)
    {
        if (!StringComparer.Ordinal.Equals(args[index], "--version"))
        {
            throw new InvalidOperationException($"Unknown option: {args[index]}");
        }

        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Missing value for --version.");
        }

        version = args[++index];
    }

    if (string.IsNullOrWhiteSpace(version))
    {
        throw new InvalidOperationException("Missing required --version MAJOR.MINOR.PATCH option.");
    }

    return version;
}
