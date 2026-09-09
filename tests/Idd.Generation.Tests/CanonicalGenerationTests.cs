using System.Text;
using System.Text.Json;
using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class CanonicalGenerationTests(GenerationFixture fixture)
{
    [Fact]
    public void CanonicalFactorySkills_ArePlatformNeutralAndKeepPlanningContract()
    {
        var forbidden = new[]
        {
            "Codex", "Claude", "spawn_agent", "wait_agent", "fork_context", "codex-dispatch",
            ".agents/skills/", "mcp__factory", "runtime/idd-factory.dll", "PowerShell", "`items`", "`message`"
        };
        fixture.AssertMissing(Path.Combine(fixture.RepoRoot, "src", "canonical", "factory"));

        foreach (var file in Directory.GetFiles(
                     Path.Combine(fixture.RepoRoot, "src", "canonical", "skills"), "idd-factory-*.md"))
        {
            var content = File.ReadAllText(file);
            foreach (var literal in forbidden)
                Assert.False(content.Contains(literal, StringComparison.Ordinal),
                    $"Canonical Factory file {fixture.Relative(file)} contains platform-specific literal '{literal}'.");
        }

        var decomposition = fixture.ReadText(Path.Combine(
            fixture.RepoRoot, "src", "canonical", "skills", "idd-factory-decompose-task.md"));
        foreach (var required in new[]
        {
            "all remaining tasks",
            "Stop before the",
            "first task whose meaningful contract",
            "# Task",
            "Do not choose capabilities"
        })
            Assert.Contains(required, decomposition);
    }

    [Fact]
    public void CanonicalRoleReader_PreservesToolsAndRejectsInvalidRoles()
    {
        var root = Path.Combine(Path.GetTempPath(), "idd-role-reader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "sample.md");
        var reader = new CanonicalRoleReader();

        try
        {
            File.WriteAllText(path, "---\ntools:\n  - file.read\n  - file.write\n  - command.execute\n---\n\n# Sample\n\nInstructions.\n");
            var role = reader.Read("sample", path);
            Assert.Equal([RoleTool.FileRead, RoleTool.FileWrite, RoleTool.CommandExecute], role.Tools);
            Assert.Equal("# Sample\n\nInstructions.", role.Instructions);

            foreach (var (name, content) in new Dictionary<string, string>
            {
                ["missing-front-matter"] = "# Sample\n",
                ["missing-tools"] = "---\nname: sample\n---\n# Sample\n",
                ["empty-tools"] = "---\ntools:\n---\n# Sample\n",
                ["unknown-tool"] = "---\ntools:\n  - workspace.write\n---\n# Sample\n",
                ["removed-repository-tool"] = "---\ntools:\n  - repository.read\n---\n# Sample\n",
                ["removed-factory-state-tool"] = "---\ntools:\n  - factory-state.write\n---\n# Sample\n",
                ["duplicate-tool"] = "---\ntools:\n  - file.read\n  - file.read\n---\n# Sample\n",
                ["invalid-yaml"] = "---\ntools: file.read\n---\n# Sample\n",
                ["empty-instructions"] = "---\ntools:\n  - file.read\n---\n"
            })
            {
                File.WriteAllText(path, content);
                var exception = Assert.Throws<InvalidOperationException>(() => reader.Read(name, path));
                Assert.Contains($"Role '{name}'", exception.Message);
                Assert.Contains(path, exception.Message);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CanonicalSkillReferences_AreOwnedValidAndMatchGeneratedCopies()
    {
        var manifestPath = Path.Combine(fixture.RepoRoot, "src", "canonical", "plugins", "plugin-manifest.json");
        using var document = JsonDocument.Parse(fixture.ReadText(manifestPath));

        foreach (var pluginProperty in document.RootElement.GetProperty("plugins").EnumerateObject())
        {
            var pluginName = pluginProperty.Name;
            var plugin = pluginProperty.Value;
            var skills = plugin.GetProperty("skills").EnumerateArray()
                .Select(skill => skill.GetString() ?? "")
                .ToHashSet(StringComparer.Ordinal);
            var destinationsBySkill = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            if (!plugin.TryGetProperty("skillReferences", out var references)) continue;

            foreach (var reference in references.EnumerateArray())
            {
                var skill = reference.GetProperty("skill").GetString() ?? "";
                var source = reference.GetProperty("source").GetString() ?? "";
                var destination = reference.GetProperty("destination").GetString() ?? "";

                Assert.Contains(skill, skills);

                var sourcePath = Path.GetFullPath(Path.Combine(
                    fixture.RepoRoot, source.Replace('/', Path.DirectorySeparatorChar)));
                var rootWithSeparator = fixture.RepoRoot.EndsWith(Path.DirectorySeparatorChar)
                    ? fixture.RepoRoot
                    : fixture.RepoRoot + Path.DirectorySeparatorChar;
                Assert.True(sourcePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase),
                    $"Reference source escapes repository root: {source}");
                fixture.AssertFile(sourcePath);

                var sourceBytes = File.ReadAllBytes(sourcePath);
                Assert.DoesNotContain((byte)0, sourceBytes);
                _ = new UTF8Encoding(false, true).GetString(sourceBytes);

                var normalizedDestination = SkillReferencePathValidator.NormalizeDestination(destination);
                if (!destinationsBySkill.TryGetValue(skill, out var destinations))
                {
                    destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    destinationsBySkill.Add(skill, destinations);
                }

                Assert.True(destinations.Add(normalizedDestination),
                    $"Plugin '{pluginName}' skill '{skill}' has a case-insensitive destination conflict at '{normalizedDestination}'.");

                foreach (var platform in new[] { "claude", "codex" })
                {
                    var generatedPath = Path.Combine(
                        fixture.MarketplaceRoot, "plugins", platform, pluginName, "skills", skill, "references",
                        normalizedDestination.Replace('/', Path.DirectorySeparatorChar));
                    fixture.AssertFile(generatedPath);
                    Assert.Equal(
                        GenerationFixture.NormalizeText(File.ReadAllText(sourcePath)),
                        GenerationFixture.NormalizeText(File.ReadAllText(generatedPath)));
                }
            }
        }

        foreach (var platform in new[] { "claude", "codex" })
            fixture.AssertMissing(Path.Combine(
                fixture.MarketplaceRoot, "plugins", platform, "idd-factory", "skills", "idd-route", "references"));
    }

    [Fact]
    public void RepositoryText_DoesNotReferenceRemovedVerificationPolicy()
    {
        var legacyPolicyPath = ".idd/" + "verification" + ".md";
        var offenders = fixture.GetTrackedTextFiles()
            .Where(relativePath =>
                File.ReadAllText(
                        Path.Combine(fixture.RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)),
                        new UTF8Encoding(false, true))
                    .Contains(legacyPolicyPath, StringComparison.Ordinal))
            .ToArray();

        Assert.True(offenders.Length == 0,
            $"Active repository content still references unsupported verification policy '{legacyPolicyPath}': {string.Join(", ", offenders)}");
    }

    [Theory]
    [InlineData("common-workflows.md")]
    [InlineData("docs/common-workflows.md")]
    public void SkillReferenceDestination_AcceptsValidRelativePaths(string destination)
    {
        Assert.Equal(destination, SkillReferencePathValidator.NormalizeDestination(destination));
    }

    [Theory]
    [InlineData("C:")]
    [InlineData("C:file.md")]
    [InlineData("C:/file.md")]
    [InlineData(@"C:\file.md")]
    [InlineData("/file.md")]
    [InlineData(@"\file.md")]
    [InlineData("//server/share/file.md")]
    [InlineData(@"\\server\share\file.md")]
    [InlineData("../file.md")]
    [InlineData("folder//file.md")]
    [InlineData("./file.md")]
    [InlineData("roles/file.md")]
    [InlineData("Roles/file.md")]
    public void SkillReferenceDestination_RejectsUnsafeOrReservedPaths(string destination)
    {
        Assert.Throws<ArgumentException>(() => SkillReferencePathValidator.NormalizeDestination(destination));
    }
}
