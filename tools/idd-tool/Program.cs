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
                "list-packs" => ListPacks(),
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
              intent-driven-development install --target <target> [--pack <pack>]... [--entry minimal|none|full] [--force]
              intent-driven-development install --all [--pack <pack>]... [--entry minimal|none|full] [--force]
              intent-driven-development list-targets
              intent-driven-development list-packs
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

    private int ListPacks()
    {
        foreach (var pack in ReadManifest().Packs.Keys.OrderBy(name => name, StringComparer.Ordinal))
        {
            Console.WriteLine(pack);
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
        EnsureNoUnknownOptions(commandArgs, "--target", "--all", "--entry", "--force", "--pack");

        var manifest = ReadManifest();
        var force = commandArgs.Contains("--force", StringComparer.Ordinal);
        var installAll = commandArgs.Contains("--all", StringComparer.Ordinal);
        var target = ValueAfter(commandArgs, "--target");
        var entryMode = ParseEntryMode(ValueAfter(commandArgs, "--entry"));
        var selectedPacks = ResolvePacks(manifest, ValuesAfter(commandArgs, "--pack"));

        if (installAll && target is not null)
        {
            return Fail("Use either --all or --target <target>, not both.");
        }

        if (!installAll && target is null)
        {
            return Fail("Missing target. Use --target <target> or --all.");
        }

        var targets = installAll ? manifest.Targets : [ValidateTarget(manifest, target!)];
        ValidateEntryModeCapabilities(manifest, targets, entryMode, installAll);
        ValidatePackTargetCapabilities(manifest, targets, selectedPacks);
        var plannedFiles = CollectTargetFiles(manifest, targets, entryMode, selectedPacks);
        CopyPlannedFiles(plannedFiles, Directory.GetCurrentDirectory(), force);
        var packText = IsDefaultPackSelection(manifest, selectedPacks)
            ? ""
            : $" and packs: {string.Join(", ", selectedPacks)}";
        Console.WriteLine($"Installed {string.Join(", ", targets)} with {FormatEntryMode(entryMode)} entry{packText}.");
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

    private static string FormatEntryMode(EntryMode entryMode) =>
        entryMode.ToString().ToLowerInvariant();

    private static void ValidateEntryModeCapabilities(Manifest manifest, IReadOnlyList<string> targets, EntryMode entryMode, bool installAll)
    {
        if (entryMode != EntryMode.None)
        {
            return;
        }

        if (manifest.TargetCapabilities is null)
        {
            throw new ToolException("Bundled manifest does not define targetCapabilities.");
        }

        var incompatible = targets
            .Where(target => !SupportsGeneratedSkills(manifest, target))
            .ToArray();

        if (incompatible.Length == 0)
        {
            return;
        }

        if (installAll)
        {
            throw new ToolException(
                $"The following targets do not support generated skills: {string.Join(", ", incompatible)}." +
                Environment.NewLine +
                "--entry none would install no entry point and no skills for those targets." +
                Environment.NewLine +
                "Use --entry minimal or install skill-capable targets explicitly.");
        }

        var target = incompatible[0];
        throw new ToolException(
            $"Target {target} does not support generated skills. --entry none would install no entry point and no skills." +
            Environment.NewLine +
            "Use --entry minimal or --entry full for this target.");
    }

    private static bool SupportsGeneratedSkills(Manifest manifest, string target)
    {
        if (manifest.TargetCapabilities is null ||
            !manifest.TargetCapabilities.TryGetValue(target, out var capabilities))
        {
            throw new ToolException($"Bundled manifest does not define targetCapabilities for target: {target}");
        }

        return capabilities.SupportsSkills;
    }

    private static IReadOnlyList<string> ResolvePacks(Manifest manifest, IReadOnlyList<string> requestedPacks)
    {
        ValidatePackManifest(manifest);
        var selected = new HashSet<string>(StringComparer.Ordinal);

        if (requestedPacks.Count == 0)
        {
            foreach (var (packName, pack) in manifest.Packs)
            {
                if (pack.Default)
                {
                    AddPackWithDependencies(manifest, packName, selected);
                }
            }
        }
        else
        {
            foreach (var packName in requestedPacks.Distinct(StringComparer.Ordinal))
            {
                if (!manifest.Packs.ContainsKey(packName))
                {
                    throw new ToolException($"Unknown pack: {packName}" + Environment.NewLine + $"Available packs: {string.Join(", ", manifest.Packs.Keys.OrderBy(name => name, StringComparer.Ordinal))}");
                }

                AddPackWithDependencies(manifest, packName, selected);
            }
        }

        return selected.OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    private static void AddPackWithDependencies(Manifest manifest, string packName, HashSet<string> selected)
    {
        foreach (var dependency in manifest.Packs[packName].Requires)
        {
            AddPackWithDependencies(manifest, dependency, selected);
        }

        selected.Add(packName);
    }

    private static bool IsDefaultPackSelection(Manifest manifest, IReadOnlyList<string> selectedPacks)
    {
        var defaultPacks = manifest.Packs
            .Where(item => item.Value.Default)
            .Select(item => item.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        return defaultPacks.SequenceEqual(selectedPacks.OrderBy(name => name, StringComparer.Ordinal), StringComparer.Ordinal);
    }

    private static void ValidatePackTargetCapabilities(Manifest manifest, IReadOnlyList<string> targets, IReadOnlyList<string> selectedPacks)
    {
        var selectedSkills = SelectedSkills(manifest, selectedPacks);
        if (selectedSkills.Count == 0)
        {
            return;
        }

        var incompatible = targets
            .Where(target => !SupportsGeneratedSkills(manifest, target))
            .ToArray();

        if (incompatible.Length > 0 && selectedPacks.Contains("factory", StringComparer.Ordinal))
        {
            throw new ToolException($"Factory pack requires generated skills. Unsupported targets: {string.Join(", ", incompatible)}.");
        }
    }

    private static void ValidatePackManifest(Manifest manifest)
    {
        foreach (var (packName, pack) in manifest.Packs)
        {
            foreach (var dependency in pack.Requires)
            {
                if (!manifest.Packs.ContainsKey(dependency))
                {
                    throw new ToolException($"Pack '{packName}' requires unknown pack '{dependency}'.");
                }
            }
        }

        foreach (var packName in manifest.Packs.Keys)
        {
            ValidatePackDependencyAcyclic(manifest, packName, [], []);
        }
    }

    private static void ValidatePackDependencyAcyclic(
        Manifest manifest,
        string packName,
        HashSet<string> visiting,
        HashSet<string> visited)
    {
        if (visited.Contains(packName))
        {
            return;
        }

        if (!visiting.Add(packName))
        {
            throw new ToolException($"Pack dependency cycle includes '{packName}'.");
        }

        foreach (var dependency in manifest.Packs[packName].Requires)
        {
            ValidatePackDependencyAcyclic(manifest, dependency, visiting, visited);
        }

        visiting.Remove(packName);
        visited.Add(packName);
    }

    private static HashSet<string> SelectedSkills(Manifest manifest, IReadOnlyList<string> selectedPacks)
    {
        var selectedSkills = new HashSet<string>(StringComparer.Ordinal);
        foreach (var packName in selectedPacks)
        {
            foreach (var skill in manifest.Packs[packName].Skills)
            {
                selectedSkills.Add(skill);
            }
        }

        return selectedSkills;
    }

    private static IReadOnlyList<PlannedFile> CollectTargetFiles(
        Manifest manifest,
        IEnumerable<string> targets,
        EntryMode entryMode,
        IReadOnlyList<string> selectedPacks)
    {
        var contentRoot = FindContentRoot();
        var byRelativePath = new Dictionary<string, PlannedFile>(StringComparer.Ordinal);
        var selectedSkills = SelectedSkills(manifest, selectedPacks);

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
                if (manifest.EntryPoints.TryGetValue(target, out var entryPoint) &&
                    StringComparer.Ordinal.Equals(relativePath, Normalize(entryPoint)))
                {
                    continue;
                }

                if (TryGetGeneratedSkillName(relativePath, out var skillName) &&
                    !selectedSkills.Contains(skillName))
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

            if (entryMode != EntryMode.None)
            {
                var fullEntry = BuildEntry(contentRoot, manifest, target, entryMode, selectedPacks);
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

        foreach (var projectFile in selectedPacks.SelectMany(pack => manifest.Packs[pack].ProjectFiles))
        {
            var projectFilesRoot = Path.Combine(contentRoot, projectFile.Source);
            if (!Directory.Exists(projectFilesRoot))
            {
                throw new ToolException($"Bundled project files not found: {projectFile.Source}");
            }

            foreach (var sourcePath in Directory.GetFiles(projectFilesRoot, "*", SearchOption.AllDirectories))
            {
                var relativePath = Normalize(Path.Combine(projectFile.Destination, Path.GetRelativePath(projectFilesRoot, sourcePath)));
                var content = File.ReadAllBytes(sourcePath);
                var plannedFile = new PlannedFile(relativePath, content, Sha256(content));

                if (byRelativePath.TryGetValue(relativePath, out var existing))
                {
                    if (!StringComparer.Ordinal.Equals(existing.Hash, plannedFile.Hash))
                    {
                        throw new ToolException($"Conflicting bundled files for path: {relativePath}");
                    }

                    continue;
                }

                byRelativePath.Add(relativePath, plannedFile);
            }
        }

        return byRelativePath.Values.ToArray();
    }

    private static bool TryGetGeneratedSkillName(string relativePath, out string skillName)
    {
        var parts = Normalize(relativePath).Split('/');
        for (var index = 0; index < parts.Length - 1; index++)
        {
            if (StringComparer.Ordinal.Equals(parts[index], "skills") && index + 1 < parts.Length)
            {
                skillName = parts[index + 1];
                return true;
            }
        }

        skillName = "";
        return false;
    }

    private static PlannedFile BuildEntry(
        string contentRoot,
        Manifest manifest,
        string target,
        EntryMode entryMode,
        IReadOnlyList<string> selectedPacks)
    {
        if (!manifest.EntryPoints.TryGetValue(target, out var entryPoint))
        {
            throw new ToolException($"No entry point configured for target: {target}");
        }

        var blocks = new List<string>
        {
            ReadRequired(Path.Combine(contentRoot, "src", "adapters", target, "entry.md")),
            ReadRequired(Path.Combine(contentRoot, "src", "canonical", "packs", "intent-driven-development.md"))
                .Replace("{{skillGuidance}}", BuildSkillGuidance(manifest, target, selectedPacks), StringComparison.Ordinal)
                .Replace("{{workflowGuidance}}", BuildWorkflowGuidance(manifest, target), StringComparison.Ordinal)
        };

        if (entryMode == EntryMode.Full)
        {
            blocks.Add(ReadCanonicalMethodology(contentRoot));
        }

        var content = string.Join(Environment.NewLine + Environment.NewLine, blocks.Select(block => block.Trim())) + Environment.NewLine;
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return new PlannedFile(Normalize(entryPoint), bytes, Sha256(bytes));
    }

    private static string BuildSkillGuidance(Manifest manifest, string target, IReadOnlyList<string> selectedPacks)
    {
        if (!SupportsGeneratedSkills(manifest, target))
        {
            return """
                This target does not use generated IDD skills. Keep IDD work focused and
                read only the documents needed for the current task.
                """;
        }

        var selectedSkills = SelectedSkills(manifest, selectedPacks)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => $"- `{name}`");
        var blocks = new List<string>
        {
            "Use installed IDD skills for specific workflows:" + Environment.NewLine + string.Join(Environment.NewLine, selectedSkills),
            """
            ## IDD Workflow Routing

            Use `spec-brainstorm` when product intent is unclear.
            Use `spec-change` when durable product behavior must change.
            Use `spec-implement` for one focused behavior already covered by
            `.specs/`, then use `spec-check-implementation`.
            Use `spec-new-document` only for a new durable product area, ADR, or
            spike.
            """
        };

        if (selectedPacks.Contains("factory", StringComparer.Ordinal))
        {
            blocks.Add("""
                ## IDD Factory Routing

                Use factory skills only for planned implementation orchestration,
                multi-step execution, task slicing, or agentic factory-style work.

                - Use `factory-create-work-plan` to create a temporary Factory Work Plan.
                - Use `factory-execute-work-plan` to execute an explicit Factory Work Plan.
                - Use `factory-review-task` after each bounded task.
                - Use `factory-review-work-result` after all tasks are complete.
                - Use `factory-finish-work` to summarize and clean temporary factory artifacts.

                Factory work plans are temporary execution state.
                They are not specs and must not be stored in `.specs/`.
                Do not read old factory work plans unless the user explicitly provides the exact path.
                """);
        }

        return string.Join(Environment.NewLine + Environment.NewLine, blocks.Select(block => block.Trim()));
    }

    private static string BuildWorkflowGuidance(Manifest manifest, string target) =>
        SupportsGeneratedSkills(manifest, target)
            ? "This file and installed IDD skills are workflow guidance.\nThey are not product specifications."
            : "This file is workflow guidance.\nIt is not a product specification.";

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
            .Where(file =>
            {
                var destination = Path.Combine(destinationRoot, file.RelativePath);
                return File.Exists(destination) &&
                    !StringComparer.Ordinal.Equals(Sha256(File.ReadAllBytes(destination)), file.Hash);
            })
            .Select(file => file.RelativePath)
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

            if (arg is "--target" or "--entry" or "--pack")
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

    private static IReadOnlyList<string> ValuesAfter(IReadOnlyList<string> commandArgs, string option)
    {
        var values = new List<string>();
        for (var index = 0; index < commandArgs.Count; index++)
        {
            if (!StringComparer.Ordinal.Equals(commandArgs[index], option))
            {
                continue;
            }

            if (index + 1 >= commandArgs.Count || commandArgs[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ToolException($"Missing value for {option}.");
            }

            values.Add(commandArgs[index + 1]);
            index++;
        }

        return values;
    }

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
    Dictionary<string, string> EntryPoints,
    Dictionary<string, TargetCapabilities> TargetCapabilities,
    Dictionary<string, PackDefinition> Packs);

internal sealed record TargetCapabilities(bool SupportsSkills);

internal sealed record PackDefinition(
    string Description,
    bool Default,
    string[] Requires,
    string[] Skills,
    string[] Agents,
    ProjectFileDefinition[] ProjectFiles);

internal sealed record ProjectFileDefinition(string Source, string Destination);

internal sealed record PlannedFile(string RelativePath, byte[] Content, string Hash);

internal enum EntryMode
{
    Minimal,
    None,
    Full
}

internal sealed class ToolException(string message) : Exception(message);
