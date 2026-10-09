using System.Text.Json.Nodes;
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
            Assert.Equal(new WorkerExecutionSettings("model-a", "medium"), settings.ExecutionProfiles["economy"]);
            Assert.Equal(new WorkerExecutionSettings("model-b", "medium"), settings.ExecutionProfiles["standard"]);
            Assert.Equal(new WorkerExecutionSettings("model-c", "medium"), settings.ExecutionProfiles["strong"]);
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
            Assert.Equal("model-a", settings.ExecutionProfiles["economy"].Model);
            Assert.Equal("model-b", settings.ExecutionProfiles["standard"].Model);
            Assert.Equal("model-c", settings.ExecutionProfiles["strong"].Model);
            Assert.Equal("medium", settings.ExecutionProfiles["standard"].ReasoningEffort);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_AppliesIndependentProfileOverrides()
    {
        var root = CreateRepositoryConfig("model-a", "low");
        try
        {
            var overrides = new Dictionary<string, string>
            {
                ["IDD_FACTORY_EVAL_ECONOMY_MODEL"] = "model-d",
                ["IDD_FACTORY_EVAL_STRONG_REASONING_EFFORT"] = "xhigh"
            };
            var settings = LiveEvalSettings.Resolve(root, null, null,
                profileEnvironment: name => overrides.GetValueOrDefault(name));
            Assert.Equal(new WorkerExecutionSettings("model-d", "medium"), settings.ExecutionProfiles["economy"]);
            Assert.Equal(new WorkerExecutionSettings("model-b", "medium"), settings.ExecutionProfiles["standard"]);
            Assert.Equal(new WorkerExecutionSettings("model-c", "xhigh"), settings.ExecutionProfiles["strong"]);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Resolve_RejectsMissingWorkerModelInsteadOfInheritingRootModel()
    {
        var root = CreateRepositoryConfig("root-model", "medium");
        try
        {
            var path = Path.Combine(root, "tests", "Idd.Factory.LiveTests", "live-eval.json");
            var config = JsonNode.Parse(File.ReadAllText(path))!;
            config["executionProfiles"]!["economy"]!.AsObject().Remove("model");
            File.WriteAllText(path, config.ToJsonString());
            var exception = Assert.Throws<InvalidOperationException>(() => LiveEvalSettings.Resolve(root, null, null));
            Assert.Contains("non-empty 'model'", exception.Message);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Resolve_RejectsIndistinguishableMappingsBeforeLiveExecution()
    {
        var root = CreateRepositoryConfig("model-a", "low");
        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() => LiveEvalSettings.Resolve(root, null, null,
                profileEnvironment: name => name.EndsWith("_MODEL", StringComparison.Ordinal) ? "model-a" :
                    name.Contains("ECONOMY", StringComparison.Ordinal) ? "low" :
                    name.Contains("STRONG", StringComparison.Ordinal) ? "high" : null));
            Assert.Contains("three distinct models", exception.Message);
        }
        finally { Directory.Delete(root, recursive: true); }
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
              "reasoningEffort": "{{reasoningEffort}}",
              "executionProfiles": {
                "economy": { "model": "model-a", "reasoningEffort": "medium" },
                "standard": { "model": "model-b", "reasoningEffort": "medium" },
                "strong": { "model": "model-c", "reasoningEffort": "medium" }
              }
            }
            """);
        return root;
    }
}
