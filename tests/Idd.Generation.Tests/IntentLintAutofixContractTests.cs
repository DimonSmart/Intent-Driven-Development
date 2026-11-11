using System.Text.Json;
using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class IntentLintAutofixContractTests(GenerationFixture fixture)
{
    private string Canonical(string file) => fixture.ReadText(Path.Combine(
        fixture.RepoRoot, "src", "canonical", "skills", file));

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void Lint_PackagesItsOwnImmutableCanonicalResources(string platform)
    {
        var root = Path.Combine(fixture.MarketplaceRoot, "plugins", platform,
            "idd-intent", "skills", "idd-intent-lint");

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
        Assert.Equal(22, desc.RootElement.EnumerateObject().Count());
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

}
