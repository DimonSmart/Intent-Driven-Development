using System.Text.Json;
using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class IntentLintAutofixContractTests(GenerationFixture fixture)
{
    private string Canonical(string file) => fixture.ReadText(Path.Combine(
        fixture.RepoRoot, "src", "canonical", "skills", file));

    [Fact]
    public void Lint_UsesOneCanonicalInstructionSetAndRetainsExistingMechanicalChecks()
    {
        var lint = Canonical("idd-intent-lint.md");

        foreach (var requiredCheck in new[]
        {
            "INDEX.md", "Related", "Replaces", "Supersedes", "Depends on",
            "import-report.md", "Archived", ".worklog", "task/progress",
            "fenced build/test shell commands", "GLOSSARY.md",
            "Mechanical Engineering Validation", "generated chat transcripts",
            "case-insensitively", "spike"
        })
            Assert.Contains(requiredCheck, lint, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("mode: check", lint);
        Assert.Contains("mode: fix", lint);
        Assert.Contains("one mandatory re-check", lint);
        Assert.Contains("read-only", lint);
        Assert.Contains("dependency", lint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("second fix", lint, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("## Optional Engineering checks", lint);
    }

    [Fact]
    public void Fix_RequiresExplicitPermissionAndDoesNotWriteGlossaryOrEngineering()
    {
        var lint = Canonical("idd-intent-lint.md");
        var glossary = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "src", "canonical", "methodology", "glossary-integration.md"));

        Assert.Contains("check and explain how to fix", lint);
        Assert.Contains("An explicit", lint);
        Assert.Contains("No `dry-run`", lint);
        Assert.Contains("never", lint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Existing Glossary and Engineering are never", lint);
        Assert.Contains("copy **only missing** files", lint);
        Assert.Contains("conflicting", lint);
        Assert.Contains("IDD-NNNN", lint);
        Assert.Contains("only `idd-glossary-build`", glossary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Routing_PreservesCheckFixAndSemanticWorkflowOwnership()
    {
        var router = Canonical("idd-route.md");
        var normalize = Canonical("idd-intent-normalize-current.md");
        var workflows = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "src", "canonical", "methodology", "common-workflows.md"));

        Assert.Contains("mode: check", router);
        Assert.Contains("mode: fix", router);
        Assert.Contains("Fix the lint", router);
        Assert.Contains("Only an unambiguous user request", router);
        Assert.Contains("Purely mechanical", normalize);
        Assert.Contains("standalone filename, heading, INDEX cell", normalize);
        Assert.Contains("idd-intent-lint mode: check", workflows);
        Assert.Contains("idd-intent-lint mode: fix", workflows);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void Lint_PackagesItsOwnImmutableCanonicalResources(string platform)
    {
        var root = Path.Combine(fixture.MarketplaceRoot, "plugins", platform,
            "idd-intent", "skills", "idd-intent-lint");
        var skill = fixture.ReadText(Path.Combine(root, "SKILL.md"));
        Assert.Contains("mode: check", skill);
        Assert.Contains("mode: fix", skill);

        foreach (var name in new[] { "README.md", "INDEX.md",
                                     "_templates/spec.md", "_templates/adr.md",
                                     "_templates/spike.md" })
        {
            var source = fixture.ReadText(Path.Combine(fixture.RepoRoot,
                "src", "canonical", "project-files", "intent", name));
            var target = fixture.ReadText(Path.Combine(root, "assets",
                "bootstrap", ".idd", "intent", name));
            Assert.Equal(GenerationFixture.NormalizeText(source),
                         GenerationFixture.NormalizeText(target));
        }

        foreach (var name in new[] { "numbering.md", "document-types.md",
                                     "glossary-integration.md", "engineering-guardrails.md" })
        {
            var source = fixture.ReadText(Path.Combine(fixture.RepoRoot,
                "src", "canonical", "methodology", name));
            var target = fixture.ReadText(Path.Combine(root, "references", name));
            Assert.Equal(GenerationFixture.NormalizeText(source),
                         GenerationFixture.NormalizeText(target));
        }

        Assert.False(Directory.Exists(Path.Combine(root, "assets", "bootstrap",
            ".idd", "engineering")));
    }

    [Fact]
    public void LintRemainsAutomaticWorkflow()
    {
        using var desc = JsonDocument.Parse(fixture.ReadText(Path.Combine(
            fixture.RepoRoot, "src", "canonical", "skills", "skill-descriptions.json")));
        Assert.Equal(20, desc.RootElement.EnumerateObject().Count());
        var lint = desc.RootElement.GetProperty("idd-intent-lint");
        Assert.Equal("workflow", lint.GetProperty("exposure").GetString());
        Assert.False(lint.TryGetProperty("invocation", out _));
        var claude = lint.GetProperty("adapters").GetProperty("claude").GetProperty("frontmatter");
        Assert.Equal("fork", claude.GetProperty("context").GetString());
        Assert.Equal("general-purpose", claude.GetProperty("agent").GetString());
        Assert.False(claude.GetProperty("background").GetBoolean());
        Assert.Equal("Read Glob Grep Edit Write Bash", claude.GetProperty("allowed-tools").GetString());
        Assert.Equal("[scope] [mode: check|fix]", claude.GetProperty("argument-hint").GetString());
    }
    [Fact]
    public void LivingAdr_LintIsMechanicalAndNeverMigratesArchitecturalHistory()
    {
        var lint = Canonical("idd-intent-lint.md");
        var types = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "src", "canonical", "methodology", "document-types.md"));
        var index = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "src", "canonical", "project-files", "intent", "INDEX.md"));

        Assert.Contains("Do not reorganize specs or ADRs", lint);
        Assert.Contains("## Status", lint);
        Assert.Contains("diagnostic", lint, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Validate a single recognized allowed ADR status", lint);
        Assert.Contains("No universal H2", types);
        Assert.Contains("| Document | Role | Area | Notes |", index);
        Assert.DoesNotContain("| Replaces |", index);
    }


    [Fact]
    public void FilenameRegex_IsIntactAndDuplicateSkillPreambleIsGone()
    {
        var lint = Canonical("idd-intent-lint.md");
        Assert.Equal(1, lint.Split("# idd-intent-lint").Length - 1);
        Assert.Contains(@"^IDD-\d{4}\.(spec|adr|spike)-[a-z0-9][a-z0-9-]*\.md$", lint);
        Assert.DoesNotContain(".md# idd-intent-lint", lint);
    }

    [Fact]
    public void OptionalTemplateHeadings_DoNotTriggerErrorsOrAutofix()
    {
        var lint = Canonical("idd-intent-lint.md");
        var types = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "src", "canonical", "methodology", "document-types.md"));

        Assert.Contains("absence of optional template headings is neither error nor", lint);
        Assert.Contains("insert `None`, `TBD`", lint);
        Assert.Contains("No ADR lifecycle status", lint);
        Assert.Contains("semantic migration", lint);
        Assert.Contains("merge historical decisions", lint);
        Assert.Contains("Mechanical lint cannot establish semantic completeness", types);
        Assert.Contains("existing safe heuristics", lint);
        Assert.Contains("do not require canonical optional headings", lint);
    }


}
