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
        "Explicit restart -> resolve the replacement request, run replacement-run intent preflight, then call `factory_restart`",
        "Explicit cancellation with no replacement run wanted -> call `factory_cancel`"
    ];

    private static readonly string[] PreflightMarkers =
    [
        "logical Factory request",
        "new run\n    -> materialized current user request",
        "replacement run\n    -> resolved replacement request",
        "The presence of `.idd/factory/current/state.json` is expected in replacement-run mode",
        "the existing run remains untouched",
        "passed unchanged to `factory_restart`",
        "Do not semantic-merge a persisted request with a partial restart delta"
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
        foreach (var marker in SkillMarkers)
            Assert.Contains(marker, skill, StringComparison.Ordinal);
        foreach (var marker in PreflightMarkers)
            Assert.Contains(marker, preflight, StringComparison.Ordinal);

        Assert.DoesNotContain("atomically owns the recovery operation", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("recoverable through explicit current-runtime cancellation and a new run", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("factory_cancel` followed by `factory_run`", preflight, StringComparison.Ordinal);
    }
}
