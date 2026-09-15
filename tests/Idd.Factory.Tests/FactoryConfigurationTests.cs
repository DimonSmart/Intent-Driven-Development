using Idd.Factory.Configuration;

namespace Idd.Factory.Tests;

public sealed class FactoryConfigurationTests
{
    [Fact]
    public void SemanticCommandTimeoutAndTechnicalRestartBudgetAreExplicitAndBounded()
    {
        using var temp = new TestWorkspace();
        var configurationPath = Path.Combine(temp.Path, "factory.yaml");
        File.WriteAllText(configurationPath, """
            schemaVersion: 4
            limits:
              maxAttemptsPerTask: 4
              maxTechnicalRestartsPerTask: 1
              maxPlanningCycles: 12
              maxWorkItems: 64
              semanticCommandTimeout: 2m
            """);

        var configuration = new FactoryConfigurationLoader().Load(temp.Path, configurationPath);

        Assert.Equal(TimeSpan.FromMinutes(2), configuration.Limits.SemanticCommandTimeout);
        Assert.Equal(1, configuration.Limits.MaxTechnicalRestartsPerTask);
    }

    [Theory]
    [InlineData("0s")]
    [InlineData("61m")]
    public void SemanticCommandTimeoutOutsideSafetyCeilingIsRejected(string timeout)
    {
        using var temp = new TestWorkspace();
        var configurationPath = Path.Combine(temp.Path, "factory.yaml");
        File.WriteAllText(configurationPath, $$"""
            schemaVersion: 4
            limits:
              maxAttemptsPerTask: 4
              maxTechnicalRestartsPerTask: 1
              maxPlanningCycles: 12
              maxWorkItems: 64
              semanticCommandTimeout: {{timeout}}
            """);

        var exception = Assert.Throws<FactoryConfigurationException>(() =>
            new FactoryConfigurationLoader().Load(temp.Path, configurationPath));

        Assert.Equal("INVALID_FACTORY_LIMITS", exception.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void TechnicalRestartBudgetAcceptsSupportedRange(int value)
    {
        using var temp = new TestWorkspace();
        var configurationPath = Path.Combine(temp.Path, "factory.yaml");
        File.WriteAllText(configurationPath, $$"""
            schemaVersion: 4
            limits:
              maxAttemptsPerTask: 4
              maxTechnicalRestartsPerTask: {{value}}
              maxPlanningCycles: 12
              maxWorkItems: 64
              semanticCommandTimeout: 10m
            """);

        var configuration = new FactoryConfigurationLoader().Load(temp.Path, configurationPath);

        Assert.Equal(value, configuration.Limits.MaxTechnicalRestartsPerTask);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void TechnicalRestartBudgetRejectsUnsupportedRange(int value)
    {
        using var temp = new TestWorkspace();
        var configurationPath = Path.Combine(temp.Path, "factory.yaml");
        File.WriteAllText(configurationPath, $$"""
            schemaVersion: 4
            limits:
              maxAttemptsPerTask: 4
              maxTechnicalRestartsPerTask: {{value}}
              maxPlanningCycles: 12
              maxWorkItems: 64
              semanticCommandTimeout: 10m
            """);

        var exception = Assert.Throws<FactoryConfigurationException>(() =>
            new FactoryConfigurationLoader().Load(temp.Path, configurationPath));

        Assert.Equal("INVALID_FACTORY_LIMITS", exception.Code);
    }
}
