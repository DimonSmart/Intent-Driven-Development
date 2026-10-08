using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class GeneratorCliTests(GenerationFixture fixture)
{
    [Fact]
    public void CheckMode_AcceptsCurrentGeneratedOutput()
    {
        var result = fixture.RunGenerator(checkOnly: true);
        Assert.True(result.ExitCode == 0,
            $"Generator --check failed with exit code {result.ExitCode}.{Environment.NewLine}{result.Stderr}");
    }


    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void StaleFactoryWorkerSkill_IsDetectedAndRemovedByOrdinaryGeneration(string platform)
    {
        var stale = Path.Combine(fixture.MarketplaceRoot, "plugins", platform, "idd-factory",
            "skills", "idd-factory-execute-subtask");
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(stale, "SKILL.md"), "# Obsolete public worker");

        try
        {
            var checkStale = fixture.RunGenerator(checkOnly: true);
            Assert.NotEqual(0, checkStale.ExitCode);
            Assert.Contains("Unexpected generated file", checkStale.Stdout + checkStale.Stderr);

            var generated = fixture.RunGenerator();
            Assert.Equal(0, generated.ExitCode);
            fixture.AssertMissing(stale);

            var checkClean = fixture.RunGenerator(checkOnly: true);
            Assert.Equal(0, checkClean.ExitCode);
        }
        finally
        {
            if (Directory.Exists(stale))
                Directory.Delete(stale, recursive: true);
            var restored = fixture.RunGenerator();
            Assert.Equal(0, restored.ExitCode);
        }
    }

    [Fact]
    public void Generation_IsDeterministicAndIdempotent()
    {
        var before = fixture.SnapshotMarketplace();
        var result = fixture.RunGenerator();
        Assert.True(result.ExitCode == 0,
            $"Second generator run failed with exit code {result.ExitCode}.{Environment.NewLine}{result.Stderr}");
        var after = fixture.SnapshotMarketplace();
        Assert.Equal(before, after);
    }
}
