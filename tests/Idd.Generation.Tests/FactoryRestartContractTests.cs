using System.Text.RegularExpressions;
using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class FactoryRestartContractTests(GenerationFixture fixture)
{
    private static readonly string[] SkillMarkers =
    [
        "## New-run intent preflight",
        "## Replacement-run intent preflight",
        ".idd/factory/current/request.md",
        "Do not call `factory_cancel` before `factory_restart`",
        "The presence of `.idd/factory/current/state.json` is expected in replacement-run preflight",
        "leave the existing Factory run unchanged",
        "do not semantic-merge it with the persisted request",
        "The current runtime owns the complete restart operation",
        "For an explicit restart of an existing supported run, resolve the replacement request, perform replacement-run preflight, then call `factory_restart`",
        "For explicit cancellation when no replacement run is wanted, call `factory_cancel`"
    ];

    private static readonly string[] PreflightMarkers =
    [
        "logical Factory request",
        "new run -> materialized current user request",
        "replacement run -> resolved replacement request",
        "The presence of `.idd/factory/current/state.json` is expected in replacement-run mode",
        "the existing run remains untouched",
        "passed unchanged to `factory_restart`",
        "Do not semantic-merge a persisted request with a partial restart delta",
        "`Covered`, `ExplicitIntentChange`, `MissingIntentDecision`, or `ImplementationOnly`",
        "`route-only`",
        "## Durable normalization"
    ];

    [Fact]
    public void CanonicalFactoryRestartContract_IsUnambiguous()
    {
        var skill = fixture.ReadText(Path.Combine(
            fixture.RepoRoot, "src", "canonical", "skills", "idd-factory-run.md"));
        var preflight = fixture.ReadText(Path.Combine(
            fixture.RepoRoot, "src", "canonical", "methodology", "intent-preflight.md"));

        AssertRestartContract(skill, preflight);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void GeneratedFactoryRestartContract_MatchesCanonical(string platform)
    {
        var skillRoot = Path.Combine(
            fixture.MarketplaceRoot,
            "plugins",
            platform,
            "idd-factory",
            "skills",
            "idd-factory-run");
        var skill = fixture.ReadText(Path.Combine(skillRoot, "SKILL.md"));
        var preflight = fixture.ReadText(Path.Combine(skillRoot, "references", "intent-preflight.md"));

        AssertRestartContract(skill, preflight);
    }

    private static void AssertRestartContract(string skill, string preflight)
    {
        var normalizedSkill = NormalizeWhitespace(skill);
        var normalizedPreflight = NormalizeWhitespace(preflight);

        foreach (var marker in SkillMarkers)
            Assert.Contains(NormalizeWhitespace(marker), normalizedSkill, StringComparison.Ordinal);
        foreach (var marker in PreflightMarkers)
            Assert.Contains(NormalizeWhitespace(marker), normalizedPreflight, StringComparison.Ordinal);

        Assert.DoesNotContain("atomically owns the recovery operation", normalizedSkill, StringComparison.Ordinal);
        Assert.DoesNotContain("recoverable through explicit current-runtime cancellation and a new run", normalizedSkill, StringComparison.Ordinal);
    }

    private static string NormalizeWhitespace(string value) =>
        Regex.Replace(value, "\\s+", " ").Trim();
}
