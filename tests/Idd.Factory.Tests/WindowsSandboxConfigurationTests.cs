using Idd.Factory.Agents;

namespace Idd.Factory.Tests;

public sealed class WindowsSandboxConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingWindowsSandboxDefaultsToUnelevated(string? configured)
    {
        Assert.Equal("unelevated", FactoryCli.ResolveWindowsSandbox(configured, isWindows: true));
    }

    [Theory]
    [InlineData("unelevated", "unelevated")]
    [InlineData("UNELEVATED", "unelevated")]
    [InlineData(" elevated ", "elevated")]
    public void ExplicitWindowsSandboxIsNormalized(string configured, string expected)
    {
        Assert.Equal(expected, FactoryCli.ResolveWindowsSandbox(configured, isWindows: true));
    }

    [Fact]
    public void InvalidWindowsSandboxFailsBeforeCodexLaunch()
    {
        var exception = Assert.Throws<AgentProtocolException>(() =>
            FactoryCli.ResolveWindowsSandbox("disabled", isWindows: true));

        Assert.Equal("INVALID_WINDOWS_SANDBOX", exception.Code);
        Assert.Contains("unelevated", exception.Message, StringComparison.Ordinal);
        Assert.Contains("elevated", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonWindowsIgnoresWindowsSandboxSetting()
    {
        Assert.Null(FactoryCli.ResolveWindowsSandbox("elevated", isWindows: false));
    }
}
