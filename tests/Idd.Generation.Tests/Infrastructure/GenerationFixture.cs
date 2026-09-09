using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Idd.Generation.Tests.Infrastructure;

[CollectionDefinition("Generation", DisableParallelization = true)]
public sealed class GenerationCollection : ICollectionFixture<GenerationFixture>
{
    public const string Name = "Generation";
}

public sealed record GeneratorProcessResult(int ExitCode, string Stdout, string Stderr);

public sealed class GenerationFixture
{
    public GenerationFixture()
    {
        RepoRoot = FindRepoRoot();
        MarketplaceRoot = Path.Combine(RepoRoot, "artifacts", "marketplace");
        GeneratorDll = Path.Combine(RepoRoot, "tools", "generate", "bin", "Debug", "net10.0", "Generate.dll");
        Version = Environment.GetEnvironmentVariable("IDD_GENERATION_TEST_VERSION") ?? "0.0.0";

        if (!File.Exists(GeneratorDll))
            throw new InvalidOperationException($"Generator assembly is missing: {GeneratorDll}. Build the solution before running generation tests.");

        var result = RunGenerator();
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Initial generation failed with exit code {result.ExitCode}.{Environment.NewLine}{result.Stderr}");
    }

    public string RepoRoot { get; }
    public string MarketplaceRoot { get; }
    public string GeneratorDll { get; }
    public string Version { get; }

    public GeneratorProcessResult RunGenerator(bool checkOnly = false)
    {
        var arguments = new List<string> { "exec", GeneratorDll };
        if (checkOnly) arguments.Add("--check");
        arguments.AddRange(["--version", Version]);
        return RunProcess("dotnet", arguments);
    }

    public GeneratorProcessResult RunProcess(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start '{executable}'.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return new(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    public JsonDocument ReadJson(params string[] relativeParts)
    {
        var path = Path.Combine(relativeParts);
        Assert.True(File.Exists(path), $"Missing file: {Relative(path)}");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    public string ReadText(string path)
    {
        Assert.True(File.Exists(path), $"Missing file: {Relative(path)}");
        return File.ReadAllText(path);
    }

    public void AssertFile(string path) => Assert.True(File.Exists(path), $"Missing file: {Relative(path)}");

    public void AssertDirectory(string path) => Assert.True(Directory.Exists(path), $"Missing directory: {Relative(path)}");

    public void AssertMissing(string path) =>
        Assert.False(File.Exists(path) || Directory.Exists(path), $"Obsolete path exists: {Relative(path)}");

    public void AssertMarketplacePath(string? relativePath)
    {
        Assert.False(string.IsNullOrWhiteSpace(relativePath), "Marketplace path is empty.");
        var fullPath = Path.GetFullPath(Path.Combine(MarketplaceRoot, relativePath!.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(MarketplaceRoot);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        Assert.True(
            fullPath.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase),
            $"Marketplace path resolves outside the marketplace root: {relativePath}");
        Assert.True(File.Exists(fullPath) || Directory.Exists(fullPath), $"Marketplace path does not exist: {relativePath}");
    }

    public string[] SnapshotMarketplace() => Directory.Exists(MarketplaceRoot)
        ? Directory.GetFiles(MarketplaceRoot, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => $"{Relative(path)}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}")
            .ToArray()
        : [];

    public IReadOnlyList<string> GetTrackedTextFiles()
    {
        var result = RunProcess("git", ["ls-files", "-z"]);
        Assert.Equal(0, result.ExitCode);
        return result.Stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(path => !IsExcludedPath(path))
            .Where(path => IsUtf8TextFile(Path.Combine(RepoRoot, path.Replace('/', Path.DirectorySeparatorChar))))
            .ToArray();
    }

    public string Relative(string path) => Path.GetRelativePath(RepoRoot, path).Replace('\\', '/');

    public static string NormalizeText(string text) => text.ReplaceLineEndings("\n").TrimEnd() + "\n";

    public static string ReadFrontMatter(string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        if (lines.Length == 0 || !StringComparer.Ordinal.Equals(lines[0], "---")) return "";
        var end = Array.FindIndex(lines, 1, line => StringComparer.Ordinal.Equals(line, "---"));
        return end < 0 ? "" : string.Join("\n", lines.Take(end + 1));
    }

    private static bool IsExcludedPath(string path) => path.Split('/').Any(segment =>
        segment.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("artifacts", StringComparison.OrdinalIgnoreCase));

    private static bool IsUtf8TextFile(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Contains((byte)0)) return false;
            _ = new UTF8Encoding(false, true).GetString(bytes);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Environment.CurrentDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "canonical")) &&
                Directory.Exists(Path.Combine(current.FullName, "tools", "generate")))
                return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
