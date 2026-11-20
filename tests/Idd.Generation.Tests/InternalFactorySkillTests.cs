using System.Text.Json;
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

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void GeneratedFactoryInstructions_UseSoftWorkerRotationTarget(string platform)
    {
        using var descriptions = JsonDocument.Parse(fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "src", "canonical", "skills", "skill-descriptions.json")));
        var description = descriptions.RootElement.GetProperty("idd-internal-factory-root")
            .GetProperty("description").GetString()!;

        Assert.Contains("rotate at or above", description);
        Assert.Contains("not a hard cap", description);
        Assert.DoesNotContain("at most", description, StringComparison.OrdinalIgnoreCase);

        var plugin = Path.Combine(fixture.MarketplaceRoot, "plugins", platform, "idd-factory", "skills");
        var root = fixture.ReadText(Path.Combine(plugin, "idd-internal-factory-root", "SKILL.md"));
        var supervisor = fixture.ReadText(Path.Combine(plugin, "idd-factory-run", "SKILL.md"));

        Assert.Contains(description, root);
        Assert.Contains("minimum target for normal", root);
        Assert.Contains("CompletedWorkers >= max_workers_per_batch > 0", supervisor);
        Assert.Contains("may exceed the positive effective target", supervisor);
        Assert.DoesNotContain("CompletedWorkers exactly equal to the limit", supervisor,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CompletedWorkers equal to the effective", supervisor,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void FactoryAdapters_GenerateCanonicalSkillBodiesWithoutInstructionAppendices(string platform)
    {
        var cases = new (string Skill, string Source)[]
        {
            ("idd-factory-run", "src/canonical/skills/idd-factory-run.md"),
            ("idd-factory-configure", "src/canonical/skills/idd-factory-configure.md"),
            ("idd-internal-factory-root", "src/canonical/factory/root.md"),
            ("idd-internal-factory-planner", "src/canonical/factory/planner.md"),
            ("idd-internal-factory-worker", "src/canonical/factory/worker.md")
        };

        foreach (var (skillName, canonicalPath) in cases)
        {
            var canonical = fixture.ReadText(Path.Combine(fixture.RepoRoot,
                canonicalPath.Replace('/', Path.DirectorySeparatorChar)));
            var generated = fixture.ReadText(Path.Combine(fixture.MarketplaceRoot,
                "plugins", platform, "idd-factory", "skills", skillName, "SKILL.md"))
                .ReplaceLineEndings("\n");

            Assert.StartsWith("---\n", generated);
            var frontMatterEnd = generated.IndexOf("\n---\n", 4, StringComparison.Ordinal);
            Assert.True(frontMatterEnd >= 0, $"Missing frontmatter terminator in {platform}/{skillName}.");
            var generatedBody = generated[(frontMatterEnd + "\n---\n".Length)..].Trim();

            Assert.Equal(canonical.ReplaceLineEndings("\n").Trim(), generatedBody);
        }

        var policy = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "src", "canonical", "methodology", "factory-execution-policy.md"));
        Assert.Contains("`codex.reasoningEffort` to `reasoning_effort`", policy);
        Assert.Contains("`claude.model` and optional `claude.effort`", policy);

        var packagedPolicy = fixture.ReadText(Path.Combine(fixture.MarketplaceRoot,
            "plugins", platform, "idd-factory", "skills", "idd-internal-factory-root",
            "references", "factory-execution-policy.md"));
        Assert.Equal(GenerationFixture.NormalizeText(policy),
            GenerationFixture.NormalizeText(packagedPolicy));
    }

    [Fact]
    public void FactoryV2Documentation_DescribesSupervisorAndInternalRootAsCurrentArchitecture()
    {
        var skills = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "docs", "factory-skills.md"));
        Assert.Contains("The public `idd-factory-run` is a two-level Supervisor.", skills);
        Assert.Contains("the **internal Root** performs Factory Preflight", skills);
        Assert.DoesNotContain("existing one-level orchestration directly", skills);
        Assert.DoesNotContain("For a new run it performs Factory Preflight", skills);

        var workflow = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "docs", "factory-workflow.md"));
        Assert.Contains("Factory packages three internal Skills", workflow);
        Assert.Contains("Supervisor resolves the Root `SKILL.md`", workflow);
        Assert.DoesNotContain("Factory packages two internal Skills", workflow);

        var installation = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "docs", "factory-native-agent-installation.md"));
        Assert.Contains("## Active Factory v2 architecture", installation);
        Assert.DoesNotContain("## Staged Supervisor migration", installation);
        Assert.Contains("Root derives its sibling Planner and Worker", installation);

        var intent = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            ".idd", "intent", "IDD-0001.spec-factory-orchestration.md"));
        Assert.Contains("Root: materialize request", intent);
        Assert.DoesNotContain("For a new run, `idd-factory-run` materializes", intent);
    }


    [Fact]
    public void FactoryV2_EntryModesAndPhaseOwnersAgreeAcrossNormativeSources()
    {
        var intent = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            ".idd", "intent", "IDD-0001.spec-factory-orchestration.md"));
        var adr = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            ".idd", "intent", "IDD-0006.adr-native-agent-factory-orchestration.md"));
        var preflight = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "src", "canonical", "methodology", "intent-preflight.md"));
        var root = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "src", "canonical", "factory", "root.md"));
        var supervisor = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "src", "canonical", "skills", "idd-factory-run.md"));
        var workflow = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "docs", "factory-workflow.md"));

        Assert.Contains("The internal Factory Root, never the public Supervisor", preflight);
        Assert.Contains("Root owns the existing Preflight", adr);
        Assert.Contains("## Phase ownership and public entry modes", intent);
        Assert.Contains("Supervisor owns **no** edits", supervisor);
        Assert.Contains("Root, not Supervisor, owns all corresponding state changes", workflow);

        foreach (var mode in new[] { "New", "Continue", "Answer", "Restart", "Cancel" })
        {
            Assert.Contains(mode, supervisor);
            Assert.Contains(mode, root);
            Assert.Contains(mode, preflight);
            Assert.Contains(mode, intent);
        }

        Assert.Contains("a request-level blocker that wrote no preparation", preflight);
        Assert.Contains("if no question was saved", supervisor);
        Assert.Contains("if their outcomes are uncertain, preserve recovery", root);
        Assert.Contains("only verified monotonic refinements", workflow);
        Assert.DoesNotContain("never edit either durable layer", workflow);
        Assert.Contains("baseline.md              # same immutable original Git baseline", workflow);
        Assert.Contains("provenance.md            # preflight and Worker checkpoints", workflow);
    }

    [Fact]
    public void FactoryV2_CancelRestartAndRecoveryDecisionsStayConsistent()
    {
        var intent = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            ".idd", "intent", "IDD-0001.spec-factory-orchestration.md"));
        var adr = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            ".idd", "intent", "IDD-0006.adr-native-agent-factory-orchestration.md"));
        var preflight = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "src", "canonical", "methodology", "intent-preflight.md"));
        var root = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "src", "canonical", "factory", "root.md"));
        var supervisor = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "src", "canonical", "skills", "idd-factory-run.md"));

        Assert.Contains("Reason: CANCELLED", root);
        Assert.Contains("Reason: CANCELLED", supervisor);
        Assert.Contains("COMPLETED/CANCELLED", preflight);
        Assert.Contains("COMPLETED/CANCELLED", intent);
        Assert.Contains("COMPLETED with Reason CANCELLED", adr);
        Assert.Contains("without claiming implementation", root);
        Assert.Contains("does not require Planner `# Done`", supervisor);

        Assert.Contains("FIRST reconcile saved", root);
        Assert.Contains("first reconcile the saved request", preflight);
        Assert.Contains("Reject a CANCELLED reason for non-Cancel modes", supervisor);
        Assert.Contains("NEEDS_INPUT with a concrete", preflight);
        Assert.Contains("NEEDS_INPUT with the precise choice", root);
        Assert.Contains("requests a concrete user decision with NEEDS_INPUT", intent);
        Assert.Contains("Root automatically reconciles only outcomes", preflight);
        Assert.Contains("uncertain owner actions", root);
    }

    [Fact]
    public void FactoryPreflight_SavesRecoverableRequestBeforeDurableOwnerMutation()
    {
        var preflight = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "src", "canonical", "methodology", "intent-preflight.md"));
        var intent = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            ".idd", "intent", "IDD-0001.spec-factory-orchestration.md"));
        var root = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "src", "canonical", "factory", "root.md"));

        static void Ordered(string content, params string[] phases)
        {
            var previous = -1;
            foreach (var phase in phases)
            {
                var index = content.IndexOf(phase, StringComparison.Ordinal);
                Assert.True(index > previous, $"Missing or out-of-order Preflight phase: {phase}");
                previous = index;
            }
        }

        Ordered(preflight,
            "3. detect request-level blockers before any write or preparation",
            "5. persist complete preparation/request.md, baseline.md, provenance.md",
            "6. checkpoint BEFORE mutating Engineering owner workflow",
            "8. checkpoint and execute authorized Product Intent owner workflows",
            "9. validate durable coverage",
            "13. create current/request.md LAST");

        Assert.Contains("`references/intent-preflight.md` is the sole normative contract", root);
        Assert.Contains("request-level blockers **before writes**", root);
        Assert.Contains("provenance **before** mutating owners", root);
        Assert.Contains("Engineering owner before", root);
        Assert.Contains("Product owner", root);
        Assert.Contains("Write `current/request.md` LAST", root);

        Ordered(intent,
            "-> reject request-level blockers before any durable write",
            "-> persist preparation/request.md + baseline.md + provenance.md BEFORE owner mutations",
            "-> checkpoint and invoke Engineering owner when required",
            "-> checkpoint and invoke Product Intent owner when required",
            "-> validate durable coverage");
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void PlannerBlocked_IsDiagnosticFailure_NotAFourthPlanningOutcome(string platform)
    {
        var planner = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "src", "canonical", "factory", "planner.md"));
        var root = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "src", "canonical", "factory", "root.md"));
        var preflight = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "src", "canonical", "methodology", "intent-preflight.md"));

        Assert.Contains("three **normal planning outcomes**", planner);
        Assert.Contains("BLOCKED\nReason: <specific nonempty single-line diagnostic>", planner);
        Assert.Contains("not a fourth planning outcome", planner);
        Assert.Contains("not `BLOCKED`", planner);
        Assert.Contains("PLANNER_BLOCKED", root);
        Assert.Contains("PLANNER_PROTOCOL_INVALID", root);
        Assert.Contains("CHILD_UNCONFIRMED", root);
        Assert.Contains("Keep the actual Root-local CompletedWorkers count", root);
        Assert.Contains("without creating a\nquestion, plan or active marker or automatically retrying", preflight);

        var plugin = Path.Combine(fixture.MarketplaceRoot,
            "plugins", platform, "idd-factory", "skills");
        var packagedPlanner = fixture.ReadText(Path.Combine(plugin,
            "idd-internal-factory-planner", "SKILL.md"));
        var packagedRoot = fixture.ReadText(Path.Combine(plugin,
            "idd-internal-factory-root", "SKILL.md"));
        var packagedPreflight = fixture.ReadText(Path.Combine(plugin,
            "idd-internal-factory-root", "references", "intent-preflight.md"));

        Assert.Contains("BLOCKED\nReason: <specific nonempty single-line diagnostic>",
            packagedPlanner.ReplaceLineEndings("\n"));
        Assert.Contains("PLANNER_PROTOCOL_INVALID", packagedRoot);
        Assert.Equal(GenerationFixture.NormalizeText(preflight),
            GenerationFixture.NormalizeText(packagedPreflight));
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void OptionalModelOverrides_AreCheckedOnlyForExplicitWorkerMappings(string platform)
    {
        var supervisor = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "src", "canonical", "skills", "idd-factory-run.md"));
        var root = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "src", "canonical", "factory", "root.md"));
        var policy = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "src", "canonical", "methodology", "factory-execution-policy.md"));

        Assert.Contains("Do not require model/effort overrides at Supervisor entry", supervisor);
        Assert.DoesNotContain("exact model and effort overrides of grandchildren",
            supervisor.ReplaceLineEndings(" ").Replace("  ", " "));
        Assert.Contains("model/effort overrides at Root entry", root);
        Assert.Contains("without checking", root);
        Assert.Contains("any effort argument actually configured", root);
        Assert.Contains("Model/effort override support is NOT a Supervisor/Preflight host gate", policy);
        Assert.Contains("omit Factory-specific model/reasoning overrides without testing override", policy);

        var skills = Path.Combine(fixture.MarketplaceRoot, "plugins", platform,
            "idd-factory", "skills");
        var packagedSupervisor = fixture.ReadText(Path.Combine(skills, "idd-factory-run", "SKILL.md"));
        var packagedRoot = fixture.ReadText(Path.Combine(skills, "idd-internal-factory-root", "SKILL.md"));
        var packagedPolicy = fixture.ReadText(Path.Combine(skills, "idd-internal-factory-root",
            "references", "factory-execution-policy.md"));

        Assert.Contains("Do not require model/effort overrides at Supervisor entry",
            packagedSupervisor);
        Assert.Contains("any effort argument actually configured", packagedRoot);
        Assert.Equal(GenerationFixture.NormalizeText(policy),
            GenerationFixture.NormalizeText(packagedPolicy));
    }

    [Fact]
    public void Root_RemainsCompactAndDelegatesDetailedPoliciesToCanonicalReferences()
    {
        var root = fixture.ReadText(Path.Combine(fixture.RepoRoot,
            "src", "canonical", "factory", "root.md"));

        Assert.True(root.Split('\n').Length <= 650,
            "Root should orchestrate; detailed Preflight, model policy and verification belong in references.");
        foreach (var reference in new[]
        {
            "references/intent-preflight.md",
            "references/factory-execution-policy.md",
            "references/project-verification.md",
            "references/engineering-guardrails.md"
        })
            Assert.Contains(reference, root);

        Assert.DoesNotContain("git status --porcelain=v1", root);
        Assert.Contains("Status: CONTINUE|COMPLETED|NEEDS_INPUT|BLOCKED", root);
        Assert.Contains("CompletedWorkers: <nonnegative actual confirmed Root-local integer>", root);
        Assert.Contains("**LAST** before the Worker loop", root);
        Assert.Contains("all writing", root, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Intent Refinement Review", root);
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
        Assert.Contains("the whole plan is finished", root);
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
