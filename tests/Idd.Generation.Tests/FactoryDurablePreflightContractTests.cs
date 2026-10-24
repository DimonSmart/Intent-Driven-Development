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
    public void FactoryRun_PersistsOriginalBaselineBeforeOwnerMutations()
    {
        var run = Canonical("skills", "idd-factory-run.md");

        var saved = run.IndexOf("save preparation/request.md, baseline.md, provenance.md BEFORE owner mutations", StringComparison.Ordinal);
        var engineering = run.IndexOf("invoke Engineering owner if required", saved, StringComparison.Ordinal);
        var product = run.IndexOf("invoke authorized Product Intent owner workflows", engineering, StringComparison.Ordinal);
        var planner = run.IndexOf("run a fresh initial Factory planner while current/request.md is absent", product, StringComparison.Ordinal);
        var marker = run.IndexOf("create current/request.md LAST", planner, StringComparison.Ordinal);

        Assert.True(saved >= 0 && saved < engineering);
        Assert.True(engineering < product && product < planner && planner < marker);
        Assert.Contains("Git status --porcelain", run, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HEAD^{commit}", run);
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
        Assert.Contains("preparation/baseline.md", preflight);
        Assert.Contains("preparation/request.md", preflight);
        Assert.Contains("unchanged base request", Canonical("factory", "planner.md"));
    }

    [Fact]
    public void IntentPreserving_AllowsOnlyFactoryMonotonicRefinement()
    {
        var preflight = Canonical("methodology", "intent-preflight.md");
        var route = Canonical("skills", "idd-route.md");
        var worker = Canonical("factory", "worker.md");
        var planner = Canonical("factory", "planner.md");
        Assert.Contains("`intent-preserving`", preflight);
        Assert.Contains("`intent-preserving`", route);
        Assert.Contains("Mutating Product/Engineering preflight is forbidden", preflight);
        Assert.Contains("Monotonic Intent Refinement (Factory Worker only)", worker);
        Assert.Contains("Do not modify `.idd/engineering`", worker);
        Assert.Contains("Do not create or modify durable Product Intent or Engineering Rules", planner);
        Assert.DoesNotContain("Do not modify `.idd/intent`, `.idd/engineering`", worker);
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
        Assert.Contains("Planner remains read-only", usingIdd);
        Assert.Contains("Factory Worker", usingIdd);
        Assert.Contains("Planner remains read-only", methodology);
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

        Assert.Contains("If the layer is absent, that is valid", Canonical("methodology", "intent-preflight.md"));
        Assert.Contains("pending", engineering);
        Assert.Contains("preparation/", intent);
        Assert.Contains("AFTER the first valid executable plan", intent);
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
        Assert.Contains("fresh initial Planner", preflight);
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


    [Fact]
    public void TechnicalDecisions_AreNotNewDurablePolicyByDefault()
    {
        var preflight = Canonical("methodology", "intent-preflight.md");
        var guardrails = Canonical("methodology", "engineering-guardrails.md");
        var noPolicy = preflight.IndexOf("### NoEngineeringManagementNeeded", StringComparison.Ordinal);
        var newPolicy = preflight.IndexOf("### EngineeringManagementRequired", StringComparison.Ordinal);
        var missingPolicy = preflight.IndexOf("### EngineeringDecisionMissing", StringComparison.Ordinal);
        Assert.True(noPolicy >= 0 && noPolicy < newPolicy && newPolicy < missingPolicy);
        Assert.Contains("even without \"choose yourself\"", preflight);
        Assert.Contains("EngineeringDecisionMissing does not mean", preflight);
        Assert.Contains("## Authority for technical decisions", guardrails);
        Assert.Contains("must constrain future implementations", guardrails);
        Assert.Contains("Never promote a successful spike", guardrails);
    }

    [Fact]
    public void RealPreflightBlocker_PrecedesPreparationAndFirstPlanner()
    {
        var run = Canonical("skills", "idd-factory-run.md");
        var preflight = Canonical("methodology", "intent-preflight.md");
        var workflow = Canonical("methodology", "common-workflows.md");
        Assert.Contains("NOT an initial Planner `# Question`", preflight);
        Assert.Contains("before durable writes, `preparation/`, and", run);
        var preparation = workflow.LastIndexOf("-> persist .idd/factory/preparation/request.md", StringComparison.Ordinal);
        var planner = workflow.IndexOf("-> fresh initial Planner [INACTIVE]", preparation, StringComparison.Ordinal);
        var marker = workflow.IndexOf("-> create .idd/factory/current/request.md LAST [ACTIVE]", planner, StringComparison.Ordinal);
        Assert.True(preparation >= 0 && preparation < planner && planner < marker);
    }

    [Fact]
    public void PlannerSuppliesRulesForWorkerDelegatedAlternatives()
    {
        var planner = Canonical("factory", "planner.md");
        var worker = Canonical("factory", "worker.md");
        var run = Canonical("skills", "idd-factory-run.md");
        var intent = Repo(".idd", "intent", "IDD-0001.spec-factory-orchestration.md");
        Assert.Contains("*before* delegating the", planner);
        Assert.Contains("The Rules remain Conditional", planner);
        Assert.Contains("Do not rely on Worker to discover", planner);
        Assert.Contains("extra Conditional IDs", worker);
        Assert.Contains("uncovered-constraint diagnostic", worker);
        Assert.Contains("*before* Worker spawn", run);
        Assert.Contains("reports missing coverage as", intent);
    }

    [Fact]
    public void WorkerAutonomy_DoesNotBypassActiveRunPolicyProtection()
    {
        var worker = Canonical("factory", "worker.md");
        var run = Canonical("skills", "idd-factory-run.md");
        var guardrails = Canonical("methodology", "engineering-guardrails.md");
        Assert.Contains("No explicit user delegation phrase is needed", worker);
        Assert.Contains("Respect explicit scope, Product Intent", worker);
        Assert.Contains("does not modify Rule IDs", worker);
        Assert.Contains("A new Engineering policy remains forbidden during an active run", run);
        Assert.Contains("Active `current/request.md` still prohibits Rule edits", guardrails);
        Assert.Contains("external identity", guardrails);
    }

    [Fact]
    public void PlannerOutputProtocol_DoesNotIntroduceDecisionSection()
    {
        var planner = Canonical("factory", "planner.md");
        Assert.Contains("# ExecutionProfile", planner);
        Assert.Contains("# TaskRelatedIntent", planner);
        Assert.Contains("# TaskRelatedEngineering", planner);
        Assert.Contains("# Question", planner);
        Assert.Contains("# Done", planner);
        Assert.DoesNotContain("# Decision", planner);
        Assert.Contains("existing pending plans", planner);
    }

    [Fact]
    public void LiveCases_RequireRealTracesAndSeparateNotRunStatus()
    {
        var live = Repo("evals", "idd-factory", "autonomous-technical-decisions.md");
        for (var id = 1; id <= 10; id++)
            Assert.Contains($"E{id:00}", live);
        Assert.Contains("codex-trace.jsonl", live);
        Assert.Contains("preparation/", live);
        Assert.Contains("current/", live);
        Assert.Contains("not run", live);
        Assert.Contains("never counted as passed", live);
    }

    [Theory]
    [InlineData("T01 T03 T17", "src/canonical/methodology/intent-preflight.md", "even without \"choose yourself\"")]
    [InlineData("T02 T07", "src/canonical/methodology/engineering-guardrails.md", "Existing Mandatory Constraint")]
    [InlineData("T04", "src/canonical/methodology/intent-preflight.md", "### MissingIntentDecision")]
    [InlineData("T05", "src/canonical/methodology/intent-preflight.md", "### EngineeringDecisionMissing")]
    [InlineData("T06", "src/canonical/factory/planner.md", "without requiring the user to say")]
    [InlineData("T08", "src/canonical/factory/planner.md", "bounded research Task")]
    [InlineData("T09 T10", "src/canonical/methodology/engineering-guardrails.md", "published identity")]
    [InlineData("T11", "src/canonical/methodology/engineering-guardrails.md", "incompatible mandatory constraints")]
    [InlineData("T12", "src/canonical/methodology/intent-preflight.md", "If the layer is absent")]
    [InlineData("T13", "src/canonical/methodology/intent-preflight.md", "idd-engineering-change")]
    [InlineData("T14", "src/canonical/skills/idd-factory-run.md", "active run")]
    [InlineData("T15", "src/canonical/factory/planner.md", "# ExecutionProfile")]
    [InlineData("T16", "src/canonical/factory/planner.md", "Multiple technical choices alone")]
    [InlineData("T18", "src/canonical/factory/planner.md", "Conditional")]
    [InlineData("T19", "src/canonical/factory/worker.md", "uncovered-constraint diagnostic")]
    [InlineData("T20", "src/canonical/methodology/intent-preflight.md", "NOT an initial Planner")]
    [InlineData("T21", "src/canonical/methodology/common-workflows.md", "create .idd/factory/current/request.md LAST")]
    [InlineData("T22", "src/canonical/methodology/engineering-guardrails.md", "purchases")]
    public void AutonomousDecisionCases_ReferenceRequiredContracts(string caseLabel, string path, string clause)
    {
        Assert.False(string.IsNullOrWhiteSpace(caseLabel));
        Assert.Contains(clause, Repo(path.Split('/')));
    }

}
