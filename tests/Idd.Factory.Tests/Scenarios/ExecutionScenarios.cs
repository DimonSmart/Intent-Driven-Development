using Idd.Factory.Domain;

namespace Idd.Factory.Tests;

public sealed class ExecutionScenarios
{
    [Fact]
    public async Task ExecutesWholeBatchThenPlansAgain()
    {
        using var scenario = FactoryScenario.Create()
            .Plan("Implement A.", "Implement B.")
            .Execute("Implement A.", "Implemented A in the current product.")
            .Execute("Implement B.", "Implemented B and preserved surrounding behavior.")
            .Done();

        var result = await scenario.Run("Complete A and B.");

        result.ShouldComplete();
        result.ShouldExecute("Implement A.", "Implement B.");
        result.ShouldHavePlanningCycles(2);
    }

    [Fact]
    public async Task ExecutorDiscoveryWaitsForTheNextPlannerCycle()
    {
        using var scenario = FactoryScenario.Create()
            .Plan("Implement A.", "Implement B.")
            .Execute("Implement A.", "Implemented A. Additional work C is required.")
            .Execute("Implement B.")
            .Plan("Implement C discovered by the previous batch.")
            .Execute("Implement C discovered by the previous batch.")
            .Done();

        var result = await scenario.Run("Complete the integrated change.");

        result.ShouldComplete();
        result.ShouldExecute("Implement A.", "Implement B.", "Implement C discovered by the previous batch.");
        result.ShouldHavePlanningCycles(3);
    }

    [Theory]
    [InlineData(AgentTerminationKind.CommandTimeout)]
    [InlineData(AgentTerminationKind.IncompleteCommand)]
    public async Task CommandFailureRetriesTheSameTaskBeforeContinuing(AgentTerminationKind terminationKind)
    {
        using var scenario = FactoryScenario.Create()
            .Plan("Implement A.", "Implement B.")
            .CommandFailure(terminationKind, "item_3 dotnet test did not complete")
            .Execute("Implement A.", invocation =>
            {
                Assert.Contains("results are partial and must not be trusted", invocation.Input, StringComparison.Ordinal);
                Assert.Contains("item_3 dotnet test did not complete", invocation.Input, StringComparison.Ordinal);
                return "Diagnosed the hang and completed A.";
            })
            .Execute("Implement B.")
            .Done();

        var result = await scenario.Run("Complete A and B without trusting hung checks.");

        result.ShouldComplete();
        result.ShouldExecute("Implement A.", "Implement A.", "Implement B.");
        result.ShouldHaveAttemptCount("W000001", 2);
    }
}
