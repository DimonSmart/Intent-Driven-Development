using System.Text.Json;
using Idd.Factory.Agents;
using Idd.Factory.Domain;
using Idd.Factory.Runtime;
using Idd.Factory.State;

namespace Idd.Factory.Tests;

public sealed class RelevantCompletedWorkTests
{
    private readonly PlannerMarkdownParser parser = new();

    [Fact]
    public void ParserAllowsTaskWithoutRelevantCompletedWork()
    {
        var task = Assert.Single(parser.Parse("# Task\nImplement A.\n").Tasks);

        Assert.Empty(task.RelevantCompletedWorkIds);
    }

    [Fact]
    public void ParserAllowsRelevantCompletedWorkWithoutTaskRelatedIntent()
    {
        var task = Assert.Single(parser.Parse(
            "# Task\nImplement B.\n# RelevantCompletedWork\nW000002\nW000004\n").Tasks);

        Assert.Equal(["W000002", "W000004"], task.RelevantCompletedWorkIds);
        Assert.Empty(task.TaskRelatedIntentIds);
    }

    [Fact]
    public void ParserAllowsTaskRelatedIntentBeforeRelevantCompletedWork()
    {
        var task = Assert.Single(parser.Parse(
            "# Task\nImplement B.\n" +
            "# TaskRelatedIntent\nIDD-0012\n" +
            "# RelevantCompletedWork\nW000002\nW000004\n").Tasks);

        Assert.Equal(["IDD-0012"], task.TaskRelatedIntentIds);
        Assert.Equal(["W000002", "W000004"], task.RelevantCompletedWorkIds);
    }

    [Theory]
    [InlineData("# Task\nA\n# RelevantCompletedWork\n")]
    [InlineData("# Task\nA\n# RelevantCompletedWork\nW000001\nW000001")]
    [InlineData("# Task\nA\n# RelevantCompletedWork\n- W000001")]
    [InlineData("# Task\nA\n# RelevantCompletedWork\nW000001, W000002")]
    [InlineData("# Task\nA\n# RelevantCompletedWork\nW1")]
    [InlineData("# Task\nA\n# RelevantCompletedWork\nW000000")]
    [InlineData("# RelevantCompletedWork\nW000001")]
    [InlineData("# Task\nA\n# RelevantCompletedWork\nW000001\n# TaskRelatedIntent\nIDD-0001")]
    [InlineData("# Task\nA\n# RelevantCompletedWork\nW000001\n# RelevantCompletedWork\nW000002")]
    [InlineData("# Question\nA?\n# RelevantCompletedWork\nW000001")]
    [InlineData("# Done\n# RelevantCompletedWork\nW000001")]
    public void ParserRejectsMalformedRelevantCompletedWorkProtocol(string output)
    {
        var error = Assert.Throws<AgentProtocolException>(() => parser.Parse(output));

        Assert.Equal("MALFORMED_PLANNER_OUTPUT", error.Code);
    }

