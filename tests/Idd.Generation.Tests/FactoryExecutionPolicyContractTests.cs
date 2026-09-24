using System.Text.RegularExpressions;
using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class FactoryExecutionPolicyContractTests(GenerationFixture fixture)
{
    private string Canonical(params string[] parts) =>
        fixture.ReadText(Path.Combine([fixture.RepoRoot, "src", "canonical", .. parts]));

    [Fact]
    public void Planner_UsesOnlyCanonicalExecutionProfilesAndDefaultsMissingToStandard()
    {
        var planner = Canonical("skills", "idd-factory-decompose-task.md");

        Assert.Contains("economy", planner);
        Assert.Contains("standard", planner);
        Assert.Contains("strong", planner);
        Assert.Contains("If it is absent, the task means `standard`", planner);
        Assert.Contains("Any other value is", planner);
        Assert.Contains("malformed planner output", planner);
        Assert.Contains("belongs to the immediately preceding", planner);
        Assert.Contains("Do not read `.idd/execution.yaml`", planner);
        Assert.Contains("Do not emit concrete model IDs", planner);
        Assert.Contains("Do not classify based on model price", planner);
    }

    [Fact]
    public void ExecutionPolicy_DefinesInheritPartialMappingsAndBlockingValidation()
    {
        var policy = Canonical("methodology", "factory-execution-policy.md");

        Assert.Contains(".idd/execution.yaml", policy);
        Assert.Contains("modelStrategy: inherit", policy);
        Assert.Contains("Mappings may be partial", policy);
        Assert.Contains("missing profile mapping", policy);
        Assert.Contains("version: 1", policy);
        Assert.Contains("Do not silently convert malformed explicit policy", policy);
        Assert.True(policy.Contains("do not substitute another model", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("same concrete model may be", policy);
    }

    [Fact]
    public void ConfigurationWorkflow_RequiresConfirmationBeforeAutomaticProposalIsSaved()
    {
        var configure = Canonical("skills", "idd-factory-configure.md");

        Assert.Contains("Use the current model for all tasks", configure);
        Assert.Contains("Configure different models by task complexity", configure);
        Assert.Contains("Use proposed mapping", configure);
        Assert.Contains("Edit mapping", configure);
        Assert.Contains("Never save an automatically proposed mapping before explicit confirmation", configure);
        Assert.Contains("request_user_input", configure);
        Assert.Contains("AskUserQuestion", configure);
        Assert.Contains("Partial override is valid", configure);
        Assert.Contains("modelStrategy: inherit", configure);
    }

    [Fact]
    public void RootAgent_PerformsMechanicalProfileLookupBeforeNativeSpawn()
    {
        var run = Canonical("skills", "idd-factory-run.md");

        Assert.Contains("planner ExecutionProfile", run);
        Assert.Contains("project configuration lookup", run);
        Assert.Contains("native child-agent spawn", run);
        Assert.Contains("Do not reconsider task complexity", run);
        Assert.Contains("never silently fall back to `inherit`", run);
        Assert.Contains("idd-factory-configure", run);
        Assert.Contains("worker skill is always", run);
        Assert.Contains("idd-factory-execute-subtask", run);
    }

    [Fact]
    public void Worker_DoesNotSelectOrReconfigureItsModel()
    {
        var worker = Canonical("skills", "idd-factory-execute-subtask.md");

        Assert.Contains("Do not read `.idd/execution.yaml`", worker);
        Assert.Contains("choose a model", worker);
        Assert.Contains("reinterpret an", worker);
        Assert.Contains("ExecutionProfile", worker);
        Assert.Contains(".idd/execution.yaml", worker);
    }

    [Fact]
    public void GeneratedPlatformGuidance_AppliesOnlyAlreadyConfiguredMappings()
    {
        foreach (var platform in new[] { "codex", "claude" })
        {
            var root = Path.Combine(fixture.MarketplaceRoot, "plugins", platform, "idd-factory", "skills");
            var run = fixture.ReadText(Path.Combine(root, "idd-factory-run", "SKILL.md"));
            var configure = fixture.ReadText(Path.Combine(root, "idd-factory-configure", "SKILL.md"));

            Assert.Contains("ExecutionProfile", run);
            Assert.Contains(".idd/execution.yaml", run);
            Assert.Contains("inherit", run);
            Assert.True(run.Contains("never pick a fallback", StringComparison.OrdinalIgnoreCase));
            Assert.True(configure.Contains("user confirmation", StringComparison.OrdinalIgnoreCase));
        }

        var codexRun = fixture.ReadText(Path.Combine(
            fixture.MarketplaceRoot, "plugins", "codex", "idd-factory", "skills", "idd-factory-run", "SKILL.md"));
        Assert.Contains("codex.model", codexRun);
        Assert.Contains("codex.reasoningEffort", codexRun);

        var claudeRun = fixture.ReadText(Path.Combine(
            fixture.MarketplaceRoot, "plugins", "claude", "idd-factory", "skills", "idd-factory-run", "SKILL.md"));
        Assert.Contains("claude.model", claudeRun);
        Assert.Contains("claude.effort", claudeRun);
    }

    [Fact]
    public void CanonicalFactorySources_DoNotHardcodeConcreteRecommendedModelFamilies()
    {
        var sources = Directory.GetFiles(
                Path.Combine(fixture.RepoRoot, "src", "canonical", "skills"),
                "idd-factory-*.md")
            .Append(Path.Combine(fixture.RepoRoot, "src", "canonical", "methodology", "factory-execution-policy.md"))
            .Append(Path.Combine(fixture.RepoRoot, "tools", "generate", "Generation", "CodexPlatformAdapter.cs"))
            .Append(Path.Combine(fixture.RepoRoot, "tools", "generate", "Generation", "ClaudePlatformAdapter.cs"));

        var concreteModelFamily = new Regex(
            @"(?i)\b(?:gpt-\d|claude-\d|sonnet\b|opus\b|haiku\b)",
            RegexOptions.CultureInvariant);

        foreach (var path in sources)
            Assert.False(concreteModelFamily.IsMatch(File.ReadAllText(path)), $"Concrete model recommendation found in {path}.");
    }

    [Fact]
    public void ProjectInit_OffersModelConfigurationOnlyForFactoryScope()
    {
        var init = Canonical("skills", "idd-project-init.md");

        Assert.Contains("Do not ask about Factory models for an `idd-intent`-only project", init);
        Assert.Contains("When Factory is enabled for this initialization", init);
        Assert.Contains("Use the current model for all tasks", init);
        Assert.Contains("Configure different models by task complexity", init);
        Assert.Contains("idd-factory-configure", init);
    }
}
