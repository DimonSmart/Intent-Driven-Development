using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class ExecutorDiscoveryContractTests(GenerationFixture fixture)
{
    private static readonly string[] RequiredMarkers =
    [
        "git ls-files --cached --others --exclude-standard",
        "tracked + untracked non-ignored",
        "scoped pathspec",
        "Get-ChildItem -Recurse",
        "find .",
        "--no-ignore",
        "Ignored/generated paths are not forbidden",
        "directly read a known ignored/generated file"
    ];

    [Fact]
    public void CanonicalExecutorSkill_DefinesGitVisibleScopedDiscoveryContract()
    {
        var executor = fixture.ReadText(Path.Combine(
            fixture.RepoRoot, "src", "canonical", "skills", "idd-factory-execute-subtask.md"));

        AssertDiscoveryContract(executor);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void GeneratedExecutorSkill_PreservesGitVisibleScopedDiscoveryContract(string platform)
    {
        var executor = fixture.ReadText(Path.Combine(
            fixture.MarketplaceRoot,
            "plugins",
            platform,
            "idd-factory",
            "skills",
            "idd-factory-execute-subtask",
            "SKILL.md"));

        AssertDiscoveryContract(executor);
    }

    private static void AssertDiscoveryContract(string executor)
    {
        foreach (var marker in RequiredMarkers)
            Assert.Contains(marker, executor, StringComparison.Ordinal);
    }
}