    [Fact]
    public async Task ReferenceMustAlreadyBeCompletedBeforeBatchMaterialization()
    {
        using var scenario = FactoryScenario.Create()
            .Planner("# Task\nImplement A.\n# RelevantCompletedWork\nW000001\n");

        var result = await scenario.Run();

        result.ShouldStopWith("MALFORMED_PLANNER_OUTPUT");
        Assert.Null(result.State.Current);
        Assert.Empty(result.State.Remaining);
        Assert.Empty(result.State.Completed);
        Assert.Equal(1, result.State.NextWorkItemNumber);
        var workItemsDirectory = Path.Combine(result.RunDirectory, "work-items");
        Assert.False(Directory.Exists(workItemsDirectory)
                     && Directory.EnumerateFiles(workItemsDirectory, "contract.md", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task ExecutorReceivesOnlyPlannerSelectedSemanticResultsInPlannerOrder()
    {
        const string taskA = "Implement A.";
        const string taskB = "Implement B.";
        const string taskC = "Implement C.";
        using var scenario = FactoryScenario.Create()
            .Plan(taskA, taskB)
            .Execute(taskA, "RESULT-A")
            .Execute(taskB, "RESULT-B")
            .Planner(invocation =>
            {
                Assert.Contains("Completed immutable work:\n## W000001", invocation.Input, StringComparison.Ordinal);
                Assert.Contains("## W000002", invocation.Input, StringComparison.Ordinal);
                Assert.Contains("Task contract:\nImplement A.", invocation.Input, StringComparison.Ordinal);
                return "# Task\nImplement C.\n# RelevantCompletedWork\nW000002\nW000001\n";
            })
            .Execute(taskC, invocation =>
            {
                Assert.Contains(
                    "Relevant completed work and results:\n## W000002\nSemantic result:\nRESULT-B\n## W000001\nSemantic result:\nRESULT-A",
                    invocation.Input,
                    StringComparison.Ordinal);
                Assert.DoesNotContain("Task contract:\nImplement A.", invocation.Input, StringComparison.Ordinal);
                Assert.DoesNotContain("Task contract:\nImplement B.", invocation.Input, StringComparison.Ordinal);
                Assert.DoesNotContain("Actual changed paths:", invocation.Input, StringComparison.Ordinal);
                return "RESULT-C";
            })
            .Done();

        var result = await scenario.Run();

        result.ShouldComplete();
        Assert.Equal(["W000002", "W000001"], result.State.Completed[2].RelevantCompletedWorkIds);
    }

    [Fact]
    public async Task EmptySelectionKeepsExecutorCompletedContextConstantAfterLongHistory()
    {
        const int historyCount = 12;
        var tasks = Enumerable.Range(1, historyCount)
            .Select(index => $"Implement history item {index}.")
            .ToArray();
        using var scenario = FactoryScenario.Create()
            .Plan(tasks);
        for (var index = 0; index < tasks.Length; index++)
            scenario.Execute(tasks[index], $"HISTORY-RESULT-{index + 1}");

        scenario
            .Planner("# Task\nImplement independent final item.\n")
            .Execute("Implement independent final item.", invocation =>
            {
                Assert.Contains("Relevant completed work and results:\nnone", invocation.Input, StringComparison.Ordinal);
                for (var index = 1; index <= historyCount; index++)
                    Assert.DoesNotContain($"HISTORY-RESULT-{index}", invocation.Input, StringComparison.Ordinal);
                return "FINAL-RESULT";
            })
            .Done();

        var result = await scenario.Run();

        result.ShouldComplete();
        Assert.Empty(result.State.Completed[^1].RelevantCompletedWorkIds);
    }

    [Fact]
    public async Task RetryPreservesRelevantCompletedWorkSelectionAndOwnRetryContextRemainsSeparate()
    {
        const string prerequisite = "Implement prerequisite.";
        const string dependent = "Implement dependent task.";
        using var scenario = FactoryScenario.Create()
            .Plan(prerequisite)
            .Execute(prerequisite, "PREREQUISITE-RESULT")
            .Planner("# Task\nImplement dependent task.\n# RelevantCompletedWork\nW000001\n")
            .CommandFailure(
                AgentTerminationKind.CommandTimeout,
                "executor timed out",
                invocation => Assert.Contains(
                    "Relevant completed work and results:\n## W000001\nSemantic result:\nPREREQUISITE-RESULT",
                    invocation.Input,
                    StringComparison.Ordinal))
            .Execute(dependent, invocation =>
            {
                Assert.Contains(
                    "Relevant completed work and results:\n## W000001\nSemantic result:\nPREREQUISITE-RESULT",
                    invocation.Input,
                    StringComparison.Ordinal);
                Assert.Contains("Previous technical execution failures for this task:", invocation.Input, StringComparison.Ordinal);
                Assert.Contains("executor timed out", invocation.Input, StringComparison.Ordinal);
                return "DEPENDENT-RESULT";
            })
            .Done();

        var result = await scenario.Run();

        result.ShouldComplete();
        Assert.Equal(["W000001"], result.State.Completed[^1].RelevantCompletedWorkIds);
        result.ShouldHaveAttemptCount("W000002", 2);
    }

    [Fact]
    public void StateValidatorRejectsUnknownRelevantCompletedReferenceAndMutationOfSelection()
    {
        Assert.Equal(16, FactoryState.CurrentSchemaVersion);
        var validator = new FactoryStateValidator();
        var state = StateStoreTests.State();
        state.Completed.Add(StateStoreTests.Completed("W000001"));
        state.Current = new PlannedWorkItem
        {
            Id = "W000002",
            ContractPath = "work-items/W000002/contract.md",
            RelevantCompletedWorkIds = ["W999999"]
        };
        state.CurrentPhase = CurrentWorkPhase.Ready;

        Assert.Equal("CORRUPT_FACTORY_STATE", Assert.Throws<FactoryStateException>(() => validator.Validate(state)).Code);

        state.Current.RelevantCompletedWorkIds.Clear();
        state.Current.RelevantCompletedWorkIds.Add("W000001");
        validator.Validate(state);
        var next = JsonSerializer.Deserialize<FactoryState>(JsonSerializer.Serialize(state, FactoryJson.Options), FactoryJson.Options)!;
        next.Revision = state.Revision + 1;
        next.Current!.RelevantCompletedWorkIds.Clear();

        Assert.Equal("CORRUPT_FACTORY_STATE", Assert.Throws<FactoryStateException>(() => validator.ValidateMutation(state, next)).Code);
    }
}
