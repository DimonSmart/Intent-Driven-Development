namespace Idd.Factory.Tests;

public sealed class WorkflowOwnershipTests
{
    private static readonly string[] StateLightRuntimeFiles =
    [
        "PlanningService.cs",
        "PlanMutationService.cs",
        "ExecutionService.cs",
        "SemanticExecutionService.cs",
        "SemanticAttemptReconciler.cs",
        "RuntimeVerificationService.cs",
        "RuntimeVerificationService.Run.cs",
        "RuntimeVerificationService.Pending.cs",
        "FactoryStopService.cs"
    ];

    private static readonly string[] ForbiddenWorkflowMutations =
    [
        "state.RunStatus =",
        "state.Blocker =",
        "state.PendingContinuation =",
        "state.PendingVerificationSession =",
        "state.CurrentPhase =",
        "state.CurrentAttemptId =",
        "state.Remaining.Clear(",
        "state.Remaining.Add",
        "state.Completed.Add"
    ];

    [Fact]
    public void OperationServicesDoNotPersistOrOwnWorkflowTransitions()
    {
        foreach (var file in StateLightRuntimeFiles)
        {
            var source = ReadRuntimeSource(file);
            Assert.DoesNotContain("context.SaveAsync(", source, StringComparison.Ordinal);
            foreach (var mutation in ForbiddenWorkflowMutations)
                Assert.DoesNotContain(mutation, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FactoryRunServiceDoesNotOwnWorkflowTransitions()
    {
        var source = ReadRuntimeSource("FactoryRunService.cs");
        foreach (var mutation in ForbiddenWorkflowMutations)
            Assert.DoesNotContain(mutation, source, StringComparison.Ordinal);
    }

    private static string ReadRuntimeSource(string fileName) =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "runtime",
            "Idd.Factory",
            "Runtime",
            fileName));

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(
                    directory.FullName,
                    "src",
                    "runtime",
                    "Idd.Factory")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
