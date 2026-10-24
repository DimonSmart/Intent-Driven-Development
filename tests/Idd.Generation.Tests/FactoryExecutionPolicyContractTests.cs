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
    public void Planner_RequiresExactlyOneCanonicalExecutionProfilePerTask()
    {
        var planner = Canonical("factory", "planner.md");

        Assert.Contains("economy", planner);
        Assert.Contains("standard", planner);
        Assert.Contains("strong", planner);
        Assert.Contains("Every `# Task` must contain exactly one `# ExecutionProfile`", planner);
        Assert.Contains("a missing, empty, repeated, or unknown profile", planner);
        Assert.Contains("malformed planner output", planner);
        Assert.Contains("belongs to the immediately preceding", planner);
        Assert.Contains("Do not read `.idd/execution.yaml`", planner);
        Assert.Contains("Do not emit concrete model IDs", planner);
        Assert.Contains("Do not classify based on model price", planner);
        Assert.Contains("# Task", planner);
        Assert.Contains("# Question", planner);
        Assert.Contains("# Done", planner);
        Assert.Contains("TaskRelatedIntent", planner);
        Assert.Contains("TaskRelatedEngineering", planner);
        Assert.Contains("## Incremental planning", planner);
        Assert.Contains("Do not create or modify durable Product Intent or Engineering Rules", planner);
    }

    [Fact]
    public void PlannerTaskExamples_DeclareOneCanonicalProfileForEveryTask()
    {
        var planners = new[] { Canonical("factory", "planner.md") }
            .Concat(new[] { "codex", "claude" }.Select(platform => fixture.ReadText(Path.Combine(
                fixture.MarketplaceRoot, "plugins", platform, "idd-factory", "skills",
                "idd-factory-run", "references", "factory-planner.md"))));
        foreach (var planner in planners)
        {
            var example = Regex.Match(planner, @"(?s)Tasks:\s*```text\s*(?<tasks>.*?)```");
            Assert.True(example.Success);
            var tasks = Regex.Matches(example.Groups["tasks"].Value,
                @"(?ms)^# Task\s*$\s*(?<task>.*?)(?=^# Task\s*$|\z)");
            Assert.NotEmpty(tasks);
            foreach (Match task in tasks)
            {
                Assert.Single(Regex.Matches(task.Groups["task"].Value, @"(?m)^# ExecutionProfile\s*$"));
                Assert.Matches(@"(?m)^# ExecutionProfile\s*\n\s*(economy|standard|strong)\s*$",
                    task.Groups["task"].Value);
            }
        }
    }

    [Fact]
    public void ExecutionPolicy_DefinesPartialMappingsInheritanceAndBlockingValidation()
    {
        var policy = Canonical("methodology", "factory-execution-policy.md");

        Assert.Contains(".idd/execution.yaml", policy);
        Assert.Contains("modelStrategy: inherit", policy);
        Assert.Contains("Partial mappings are valid", policy);
        Assert.Contains("A missing profile or missing active-platform mapping", policy);
        Assert.Contains("Codex-only mapping", policy);
        Assert.Contains("Codex `economy` and `standard`, and every Claude profile, inherit host settings", policy);
        Assert.Contains("validate every explicit mapping", policy);
        Assert.Contains("a platform mapping with a missing/blank `model`", policy);
        Assert.Contains("Absence of this file means", policy);
        Assert.Contains("malformed existing configuration is a", policy);
        Assert.Contains("version: 1", policy);
        Assert.Contains("Do not silently convert malformed explicit policy", policy);
        Assert.True(policy.Contains("do not substitute another model", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("same concrete model may be", policy);
        Assert.Contains("The two strategies do not mix", policy);
        Assert.Contains("Per-profile inheritance is represented by an absent mapping", policy);
    }

    [Fact]
    public void ConfigurationWorkflow_RequiresAuthorizationForSelectionAndPreservesExactEdits()
    {
        var configure = Canonical("skills", "idd-factory-configure.md");

        Assert.Contains("default (inherit)", configure);
        Assert.Contains("configure — explicitly configure models", configure);
        Assert.Contains("Apply", configure);
        Assert.Contains("Edit", configure);
        Assert.Contains("Reject", configure);
        Assert.Contains("confirmation", configure);
        Assert.Contains("request_user_input", configure);
        Assert.Contains("AskUserQuestion", configure);
        Assert.Contains("Missing policy means", configure);
        Assert.Contains("Unselected", configure, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("modelStrategy: inherit", configure);
        Assert.Contains("removes", configure);
        Assert.Contains("All missing mappings inherit host behavior", configure);
        Assert.Contains("a concrete model", configure, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("all-or-nothing", configure);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void PublishedFactorySkills_PreserveExistingPlansAndPartialPolicy(string platform)
    {
        var skills = Path.Combine(fixture.MarketplaceRoot, "plugins", platform, "idd-factory", "skills");
        var run = fixture.ReadText(Path.Combine(skills, "idd-factory-run", "SKILL.md"));
        Assert.Contains("missing ExecutionProfile in an existing plan.md means standard", run);
        Assert.Contains("validate\nit before saving a new batch", run.ReplaceLineEndings("\n"));
        Assert.Contains("Do not require a restart", run);
        Assert.Contains("Partial mappings are valid", run);

        foreach (var name in new[] { "idd-factory-run", "idd-factory-configure" })
        {
            var policy = fixture.ReadText(Path.Combine(skills, name, "references", "factory-execution-policy.md"));
            Assert.Contains("missing profile/platform mapping means inherit", policy);
            Assert.Contains("Codex-only mapping", policy);
            Assert.Contains("a platform mapping with a missing/blank `model`", policy);
            Assert.Contains("malformed and blocks execution", policy);
            Assert.DoesNotContain("all three profile mappings for the active platform", policy);
        }

        var configure = fixture.ReadText(Path.Combine(skills, "idd-factory-configure", "SKILL.md"));
        Assert.Contains("Returning one mapping to inheritance", configure);
        Assert.Contains("Missing policy means `inherit (host)`", configure);
        Assert.Contains("Do not claim to know the actual host model", configure);
        Assert.Contains("All missing mappings inherit host behavior", configure);
    }

    [Theory]
    [InlineData("Review-only use must not create the file")]
    [InlineData("An explicitly selected")]
    [InlineData("create `executionProfiles` only for requested")]
    [InlineData("shared by several profiles")]
    [InlineData("Preserve every other profile")]
    [InlineData("returning one mapping to inheritance")]
    [InlineData("removes")]
    [InlineData("never mix the two strategy shapes")]
    [InlineData("exact requested ID")]
    [InlineData("Automatically chosen or ambiguously interpreted")]
    [InlineData("stop with no writes")]
    [InlineData("block that candidate")]
    [InlineData("Never create a reasoning-only mapping")]
    [InlineData("A malformed existing policy is a blocking diagnostic")]
    [InlineData("semantically/structurally unchanged")]
    [InlineData("rebase only the authorized operations")]
    [InlineData("account availability")]
    [InlineData("Direct invocation")]
    [InlineData("next worker spawn")]
    public void ConfigurationSkill_CoversModelManagementScenariosAsTextContracts(string instruction)
    {
        // This checks written instructions, not execution of LLM decisions.
        Assert.Contains(instruction, Canonical("skills", "idd-factory-configure.md"),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RoutingAndReference_KeepOneLifecycleOwnerAndNoStaleEntryPoint()
    {
        var configure = Canonical("skills", "idd-factory-configure.md");
        var reference = Canonical("methodology", "factory-execution-policy.md");
        var route = Canonical("skills", "idd-route.md");

        Assert.Contains("no separate model-update command", configure);
        Assert.Contains("Stop rather than risk loss of user edits", configure);
        Assert.Contains("No second update skill or persistence handoff exists", reference);
        Assert.Contains("override without a concrete model is invalid", reference);
        Assert.Contains("## Direct Factory Model-Policy Route", route);
        Assert.Contains("Before assigning any `Classification`", route);
        Assert.Contains("`idd-factory-configure` directly as the recommended first skill", route);
        Assert.DoesNotContain("factory-model-update", route);
        Assert.DoesNotContain("idd-factory-update-effort-models", route);
        Assert.DoesNotContain("idd-factory-update-effort-models", reference);
    }


    [Theory]
    [InlineData("Perform initial Factory model setup")]
    [InlineData("Change only an existing mapping's reasoning settings")]
    [InlineData("return one mapping to host inheritance")]
    [InlineData("Review current model assignments")]
    [InlineData("Refresh/upgrade/downgrade models")]
    public void DirectFactoryRoute_DescribesSupportedStandaloneModes(string scenario)
    {
        // These assert written routing contracts, not the model's semantic behavior.
        var route = Canonical("skills", "idd-route.md");
        var workflows = Canonical("methodology", "common-workflows.md");

        Assert.Contains(scenario, route);
        Assert.Contains("## Direct Factory Model-Policy Route", route);
        Assert.Contains("## Workflow Family: Factory Model Configuration and Review", workflows);
        Assert.Contains("selects `Recommended first skill: idd-factory-configure` directly", workflows);
        Assert.DoesNotContain("factory-model-update", route);
        Assert.DoesNotContain("factory-model-update", workflows);
        Assert.DoesNotContain("factory-model-configuration", route);
        Assert.DoesNotContain("factory-model-management", route);
    }

    [Fact]
    public void DirectFactoryRoute_KeepsSeparateExamplesAndAuthorizationBoundaries()
    {
        var route = Canonical("skills", "idd-route.md");
        var workflows = Canonical("methodology", "common-workflows.md");
        var examples = route[route.IndexOf("## Direct Factory Model-Policy Output Examples",
            StringComparison.Ordinal)..];
        var blocks = Regex.Matches(examples, @"(?s)```text\s*(.*?)```");
        Assert.Equal(2, blocks.Count);

        var apply = blocks[0].Groups[1].Value;
        var review = blocks[1].Groups[1].Value;
        foreach (var example in new[] { apply, review })
        {
            Assert.Contains("Recommended first skill: idd-factory-configure", example);
            Assert.Contains("Expected complete workflow: idd-factory-configure", example);
            Assert.Contains("Stop after:", example);
            Assert.DoesNotContain("Classification:", example);
            Assert.DoesNotContain("Operation:", example);
            Assert.DoesNotContain("Clarity:", example);
            Assert.DoesNotContain("Execution depth:", example);
            Assert.DoesNotContain("Audit scope:", example);
        }

        Assert.Contains("Requested scope: end-to-end", apply);
        Assert.Contains("Current handoff: idd-factory-configure", apply);
        Assert.Contains("Requested scope: route-only", review);
        Assert.Contains("Current handoff: none", review);
        Assert.Contains("read-only", route);
        Assert.Contains("does not authorize writes", workflows);
        Assert.Contains("For **mixed requests**", workflows);
        Assert.Contains("general workflow classification and handoff", route);
        Assert.Contains("the separate `idd-factory` plugin", route);
        Assert.Contains("Current handoff: none", workflows);
        Assert.Contains("For ordinary classified workflows", route);
        Assert.Contains("| `product-change` | `idd-intent-change` |", route);
    }

    [Fact]
    public void RootAgent_PerformsMechanicalProfileLookupBeforeNativeSpawn()
    {
        var run = Canonical("skills", "idd-factory-run.md");

        Assert.Contains("planner ExecutionProfile", run);
        Assert.Contains("re-read one project configuration document when present", run);
        Assert.Contains("If the file is absent, inherit normal host model/reasoning settings", run);
        Assert.Matches(@"malformed\s+existing policy blocks", run);
        Assert.Contains("native child-agent spawn", run);
        Assert.Contains("Do not reconsider task complexity", run);
        Assert.Contains("never silently fall back to `inherit`", run);
        Assert.Contains("missing ExecutionProfile in an existing plan.md means standard", run);
        Assert.Contains("Partial mappings are valid", run);
        Assert.Contains("New planner output must include exactly one canonical profile", run);
        Assert.Contains("idd-factory-configure", run);
        Assert.Contains("same canonical Factory", run);
        Assert.Contains("references/factory-worker.md", run);
    }

    [Fact]
    public void Worker_DoesNotSelectOrReconfigureItsModel()
    {
        var worker = Canonical("factory", "worker.md");

        Assert.Contains("Do not read `.idd/execution.yaml`", worker);
        Assert.Contains("choose a model", worker);
        Assert.Contains("reinterpret an", worker);
        Assert.Contains("ExecutionProfile", worker);
        Assert.Contains(".idd/execution.yaml", worker);
    }

    [Fact]
    public void WorkerProtocol_PreservesSingleTaskBoundaryAndWorkerRestrictions()
    {
        var worker = Canonical("factory", "worker.md");
        var run = Canonical("skills", "idd-factory-run.md");

        Assert.StartsWith("# IDD Factory Worker Protocol", worker);
        Assert.Contains("You are the Factory worker.", worker);
        Assert.Contains("internal protocol owned by `idd-factory-run`", worker);
        Assert.Contains("exactly one Factory task", worker);
        Assert.Contains("fresh semantic context", worker);
        Assert.Contains("Do not read `.idd/execution.yaml`", worker);
        Assert.Contains("choose a model", worker);
        Assert.Contains("Do not modify `.idd/engineering`", worker);
        Assert.Contains("Monotonic Intent Refinement", worker);
        Assert.Contains("`.idd/factory/current`", worker);
        Assert.Contains("Do not search for additional Conditional Engineering Rules", worker);
        Assert.Contains("git ls-files --cached --others --exclude-standard", worker);
        Assert.Contains("semantic idempotency", worker);
        Assert.Contains("at-least-once", worker);
        Assert.Contains("focused checks", worker);
        Assert.Contains("short semantic result", worker);
        Assert.Contains("perform Factory finalization", worker);
        Assert.Contains("Do not assume inherited parent-skill references", worker);

        Assert.Contains("Before **every** worker spawn", run);
        Assert.Contains("If that reference is missing, empty, or", run);
        Assert.Contains("complete worker", run);
        Assert.Contains("--- Factory worker assignment ---", run);
        Assert.Contains("--- End Factory worker assignment ---", run);
        Assert.Contains("trusted terminal result", run);
        Assert.DoesNotContain("invoke `idd-factory-execute-subtask`", run);
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
        Assert.Contains("reasoning_effort", codexRun);
        Assert.Contains("fork_turns: \"none\"", codexRun);
        Assert.Contains("`task_name` in native `spawn_agent` arguments", codexRun);
        Assert.Contains("idd_factory_planner_01", codexRun);
        Assert.Contains("idd_factory_worker_01", codexRun);
        Assert.Contains("^idd_factory_(planner|worker)_[0-9]+$", codexRun);
        Assert.Contains("rejects the attempt", codexRun);

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
            .Append(Path.Combine(fixture.RepoRoot, "src", "canonical", "factory", "planner.md"))
            .Append(Path.Combine(fixture.RepoRoot, "src", "canonical", "factory", "worker.md"))
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
        Assert.Contains("default (inherit)", init);
        Assert.Contains("configure — explicitly configure models", init);
        Assert.Contains("Partial mappings are valid", init);
        Assert.Contains("idd-factory-configure", init);
    }
}
