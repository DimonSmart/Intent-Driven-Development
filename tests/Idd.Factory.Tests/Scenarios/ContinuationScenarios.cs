using Idd.Factory.Domain;

namespace Idd.Factory.Tests;

public sealed class ContinuationScenarios
{
    [Fact]
    public async Task PlannerQuestionPausesUntilAnAnswerResumesTheSameRun()
    {
        const string question = "Should deletion be automatic or require explicit confirmation?";
        const string answer = "Require explicit confirmation.";
        const string task = "Implement deletion with explicit confirmation.";
        using var scenario = FactoryScenario.Create()
            .Question(question)
            .Planner(invocation =>
            {
                Assert.Contains($"# Answer\n\n{answer}", invocation.Input, StringComparison.Ordinal);
                return $"# Task\n\n{task}";
            })
            .Execute(task)
            .Done();

        var paused = await scenario.Run("Implement deletion behavior.");
        paused.ShouldBeBlockedBy("USER_DECISION_REQUIRED");
        paused.ShouldHaveContinuation(resumable: true);
        paused.ShouldHavePlanningCycles(1);

        var stillPaused = await scenario.Continue();
        stillPaused.ShouldBeBlockedBy("USER_DECISION_REQUIRED");
        stillPaused.ShouldHavePlanningCycles(1);

        var completed = await scenario.Continue(userAnswer: answer);
        completed.ShouldComplete();
        completed.ShouldExecute(task);
        completed.ShouldHavePlanningCycles(3);
        Assert.Contains(answer, completed.ReadArtifact("planning-answers/Q000001.md"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplicitRetryExtendsOnlyTheExhaustedWorkItem()
    {
        const string task = "Implement the requested change.";
        using var scenario = FactoryScenario.Create()
            .WithVerificationCheck("always-fail", "exit 1")
            .Plan(task);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var capturedAttempt = attempt;
            scenario.Execute(task, _ =>
            {
                File.WriteAllText(Path.Combine(scenario.WorkspacePath, $"change-{capturedAttempt}.txt"), "changed");
                return $"Attempt {capturedAttempt} changed the workspace.";
            });
        }

        var exhausted = await scenario.Run();
        exhausted.ShouldBeBlockedBy("RETRY_BUDGET_EXHAUSTED");
        exhausted.ShouldHaveAttemptCount("W000001", 4);
        Assert.Equal(0, exhausted.State.Current!.AdditionalAttemptBudget);

        var retried = await scenario.RetryExhausted(1);
        retried.ShouldBeBlockedBy("RETRY_BUDGET_EXHAUSTED");
        retried.ShouldHaveAttemptCount("W000001", 5);
        Assert.Equal(1, retried.State.Current!.AdditionalAttemptBudget);
        retried.ShouldHaveContinuation(resumable: false);
    }
}
