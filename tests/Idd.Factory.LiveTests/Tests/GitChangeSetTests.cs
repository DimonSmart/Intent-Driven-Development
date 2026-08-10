using Idd.Factory.LiveTests.Infrastructure;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class GitChangeSetTests
{
    [Fact]
    public void Parse_IncludesTrackedDeletedRenamedAndUntrackedPaths()
    {
        var changeSet = GitChangeSet.Parse(" M src/MiniCatalog/Catalog.cs\0D  src/MiniCatalog/Old.cs\0R  src/MiniCatalog/New.cs\0src/MiniCatalog/Renamed.cs\0?? unexpected.txt\0");

        Assert.Equal([
            "src/MiniCatalog/Catalog.cs",
            "src/MiniCatalog/New.cs",
            "src/MiniCatalog/Old.cs",
            "src/MiniCatalog/Renamed.cs",
            "unexpected.txt"
        ], changeSet.Paths);
    }

    [Fact]
    public void Parse_RejectsMalformedPorcelainEntry() =>
        Assert.Throws<InvalidOperationException>(() => GitChangeSet.Parse("not porcelain\0"));

    [Fact]
    public void Parse_RejectsRenameWithoutSourcePath() =>
        Assert.Throws<InvalidOperationException>(() => GitChangeSet.Parse("R  src/MiniCatalog/New.cs\0"));
}
