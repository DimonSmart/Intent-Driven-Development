using System.Text.Json;
using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class IntentDocumentCreationContractTests(GenerationFixture fixture)
{
    private string Canonical(params string[] parts) =>
        fixture.ReadText(Path.Combine([fixture.RepoRoot, "src", "canonical", .. parts]));

    private string Workflow => Canonical("methodology", "new-intent-document.md");

    [Fact]
    public void DocumentCreator_IsInternalReferenceNotPublicSkill()
    {
        using var manifest = JsonDocument.Parse(Canonical("plugins", "plugin-manifest.json"));
        var plugin = manifest.RootElement.GetProperty("plugins").GetProperty("idd-intent");
        var skills = plugin.GetProperty("skills").EnumerateArray()
            .Select(item => item.GetString()!).ToArray();

        Assert.Contains("idd-intent-change", skills);
        Assert.DoesNotContain("idd-intent-new-document", skills);
        var reference = Assert.Single(plugin.GetProperty("skillReferences").EnumerateArray(),
            item => item.GetProperty("destination").GetString() == "new-intent-document.md");
        Assert.Equal("idd-intent-change", reference.GetProperty("skill").GetString());
        Assert.Equal("src/canonical/methodology/new-intent-document.md",
            reference.GetProperty("source").GetString());

        using var descriptions = JsonDocument.Parse(Canonical("skills", "skill-descriptions.json"));
        Assert.False(descriptions.RootElement.TryGetProperty("idd-intent-new-document", out _));
        fixture.AssertMissing(Path.Combine(fixture.RepoRoot, "src", "canonical",
            "skills", "idd-intent-new-document.md"));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void BothPlatforms_ContainExactLazyReferenceWithoutStandaloneSkill(string platform)
    {
        var root = Path.Combine(fixture.MarketplaceRoot, "plugins", platform, "idd-intent");
        fixture.AssertMissing(Path.Combine(root, "skills", "idd-intent-new-document"));
        var referencePath = Path.Combine(root, "skills", "idd-intent-change",
            "references", "new-intent-document.md");
        fixture.AssertFile(referencePath);
        Assert.Equal(GenerationFixture.NormalizeText(Workflow),
            GenerationFixture.NormalizeText(fixture.ReadText(referencePath)));


    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void SemanticDocumentTypeReference_IsPackagedForCreationImportAndBootstrap(string platform)
    {
        var typeContract = GenerationFixture.NormalizeText(
            Canonical("methodology", "document-types.md"));

        foreach (var skill in new[]
            { "idd-intent-change", "idd-intent-import", "idd-intent-bootstrap", "idd-intent-lint" })
        {
            var path = Path.Combine(fixture.MarketplaceRoot, "plugins", platform,
                "idd-intent", "skills", skill, "references", "document-types.md");
            Assert.Equal(typeContract,
                GenerationFixture.NormalizeText(fixture.ReadText(path)));
        }
    }


}
