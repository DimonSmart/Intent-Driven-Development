using System.Text.Json;
using Idd.Factory.Agents;
using Idd.Factory.Configuration;
using Idd.Factory.Domain;
using Idd.Factory.Runtime;

namespace Idd.Factory.Tests;

public sealed class BatchProtocolTests
{
    private readonly PlannerMarkdownParser parser = new();

    [Fact]
    public void PlannerMarkdownMaterializesOrderedTasks()
    {
        var plan = parser.Parse("# Task\n\nImplement A.\n\n# Task\n\nImplement B.\nPreserve C.\n");
        Assert.Equal(["Implement A.", "Implement B.\nPreserve C."], plan);
        Assert.Null(plan.Question);
    }

    [Fact]
    public void PlannerMayReturnOneUserQuestionWhenNoTaskCanBeContracted()
    {
        var plan = parser.Parse("# Question\n\nShould deletion be automatic or require confirmation?\n");
        Assert.Empty(plan);
        Assert.Equal("Should deletion be automatic or require confirmation?", plan.Question);
    }

    [Fact]
    public void PlannerDoneMeansNoRemainingWork()
    {
        var plan = parser.Parse("# Done\n");
        Assert.Empty(plan);
        Assert.Null(plan.Question);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\n\n")]
    public void BlankPlannerOutputIsRejected(string output) =>
        Assert.Equal("MALFORMED_PLANNER_OUTPUT", Assert.Throws<AgentProtocolException>(() => parser.Parse(output)).Code);

    [Fact]
    public void PlannerDoneWithBodyIsRejected()
    {
        var error = Assert.Throws<AgentProtocolException>(() => parser.Parse("# Done\n\nEverything is complete."));
        Assert.Equal("MALFORMED_PLANNER_OUTPUT", error.Code);
    }

    [Fact]
    public void PlannerTaskAndDoneCannotBeMixed()
    {
        var error = Assert.Throws<AgentProtocolException>(() => parser.Parse("# Task\n\nImplement A.\n\n# Done\n"));
        Assert.Equal("MALFORMED_PLANNER_OUTPUT", error.Code);
    }

    [Fact]
    public void PlannerQuestionAndDoneCannotBeMixed()
    {
        var error = Assert.Throws<AgentProtocolException>(() => parser.Parse("# Question\n\nChoose A or B?\n\n# Done\n"));
        Assert.Equal("MALFORMED_PLANNER_OUTPUT", error.Code);
    }

    [Fact]
    public void PlannerMayReturnOnlyOneDoneMarker()
    {
        var error = Assert.Throws<AgentProtocolException>(() => parser.Parse("# Done\n\n# Done\n"));
        Assert.Equal("MALFORMED_PLANNER_OUTPUT", error.Code);
    }

    [Fact]
    public void TaskHeadingInsideFencedCodeIsTaskContentNotAProtocolBoundary()
    {
        var plan = parser.Parse(
            "# Task\n\nDocument the planner protocol.\n\n```markdown\n# Task\nThis is an example only.\n```\n\nKeep the example intact.\n\n# Task\n\nImplement the parser.");

        Assert.Equal(2, plan.Count);
        Assert.Contains("```markdown\n# Task\nThis is an example only.\n```", plan[0], StringComparison.Ordinal);
        Assert.Contains("Keep the example intact.", plan[0], StringComparison.Ordinal);
        Assert.Equal("Implement the parser.", plan[1]);
    }

    [Fact]
    public void DoneHeadingInsideFencedCodeIsTaskContentNotAProtocolBoundary()
    {
        var plan = parser.Parse("# Task\n\nDocument this example:\n\n```markdown\n# Done\n```\n");

        Assert.Single(plan);
        Assert.Null(plan.Question);
        Assert.Contains("```markdown\n# Done\n```", plan[0], StringComparison.Ordinal);
    }

    [Fact]
    public void QuestionHeadingInsideBlockQuoteIsTaskContentNotAProtocolBoundary()
    {
        var plan = parser.Parse("# Task\n\nPreserve this example:\n\n> # Question\n> Example only.\n");

        Assert.Single(plan);
        Assert.Null(plan.Question);
        Assert.Contains("> # Question", plan[0], StringComparison.Ordinal);
    }

    [Fact]
    public void DoneHeadingInsideBlockQuoteIsTaskContentNotAProtocolBoundary()
    {
        var plan = parser.Parse("# Task\n\nPreserve this example:\n\n> # Done\n");

        Assert.Single(plan);
        Assert.Null(plan.Question);
        Assert.Contains("> # Done", plan[0], StringComparison.Ordinal);
    }

    [Fact]
    public void DoneAllowsBomAndLineEndingNormalization()
    {
        var plan = parser.Parse("\uFEFF# Done\r\n\r\n");
        Assert.Empty(plan);
        Assert.Null(plan.Question);
    }

    [Theory]
    [InlineData("Explanation only")]
    [InlineData("# Task\n\n")]
    [InlineData("# Task\nA\n# Task\n")]
    [InlineData("# Question\nA?\n# Question\nB?")]
    [InlineData("# Task\nA\n# Question\nB?")]
    [InlineData("Task\n====\n\nImplement A.")]
    [InlineData("# Task #\n\nImplement A.")]
    [InlineData("Done")]
    [InlineData("## Done")]
    [InlineData("# done")]
    [InlineData("# DONE")]
    [InlineData("# Done #")]
    [InlineData("Done\n====")]
    public void MalformedPlannerOutputIsRejected(string output) =>
        Assert.Equal("MALFORMED_PLANNER_OUTPUT", Assert.Throws<AgentProtocolException>(() => parser.Parse(output)).Code);

    [Fact]
    public async Task ExecutorResultIsFreeFormTextAndMetadataOnlyReferencesIt()
    {
        using var temp = new TestWorkspace();
        var output = Path.Combine(temp.Path, ".idd", "factory", "current", "attempts", "A000001", "semantic-result.md");
        var backend = new FakeAgentBackend();
        backend.Enqueue(_ => "Changed the renderer.\n\nA platform constraint remains visible to the next planner.");
        var invocation = Invocation(temp.Path, output);

        var execution = await new FactoryAgentExecutor(backend).ExecuteAsync(invocation, default);

        Assert.Contains("platform constraint", execution.Result.SemanticResult);
        using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(output)!, "result.json")));
        Assert.Equal("attempts/A000001/semantic-result.md", metadata.RootElement.GetProperty("semanticResultPath").GetString());
        Assert.False(metadata.RootElement.TryGetProperty("semanticResult", out _));
        Assert.False(metadata.RootElement.TryGetProperty("outcome", out _));
    }

    [Fact]
    public async Task EmptyExecutorResultIsRejected()
    {
        using var temp = new TestWorkspace();
        var output = Path.Combine(temp.Path, ".idd", "factory", "current", "attempts", "A000001", "semantic-result.md");
        var backend = new FakeAgentBackend();
        backend.Enqueue(_ => "");
        var error = await Assert.ThrowsAsync<AgentProtocolException>(() => new FactoryAgentExecutor(backend).ExecuteAsync(Invocation(temp.Path, output), default));
        Assert.Equal("MALFORMED_AGENT_RESULT", error.Code);
    }

    [Fact]
    public void SchedulerPlansAfterEveryExhaustedBatchAndAfterFinalFailure()
    {
        var scheduler = new FactoryScheduler();
        var state = StateStoreTests.State();
        Assert.Equal(FactoryCommandKind.Plan, scheduler.Decide(state).Kind);

        state.PlanningCycleCount = 1;
        state.Completed.Add(StateStoreTests.Completed("W000001"));
        Assert.Equal(FactoryCommandKind.Plan, scheduler.Decide(state).Kind);

        state.PlannedThroughCompletedCount = 1;
        state.PlanRevision = 2;
        Assert.Equal(FactoryCommandKind.RunFinalVerification, scheduler.Decide(state).Kind);

        state.FinalVerificationPlanRevision = 2;
        state.FinalVerificationPassed = false;
        Assert.Equal(FactoryCommandKind.Plan, scheduler.Decide(state).Kind);

        state.FinalVerificationPassed = true;
        Assert.Equal(FactoryCommandKind.Finalize, scheduler.Decide(state).Kind);
    }

    [Fact]
    public void ConfigurationContainsOnlySemanticNeutralBudgets()
    {
        var configuration = FactoryRuntimeTestHarness.CreateConfiguration();
        Assert.Equal(2, configuration.SchemaVersion);
        Assert.Equal(4, configuration.Limits.MaxAttemptsPerTask);
        Assert.Equal(12, configuration.Limits.MaxPlanningCycles);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(configuration, FactoryJson.Options));
        Assert.False(json.RootElement.TryGetProperty("finalReview", out _));
        Assert.False(json.RootElement.TryGetProperty("allowedCapabilities", out _));
    }

    private static AgentInvocation Invocation(string workspace, string output) => new()
    {
        RunId = "run",
        AttemptId = "A000001",
        Capability = "implementation",
        Role = "executor",
        WorkItemId = "W000001",
        Workspace = workspace,
        SemanticOutputPath = output,
        SkillName = "idd-factory-execute-subtask",
        ExecutionProfile = AgentExecutionProfile.WorkspaceWrite,
        Input = "Execute the task.",
        StartedAt = DateTimeOffset.UtcNow
    };
}
