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
    public void GeneratedClaudeAndCodexArtifacts_PreserveCanonicalEngineeringReference()
    {
        var source = GenerationFixture.NormalizeText(Canonical("methodology", "engineering-guardrails.md"));
        var skills = new[]
        {
            ("idd-intent", "idd-code-implement"),
            ("idd-intent", "idd-code-check-implementation"),
            ("idd-intent", "idd-intent-lint"),
            ("idd-intent", "idd-engineering-change"),
            ("idd-intent", "idd-intent-import"),
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
    public void PublicSkillName_AllowsOnlyExplicitEngineeringChangeSpecialCase()
    {
        SkillDescriptionValidator.GuardPublicSkillName("test", "idd-engineering-change");

        Assert.Throws<InvalidOperationException>(() =>
            SkillDescriptionValidator.GuardPublicSkillName("test", "idd-engineering-add"));
        Assert.Throws<InvalidOperationException>(() =>
            SkillDescriptionValidator.GuardPublicSkillName("test", "idd-engineering-remove"));
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
    }

    [Fact]
    public void MechanicalEngineeringValidation_IsSharedAndAllocatorRegexIsCanonical()
    {
        var guardrails = Canonical("methodology", "engineering-guardrails.md");
        var engineeringChange = Canonical("skills", "idd-engineering-change.md");
        var lint = Canonical("skills", "idd-intent-lint.md");

        Assert.Contains("## Mechanical Engineering Validation", guardrails);
        Assert.Contains("^Next ID: ENG-\\d{4}$", guardrails);
        Assert.DoesNotContain("^Next ID: ENG-\\\\d{4}$", guardrails);
        Assert.Contains("Mechanical Engineering Validation", engineeringChange);
        Assert.Contains("Mechanical Engineering Validation", lint);
        Assert.DoesNotContain("## Optional Engineering checks", lint);
    }

    [Fact]
    public void EngineeringManagement_DoesNotRouteValidationThroughIntentLint()
    {
        var guardrails = Canonical("methodology", "engineering-guardrails.md");
        var workflows = Canonical("methodology", "common-workflows.md");
        var engineeringChange = Canonical("skills", "idd-engineering-change.md");

        Assert.DoesNotContain("idd-intent-lint structural validation", guardrails);
        Assert.DoesNotContain("structural validation through idd-intent-lint", workflows);
        Assert.False(engineeringChange.Contains("idd-intent-lint", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("-> Mechanical Engineering Validation", guardrails);
        Assert.Contains("-> direct Mechanical Engineering Validation", workflows);
    }

    [Fact]
    public void ProjectLint_IsProjectOwnedAndForbidsAdHocValidatorPrograms()
    {
        var lint = Canonical("skills", "idd-intent-lint.md");
        var engineeringChange = Canonical("skills", "idd-engineering-change.md");

        Assert.Contains("Do not generate a general-purpose validator program solely to perform IDD lint.", lint);
        Assert.Contains("Do not generate a general-purpose validator program", engineeringChange);
        Assert.Contains("installed plugin cache", lint);
        Assert.Contains("canonical IDD repository", lint);
        Assert.DoesNotContain("skills do not contain an archive-enabling flag", lint);
        Assert.DoesNotContain("skills do not contain an archive import action", lint);
        Assert.DoesNotContain("docs describe archive as a normal lifecycle", lint);
    }

    [Fact]
    public void EngineeringManagement_SupportsBatchPlanningAndSequentialAllocation()
    {
        var guardrails = Canonical("methodology", "engineering-guardrails.md");
        var engineeringChange = Canonical("skills", "idd-engineering-change.md");

        Assert.Contains("one or more durable Engineering decisions", guardrails);
        Assert.Contains("One invocation may process multiple durable Engineering decisions.", engineeringChange);
        Assert.Contains("allocate sequentially", guardrails);
        Assert.Contains("allocate consecutive IDs only to `new` candidates", engineeringChange);
        Assert.DoesNotContain("creates one Rule", guardrails);
    }

    [Fact]
    public void EngineeringBootstrapAssets_RemainPackaged()
    {
        foreach (var platform in new[] { "claude", "codex" })
        {
            foreach (var skill in new[] { "idd-engineering-change", "idd-intent-import" })
            {
                var root = Path.Combine(
                    fixture.MarketplaceRoot, "plugins", platform, "idd-intent", "skills",
                    skill, "assets", "bootstrap", ".idd", "engineering");

                fixture.AssertFile(Path.Combine(root, "README.md"));
                fixture.AssertFile(Path.Combine(root, "INDEX.md"));
            }
        }
    }

    [Fact]
    public void IntentImport_HasBoundedEngineeringMigrationAuthority()
    {
        var import = Canonical("skills", "idd-intent-import.md");
        var guardrails = Canonical("methodology", "engineering-guardrails.md");
        var engineeringChange = Canonical("skills", "idd-engineering-change.md");

        Assert.Contains("explicit durable Engineering decision", import);
        Assert.Contains("Repository code, package references, tests, runtime wiring", import);
        Assert.Contains("mutate no Engineering Rules in this invocation", import);
        Assert.Matches(@"never changes\s+`\.idd/verification\.yaml`", import);
        Assert.Contains("Do not invoke `idd-engineering-change` as an executable subroutine.", import);
        Assert.Contains("ENG-9999", import);
        Assert.Contains(".idd/factory/current/request.md", import);
        Assert.Contains("valid legacy layers gain an allocator", import);
        Assert.Contains("idd-intent-import", guardrails);
        Assert.Contains("narrow migration exception", guardrails);
        Assert.Contains("sole bounded exception", engineeringChange);
    }

    [Fact]
    public void IntentImport_PackagesSharedEngineeringReferenceAndBootstrap()
    {
        var source = GenerationFixture.NormalizeText(Canonical("methodology", "engineering-guardrails.md"));

        foreach (var platform in new[] { "claude", "codex" })
        {
            var root = Path.Combine(
                fixture.MarketplaceRoot, "plugins", platform, "idd-intent", "skills",
                "idd-intent-import");

            var reference = fixture.ReadText(Path.Combine(root, "references", "engineering-guardrails.md"));
            Assert.Equal(source, GenerationFixture.NormalizeText(reference));

            fixture.AssertFile(Path.Combine(root, "assets", "bootstrap", ".idd", "engineering", "README.md"));
            fixture.AssertFile(Path.Combine(root, "assets", "bootstrap", ".idd", "engineering", "INDEX.md"));
        }
    }

}
