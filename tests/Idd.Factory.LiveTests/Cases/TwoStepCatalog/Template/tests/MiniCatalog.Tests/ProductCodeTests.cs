using System.Reflection;
using Xunit;

namespace MiniCatalog.Tests;

public sealed class ProductCodeTests
{
    [Theory]
    [InlineData(" ab-12 ", "AB-12")]
    [InlineData("AbC", "ABC")]
    public void Canonicalizes_codes(string input, string expected) => Assert.Equal(expected, Create(input).ToString());

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_empty_canonical_code(string input) => Assert.ThrowsAny<Exception>(() => Create(input));

    [Fact]
    public void Equality_uses_canonical_value() => Assert.Equal(Create("ab"), Create(" AB "));

    private static object Create(string value)
    {
        var type = typeof(Catalog).Assembly.GetType("MiniCatalog.ProductCode") ?? throw new Xunit.Sdk.XunitException("ProductCode production type is missing.");
        return Activator.CreateInstance(type, value) ?? throw new Xunit.Sdk.XunitException("ProductCode could not be constructed.");
    }
}
