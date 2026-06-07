using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

var app = new IntentDrivenDevelopmentTool(args);
return app.Run();

internal sealed class IntentDrivenDevelopmentTool(string[] args)
{
    private readonly string[] args = args;

    public int Run()
    {
        try
        {
            var command = args.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(command) ||
                command is "help" or "--help" or "-h")
            {
                PrintUsage();
                return 0;
            }

            return command switch
            {
                "list-targets" => ListTargets(),
                "version" => PrintVersion(),
                "init" => Init(),
                "install" => Install(),
                _ => Fail($"Unknown command: {command}")
            };
        }
        catch (ToolException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            Usage:
              intent-driven-development init [--force]
              intent-driven-development install --target <target> [--entry minimal|none|full] [--force]
              intent-driven-development install --all [--entry minimal|none|full] [--force]
              intent-driven-development list-targets
              intent-driven-development version
            """);
    }

    private int ListTargets()
    {
        foreach (var target in ReadManifest().Targets)
        {
            Console.WriteLine(target);
        }

        return 0;
    }

    private int PrintVersion()
    {
        var manifest = ReadManifest();
        var packageVersion = typeof(IntentDrivenDevelopmentTool).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        Console.WriteLine($"package: {packageVersion}");
        Console.WriteLine($"manifest: {manifest.Version}");
        return 0;
    }

    private int Init()
    {
        var commandArgs = args.Skip(1).ToArray();
        EnsureNoUnknownOptions(commandArgs, "--force");
        var force = commandArgs.Contains("--force", StringComparer.Ordinal);
        var source = Path.Combine(FindContentRoot(), "src", "canonical", "project-files", "specs");
        var destination = Path.Combine(Directory.GetCurrentDirectory(), ".specs");

        if (!Directory.Exists(source))
        {
            return Fail($"Bundled canonical project files not found: {source}");
        }

        if (Directory.Exists(destination) && !force)
        {
            return Fail("File already exists: .specs" + Environment.NewLine + "Use --force to overwrite.");
        }

        CopyDirectory(source, destination, force);
        Console.WriteLine("Initialized .specs.");
        return 0;
    }

    private int Install()
    {
        var commandArgs = args.Skip(1).ToArray();
        EnsureNoUnknownOptions(commandArgs, "--target", "--all", "--entry", "--force");

        var manifest = ReadManifest();
        var force = commandArgs.Contains("--force", StringComparer.Ordinal);
        var installAll = commandArgs.Contains("--all", StringComparer.Ordinal);
        var target = ValueAfter(commandArgs, "--target");
        var entryMode = ParseEntryMode(ValueAfter(commandArgs, "--entry"));

        if (installAll && target is not null)
        {
            return Fail("Use either --all or --target <target>, not both.");
        }

        if (!installAll && target is null)
        {
            return Fail("Missing target. Use --target <target> or --all.");
        }

        var targets = installAll ? manifest.Targets : [ValidateTarget(manifest, target!)];
        var plannedFiles = CollectTargetFiles(manifest, targets, entryMode);
        CopyPlannedFiles(plannedFiles, Directory.GetCurrentDirectory(), force);
        Console.WriteLine($"Installed {string.Join(", ", targets)} with {entryMode} entry.");
        return 0;
    }

    private Manifest ReadManifest()
    {
        var manifestPath = Path.Combine(FindContentRoot(), "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new ToolException($"Bundled manifest not found: {manifestPath}");
        }

        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (manifest is null)
        {
            throw new ToolException($"Invalid bundled manifest: {manifestPath}");
        }

        return manifest;
    }

    private static string FindContentRoot()
    {
        var installedContentRoot = Path.Combine(AppContext.BaseDirectory, "package-content");
        if (Directory.Exists(installedContentRoot))
        {
            return installedContentRoot;
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "canonical")) &&
                Directory.Exists(Path.Combine(current.FullName, "generated")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new ToolException("Could not locate bundled Intent-Driven Development content.");
    }

    private static string ValidateTarget(Manifest manifest, string target)
    {
        if (manifest.Targets.Contains(target, StringComparer.Ordinal))
        {
            return target;
        }

        throw new ToolException($"Unknown target: {target}" + Environment.NewLine + $"Available targets: {string.Join(", ", manifest.Targets)}");
    }

    private static EntryMode ParseEntryMode(string? value)
    {
        if (value is null)
        {
            return EntryMode.Minimal;
        }

        return value switch
        {
            "minimal" => EntryMode.Minimal,
            "none" => EntryMode.None,
            "full" => EntryMode.Full,
            _ => throw new ToolException($"Unknown entry mode: {value}" + Environment.NewLine + "Available entry modes: minimal, none, full")
        };
    }

    private static IReadOnlyList<PlannedFile> CollectTargetFiles(Manifest manifest, IEnumerable<string> targets, EntryMode entryMode)
    {
        var contentRoot = FindContentRoot();
        var byRelativePath = new Dictionary<string, PlannedFile>(StringComparer.Ordinal);

        foreach (var target in targets)
        {
            var sourceRoot = Path.Combine(contentRoot, "generated", target);
            if (!Directory.Exists(sourceRoot))
            {
                throw new ToolException($"Bundled generated target not found: {target}");
            }

            foreach (var sourcePath in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                var relativePath = Normalize(Path.GetRelativePath(sourceRoot, sourcePath));
                if (entryMode != EntryMode.Minimal &&
                    manifest.EntryPoints.TryGetValue(target, out var entryPoint) &&
                    StringComparer.Ordinal.Equals(relativePath, Normalize(entryPoint)))
                {
                    continue;
                }

                var content = File.ReadAllBytes(sourcePath);
                var hash = Sha256(content);

                if (byRelativePath.TryGetValue(relativePath, out var existing))
                {
                    if (!StringComparer.Ordinal.Equals(existing.Hash, hash))
                    {
                        throw new ToolException($"Conflicting bundled files for path: {relativePath}");
                    }

                    continue;
                }

                byRelativePath.Add(relativePath, new PlannedFile(relativePath, content, hash));
            }

            if (entryMode == EntryMode.Full)
            {
                var fullEntry = BuildFullEntry(contentRoot, manifest, target);
                if (byRelativePath.TryGetValue(fullEntry.RelativePath, out var existing))
                {
                    if (!StringComparer.Ordinal.Equals(existing.Hash, fullEntry.Hash))
                    {
                        throw new ToolException($"Conflicting bundled files for path: {fullEntry.RelativePath}");
                    }

                    continue;
                }

                byRelativePath.Add(fullEntry.RelativePath, fullEntry);
            }
        }

        return byRelativePath.Values.ToArray();
    }

    private static PlannedFile BuildFullEntry(string contentRoot, Manifest manifest, string target)
    {
        if (!manifest.EntryPoints.TryGetValue(target, out var entryPoint))
        {
            throw new ToolException($"No entry point configured for target: {target}");
        }

        var blocks = new[]
        {
            ReadRequired(Path.Combine(contentRoot, "src", "adapters", target, "entry.md")),
            ReadRequired(Path.Combine(contentRoot, "src", "canonical", "packs", "intent-driven-development.md"))
                .Replace("{{skillGuidance}}", "Use the generated IDD skills when they are available for the target.", StringComparison.Ordinal)
                .Replace("{{workflowGuidance}}", "This file and installed IDD skills are workflow guidance.\nThey are not product specifications.", StringComparison.Ordinal),
            ReadCanonicalMethodology(contentRoot)
        };

        var content = Header + string.Join(Environment.NewLine + Environment.NewLine, blocks.Select(block => block.Trim())) + Environment.NewLine;
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return new PlannedFile(Normalize(entryPoint), bytes, Sha256(bytes));
    }

    private static string ReadCanonicalMethodology(string contentRoot)
    {
        var methodologyRoot = Path.Combine(contentRoot, "src", "canonical", "methodology");
        var names = new[]
        {
            "intent-driven-development.md",
            "numbering.md",
            "document-types.md",
            "semantic-changes.md",
            "agent-workflow.md"
        };

        return string.Join(Environment.NewLine + Environment.NewLine, names.Select(name => ReadRequired(Path.Combine(methodologyRoot, name)).Trim()));
    }

    private static void CopyPlannedFiles(IReadOnlyList<PlannedFile> files, string destinationRoot, bool force)
    {
        var conflicts = files
            .Select(file => file.RelativePath)
            .Where(relativePath => File.Exists(Path.Combine(destinationRoot, relativePath)))
            .ToArray();

        if (conflicts.Length > 0 && !force)
        {
            throw new ToolException(string.Join(Environment.NewLine, conflicts.Select(path => $"File already exists: {path}")) +
                Environment.NewLine +
                "Use --force to overwrite.");
        }

        foreach (var file in files)
        {
            var destination = Path.Combine(destinationRoot, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllBytes(destination, file.Content);
        }
    }

    private static void CopyDirectory(string source, string destination, bool force)
    {
        if (Directory.Exists(destination) && !force)
        {
            throw new ToolException($"File already exists: {Normalize(Path.GetRelativePath(Directory.GetCurrentDirectory(), destination))}" +
                Environment.NewLine +
                "Use --force to overwrite.");
        }

        foreach (var sourcePath in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, sourcePath);
            var destinationPath = Path.Combine(destination, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }

    private static void EnsureNoUnknownOptions(IReadOnlyList<string> commandArgs, params string[] known)
    {
        for (var index = 0; index < commandArgs.Count; index++)
        {
            var arg = commandArgs[index];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            if (!known.Contains(arg, StringComparer.Ordinal))
            {
                throw new ToolException($"Unknown option: {arg}");
            }

            if (arg is "--target" or "--entry")
            {
                index++;
            }
        }
    }

    private static string? ValueAfter(IReadOnlyList<string> commandArgs, string option)
    {
        var index = Array.IndexOf(commandArgs.ToArray(), option);
        if (index < 0)
        {
            return null;
        }

        if (index + 1 >= commandArgs.Count || commandArgs[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ToolException($"Missing value for {option}.");
        }

        return commandArgs[index + 1];
    }

    private const string Header = """
        <!--
        Generated from Intent-Driven-Development canonical sources.
        Do not edit this file directly.
        Edit files under src/canonical instead.
        -->

        """;

    private static string ReadRequired(string path) =>
        File.Exists(path) ? File.ReadAllText(path) : throw new ToolException($"Required bundled file not found: {path}");

    private static string Sha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static string Normalize(string value) => value.Replace('\\', '/');

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}

internal sealed record Manifest(
    string Name,
    string Version,
    string CanonicalSource,
    string GeneratedRoot,
    string[] Targets,
    Dictionary<string, string> EntryPoints);

internal sealed record PlannedFile(string RelativePath, byte[] Content, string Hash);

internal enum EntryMode
{
    Minimal,
    None,
    Full
}

internal sealed class ToolException(string message) : Exception(message);
