using System.Text;
using System.Text.Json;

var repoRoot = FindRepoRoot();
var checkOnly = args.Contains("--check", StringComparer.OrdinalIgnoreCase);
var generator = new Generator(repoRoot);
var result = generator.Run(checkOnly);

if (result.Count == 0)
{
    Console.WriteLine(checkOnly ? "Generated files are current." : "Generated files updated.");
    return 0;
}

foreach (var item in result)
{
    Console.Error.WriteLine(item);
}

return 1;

static string FindRepoRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        if (Directory.Exists(Path.Combine(current.FullName, "src", "canonical")) &&
            Directory.Exists(Path.Combine(current.FullName, "src", "adapters")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new InvalidOperationException("Could not locate repository root.");
}

internal sealed class Generator(string repoRoot)
{
    private const int EntryPointLineLimit = 80;

    public IReadOnlyList<string> Run(bool checkOnly)
    {
        var errors = new List<string>();
        var adaptersRoot = Path.Combine(repoRoot, "src", "adapters");
        var adapterDefinitions = Directory
            .GetDirectories(adaptersRoot)
            .OrderBy(Path.GetFileName)
            .Select(adapterDir => new AdapterDefinition(adapterDir, ReadAdapter(adapterDir)))
            .ToArray();
        var knownAdapterNames = adapterDefinitions
            .Select(definition => definition.Config.Agent)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var adapterDefinition in adapterDefinitions)
        {
            var adapter = adapterDefinition.Config;
            var expectedFiles = BuildFiles(adapterDefinition.Directory, adapter, knownAdapterNames);
            var outputRoot = Path.Combine(repoRoot, "generated", adapter.Agent);

            if (checkOnly)
            {
                errors.AddRange(CheckFiles(outputRoot, expectedFiles));
                continue;
            }

            Directory.CreateDirectory(outputRoot);
            CleanOutput(outputRoot);
            foreach (var file in expectedFiles)
            {
                var fullPath = Path.Combine(outputRoot, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllText(fullPath, file.Content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }

        return errors;
    }

    private AdapterConfig ReadAdapter(string adapterDir)
    {
        var json = File.ReadAllText(Path.Combine(adapterDir, "adapter.json"));
        return JsonSerializer.Deserialize<AdapterConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException($"Invalid adapter config in {adapterDir}.");
    }

    private IReadOnlyList<GeneratedFile> BuildFiles(
        string adapterDir,
        AdapterConfig adapter,
        IReadOnlySet<string> knownAdapterNames)
    {
        var files = new List<GeneratedFile>();
        var entry = ReadRequired(Path.Combine(adapterDir, "entry.md"));
        var pack = BuildPack(adapter);
        var entryPoint = NormalizeContent(JoinBlocks(entry, pack));
        GuardEntryPointSize(adapter.EntryPoint, entryPoint);
        files.Add(new GeneratedFile(adapter.EntryPoint, entryPoint));

        if (adapter.SupportsSkills)
        {
            if (string.IsNullOrWhiteSpace(adapter.SkillsRoot))
            {
                throw new InvalidOperationException($"{adapter.Agent} supports skills but has no skillsRoot.");
            }

            var skillsRoot = Path.Combine(repoRoot, "src", "canonical", "skills");
            var skillDescriptionPath = Path.Combine(skillsRoot, "skill-descriptions.json");
            var skillDescriptions = ReadSkillDescriptions(skillDescriptionPath, knownAdapterNames);
            var skillPaths = Directory.GetFiles(skillsRoot, "spec-*.md").OrderBy(Path.GetFileName).ToArray();
            var skillNames = skillPaths.Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.Ordinal);

            foreach (var skillName in skillDescriptions.Keys.OrderBy(name => name, StringComparer.Ordinal))
            {
                if (!skillNames.Contains(skillName))
                {
                    throw new InvalidOperationException($"Unused skill description: {skillName}.");
                }
            }

            foreach (var skillPath in skillPaths)
            {
                var skillName = Path.GetFileNameWithoutExtension(skillPath);
                if (!skillDescriptions.TryGetValue(skillName, out var skillDescription))
                {
                    throw new InvalidOperationException($"Missing skill description for {skillName} in src/canonical/skills/skill-descriptions.json.");
                }

                var content = ReadRequired(skillPath);
                if (adapter.SupportsFrontMatter)
                {
                    content = JoinBlocks(
                        BuildSkillFrontMatter(skillName, skillDescription, adapter.Agent),
                        content);
                }

                var relativePath = Path.Combine(adapter.SkillsRoot!, skillName, "SKILL.md");
                files.Add(new GeneratedFile(relativePath, NormalizeContent(content)));
            }
        }

        return files;
    }

    private string BuildPack(AdapterConfig adapter)
    {
        var pack = ReadRequired(Path.Combine(repoRoot, "src", "canonical", "packs", "intent-driven-development.md"));
        var skillGuidance = adapter.SupportsSkills
            ? """
              Use IDD skills for specific workflows:
              - `spec-audit`
              - `spec-change`
              - `spec-implement`
              - `spec-import`
              - `spec-lint`
              - `spec-new-document`
              - `spec-normalize-current`
              - `spec-check-implementation`
              - `spec-update-from-implementation`

              ## IDD Workflow Routing

              When the user asks to change product behavior: use `spec-change`,
              then `spec-implement`, then `spec-check-implementation`.

              When the user asks to implement behavior already described in
              `.specs/`: use `spec-implement`, then `spec-check-implementation`.

              When the user reports a possible bug: use
              `spec-check-implementation`; if the current spec is clear, fix
              implementation with `spec-implement`; if the desired behavior
              changes product intent, use `spec-change` first.

              When the user asks to create a new feature: use `spec-change` if
              the feature extends an existing product area. Use `spec-new-document`
              only if the feature needs a new durable product area, ADR, or
              spike.

              Do not create a new spec merely because the user described a new
              task. Prefer updating the existing owning spec.
              """
            : """
              This target does not use generated IDD skills. Keep IDD work focused and
              read only the documents needed for the current task.
              """;
        var workflowGuidance = adapter.SupportsSkills
            ? "This file and installed IDD skills are workflow guidance.\nThey are not product specifications."
            : "This file is workflow guidance.\nIt is not a product specification.";

        return pack
            .Replace("{{skillGuidance}}", skillGuidance.Trim(), StringComparison.Ordinal)
            .Replace("{{workflowGuidance}}", workflowGuidance.Trim(), StringComparison.Ordinal);
    }

    private static void GuardEntryPointSize(string relativePath, string content)
    {
        var lineCount = content.ReplaceLineEndings("\n").Split('\n').Length;
        if (lineCount > EntryPointLineLimit)
        {
            throw new InvalidOperationException(
                $"Entry point is too large: {relativePath} has {lineCount} lines, limit is {EntryPointLineLimit}." +
                Environment.NewLine +
                "Move detailed workflow into skills or path-scoped instructions.");
        }
    }

    private static IReadOnlyList<string> CheckFiles(string outputRoot, IReadOnlyList<GeneratedFile> expectedFiles)
    {
        var errors = new List<string>();
        var expectedByPath = expectedFiles.ToDictionary(file => Normalize(file.RelativePath), file => file.Content);

        foreach (var expected in expectedByPath)
        {
            var fullPath = Path.Combine(outputRoot, expected.Key);
            if (!File.Exists(fullPath))
            {
                errors.Add($"Missing generated file: {Path.GetRelativePath(Directory.GetCurrentDirectory(), fullPath)}");
                continue;
            }

            var actual = File.ReadAllText(fullPath);
            if (!StringComparer.Ordinal.Equals(actual, expected.Value))
            {
                errors.Add($"Outdated generated file: {Path.GetRelativePath(Directory.GetCurrentDirectory(), fullPath)}");
            }
        }

        if (Directory.Exists(outputRoot))
        {
            foreach (var actualPath in Directory.GetFiles(outputRoot, "*", SearchOption.AllDirectories))
            {
                var relative = Normalize(Path.GetRelativePath(outputRoot, actualPath));
                if (!expectedByPath.ContainsKey(relative))
                {
                    errors.Add($"Unexpected generated file: {Path.GetRelativePath(Directory.GetCurrentDirectory(), actualPath)}");
                }
            }
        }

        return errors;
    }

    private static void CleanOutput(string outputRoot)
    {
        foreach (var entry in Directory.GetFileSystemEntries(outputRoot))
        {
            if (Directory.Exists(entry))
            {
                Directory.Delete(entry, recursive: true);
            }
            else
            {
                File.Delete(entry);
            }
        }
    }

    private static string NormalizeContent(string content) => content.TrimEnd() + Environment.NewLine;

    private static string ReadRequired(string path) =>
        File.Exists(path) ? File.ReadAllText(path) : throw new FileNotFoundException("Required file not found.", path);

    private static IReadOnlyDictionary<string, SkillDescription> ReadSkillDescriptions(
        string path,
        IReadOnlySet<string> knownAdapterNames)
    {
        var json = ReadRequired(path);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Invalid skill descriptions in {path}: root must be a JSON object.");
        }

        var descriptions = new Dictionary<string, SkillDescription>(StringComparer.Ordinal);
        foreach (var skillProperty in document.RootElement.EnumerateObject())
        {
            descriptions.Add(
                skillProperty.Name,
                ReadSkillDescription(path, skillProperty.Name, skillProperty.Value, knownAdapterNames));
        }

        return descriptions;
    }

    private static SkillDescription ReadSkillDescription(
        string path,
        string skillName,
        JsonElement value,
        IReadOnlySet<string> knownAdapterNames)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var description = value.GetString();
            GuardDescription(path, skillName, description);
            return new SkillDescription(description!, Adapters: null);
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Invalid skill description for {skillName} in {path}: expected string or object.");
        }

        if (!value.TryGetProperty("description", out var descriptionElement) ||
            descriptionElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Invalid skill description for {skillName} in {path}: description is required.");
        }

        var objectDescription = descriptionElement.GetString();
        GuardDescription(path, skillName, objectDescription);

        IReadOnlyDictionary<string, AdapterSkillMetadata>? adapters = null;
        if (value.TryGetProperty("adapters", out var adaptersElement))
        {
            if (adaptersElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"Invalid skill description for {skillName} in {path}: adapters must be an object.");
            }

            var adapterMetadata = new Dictionary<string, AdapterSkillMetadata>(StringComparer.Ordinal);
            foreach (var adapterProperty in adaptersElement.EnumerateObject())
            {
                if (!knownAdapterNames.Contains(adapterProperty.Name))
                {
                    throw new InvalidOperationException(
                        $"Invalid skill description for {skillName} in {path}: unknown adapter '{adapterProperty.Name}'.");
                }

                adapterMetadata.Add(
                    adapterProperty.Name,
                    ReadAdapterSkillMetadata(path, skillName, adapterProperty.Name, adapterProperty.Value));
            }

            adapters = adapterMetadata;
        }

        return new SkillDescription(objectDescription!, adapters);
    }

    private static AdapterSkillMetadata ReadAdapterSkillMetadata(
        string path,
        string skillName,
        string adapterName,
        JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"Invalid metadata for {skillName}/{adapterName} in {path}: adapter metadata must be an object.");
        }

        IReadOnlyDictionary<string, JsonElement>? frontMatter = null;
        if (value.TryGetProperty("frontmatter", out var frontMatterElement))
        {
            if (frontMatterElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    $"Invalid frontmatter for {skillName}/{adapterName} in {path}: frontmatter must be an object.");
            }

            var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var field in frontMatterElement.EnumerateObject())
            {
                if (StringComparer.Ordinal.Equals(field.Name, "name") ||
                    StringComparer.Ordinal.Equals(field.Name, "description"))
                {
                    throw new InvalidOperationException(
                        $"Invalid frontmatter for {skillName}/{adapterName} in {path}: '{field.Name}' is generated automatically and cannot be overridden.");
                }

                GuardSupportedFrontMatterValue(path, skillName, adapterName, field.Name, field.Value);
                fields.Add(field.Name, field.Value.Clone());
            }

            frontMatter = fields;
        }

        return new AdapterSkillMetadata(frontMatter);
    }

    private static void GuardDescription(string path, string skillName, string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new InvalidOperationException($"Invalid skill description for {skillName} in {path}: description cannot be empty.");
        }
    }

    private static void GuardSupportedFrontMatterValue(
        string path,
        string skillName,
        string adapterName,
        string fieldName,
        JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.String or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Number)
        {
            return;
        }

        if (value.ValueKind == JsonValueKind.Array &&
            value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Invalid frontmatter for {skillName}/{adapterName} in {path}: '{fieldName}' must be a string, bool, number, or string array.");
    }

    private static string BuildSkillFrontMatter(string skillName, SkillDescription skillDescription, string adapterName)
    {
        var lines = new List<string>
        {
            "---",
            $"name: {ToYamlString(skillName)}",
            $"description: {ToYamlString(skillDescription.Description)}"
        };

        if (skillDescription.Adapters?.TryGetValue(adapterName, out var adapterMetadata) == true &&
            adapterMetadata.Frontmatter is not null)
        {
            foreach (var field in adapterMetadata.Frontmatter)
            {
                lines.Add($"{field.Key}: {ToYamlValue(field.Value)}");
            }
        }

        lines.Add("---");
        return string.Join(Environment.NewLine, lines);
    }

    private static string ToYamlValue(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => ToYamlString(value.GetString() ?? ""),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.Array => "[" + string.Join(", ", value.EnumerateArray().Select(item => ToYamlString(item.GetString() ?? ""))) + "]",
            _ => throw new InvalidOperationException($"Unsupported YAML frontmatter value: {value.ValueKind}.")
        };

    private static string ToYamlString(string value)
    {
        if (NeedsQuotedYamlString(value))
        {
            return JsonSerializer.Serialize(value);
        }

        return value;
    }

    private static bool NeedsQuotedYamlString(string value)
    {
        if (value.Length == 0 || !StringComparer.Ordinal.Equals(value, value.Trim()))
        {
            return true;
        }

        return value.Any(character => character is ':' or '[' or ']' or '{' or '}' or '#' or '\r' or '\n' or '"' or '\'');
    }

    private static string JoinBlocks(params string[] blocks) =>
        string.Join(Environment.NewLine + Environment.NewLine, blocks.Select(block => block.Trim()));

    private static string Normalize(string path) => path.Replace('\\', '/');
}

internal sealed record AdapterConfig(
    string Agent,
    string EntryPoint,
    string? SkillsRoot,
    bool SupportsSkills,
    bool SupportsFrontMatter);

internal sealed record AdapterDefinition(string Directory, AdapterConfig Config);

internal sealed record SkillDescription(
    string Description,
    IReadOnlyDictionary<string, AdapterSkillMetadata>? Adapters);

internal sealed record AdapterSkillMetadata(
    IReadOnlyDictionary<string, JsonElement>? Frontmatter);

internal sealed record GeneratedFile(string RelativePath, string Content);
