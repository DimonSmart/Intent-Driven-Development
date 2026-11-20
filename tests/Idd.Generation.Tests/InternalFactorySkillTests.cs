using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class InternalFactorySkillTests(GenerationFixture fixture)
{
    [Fact]
    public void ObsoleteExperimentFlag_IsRejected()
    {
        var result = fixture.RunProcess("dotnet",
            ["exec", fixture.GeneratorDll, "--version", fixture.Version, "--internal-agent-experiment"]);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Unknown option: --internal-agent-experiment", result.Stderr);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void NormalGeneration_PackagesCanonicalSkills_NotNativeRoles(string platform)
    {
        var plugin = Path.Combine(fixture.MarketplaceRoot, "plugins", platform, "idd-factory");
        fixture.AssertMissing(Path.Combine(plugin, "agents"));
        fixture.AssertMissing(Path.Combine(plugin, "skills", "idd-factory-configure",
            "scripts", "register-codex-agents.py"));
        foreach (var role in new[] { "root", "planner", "worker" })
        {
            var canonical = fixture.ReadText(Path.Combine(fixture.RepoRoot,
                "src", "canonical", "factory", role + ".md"));
            var skill = fixture.ReadText(Path.Combine(plugin, "skills",
                "idd-internal-factory-" + role, "SKILL.md"));
            Assert.Contains(canonical.ReplaceLineEndings("\n").Trim(), skill.ReplaceLineEndings("\n"));
            if (platform == "claude")
            {
                Assert.Contains("user-invocable: false", skill);
                Assert.Contains("disable-model-invocation: false", skill);
                Assert.DoesNotContain("context: fork", GenerationFixture.ReadFrontMatter(skill));
            }
            else fixture.AssertMissing(Path.Combine(plugin, "skills",
                "idd-internal-factory-" + role, "agents", "openai.yaml"));
        }
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void InternalFactoryRoot_PackagesItsOwnCompletePreflightReferences(string platform)
    {
        var plugin = Path.Combine(fixture.MarketplaceRoot, "plugins", platform, "idd-factory");
        var references = Path.Combine(plugin, "skills", "idd-internal-factory-root", "references");
        foreach (var name in new[]
        {
            "intent-preflight.md",
            "project-verification.md",
            "factory-execution-policy.md",
            "engineering-guardrails.md",
            "methodology-version.json"
        })
            fixture.AssertFile(Path.Combine(references, name));

        var run = Path.Combine(plugin, "skills", "idd-factory-run", "SKILL.md");
        var root = Path.Combine(plugin, "skills", "idd-internal-factory-root", "SKILL.md");
        var runText = fixture.ReadText(run);
        var rootText = fixture.ReadText(root);
        Assert.Contains("Factory Preflight", rootText);
        Assert.Contains("current/request.md", rootText);
        Assert.Contains("project-verification.md", rootText);
        Assert.Contains("idd-internal-factory-root/SKILL.md", rootText);
        Assert.DoesNotContain("## Factory Preflight — Root entrypoint", runText);
        Assert.Contains("Factory Preflight", rootText);
        fixture.AssertMissing(Path.Combine(plugin, "skills",
            "idd-internal-factory-root", "agents", "openai.yaml"));
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void Preflight_RequiresInstalledPlannerWorkerSkills_NotRegisteredRoles(string platform)
    {
        var canonical = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "src", "canonical", "methodology", "intent-preflight.md"));
        var packaged = fixture.ReadText(Path.Combine(fixture.MarketplaceRoot,
            "plugins", platform, "idd-factory", "skills", "idd-internal-factory-root",
            "references", "intent-preflight.md"));

        foreach (var preflight in new[] { canonical, packaged })
        {
            Assert.Contains("idd-internal-factory-planner/SKILL.md", preflight);
            Assert.Contains("idd-internal-factory-worker/SKILL.md", preflight);
            Assert.Contains("agent roles or `agent_type` values are not required", preflight);
            Assert.DoesNotContain("both registered", preflight, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void NativeFactoryRotationContractRequiresCompletePlans()
    {
        var root = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "src", "canonical", "factory", "root.md"));
        var planner = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "src", "canonical", "factory", "planner.md"));
        var supervisor = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "src", "canonical", "skills", "idd-factory-run.md"));

        Assert.Contains("at most **4 `# Task`", planner);
        Assert.Contains("five or more", root, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("previously persisted plan", root);
        Assert.Contains("complete plan boundary", root);
        Assert.Contains("CompletedWorkers >= max_workers_per_batch > 0", supervisor);
        Assert.DoesNotContain("cannot exceed the effective per-batch limit", supervisor);
        Assert.DoesNotContain("immediately return Status CONTINUE", root);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void FactoryRun_ReferencesInstalledSkills_WithoutRoleRegistry(string platform)
    {
        var skill = fixture.ReadText(Path.Combine(fixture.MarketplaceRoot,
            "plugins", platform, "idd-factory", "skills", "idd-factory-run", "SKILL.md"));
        Assert.Contains("idd-internal-factory-root/SKILL.md", skill);
        Assert.DoesNotContain("idd-internal-factory-planner/SKILL.md", skill);
        Assert.DoesNotContain("idd-internal-factory-worker/SKILL.md", skill);
        Assert.Contains("BLOCKED", skill);
        Assert.DoesNotContain("use their registered custom-agent", skill);
    }
}
