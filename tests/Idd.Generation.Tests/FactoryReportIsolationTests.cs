using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class FactoryReportIsolationTests(GenerationFixture fixture)
{
    [Fact]
    public void GeneratedMarketplace_DoesNotContainFactoryReportUtility()
    {
        var entries = Directory.EnumerateFileSystemEntries(
                fixture.MarketplaceRoot,
                "*",
                SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(fixture.MarketplaceRoot, path))
            .ToArray();

        Assert.DoesNotContain(entries,
            path => path.Contains("idd-factory-report", StringComparison.OrdinalIgnoreCase));
    }
}
