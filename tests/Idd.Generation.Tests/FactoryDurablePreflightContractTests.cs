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
        var state = run.IndexOf("create .idd/factory/current/request.md", StringComparison.Ordinal);

        Assert.True(engineering >= 0 && engineering < intent);
        Assert.True(intent < state);
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
        var planner = Canonical("skills", "idd-factory-decompose-task.md");
        var worker = Canonical("skills", "idd-factory-execute-subtask.md");

        Assert.Contains("Do not create or modify durable Product Intent or Engineering Rules", planner);
        Assert.Contains("run `idd-engineering-change`", planner);
        Assert.Contains("Do not modify `.idd/intent`, `.idd/engineering`", worker);
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
}
