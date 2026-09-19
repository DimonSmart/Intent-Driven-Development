using System.Text.Json;
using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class EngineeringGuardrailsContractTests(GenerationFixture fixture)
{
    private string Canonical(params string[] parts) =>
        fixture.ReadText(Path.Combine([fixture.RepoRoot, "src", "canonical", .. parts]));

    [Fact]
    public void CanonicalEngineeringBootstrapFiles_HaveExpectedStructure()
    {
        var readme = Canonical("project-files", "engineering", "README.md");
        var index = Canonical("project-files", "engineering", "INDEX.md");

        Assert.Contains("^ENG-\\d{4}\\.rule-[a-z0-9][a-z0-9-]*\\.md$", readme, StringComparison.Ordinal);
        foreach (var heading in new[] { "## Rule", "## Applicability", "## Applies when", "## Rationale", "## Guidance", "## Verification" })
            Assert.Contains(heading, readme, StringComparison.Ordinal);
        Assert.Contains("Always", readme, StringComparison.Ordinal);
        Assert.Contains("Conditional", readme, StringComparison.Ordinal);
        Assert.Contains("| Rule | Applicability | Applies when | Summary |", index, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectWithoutEngineering_RemainsValidAndInitializationDoesNotCreateIt()
    {
        fixture.AssertMissing(Path.Combine(fixture.RepoRoot, ".idd", "engineering"));

        var reference = Canonical("methodology", "engineering-guardrails.md");
        var init = Canonical("skills", "idd-project-init.md");

        Assert.Contains("Absence of `.idd/engineering/` is valid", reference, StringComparison.Ordinal);
        Assert.Contains("Do not create `.idd/engineering/` during project initialization", init, StringComparison.Ordinal);
    }

    [Fact]
    public void EngineeringIds_UseCanonicalFormat()
    {
        var reference = Canonical("methodology", "engineering-guardrails.md");

        Assert.Contains("ENG-NNNN", reference, StringComparison.Ordinal);
        Assert.Contains("^ENG-\\d{4}\\.rule-[a-z0-9][a-z0-9-]*\\.md$", reference, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateEngineeringIds_AreBlocking()
    {
        var reference = Canonical("methodology", "engineering-guardrails.md");
        var lint = Canonical("skills", "idd-intent-lint.md");

        Assert.Contains("Each stable `ENG-NNNN` occurs in exactly one rule document", reference, StringComparison.Ordinal);
        Assert.Contains("same stable `ENG-NNNN` resolves to more than one rule document", lint, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingOrAmbiguousEngineeringResolution_IsBlocking()
    {
        var worker = Canonical("skills", "idd-factory-execute-subtask.md");

        Assert.Contains("If an ID is missing, ambiguous, malformed, or metadata-inconsistent, do not", worker, StringComparison.Ordinal);
        Assert.Contains("ENG-NNNN -> exactly one .idd/engineering/ENG-NNNN.rule-*.md", worker, StringComparison.Ordinal);
    }

    [Fact]
    public void IndexAndDocumentApplicability_MustMatch()
    {
        var reference = Canonical("methodology", "engineering-guardrails.md");

        Assert.Contains("INDEX Applicability equals document Applicability", reference, StringComparison.Ordinal);
    }

    [Fact]
    public void ConditionalRule_RequiresAppliesWhen()
    {
        var reference = Canonical("methodology", "engineering-guardrails.md");

        Assert.Contains("Every Conditional rule has a non-empty `## Applies when`", reference, StringComparison.Ordinal);
    }

    [Fact]
    public void PlannerProtocol_ContainsTaskRelatedEngineering()
    {
        var planner = Canonical("skills", "idd-factory-decompose-task.md");

        Assert.Contains("# TaskRelatedEngineering", planner, StringComparison.Ordinal);
        Assert.Contains("stable `ENG-NNNN` IDs only", planner, StringComparison.Ordinal);
    }

    [Fact]
    public void PlannerGuidance_ExcludesAlwaysRulesFromTaskRelatedEngineering()
    {
        var planner = Canonical("skills", "idd-factory-decompose-task.md");

        Assert.Contains("Never put Always rules there", planner, StringComparison.Ordinal);
        Assert.Contains("Never emit an Always rule in `TaskRelatedEngineering`", planner, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerProtocol_ConsumesConditionalAndAlwaysEngineering()
    {
        var worker = Canonical("skills", "idd-factory-execute-subtask.md");

        Assert.Contains("optional TaskRelatedEngineering IDs", worker, StringComparison.Ordinal);
        Assert.Contains("AlwaysEngineering IDs", worker, StringComparison.Ordinal);
        Assert.Contains("read all TaskRelatedIntent, TaskRelatedEngineering, and AlwaysEngineering", worker, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FactoryRun_DeterministicallyPropagatesAlwaysRules()
    {
        var run = Canonical("skills", "idd-factory-run.md");

        Assert.Contains("deterministically re-read the current Engineering INDEX", run, StringComparison.Ordinal);
        Assert.Contains("enumerate all current Always ENG IDs", run, StringComparison.Ordinal);
        Assert.Contains("recomputed", run, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectImplementation_UsesOptionalEngineeringLayer()
    {
        var implementation = Canonical("skills", "idd-code-implement.md");

        Assert.Contains("When `.idd/engineering/` is absent, continue exactly as before", implementation, StringComparison.Ordinal);
        Assert.Contains("Mechanically enumerate all INDEX entries with `Applicability = Always`", implementation, StringComparison.Ordinal);
        Assert.Contains("semantically relevant Conditional rules", implementation, StringComparison.Ordinal);
    }

    [Fact]
    public void ImplementationCheck_SeparatesProductEngineeringAndVerification()
    {
        var check = Canonical("skills", "idd-code-check-implementation.md");

        Assert.Contains("### Product intent conformance", check, StringComparison.Ordinal);
        Assert.Contains("### Engineering guardrail conformance", check, StringComparison.Ordinal);
        Assert.Contains("### Verification evidence", check, StringComparison.Ordinal);
        Assert.Contains("engineering-mismatch", check, StringComparison.Ordinal);
    }

    [Fact]
    public void ManagedInstructionBlock_ReferencesOptionalEngineering()
    {
        var init = Canonical("skills", "idd-project-init.md");

        Assert.Contains("When `.idd/engineering/` exists, treat it as the current", init, StringComparison.Ordinal);
        Assert.Contains("durable engineering guardrails", init, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedClaudeAndCodexArtifacts_PreserveCanonicalEngineeringSemantics()
    {
        var source = GenerationFixture.NormalizeText(Canonical("methodology", "engineering-guardrails.md"));
        var skills = new[]
        {
            ("idd-intent", "idd-code-implement"),
            ("idd-intent", "idd-code-check-implementation"),
            ("idd-intent", "idd-intent-lint"),
            ("idd-factory", "idd-factory-run"),
            ("idd-factory", "idd-factory-decompose-task"),
            ("idd-factory", "idd-factory-execute-subtask")
        };

        foreach (var platform in new[] { "claude", "codex" })
        foreach (var (plugin, skill) in skills)
        {
            var generated = fixture.ReadText(Path.Combine(
                fixture.MarketplaceRoot, "plugins", platform, plugin, "skills", skill,
                "references", "engineering-guardrails.md"));
            Assert.Equal(source, GenerationFixture.NormalizeText(generated));
        }
    }

    [Fact]
    public void FactoryPackaging_DoesNotReintroduceRuntimeMcpOrEngineeringPlugin()
    {
        using var manifest = JsonDocument.Parse(Canonical("plugins", "plugin-manifest.json"));
        var plugins = manifest.RootElement.GetProperty("plugins");

        Assert.True(plugins.TryGetProperty("idd-intent", out _));
        Assert.True(plugins.TryGetProperty("idd-factory", out _));
        Assert.False(plugins.TryGetProperty("idd-engineering", out _));
        Assert.Equal(2, plugins.EnumerateObject().Count());

        foreach (var platform in new[] { "claude", "codex" })
        {
            var factory = Path.Combine(fixture.MarketplaceRoot, "plugins", platform, "idd-factory");
            fixture.AssertMissing(Path.Combine(factory, "runtime"));
            fixture.AssertMissing(Path.Combine(factory, ".mcp.json"));
        }

        var run = Canonical("skills", "idd-factory-run.md");
        Assert.Contains("Do not recreate the removed", run, StringComparison.Ordinal);
        Assert.Contains("C# runtime", run, StringComparison.Ordinal);
    }
}
