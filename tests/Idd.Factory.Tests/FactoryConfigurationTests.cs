using Idd.Factory.Configuration;

namespace Idd.Factory.Tests;

public sealed class FactoryConfigurationTests
{
    [Fact]
    public void SemanticCommandTimeoutIsExplicitAndBounded()
    {
        using var temp = new TestWorkspace();
        var configurationPath = Path.Combine(temp.Path, "factory.yaml");
        File.WriteAllText(configurationPath, """
            schemaVersion: 3
            limits:
              maxAttemptsPerTask: 4
              maxPlanningCycles: 12
              maxWorkItems: 64
              semanticCommandTimeout: 2m
            """);

        var configuration = new FactoryConfigurationLoader().Load(temp.Path, configurationPath);

        Assert.Equal(TimeSpan.FromMinutes(2), configuration.Limits.SemanticCommandTimeout);
    }

    [Theory]
    [InlineData("0s")]
    [InlineData("61m")]
    public void SemanticCommandTimeoutOutsideSafetyCeilingIsRejected(string timeout)
    {
        using var temp = new TestWorkspace();
        var configurationPath = Path.Combine(temp.Path, "factory.yaml");
        File.WriteAllText(configurationPath, $$"""
            schemaVersion: 3
            limits:
              maxAttemptsPerTask: 4
              maxPlanningCycles: 12
              maxWorkItems: 64
              semanticCommandTimeout: {{timeout}}
            """);

        var exception = Assert.Throws<FactoryConfigurationException>(() =>
            new FactoryConfigurationLoader().Load(temp.Path, configurationPath));

        Assert.Equal("INVALID_FACTORY_LIMITS", exception.Code);
    }
}
