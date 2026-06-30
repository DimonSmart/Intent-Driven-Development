using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

internal sealed partial class SmokeTestSuite
{
    static string LegacySpecsDirectory => ".sp" + "ecs";

    void ExpectTempFile(string root, string relativePath, string failure)
    {
        if (!File.Exists(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar))))
        {
            failures.Add(failure);
        }
    }

    void ExpectTempMissing(string root, string relativePath, string failure)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(path) || Directory.Exists(path))
        {
            failures.Add(failure);
        }
    }

    void CopyDirectoryRecursive(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var destinationPath = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
        }
    }

    void ExpectSecondRunStable()
    {
        var before = SnapshotGeneratedFiles();
        RunGenerator();
        var after = SnapshotGeneratedFiles();
        if (!before.SequenceEqual(after))
        {
            failures.Add("Running generator twice changed generated output.");
        }
    }

    void RunGenerator()
    {
        var exitCode = RunProcess("dotnet", $"exec \"{generatorDll}\"");
        if (exitCode != 0)
        {
            failures.Add("Generator failed.");
        }
    }

    IEnumerable<string> GeneratedFiles() =>
        Directory.Exists(Path.Combine(repoRoot, "generated"))
            ? Directory.GetFiles(Path.Combine(repoRoot, "generated"), "*", SearchOption.AllDirectories).OrderBy(path => path)
            : Array.Empty<string>();

    string[] SnapshotGeneratedFiles() =>
        GeneratedFiles()
            .Select(path => $"{Relative(path)}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}")
            .OrderBy(value => value)
            .ToArray();

    int RunProcess(string fileName, string arguments, string? workingDirectory = null)
    {
        return RunProcessResult(fileName, arguments, workingDirectory).ExitCode;
    }

    ProcessResult RunProcessResult(string fileName, string arguments, string? workingDirectory = null, bool echoOutput = true)
    {
        using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory ?? repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        });

        if (process is null)
        {
            failures.Add($"Could not start process: {fileName}");
            return new ProcessResult(1, "", $"Could not start process: {fileName}");
        }

        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (echoOutput && !string.IsNullOrWhiteSpace(standardOutput))
        {
            Console.Write(standardOutput);
        }

        if (echoOutput && !string.IsNullOrWhiteSpace(standardError))
        {
            Console.Error.Write(standardError);
        }

        return new ProcessResult(process.ExitCode, standardOutput, standardError);
    }

    string Relative(string path) => Path.GetRelativePath(repoRoot, path).Replace('\\', '/');

    string[] EntryPoints() =>
    [
        "generated/claude/CLAUDE.md",
        "generated/codex/AGENTS.md",
        "generated/gemini/GEMINI.md",
        "generated/copilot/.github/copilot-instructions.md"
    ];

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

}
