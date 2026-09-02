namespace Idd.Factory.Tests;

public sealed class IntentPreflightContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void FactoryLauncherPackagesIntentPreflightReference()
    {
        var manifest = Read("src/canonical/plugins/plugin-manifest.json");
        var launcher = Read("src/canonical/skills/idd-factory-run.md");

        Assert.Contains("src/canonical/methodology/intent-preflight.md", manifest, StringComparison.Ordinal);
        Assert.Contains("references/intent-preflight.md", launcher, StringComparison.Ordinal);
        Assert.Contains("ExplicitIntentChange", launcher, StringComparison.Ordinal);
        Assert.Contains("MissingIntentDecision", launcher, StringComparison.Ordinal);
    }

    [Fact]
    public void PreflightPreservesRuntimeAndIntentBoundaries()
    {
        var contract = Read("src/canonical/methodology/intent-preflight.md");

        Assert.Contains("original user request remains authoritative", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Missing documentation", contract, StringComparison.Ordinal);
        Assert.Contains("Do not call the runtime", Read("src/canonical/skills/idd-factory-run.md"), StringComparison.Ordinal);
        Assert.Contains("Factory implementation and research workers never edit intent", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void FactoryPlanningDoesNotRematerializeIntentPreflightWork()
    {
        var decomposer = Read("src/canonical/skills/idd-factory-decompose-task.md");
        var planningRuntime = Read("src/runtime/Idd.Factory/Runtime/FactoryRuntime.Planning.cs");

        Assert.Contains("Durable intent is read-only", decomposer, StringComparison.Ordinal);
        Assert.Contains("must not be materialized", decomposer, StringComparison.Ordinal);
        Assert.Contains("rather than returning an intent-editing task", decomposer, StringComparison.Ordinal);
        Assert.Contains("Factory planning boundary", planningRuntime, StringComparison.Ordinal);
        Assert.Contains("Durable intent is read-only Factory input and never remaining Factory work", planningRuntime, StringComparison.Ordinal);
        Assert.Contains("not a Factory task", planningRuntime, StringComparison.Ordinal);
    }

    [Fact]
    public void FocusedEvalSuiteCoversPositiveAndNegativeRelations()
    {
        var cases = Read("evals/idd-factory-intent-preflight/cases.yaml");

        Assert.Contains("expected_relation: ExplicitIntentChange", cases, StringComparison.Ordinal);
        Assert.Contains("expected_relation: Covered", cases, StringComparison.Ordinal);
        Assert.Contains("expected_relation: MissingIntentDecision", cases, StringComparison.Ordinal);
        Assert.Contains("implementation-only-forbids-intent-write", cases, StringComparison.Ordinal);
        Assert.Contains("technical-research-only", cases, StringComparison.Ordinal);
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Intent-Driven-Development.slnx"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
