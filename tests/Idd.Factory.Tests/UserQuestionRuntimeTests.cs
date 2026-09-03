using Idd.Factory.Domain;

namespace Idd.Factory.Tests;

public sealed class UserQuestionRuntimeTests
{
    [Fact]
    public async Task PlannerQuestionPausesAndExactUserAnswerResumesSameRun()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(_ => "# Question\n\nShould deletion be automatic or require explicit confirmation?");
        backend.Enqueue(invocation =>
        {
            Assert.Contains("# Answer\n\nRequire explicit confirmation.", invocation.Input, StringComparison.Ordinal);
            return "# Task\n\nImplement deletion with explicit confirmation.";
        });
        backend.Enqueue(_ => "Implemented the confirmed deletion behavior.");
        backend.Enqueue(_ => "");
        var runtime = FactoryRuntimeTestHarness.CreateRuntime(temp.Path, backend);

        var paused = await runtime.RunRequestAsync("Implement deletion behavior.", "test", default);

        Assert.Equal("USER_DECISION_REQUIRED", paused.FactoryOutcome);
        Assert.Equal("Should deletion be automatic or require explicit confirmation?", paused.Reason);
        var pausedState = await FactoryRuntimeTestHarness.LoadState(temp.Path);
        Assert.Equal(FactoryRunStatus.Blocked, pausedState.RunStatus);
        Assert.Equal(ContinuationKind.UserQuestion, pausedState.PendingContinuation!.Kind);
        Assert.True(pausedState.PendingContinuation.IsResumable);
        Assert.Empty(pausedState.Completed);
        Assert.Empty(pausedState.Remaining);

        var stillPaused = await runtime.ContinueAsync(default);
        Assert.Equal("USER_DECISION_REQUIRED", stillPaused.FactoryOutcome);
        Assert.Single(backend.Invocations);

        var completed = await runtime.ContinueAsync(default, userAnswer: "Require explicit confirmation.");

        Assert.Equal("COMPLETED", completed.FactoryOutcome);
        Assert.Equal(["planning", "planning", "implementation", "planning"], backend.Invocations.Select(x => x.Capability));
        var answer = await File.ReadAllTextAsync(Path.Combine(completed.ResultDirectory!, "planning-answers", "Q000001.md"));
        Assert.Contains("# Question\n\nShould deletion be automatic or require explicit confirmation?", answer, StringComparison.Ordinal);
        Assert.Contains("# Answer\n\nRequire explicit confirmation.", answer, StringComparison.Ordinal);
    }
}
