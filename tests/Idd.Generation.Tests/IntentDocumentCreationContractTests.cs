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
        Assert.StartsWith("# Internal Intent Document Creation Workflow", Workflow);
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

        var generated = fixture.ReadText(Path.Combine(root, "skills", "idd-intent-change", "SKILL.md"));
        Assert.Contains("references/new-intent-document.md", generated);
        Assert.Contains("new-spec-required", generated);
        Assert.Contains("adr-required", generated);
        Assert.Contains("spike-required", generated);

        var inventory = fixture.ReadText(Path.Combine(root, "skills", "idd-help",
            "references", "skill-descriptions.json"));
        Assert.DoesNotContain("idd-intent-new-document", inventory);
    }

    [Fact]
    public void ParentWorkflow_LoadsCreationOnlyForThreeClassifications()
    {
        var parent = Canonical("skills", "idd-intent-change.md");
        Assert.Contains("If `new-spec-required`, `adr-required`, or `spike-required`, read", parent);
        Assert.Contains("`references/new-intent-document.md`", parent);
        Assert.Contains("Ordinary existing-spec updates do not", parent);
        Assert.Contains("inside this same execution", parent);
        Assert.Contains("without a child agent or additional confirmation", parent);
        Assert.Contains("return to step 8 without recursively invoking this skill", parent);
        Assert.Contains("For `existing-spec-update`, update the owning current spec", parent);
        Assert.DoesNotContain("idd-intent-new-document", parent);
    }

    [Fact]
    public void OwnershipContract_PreventsDuplicateSpecsAndPreservesDistinctDecisions()
    {
        Assert.Contains("Read `.idd/intent/README.md`", Workflow);
        Assert.Contains("Read `.idd/intent/INDEX.md`", Workflow);
        Assert.Contains("Read the relevant current `IDD-NNNN` documents", Workflow);
        Assert.Contains("if a current spec already owns the area", Workflow);
        Assert.Contains("not create a new spec", Workflow);
        Assert.Contains("Return control to the current", Workflow);
        Assert.Contains("Do not", Workflow);
        Assert.Contains("invoke it recursively", Workflow);
        Assert.Contains("For `adr-required`", Workflow);
        Assert.Contains("For `spike-required`", Workflow);
        Assert.Contains("An existing behavior-owning", Workflow);
        Assert.Contains("stop and", Workflow);
    }

    [Fact]
    public void AllocationContract_UsesHistoryAndBlocksOnUncertainIds()
    {
        Assert.Contains("IDD-NNNN.type-short-title.md", Workflow);
        Assert.Contains("all current `.idd/intent/IDD-NNNN.*.md` filenames", Workflow);
        Assert.Contains("all historically allocated `IDD-NNNN` identifiers", Workflow);
        Assert.Contains("including deleted documents", Workflow);
        Assert.Contains("max(previously allocated NNNN) + 1", Workflow);
        Assert.Contains("Do not reuse deleted IDs", Workflow);
        Assert.Contains("stop and request resolution", Workflow);
        Assert.Contains("Never use a bare", Workflow);
    }

    [Theory]
    [InlineData("spec", "## Behavior", "## Verification")]
    [InlineData("adr", "## Decision", "## Alternatives Considered")]
    [InlineData("spike", "## Question", "## Recommendation")]
    public void CreationContract_PreservesCanonicalTemplateAndSections(
        string type, string section1, string section2)
    {
        var template = Canonical("project-files", "intent", "_templates", type + ".md");
        Assert.Contains("# IDD-NNNN." + type + "-short-title", template);
        Assert.Contains(section1, template);
        Assert.Contains(section2, template);
        Assert.Contains("`.idd/intent/_templates/" + type + ".md`", Workflow);
        Assert.Contains("Update `.idd/intent/INDEX.md` in the same change", Workflow);
        Assert.Contains("stable plain-text `IDD-NNNN` identifier", Workflow);
        Assert.Contains("normative relations", Workflow);
        Assert.Contains("Only current normative content", Workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BrainstormAndFactory_PreflightAuthorityRemainsIntact()
    {
        var brainstorm = Canonical("skills", "idd-intent-brainstorm.md");
        var factory = Canonical("skills", "idd-factory-run.md");
        var preflight = Canonical("methodology", "intent-preflight.md");
        Assert.Contains("never edits files", brainstorm);
        Assert.Contains("If the user confirms a product direction", brainstorm);
        Assert.Contains("route to", brainstorm);
        Assert.Contains("`idd-intent-change`", brainstorm);
        Assert.DoesNotContain("idd-intent-new-document", brainstorm);
        Assert.Contains("defer", factory);
        Assert.Contains("Factory planners and workers cannot create or modify Product Intent", factory);
        Assert.Contains("Engineering management has completed", preflight);
        Assert.Contains("MissingIntentDecision", preflight);
        Assert.DoesNotContain("idd-intent-new-document", preflight);
    }

    [Fact]
    public void OtherWorkflowBoundariesRemainIndependent()
    {
        var bootstrap = Canonical("skills", "idd-intent-bootstrap.md");
        var import = Canonical("skills", "idd-intent-import.md");
        var drift = Canonical("skills", "idd-intent-drift-audit.md");
        var updateFromCode = Canonical("skills", "idd-code-update-intent.md");
        Assert.Contains("Bootstrap may still create its approved initial document set directly", bootstrap);
        Assert.Contains("Create target documents by durable product area", import);
        Assert.Contains("Use `idd-intent-change`", drift);
        Assert.DoesNotContain("idd-intent-new-document", drift);
        Assert.Contains("user explicitly confirms", updateFromCode);
        Assert.Contains("Update existing current specifications before creating new documents", updateFromCode);
    }

    [Fact]
    public void DirectCreationRequests_RouteThroughIntentChange()
    {
        var router = Canonical("skills", "idd-route.md");
        var docs = fixture.ReadText(Path.Combine(fixture.RepoRoot, "docs", "new-project.md"));
        Assert.Contains("create a new product specification, record an ADR", router);
        Assert.Contains("investigate an unresolved decision in a spike", router);
        Assert.Contains("Route them through", router);
        Assert.Contains("`idd-intent-change`", router);
        Assert.Contains("internal document-creation", docs);
        Assert.DoesNotContain("idd-intent-new-document", router);
        Assert.DoesNotContain("idd-intent-new-document", docs);
    }
}
