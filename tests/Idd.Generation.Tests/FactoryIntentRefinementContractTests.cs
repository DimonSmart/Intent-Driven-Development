using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class FactoryIntentRefinementContractTests(GenerationFixture fixture)
{
    private string Canonical(params string[] parts) =>
        fixture.ReadText(Path.Combine([fixture.RepoRoot, "src", "canonical", .. parts]));

    private string Intent() => fixture.ReadText(Path.Combine(
        fixture.RepoRoot, ".idd", "intent", "IDD-0001.spec-factory-orchestration.md"));

    [Fact]
    public void NewRun_RequiresOriginalCleanIddButContinuationDoesNot()
    {
        var run = Canonical("skills", "idd-factory-run.md");
        var preflight = Canonical("methodology", "intent-preflight.md");
        Assert.Contains("git status --porcelain=v1 -z --untracked-files=all", run);
        Assert.Contains("HEAD^{commit}", run);
        Assert.Contains("new independent Factory run", run);
        Assert.Contains("dirty code/tests", run);
        Assert.Contains("Continuation never requires cleanliness", run);
        Assert.Contains("preparation/baseline.md", preflight);
        Assert.Contains("external-unknown", preflight);
    }

    [Fact]
    public void SavedBaselineAndAuthorizationAreSeparateFromWorkerRefinement()
    {
        var run = Canonical("skills", "idd-factory-run.md");
        var planner = Canonical("factory", "planner.md");
        var worker = Canonical("factory", "worker.md");
        Assert.Contains("effective normative contract", run);
        Assert.Contains("exact saved request/Q/A", run);
        Assert.Contains("preflight boundary", run);
        Assert.Contains("Unknown origin stays unknown", run);
        Assert.Contains("Effective approved contract and refinements", planner);
        Assert.Contains("Unattributed edits do not establish", worker);
        Assert.Contains("Absence of textual contradiction", Intent());
    }

    [Fact]
    public void WorkerCannotAlterProductDecisionsOrEngineeringRules()
    {
        var worker = Canonical("factory", "worker.md");
        var code = Canonical("skills", "idd-code-implement.md");
        var route = Canonical("skills", "idd-route.md");
        Assert.Contains("Monotonic Intent Refinement (Factory Worker only)", worker);
        Assert.Contains("Never edit `.idd/engineering/`", worker);
        Assert.Contains("Acceptance Criteria", worker);
        Assert.Contains("GLOSSARY.md", worker);
        Assert.Contains("user", worker);
        Assert.Contains("`intent-preserving`", route);
        Assert.Contains("preserve-current-intent", code);
        Assert.DoesNotContain("intent-preserving", code);
    }

    [Fact]
    public void ReviewCannotBlindlyRollbackAndDeferralIsNotActiveFactory()
    {
        var run = Canonical("skills", "idd-factory-run.md");
        Assert.Contains("Intent Refinement Review", run);
        Assert.Contains("git reset --hard", run);
        Assert.Contains("revert an unrelated commit", run);
        Assert.Contains(".idd/factory/results/", run);
        Assert.Contains("Deferred review never keeps Factory active", run);
    }

    [Fact]
    public void ExternalOriginalsAreNotParallelNormativeDocumentation()
    {
        var import = Canonical("skills", "idd-intent-import.md");
        var init = Canonical("skills", "idd-project-init.md");
        var methodology = Canonical("methodology", "intent-driven-development.md");
        Assert.Contains("External originals and generated sources", import);
        Assert.Contains("original", init);
        Assert.Contains("doc/", methodology);
        Assert.Contains("docs/", methodology);
        Assert.Contains("Do not create competing normative documentation", methodology);
    }
}
