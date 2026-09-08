using Idd.Factory.Domain;
using Idd.Factory.Runtime;

namespace Idd.Factory.Tests;

public sealed class RetryExhaustedRuntimeTests
{
    [Fact]
    public async Task ExplicitRetryExtendsOnlyTheExhaustedCurrentWorkItemAndPreservesPriorAttempts()
    {
        using var temp = new TestWorkspace();
        temp.Write(".idd/verification.yaml", """
            version: 1
            checks:
              always-fail:
                run: exit 1
            default:
              use: []
            subtask:
              use:
                - always-fail
            final:
              use: []
            """);
        var backend = new FakeAgentBackend();
        backend.Enqueue(_ => "# Task\n\nImplement the requested change.");
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var capturedAttempt = attempt;
            backend.Enqueue(_ =>
            {
                File.WriteAllText(Path.Combine(temp.Path, $"change-{capturedAttempt}.txt"), "changed");
                return $"Attempt {capturedAttempt} changed the workspace.";
            });
        }

        var runtime = FactoryRuntimeTestHarness.CreateRuntime(temp.Path, backend);
        var exhausted = await runtime.RunRequestAsync("Implement the requested change.", "test", default);

        Assert.Equal("RETRY_BUDGET_EXHAUSTED", exhausted.FactoryOutcome);
        Assert.Contains("factory_retry", exhausted.ResumeWhen, StringComparison.Ordinal);
        var beforeRetry = await FactoryRuntimeTestHarness.LoadState(temp.Path);
        Assert.Equal(4, beforeRetry.Current!.AttemptCount);
        Assert.Equal(0, beforeRetry.Current.AdditionalAttemptBudget);
        var attemptsDirectory = Path.Combine(temp.Path, ".idd", "factory", "current", "attempts");
        Assert.Equal(5, Directory.EnumerateDirectories(attemptsDirectory).Count());

        var exhaustedAgain = await runtime.RetryExhaustedAsync(1, default);

        Assert.Equal("RETRY_BUDGET_EXHAUSTED", exhaustedAgain.FactoryOutcome);
        var afterRetry = await FactoryRuntimeTestHarness.LoadState(temp.Path);
        Assert.Equal(5, afterRetry.Current!.AttemptCount);
        Assert.Equal(1, afterRetry.Current.AdditionalAttemptBudget);
        Assert.Equal("RETRY_BUDGET_EXHAUSTED", afterRetry.Blocker!.Code);
        Assert.False(afterRetry.PendingContinuation!.IsResumable);
        Assert.Equal(6, Directory.EnumerateDirectories(attemptsDirectory).Count());
    }

}
