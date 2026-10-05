using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class LiveEvalSettingsTests
{
    [Fact]
    public void Resolve_UsesRepositoryConfig_WhenEnvironmentDoesNotOverrideIt()
    {
        var root = CreateRepositoryConfig("gpt-6-luna", "low");
        try
        {
            var settings = LiveEvalSettings.Resolve(root, null, null);

            Assert.Equal("gpt-6-luna", settings.Model);
            Assert.Equal("low", settings.ReasoningEffort);
            Assert.Equal("repository config", settings.ModelSource);
            Assert.Equal("repository config", settings.ReasoningEffortSource);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_UsesEnvironmentOverrides_AndRetainsTheirSource()
    {
        var root = CreateRepositoryConfig("gpt-6-luna", "low");
        try
        {
            var settings = LiveEvalSettings.Resolve(root, "gpt-6-astra", "high", "argument", "argument");

            Assert.Equal("gpt-6-astra", settings.Model);
            Assert.Equal("high", settings.ReasoningEffort);
            Assert.Equal("argument", settings.ModelSource);
            Assert.Equal("argument", settings.ReasoningEffortSource);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRepositoryConfig(string model, string reasoningEffort)
    {
        var root = Path.Combine(Path.GetTempPath(), "idd-factory-live-settings-", Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "tests", "Idd.Factory.LiveTests");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "live-eval.json"),
            $$"""
            {
              "model": "{{model}}",
              "reasoningEffort": "{{reasoningEffort}}"
            }
            """);
        return root;
    }
}
