using Idd.Factory.Runtime;

namespace Idd.Factory.Tests;

public sealed class WorkspaceChangeCalculatorTests
{
    [Fact]
    public void CalculateReturnsAddedModifiedAndDeletedPathsInStableOrder()
    {
        var before = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["unchanged.txt"] = "same",
            ["modified.txt"] = "before",
            ["deleted.txt"] = "deleted"
        };
        var after = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["unchanged.txt"] = "same",
            ["modified.txt"] = "after",
            ["added.txt"] = "added"
        };

        var changed = new WorkspaceChangeCalculator().Calculate(before, after);

        Assert.Equal(["added.txt", "deleted.txt", "modified.txt"], changed);
    }

    [Fact]
    public void CalculateReturnsEmptyWhenWorkspaceIsUnchanged()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a.txt"] = "A",
            ["b.txt"] = "B"
        };

        Assert.Empty(new WorkspaceChangeCalculator().Calculate(files, files));
    }
}
