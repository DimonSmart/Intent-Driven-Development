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
