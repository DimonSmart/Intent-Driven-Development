using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

var repoRoot = FindRepoRoot();
var failures = new List<string>();
var generatorDll = Path.Combine(repoRoot, "tools", "generate", "bin", "Debug", "net10.0", "Generate.dll");

RunGenerator();
ExpectMarketplace("claude", ".claude-plugin/plugin.json", "CLAUDE.md");
ExpectMarketplace("codex", ".codex-plugin/plugin.json", "AGENTS.md");
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
    if (RunProcess("dotnet", $"exec \"{generatorDll}\"") != 0)
    {
        failures.Add("Generator failed.");
    }
}

void ExpectMarketplace(string platform, string pluginManifestPath, string entryPoint)
{
    var platformRoot = Path.Combine(repoRoot, "artifacts", "marketplace", platform);
    ExpectFile(Path.Combine(platformRoot, "marketplace.json"));

    using var marketplace = JsonDocument.Parse(File.ReadAllText(Path.Combine(platformRoot, "marketplace.json")));
    var plugins = marketplace.RootElement.GetProperty("plugins").EnumerateArray().Select(plugin => plugin.GetProperty("name").GetString()).ToArray();
    if (!plugins.SequenceEqual(["idd-core", "idd-factory"]))
    {
        failures.Add($"{platform} marketplace plugins are not idd-core and idd-factory.");
    }

    foreach (var pluginName in plugins)
    {
        if (pluginName is null)
        {
            continue;
        }

        var pluginRoot = Path.Combine(platformRoot, "plugins", pluginName);
        ExpectFile(Path.Combine(pluginRoot, pluginManifestPath.Replace('/', Path.DirectorySeparatorChar)));
        ExpectFile(Path.Combine(pluginRoot, "idd-plugin.json"));
        ExpectDirectory(Path.Combine(pluginRoot, "skills"));
    }

    ExpectFile(Path.Combine(platformRoot, "plugins", "idd-core", "skills", "idd-project-init", "SKILL.md"));
    ExpectFile(Path.Combine(platformRoot, "plugins", "idd-core", entryPoint));
    ExpectMissing(Path.Combine(platformRoot, "plugins", "idd-factory", entryPoint));

    using var factoryMetadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(platformRoot, "plugins", "idd-factory", "idd-plugin.json")));
    var dependencies = factoryMetadata.RootElement.GetProperty("dependencies").EnumerateArray().Select(item => item.GetString()).ToArray();
    if (!dependencies.SequenceEqual(["idd-core"]))
    {
        failures.Add($"{platform} idd-factory does not depend on idd-core.");
    }
}

void ExpectGeneratorCheckPasses()
{
    if (RunProcess("dotnet", $"exec \"{generatorDll}\" --check") != 0)
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
        .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}tools{Path.DirectorySeparatorChar}smoke-tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
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
    Directory.GetFiles(Path.Combine(repoRoot, "artifacts", "marketplace"), "*", SearchOption.AllDirectories)
        .Select(path => $"{Relative(path)}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}")
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

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
