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
}
