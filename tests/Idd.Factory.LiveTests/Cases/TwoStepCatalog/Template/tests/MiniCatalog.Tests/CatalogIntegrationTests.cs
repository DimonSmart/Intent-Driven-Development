using System.Reflection;
using Xunit;

namespace MiniCatalog.Tests;

public sealed class CatalogIntegrationTests
{
    [Fact]
    public void Stores_canonical_values()
    {
        var catalog = new Catalog(); catalog.Add(" ab ");
        Assert.Equal("AB", Assert.Single(catalog.Codes));
    }

    [Fact]
    public void Rejects_duplicates_after_normalization()
    {
        var catalog = new Catalog(); catalog.Add("ab");
        Assert.ThrowsAny<Exception>(() => catalog.Add(" AB "));
    }

    [Fact]
    public void Summary_uses_canonical_values_in_ordinal_order()
    {
        var catalog = new Catalog(); catalog.Add("z"); catalog.Add(" a ");
        var summary = typeof(Catalog).GetMethod("Summary", BindingFlags.Instance | BindingFlags.Public)?.Invoke(catalog, null) as string;
        Assert.Equal("A\nZ", summary);
    }
}
