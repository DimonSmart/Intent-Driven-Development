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
        Assert.Contains("Next ID: ENG-0001", index, StringComparison.Ordinal);
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
        Assert.Contains("Before every worker execution, mechanically enumerate all current", run, StringComparison.Ordinal);
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
    public void EngineeringLint_CanonicalTextIsSingleAndWellFormed()
    {
        var lint = Canonical("skills", "idd-intent-lint.md");

        Assert.Equal(1, lint.Split("# idd-intent-lint", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, lint.Split("## Optional Engineering checks", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain(".md# idd-intent-lint", lint, StringComparison.Ordinal);
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
            ("idd-intent", "idd-engineering-change"),
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
    public void EngineeringManagement_IsOwnedByIntentPlugin()
    {
        using var manifest = JsonDocument.Parse(Canonical("plugins", "plugin-manifest.json"));
        var plugins = manifest.RootElement.GetProperty("plugins");
        var intentSkills = plugins.GetProperty("idd-intent").GetProperty("skills")
            .EnumerateArray().Select(value => value.GetString()).ToArray();

        Assert.Contains("idd-engineering-change", intentSkills);
        Assert.False(plugins.TryGetProperty("idd-engineering", out _));
        Assert.Equal(2, plugins.EnumerateObject().Count());
    }

    [Fact]
    public void EngineeringManagementSkill_DefinesLifecycleAndFactoryGuard()
    {
        var skill = Canonical("skills", "idd-engineering-change.md");

        Assert.Contains("Supported operations: `add`, `modify`, and `remove`.", skill, StringComparison.Ordinal);
        Assert.Contains("assets/bootstrap/.idd/engineering/", skill, StringComparison.Ordinal);
        Assert.Contains(".idd/factory/current/request.md", skill, StringComparison.Ordinal);
        Assert.Contains("state.json` alone, is not an active run", skill, StringComparison.Ordinal);
        Assert.Contains("Equivalent existing Rule => `no-op`", skill, StringComparison.Ordinal);
        Assert.Contains("Never change Next ID", skill, StringComparison.Ordinal);
        Assert.Contains("delete its document and INDEX row, preserve Next ID", skill, StringComparison.Ordinal);
        Assert.Contains("Do not fake it with deterministic filename, keyword, embedding, or similarity heuristics", skill, StringComparison.Ordinal);
        Assert.Contains("This skill changes durable knowledge, not implementation", skill, StringComparison.Ordinal);
    }

    [Fact]
    public void EngineeringAllocator_IsStableAndLegacyCompatible()
    {
        var reference = Canonical("methodology", "engineering-guardrails.md");
        var lint = Canonical("skills", "idd-intent-lint.md");

        Assert.Contains("Next ID = max(current ENG IDs) + 1", reference, StringComparison.Ordinal);
        Assert.Contains("deleted IDs are not reused", reference, StringComparison.Ordinal);
        Assert.Contains("valid legacy Engineering layer with no allocator line remains structurally readable", lint, StringComparison.Ordinal);
        Assert.Contains("allocator ID is less than or equal to any current", lint, StringComparison.Ordinal);
    }

    [Fact]
    public void Routing_RecognizesEngineeringChangeOperations()
    {
        var route = Canonical("skills", "idd-route.md");
        var workflows = Canonical("methodology", "common-workflows.md");

        Assert.Contains("- engineering-change", route, StringComparison.Ordinal);
        Assert.Contains("| `engineering-change` | `idd-engineering-change` |", route, StringComparison.Ordinal);
        Assert.Contains("For `product-change` and `engineering-change`, set `Operation`", route, StringComparison.Ordinal);
        Assert.Contains("## Workflow Family: Engineering Management", workflows, StringComparison.Ordinal);
        Assert.Contains("idd-engineering-change(operation: add | modify | remove)", workflows, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingIntentWorkflows_HandOffEngineeringCandidatesWithoutMutating()
    {
        var bootstrap = Canonical("skills", "idd-intent-bootstrap.md");
        var import = Canonical("skills", "idd-intent-import.md");
        var update = Canonical("skills", "idd-code-update-intent.md");
        var audit = Canonical("skills", "idd-intent-audit.md");

        Assert.Contains("offer `idd-engineering-change`", bootstrap, StringComparison.Ordinal);
        Assert.Contains("Bootstrap itself never writes `.idd/engineering/`", bootstrap, StringComparison.Ordinal);
        Assert.Contains("offer `idd-engineering-change`", import, StringComparison.Ordinal);
        Assert.Contains("import skill itself never mutates Engineering", import, StringComparison.Ordinal);
        Assert.Contains("this skill never creates, modifies, removes, or migrates Engineering Rules", update, StringComparison.Ordinal);
        Assert.Contains("This audit remains", audit, StringComparison.Ordinal);
        Assert.Contains("read-only", audit, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicSkillName_AllowsOnlyExplicitEngineeringChangeSpecialCase()
    {
        var validator = fixture.ReadText(Path.Combine(
            fixture.RepoRoot, "tools", "generate", "Validation", "SkillDescriptionValidator.cs"));

        Assert.Contains("idd-engineering-change|idd-(intent|code|factory)", validator, StringComparison.Ordinal);
        Assert.DoesNotContain("idd-engineering-[a-z0-9]", validator, StringComparison.Ordinal);
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
