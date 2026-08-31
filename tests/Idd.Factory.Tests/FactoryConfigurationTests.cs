using Idd.Factory.Configuration;

namespace Idd.Factory.Tests;

public sealed class FactoryConfigurationTests
{
    private const string Valid = """
        schemaVersion: 1
        limits:
          maxAgentAttempts: 4
          maxReplans: 3
          maxCorrectiveCycles: 5
          maxWorkItems: 64
        finalReview:
          required: true
        capabilities:
          allow:
            - implementation
            - research
            - semantic-review
            - documentation
        """;

    [Fact]
    public void PolicyConfigurationLoadsAndHashIsStable()
    {
        using var temp = new TestWorkspace();
        var path = temp.Write("factory.yaml", Valid);
        var loader = new FactoryConfigurationLoader();

        var first = loader.Load(temp.Path, path);
        var second = loader.Load(temp.Path, path);

        Assert.Equal(first.Hash, second.Hash);
        Assert.Equal(64, first.Limits.MaxWorkItems);
        Assert.Contains("research", first.AllowedCapabilities);
        Assert.True(first.FinalReview.Required);
    }

    [Theory]
    [InlineData("steps:\n  - id: execute", "INVALID_FACTORY_CONFIGURATION_YAML")]
    [InlineData("workflow: execute", "INVALID_FACTORY_CONFIGURATION_YAML")]
    [InlineData("transitions: {}", "INVALID_FACTORY_CONFIGURATION_YAML")]
    public void GlobalOrchestrationDslIsRejected(string extra, string expectedCode)
    {
        using var temp = new TestWorkspace();
        var path = temp.Write("factory.yaml", Valid + "\n" + extra + "\n");

        var exception = Assert.Throws<FactoryConfigurationException>(() => new FactoryConfigurationLoader().Load(temp.Path, path));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public void UnknownCapabilityIsRejected()
    {
        using var temp = new TestWorkspace();
        var path = temp.Write("factory.yaml", Valid.Replace("    - documentation", "    - documentation\n    - mystery"));

        var exception = Assert.Throws<FactoryConfigurationException>(() => new FactoryConfigurationLoader().Load(temp.Path, path));

        Assert.Equal("UNKNOWN_CAPABILITY", exception.Code);
    }

    [Fact]
    public void WorkspaceOverrideIsPinnedByContentHash()
    {
        using var temp = new TestWorkspace();
        var packaged = temp.Write("packaged.yaml", Valid);
        var baseline = new FactoryConfigurationLoader().Load(temp.Path, packaged);
        temp.Write(".idd/factory.yaml", Valid.Replace("maxWorkItems: 64", "maxWorkItems: 63"));

        var overridden = new FactoryConfigurationLoader().Load(temp.Path, packaged);

        Assert.Equal(63, overridden.Limits.MaxWorkItems);
        Assert.NotEqual(baseline.Hash, overridden.Hash);
        Assert.EndsWith(Path.Combine(".idd", "factory.yaml"), overridden.SourcePath);
    }
}
