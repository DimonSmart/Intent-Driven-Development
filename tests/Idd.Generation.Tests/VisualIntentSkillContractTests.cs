using System.Text.Json;
using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class VisualIntentSkillContractTests(GenerationFixture fixture)
{
    [Fact]
    public void VisualIntent_IsAnIntentWorkflow_WithPackagedVisualReferences()
    {
        var root = Path.Combine(fixture.RepoRoot, "src", "canonical");
        var body = fixture.ReadText(Path.Combine(root, "skills", "idd-intent-visual-brainstorm.md"));
        Assert.Contains("Observed", body);
        Assert.Contains("Interpreted", body);
        Assert.Contains("Preferred", body);
        Assert.Contains("Normative", body);
        Assert.Contains("Essential", body);
        Assert.Contains("Avoid", body);
        Assert.Contains("idd-intent-change", body);
        Assert.Contains("Scope", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Candidate observable checks", body);
        Assert.Contains("Unconfirmed observations", body);
        Assert.Contains("separate request", body);

        using var manifest = JsonDocument.Parse(fixture.ReadText(
            Path.Combine(root, "plugins", "plugin-manifest.json")));
        var plugins = manifest.RootElement.GetProperty("plugins");
        var intent = plugins.GetProperty("idd-intent");
        Assert.Contains(intent.GetProperty("skills").EnumerateArray(),
            entry => entry.GetString() == "idd-intent-visual-brainstorm");
        Assert.DoesNotContain(plugins.GetProperty("idd-factory").GetProperty("skills").EnumerateArray(),
            entry => entry.GetString() == "idd-intent-visual-brainstorm");

        foreach (var platform in new[] { "claude", "codex" })
        {
            var skill = Path.Combine(fixture.MarketplaceRoot, "plugins", platform,
                "idd-intent", "skills", "idd-intent-visual-brainstorm");
            fixture.AssertFile(Path.Combine(skill, "SKILL.md"));
            fixture.AssertFile(Path.Combine(skill, "references", "visual-intent-method.md"));
            fixture.AssertFile(Path.Combine(skill, "references", "visual-card-library.md"));
            var sourceAssets = Path.Combine(root, "visual-intent-library");
            var generatedAssets = Path.Combine(skill, "assets", "visual-intent");
            fixture.AssertFile(Path.Combine(generatedAssets, "README.md"));
            foreach (var sourceFile in Directory.GetFiles(sourceAssets, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceAssets, sourceFile);
                var generatedFile = Path.Combine(generatedAssets, relative);
                fixture.AssertFile(generatedFile);
                Assert.Equal(File.ReadAllBytes(sourceFile), File.ReadAllBytes(generatedFile));
            }
        }

    }

    [Fact]
    public void VisualProductChange_PreservesExistingOperationsAndOwnership()
    {
        var root = Path.Combine(fixture.RepoRoot, "src", "canonical");
        var change = fixture.ReadText(Path.Combine(root, "skills", "idd-intent-change.md"));
        var method = fixture.ReadText(Path.Combine(root, "methodology", "visual-intent-method.md"));
        var newDocument = fixture.ReadText(Path.Combine(root, "methodology", "new-intent-document.md"));
        var router = fixture.ReadText(Path.Combine(root, "skills", "idd-route.md"));
        var workflows = fixture.ReadText(Path.Combine(root, "methodology", "common-workflows.md"));
        var selfIntent = fixture.ReadText(Path.Combine(fixture.RepoRoot, ".idd", "intent",
            "IDD-0003.spec-core-intent-and-generation.md"));

        foreach (var operation in new[] { "add", "modify", "remove", "not-applicable" })
            Assert.Contains(operation, change);

        foreach (var level in new[] { "Essential", "Preferred", "Free", "Avoid" })
        {
            Assert.Contains(level, change);
            Assert.Contains(level, method);
        }

        Assert.Contains("existing-spec-update", change);
        Assert.Contains("new-spec-required", change);
        Assert.Contains("references/new-intent-document.md", change);
        Assert.Contains("confirmed visual", newDocument, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("optional", newDocument, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("approved handoff", router, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("implementation-change", workflows);
        Assert.Contains("confirmed visual", selfIntent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("## Visual Intent", fixture.ReadText(
            Path.Combine(root, "project-files", "intent", "_templates", "spec.md")));
    }

    [Theory]
    [InlineData("a comparison-card choice", "card")]
    [InlineData("an ephemeral fidelity reference", "temp")]
    [InlineData("a scope-specific modification", "exceptions")]
    [InlineData("softening Essential to Preferred", "hard")]
    public void VisualIntentMaintenance_ExplicitlyGuardsAgainstLossOfMeaning(string scenario, string requiredTerm)
    {
        var root = Path.Combine(fixture.RepoRoot, "src", "canonical");
        var change = fixture.ReadText(Path.Combine(root, "skills", "idd-intent-change.md"));
        var method = fixture.ReadText(Path.Combine(root, "methodology", "visual-intent-method.md"));
        Assert.Contains(requiredTerm, change + method, StringComparison.OrdinalIgnoreCase);
    }
}
