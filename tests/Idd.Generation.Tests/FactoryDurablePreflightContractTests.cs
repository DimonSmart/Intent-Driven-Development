using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class FactoryDurablePreflightContractTests(GenerationFixture fixture)
{
    private string Canonical(params string[] parts) =>
        fixture.ReadText(Path.Combine([fixture.RepoRoot, "src", "canonical", .. parts]));

    private string Repo(params string[] parts) =>
        fixture.ReadText(Path.Combine([fixture.RepoRoot, .. parts]));

    [Fact]
    public void FactoryRun_PreparesBothDurableLayersBeforeActiveState()
    {
        var run = Canonical("skills", "idd-factory-run.md");

        Assert.Contains("EngineeringManagementRequired", run);
        Assert.Contains("invoke idd-engineering-change with the complete logical request", run);
        Assert.Contains("apply required Product Intent mutation through existing intent workflows", run);
        Assert.Contains("validate durable coverage against the complete logical request", run);

        var engineering = run.IndexOf("invoke idd-engineering-change with the complete logical request", StringComparison.Ordinal);
        var intent = run.IndexOf("apply required Product Intent mutation through existing intent workflows", StringComparison.Ordinal);
        var initialPlanner = run.IndexOf("run a fresh initial Factory planner while current/request.md is absent", StringComparison.Ordinal);
        var activation = run.IndexOf("create current/request.md LAST", StringComparison.Ordinal);

        Assert.True(engineering >= 0 && engineering < intent);
        Assert.True(intent < initialPlanner);
        Assert.True(initialPlanner < activation);
    }

    [Fact]
    public void Factory_DoesNotDuplicateEngineeringOwnership()
    {
        var run = Canonical("skills", "idd-factory-run.md");

        Assert.Contains("exclusively owned by `idd-engineering-change`", run);
        Assert.Contains("authorize Factory to determine `new`", run);
        Assert.Contains("semantic equivalence", run);
        Assert.Contains("allocator changes", run);
        Assert.Contains("INDEX edits", run);
    }

    [Fact]
    public void RequestIdentity_RemainsTheCompleteLogicalRequest()
    {
        var run = Canonical("skills", "idd-factory-run.md");
        var preflight = Canonical("methodology", "intent-preflight.md");

        Assert.Contains("materialized request is authoritative", run);
        Assert.Contains("complete self-contained logical request", preflight);
        Assert.Contains("Do not substitute a route summary", preflight);
        Assert.Contains("Only after successful durable preflight", preflight);
        Assert.Contains("preparation/request.md", preflight);
        Assert.Contains("unchanged base request", Canonical("factory", "planner.md"));
    }

    [Fact]
    public void ImplementationOnly_ProtectsIntentAndEngineering()
    {
        var preflight = Canonical("methodology", "intent-preflight.md");
        var route = Canonical("skills", "idd-route.md");

        Assert.Contains("never mutate `.idd/intent/*`", preflight);
        Assert.Contains("`.idd/engineering/*`", preflight);
        Assert.Contains("Do not change `.idd/intent/*`", route);
        Assert.Contains("`.idd/engineering/*`", route);
    }

    [Fact]
    public void PlannerAndWorker_CannotMutateDurableKnowledge()
    {
        var planner = Canonical("factory", "planner.md");
        var worker = Canonical("factory", "worker.md");

        Assert.Contains("Do not create or modify durable Product Intent or Engineering Rules", planner);
        Assert.Contains("run `idd-engineering-change`", planner);
        Assert.Contains("Do not modify `.idd/intent`, `.idd/engineering`", worker);
    }

    [Fact]
    public void FactoryIntent_ModelsPlannerAsInternalProtocolOwnedByRun()
    {
        var intent = Repo(".idd", "intent", "IDD-0001.spec-factory-orchestration.md");
        var route = Canonical("skills", "idd-route.md");

        Assert.Contains("Registered Factory skills are", intent);
        Assert.Contains("Internal Factory protocols", intent);
        Assert.Contains("worker                        owned by idd-factory-run", intent);
        Assert.Contains("owned by idd-factory-run", intent);
        Assert.DoesNotContain("idd-factory-decompose-task", intent);
        Assert.DoesNotContain("idd-factory-decompose-task", route);
    }

    [Fact]
    public void EngineeringLayerAbsence_RemainsValidAndDoesNotBootstrapFactory()
    {
        var preflight = Canonical("methodology", "intent-preflight.md");
        var engineering = Canonical("methodology", "engineering-guardrails.md");

        Assert.Contains("If the layer is absent, that is valid", preflight);
        Assert.Contains("Do not warn and do not create it merely", preflight);
        Assert.Contains("A new Factory entry is a valid coordinator only before that marker exists", engineering);
    }

    [Fact]
    public void EngineeringActiveRunGuard_RemainsUnchanged()
    {
        var change = Canonical("skills", "idd-engineering-change.md");

        Assert.Contains("Before reading or planning a mutation, check `.idd/factory/current/request.md`", change);
        Assert.Contains("If it exists, return `blocked` and mutate nothing", change);
        Assert.DoesNotContain("allowEngineeringMutationInsideFactory", change);
        Assert.DoesNotContain("ignoreActiveFactoryGuard", change);
    }

    [Fact]
    public void EngineeringChangingReplacement_IsExplicitlyBlocked()
    {
        var run = Canonical("skills", "idd-factory-run.md");
        var preflight = Canonical("methodology", "intent-preflight.md");

        Assert.Contains("EngineeringManagementRequired`, stop before any durable write", run);
        Assert.Contains("Engineering-changing replacement", preflight);
        Assert.Contains("complete replacement request started as a new Factory run", preflight);
    }

    [Fact]
    public void MixedProductEngineeringAndImplementation_NewRunIsSupported()
    {
        var workflows = Canonical("methodology", "common-workflows.md");
        var intent = Repo(".idd", "intent", "IDD-0001.spec-factory-orchestration.md");

        Assert.Contains("simultaneously changes Product Intent", workflows);
        Assert.Contains("explicit durable Engineering decisions", workflows);
        Assert.Contains("create active Factory state", intent);
        Assert.Contains("idd-engineering-change when required", intent);
    }

    [Fact]
    public void PlannerEngineeringAnswer_DoesNotMutateInsideActiveRun()
    {
        var run = Canonical("skills", "idd-factory-run.md");
        var preflight = Canonical("methodology", "intent-preflight.md");

        Assert.Contains("introduces a new durable Engineering decision", run);
        Assert.Contains("do not invoke", run);
        Assert.Contains("Factory paused", preflight);
        Assert.Contains("do not call", preflight);
    }

    [Fact]
    public void FactorySkillDescription_MentionsBothDurableConcerns()
    {
        var descriptions = Canonical("skills", "skill-descriptions.json");

        Assert.Contains("Prepare required durable Product Intent and explicit Engineering policy", descriptions);
    }

    [Fact]
    public void PublicDocs_DistinguishEntryPreflightFromPlannerWorkerExecution()
    {
        var workflow = Repo("docs", "factory-workflow.md");
        var usingIdd = Repo("docs", "using-idd.md");
        var methodology = Repo("docs", "methodology.md");

        Assert.Contains("Factory Preflight", workflow);
        Assert.Contains("planner and", usingIdd);
        Assert.Contains("workers themselves remain implementation-only", usingIdd);
        Assert.Contains("Factory planners and workers never mutate Product Intent or Engineering", methodology);
        Assert.DoesNotContain("Factory must not create or change product intent", usingIdd);
    }

    [Fact]
    public void InitialPlanner_ReceivesPreparationAndActiveInputsThroughSameProtocol()
    {
        var planner = Canonical("factory", "planner.md");
        var run = Canonical("skills", "idd-factory-run.md");

        Assert.Contains("preparation/request.md", planner);
        Assert.Contains("current/request.md", planner);
        Assert.Contains("preparation/answers.md", planner);
        Assert.Contains("current/answers.md", planner);
        Assert.Contains("No `completed.md` exists before any Worker", run);
        Assert.Contains("read-only Factory Planner", Canonical("methodology", "intent-preflight.md"));
        Assert.Contains("fork_turns: \"none\"", run);
        Assert.Contains("idd_factory_planner_01", run);
    }

    [Fact]
    public void InitialQuestion_LeavesRunInactiveAndPersistsExactQuestionAnswer()
    {
        var run = Canonical("skills", "idd-factory-run.md");
        var planner = Canonical("factory", "planner.md");

        Assert.Contains("Do NOT write `current/request.md` or `current/plan.md`", run);
        Assert.Contains("preparation/question.md", run);
        Assert.Contains("question AND answer verbatim", run);
        Assert.Contains("chronological exact question-and-answer pairs", planner);
        Assert.Contains("NEW isolated planner", run);
    }

    [Fact]
    public void PreparationAnswers_UseAuthorizedOwnersBeforeFreshPlanning()
    {
        var run = Canonical("skills", "idd-factory-run.md");
        var preflight = Canonical("methodology", "intent-preflight.md");

        Assert.Contains("does not implicitly promote", run);
        Assert.Contains("Engineering before Product Intent", run);
        Assert.Contains("ambiguous", run);
        Assert.Contains("blocked", run);
        Assert.Contains("request-level blocker checks", preflight);
        Assert.Contains("original requested scope", preflight);
        Assert.Contains("preparation/answers.md", preflight);
    }

    [Fact]
    public void ValidFirstBatch_ActivatesOnceAndWorkersRequireBothFiles()
    {
        var run = Canonical("skills", "idd-factory-run.md");

        Assert.Contains("THAT exact Task batch", run);
        Assert.Contains("exactly one explicit `# ExecutionProfile`", run);
        Assert.Contains("existing unique Conditional", run);
        Assert.Contains("Only after BOTH", run);
        Assert.Contains("current/request.md` LAST", run);
        Assert.Contains("legacy", run);
        Assert.Contains("without `# ExecutionProfile` remain valid", run);
    }

    [Fact]
    public void BeforeActivationDone_IsVerifiedWithoutEmptyActiveRun()
    {
        var run = Canonical("skills", "idd-factory-run.md");
        var preflight = Canonical("methodology", "intent-preflight.md");

        Assert.Contains("`# Done` before activation", run);
        Assert.Contains("`final` context", run);
        Assert.Contains("WITHOUT active marker or empty plan", run);
        Assert.Contains("preparation/verification-failure.md", run);
        Assert.Contains("Not verified", run);
        Assert.Contains("unchanged repeated failure", preflight);
    }

    [Fact]
    public void MalformedPlannerAndBrokenOwner_DoNotActivate()
    {
        var run = Canonical("skills", "idd-factory-run.md");

        Assert.Contains("Empty/mixed/malformed Planner output", run);
        Assert.Contains("unresolved IDs", run);
        Assert.Contains("Preserve preparation", run);
        Assert.Contains("material changes invalidate", run);
        Assert.Contains("same failure persists", run);
    }

    [Fact]
    public void PreparationResumeAndCancellation_DoNotChangeActiveGuard()
    {
        var run = Canonical("skills", "idd-factory-run.md");
        var preflight = Canonical("methodology", "intent-preflight.md");
        var change = Canonical("skills", "idd-engineering-change.md");

        Assert.Contains("restore the full base request", run);
        Assert.Contains("ask whether to continue", run);
        Assert.Contains("never roll back", run);
        Assert.Contains("orphaned inactive files", run);
        Assert.Contains("resume preparation from", preflight);
        Assert.Contains("If it exists, return `blocked` and mutate nothing", change);
    }

    [Fact]
    public void OptionalEngineeringAndDualScopeStayCompatible()
    {
        var run = Canonical("skills", "idd-factory-run.md");
        var engineering = Canonical("methodology", "engineering-guardrails.md");
        var intent = Repo(".idd", "intent", "IDD-0001.spec-factory-orchestration.md");

        Assert.Contains("Absence of the Engineering layer is valid", run);
        Assert.Contains("pending", engineering);
        Assert.Contains("preparation/", intent);
        Assert.Contains("after the first valid executable plan", intent);
        Assert.Contains("at-least-once", intent);
    }

    [Fact]
    public void CanonicalPluginReferences_AreDeclaredForBothPlatforms()
    {
        var manifest = Canonical("plugins", "plugin-manifest.json");
        var planner = Canonical("factory", "planner.md");
        var preflight = Canonical("methodology", "intent-preflight.md");

        Assert.Contains("src/canonical/factory/planner.md", manifest);
        Assert.Contains("src/canonical/methodology/intent-preflight.md", manifest);
        Assert.Contains("src/canonical/methodology/engineering-guardrails.md", manifest);
        Assert.Contains("before activation", planner);
        Assert.Contains("fresh initial planner", preflight);
    }

    [Fact]
    public void UserFacingFactoryDocs_DescribeMarkerLastActivation()
    {
        var workflow = Repo("docs", "factory-workflow.md");
        var usingIdd = Repo("docs", "using-idd.md");
        var methodology = Repo("docs", "methodology.md");
        var engineering = Repo("docs", "engineering-guardrails.md");

        Assert.Contains("fresh initial Planner [INACTIVE]", workflow);
        Assert.Contains("current/request.md LAST", workflow);
        Assert.Contains("preparation/question.md", workflow);
        Assert.Contains("Only `current/request.md`", methodology);
        Assert.Contains("preparation/", usingIdd);
        Assert.Contains("Engineering mutation is blocked", engineering);
    }

}
