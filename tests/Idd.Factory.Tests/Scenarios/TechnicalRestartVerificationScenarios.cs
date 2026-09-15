using Idd.Factory.Domain;

namespace Idd.Factory.Tests;

public sealed class TechnicalRestartVerificationScenarios
{
    [Fact]
    public async Task TechnicalRestartInsideVerificationDrivenSemanticRetryPreservesNoProgressRule()
    {
        const string task = "Implement A.";
        using var scenario = FactoryScenario.Create();
        scenario.WithVerificationCheck("subtask-fail", "exit 7")
            .Plan(task)
            .Execute(task, invocation =>
            {
                File.WriteAllText(Path.Combine(invocation.Workspace, "first-change.txt"), "changed");
                return "Initial implementation changed the workspace.";
            })
            .CommandFailure(AgentTerminationKind.CommandTimeout, "semantic retry executor timed out")
            .Execute(task, invocation =>
            {
                Assert.Equal(WorkItemInvocationKind.TechnicalRestart, invocation.InvocationKind);
                Assert.Equal(2, invocation.SemanticAttemptNumber);
                Assert.Equal(1, invocation.TechnicalRestartNumber);
                return "Technical restart completed without additional workspace changes.";
            });

        var result = await scenario.Run("Implement A and verify it.");

        result.ShouldBeBlockedBy("VERIFICATION_RETRY_NO_PROGRESS");
        Assert.Equal(2, result.State.Current!.SemanticAttemptCount);
        Assert.Equal(1, result.State.Current.TechnicalRestartCount);
        Assert.Equal(3, result.Invocations.Count(x => x.WorkItemId == "W000001"));
    }
}
