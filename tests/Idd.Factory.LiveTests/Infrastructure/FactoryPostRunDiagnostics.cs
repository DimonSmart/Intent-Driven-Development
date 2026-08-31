using Idd.Factory.LiveTests.Models;

namespace Idd.Factory.LiveTests.Infrastructure;

public static class FactoryPostRunDiagnostics
{
    public static void Assert(EvalAssertionCollector assertions, ExecutionResponseReadResult execution, FactoryResultReadResult factoryResult, string methodologyVersion)
    {
        if (!execution.IsSuccess)
        {
            assertions.Require(false, "Factory contract", "Execution response", execution.Error!);
            return;
        }

        if (execution.Response!.FactoryOutcome == "COMPLETED")
        {
            assertions.Require(factoryResult.IsSuccess, "Factory contract", "Factory result", factoryResult.Error ?? "factory-result.json is unavailable.");
            if (factoryResult.IsSuccess) AssertFactoryResult(assertions, factoryResult.Result!, methodologyVersion);
            return;
        }

        assertions.Require(false, "Factory execution", "Factory outcome", $"TwoStepCatalog requires COMPLETED, but Factory stopped with {execution.Response.FactoryOutcome}: {execution.Response.Reason}");
        var unexpectedSuccessfulResult = factoryResult.IsSuccess && factoryResult.Result!.String("factoryOutcome") == "COMPLETED";
        assertions.Require(!unexpectedSuccessfulResult, "Factory contract", "Unexpected Factory result", "A non-success Factory outcome must not produce a successful factory-result.json.");
    }

    private static void AssertFactoryResult(EvalAssertionCollector assertions, FactoryResult result, string version)
    {
        assertions.Require(result.String("methodologyVersion") == version, "Version", "Factory methodology version", $"Expected factory-result.json methodologyVersion '{version}', but it reports '{result.String("methodologyVersion") ?? "missing"}'.");
        var commitPath = result.String("commitMessagePath");
        var workspace = new DirectoryInfo(Path.GetDirectoryName(result.Path)!).Parent!.Parent!.Parent!.Parent!.FullName;
        assertions.Require(!string.IsNullOrWhiteSpace(commitPath) && File.Exists(Path.Combine(workspace, commitPath.Replace('/', Path.DirectorySeparatorChar))), "Factory contract", "Commit message path", $"Expected factory-result.json commitMessagePath to point to an existing file, but it reports '{commitPath ?? "missing"}'.");
        assertions.Require(result.Int("completedWorkCount") is >= 2, "Factory contract", "completedWorkCount", $"Expected at least two completed ordered work items, but factory-result.json reports '{result.Int("completedWorkCount")?.ToString() ?? "missing"}'.");
        foreach (var (name, expected) in new (string, object)[] { ("factoryOutcome", "COMPLETED"), ("finalReviewVerdict", "approved"), ("verificationStatus", "passed") })
        {
            object? actual = expected is int ? result.Int(name) : result.String(name);
            assertions.Require(Equals(actual, expected), "Factory contract", name, $"Expected Factory {name} to be '{expected}', but factory-result.json reports '{actual ?? "missing"}'.");
        }
    }

    public static string Outcome(bool productPassed, bool factoryPassed, bool infrastructurePassed = true) => !infrastructurePassed ? "INFRASTRUCTURE_FAILURE" : productPassed && factoryPassed ? "PASSED" : productPassed ? "FACTORY_FAILURE" : factoryPassed ? "PRODUCT_FAILURE" : "PRODUCT_AND_FACTORY_FAILURE";
}
