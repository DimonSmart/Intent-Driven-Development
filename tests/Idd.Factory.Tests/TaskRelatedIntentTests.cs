using Idd.Factory.Agents;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.Runtime;
using Idd.Factory.State;

namespace Idd.Factory.Tests;

public sealed class TaskRelatedIntentTests
{
    private readonly PlannerMarkdownParser parser = new();

    [Fact]
    public void ParserSeparatesOneRelatedIntentFromContract()
    {
        var plan = parser.Parse("# Task\nImplement settings.\n# TaskRelatedIntent\nIDD-0012\n");

        var task = Assert.Single(plan.Tasks);
        Assert.Equal("Implement settings.", task.Contract);
        Assert.Equal(["IDD-0012"], task.TaskRelatedIntentIds);
    }

    [Fact]
    public void ParserPreservesMultipleIntentIdsAndPerTaskSelectionOrder()
    {
        var plan = parser.Parse(
            "# Task\nImplement A.\n# TaskRelatedIntent\nIDD-0012\nIDD-0027\n\n" +
            "# Task\nImplement B.\n# TaskRelatedIntent\nIDD-0003\n");

        Assert.Equal(2, plan.Tasks.Count);
        Assert.Equal(["IDD-0012", "IDD-0027"], plan.Tasks[0].TaskRelatedIntentIds);
        Assert.Equal(["IDD-0003"], plan.Tasks[1].TaskRelatedIntentIds);
    }

    [Fact]
    public void ParserAllowsTaskWithoutRelatedIntent()
    {
        var task = Assert.Single(parser.Parse("# Task\nRename local helper.\n").Tasks);
        Assert.Empty(task.TaskRelatedIntentIds);
    }

    [Theory]
    [InlineData("# Task\nA\n# TaskRelatedIntent\nIDD-0001\nIDD-0001")]
    [InlineData("# Task\nA\n# TaskRelatedIntent\n0001")]
    [InlineData("# Task\nA\n# TaskRelatedIntent\nIDD-1")]
    [InlineData("# Task\nA\n# TaskRelatedIntent\n- IDD-0001")]
    [InlineData("# Task\nA\n# TaskRelatedIntent\nIDD-0001, IDD-0002")]
    [InlineData("# TaskRelatedIntent\nIDD-0001")]
    [InlineData("# Task\nA\n# TaskRelatedIntent\nIDD-0001\n# TaskRelatedIntent\nIDD-0002")]
    [InlineData("# Question\nA?\n# TaskRelatedIntent\nIDD-0001")]
    [InlineData("# Done\n# TaskRelatedIntent\nIDD-0001")]
    public void ParserRejectsMalformedRelatedIntentProtocol(string output)
    {
        var error = Assert.Throws<AgentProtocolException>(() => parser.Parse(output));
        Assert.Equal("MALFORMED_PLANNER_OUTPUT", error.Code);
    }

    [Fact]
    public void RelatedIntentHeadingInsideFencedCodeRemainsTaskContent()
    {
        var task = Assert.Single(parser.Parse(
            "# Task\nDocument this example:\n```markdown\n# TaskRelatedIntent\nIDD-0001\n```\nKeep it.\n").Tasks);

        Assert.Empty(task.TaskRelatedIntentIds);
        Assert.Contains("# TaskRelatedIntent\nIDD-0001", task.Contract, StringComparison.Ordinal);
    }

    [Fact]
    public void ParserNormalizesBomAndLineEndingsWithRelatedIntent()
    {
        var task = Assert.Single(parser.Parse(
            "\uFEFF# Task\r\nImplement A.\r\n# TaskRelatedIntent\r\nIDD-0002\r\nIDD-0001\r\n").Tasks);

        Assert.Equal("Implement A.", task.Contract);
        Assert.Equal(["IDD-0002", "IDD-0001"], task.TaskRelatedIntentIds);
    }

    [Fact]
    public void ResolverResolvesExactlyOneDirectCurrentDocument()
    {
        using var temp = new TestWorkspace();
        temp.Write(".idd/intent/IDD-0012.spec-settings.md", "selected");
        temp.Write(".idd/intent/sub/IDD-0012.spec-shadow.md", "nested");

        var path = new IntentDocumentResolver(temp.Path).ResolvePath("IDD-0012");

        Assert.EndsWith("IDD-0012.spec-settings.md", path, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolverRejectsUnknownAmbiguousAndNonCanonicalReferences()
    {
        using var temp = new TestWorkspace();
        temp.Write(".idd/intent/IDD-0001.spec-a.md", "a");
        temp.Write(".idd/intent/IDD-0001.adr-b.md", "b");
        temp.Write(".idd/intent/idd-0002.spec-lowercase.md", "lower");
        var resolver = new IntentDocumentResolver(temp.Path);

        Assert.Equal(IntentResolutionFailureKind.Ambiguous,
            Assert.Throws<IntentResolutionException>(() => resolver.ResolvePath("IDD-0001")).Kind);
        Assert.Equal(IntentResolutionFailureKind.Unknown,
            Assert.Throws<IntentResolutionException>(() => resolver.ResolvePath("IDD-0002")).Kind);
        Assert.Equal(IntentResolutionFailureKind.MalformedId,
            Assert.Throws<IntentResolutionException>(() => resolver.ResolvePath("IDD-1")).Kind);
    }

    [Fact]
    public async Task InvalidReferenceRejectsWholeBatchBeforeWorkIdentityOrContracts()
    {
        using var scenario = FactoryScenario.Create()
            .WithFile(".idd/intent/IDD-0001.spec-valid.md", "valid")
            .Planner(
                "# Task\nValid task.\n# TaskRelatedIntent\nIDD-0001\n\n" +
                "# Task\nInvalid task.\n# TaskRelatedIntent\nIDD-9999\n");

        var result = await scenario.Run("Change the product.");

        result.ShouldStopWith("MALFORMED_PLANNER_OUTPUT");
        Assert.Null(result.State.Current);
        Assert.Empty(result.State.Remaining);
        Assert.Equal(1, result.State.NextWorkItemNumber);
        var workItemsDirectory = Path.Combine(result.RunDirectory, "work-items");
        Assert.False(Directory.Exists(workItemsDirectory)
                     && Directory.EnumerateFiles(workItemsDirectory, "contract.md", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task SelectedDurableIntentIsGuaranteedExecutorContextWithoutContractDuplication()
    {
        const string contract = "Implement the requested behavior in the existing settings component.";
        const string selectedConstraint = "IMPORTANT-CONSTRAINT: settings must remain usable offline.";
        const string unrelatedConstraint = "UNRELATED-CONSTRAINT: reports use purple borders.";
        using var scenario = FactoryScenario.Create()
            .WithFile(".idd/intent/README.md", "intent rules")
            .WithFile(".idd/intent/INDEX.md", "INDEX-ONLY-MARKER")
            .WithFile(".idd/intent/IDD-0012.spec-settings.md", $"# IDD-0012\n\n{selectedConstraint}\n")
            .WithFile(".idd/intent/IDD-0013.spec-reports.md", $"# IDD-0013\n\n{unrelatedConstraint}\n")
            .Planner($"# Task\n{contract}\n# TaskRelatedIntent\nIDD-0012\n")
            .Execute(contract, invocation =>
            {
                Assert.Contains($"Work item contract:\n{contract}", invocation.Input, StringComparison.Ordinal);
                Assert.Contains("Task-related durable intent:\n--- IDD-0012 ---", invocation.Input, StringComparison.Ordinal);
                Assert.Contains(selectedConstraint, invocation.Input, StringComparison.Ordinal);
                Assert.DoesNotContain(unrelatedConstraint, invocation.Input, StringComparison.Ordinal);
                Assert.DoesNotContain("INDEX-ONLY-MARKER", invocation.Input, StringComparison.Ordinal);
                return "Implemented from supplied contract and durable intent.";
            })
            .Done();

        var result = await scenario.Run("Implement the requested settings behavior.");

        result.ShouldComplete();
        var contractFile = result.ReadArtifact("work-items/W000001/contract.md");
        Assert.Equal(contract, contractFile.Trim());
        Assert.DoesNotContain("TaskRelatedIntent", contractFile, StringComparison.Ordinal);
        Assert.DoesNotContain(selectedConstraint, contractFile, StringComparison.Ordinal);
        Assert.Equal(["IDD-0012"], Assert.Single(result.State.Completed).TaskRelatedIntentIds);
    }

    [Fact]
    public async Task MultipleSelectedDocumentsAreInjectedCompletelyInPlannerOrder()
    {
        const string contract = "Implement ordered intent case.";
        const string firstDocument = "# IDD-0002\n\nFIRST-DOCUMENT-BEGIN\nline two\nFIRST-DOCUMENT-END\n";
        const string secondDocument = "# IDD-0001\n\nSECOND-DOCUMENT-BEGIN\nline two\nSECOND-DOCUMENT-END\n";
        using var scenario = FactoryScenario.Create()
            .WithFile(".idd/intent/IDD-0001.spec-one.md", secondDocument)
            .WithFile(".idd/intent/IDD-0002.spec-two.md", firstDocument)
            .Planner($"# Task\n{contract}\n# TaskRelatedIntent\nIDD-0002\nIDD-0001")
            .Execute(contract, invocation =>
            {
                var firstMarker = invocation.Input.IndexOf("--- IDD-0002 ---", StringComparison.Ordinal);
                var secondMarker = invocation.Input.IndexOf("--- IDD-0001 ---", StringComparison.Ordinal);
                Assert.True(firstMarker >= 0 && secondMarker > firstMarker);
                Assert.Contains(firstDocument.Trim(), invocation.Input, StringComparison.Ordinal);
                Assert.Contains(secondDocument.Trim(), invocation.Input, StringComparison.Ordinal);
                return "Implemented.";
            })
            .Done();

        var result = await scenario.Run();

        result.ShouldComplete();
        Assert.Equal(["IDD-0002", "IDD-0001"], Assert.Single(result.State.Completed).TaskRelatedIntentIds);
    }

    [Fact]
    public async Task ExecutorReceivesNoneWhenTaskHasNoSelectedIntent()
    {
        const string contract = "Rename the local helper.";
        using var scenario = FactoryScenario.Create()
            .Plan(contract)
            .Execute(contract, invocation =>
            {
                Assert.Contains("Task-related durable intent:\nnone", invocation.Input, StringComparison.Ordinal);
                return "Renamed helper.";
            })
            .Done();

        var result = await scenario.Run();
        result.ShouldComplete();
        Assert.Empty(Assert.Single(result.State.Completed).TaskRelatedIntentIds);
    }

    [Theory]
    [InlineData(AgentTerminationKind.CommandTimeout)]
    [InlineData(AgentTerminationKind.IncompleteCommand)]
    public async Task CommandRetryPreservesSelectionAndReinjectsCurrentDocument(AgentTerminationKind failureKind)
    {
        const string contract = "Implement A.";
        const string constraint = "RETRY-CONSTRAINT";
        using var scenario = FactoryScenario.Create()
            .WithFile(".idd/intent/IDD-0007.spec-a.md", constraint)
            .Planner($"# Task\n{contract}\n# TaskRelatedIntent\nIDD-0007\n")
            .CommandFailure(failureKind, "command did not finish")
            .Execute(contract, invocation =>
            {
                Assert.Contains("--- IDD-0007 ---", invocation.Input, StringComparison.Ordinal);
                Assert.Contains(constraint, invocation.Input, StringComparison.Ordinal);
                return "Completed A after retry.";
            })
            .Done();

        var result = await scenario.Run();

        result.ShouldComplete();
        result.ShouldHaveAttemptCount("W000001", 2);
        result.ShouldHavePlanningCycles(2);
        Assert.All(
            result.Invocations.Where(x => x.WorkItemId == "W000001"),
            invocation => Assert.Contains("--- IDD-0007 ---", invocation.Input, StringComparison.Ordinal));
        Assert.Equal(["IDD-0007"], Assert.Single(result.State.Completed).TaskRelatedIntentIds);
    }

    [Fact]
    public async Task VerificationDrivenRetryPreservesExactSelection()
    {
        const string contract = "Implement verified change.";
        using var scenario = FactoryScenario.Create();
        scenario
            .WithFile(".idd/intent/IDD-0008.spec-verified.md", "VERIFICATION-INTENT")
            .WithVerificationCheck(
                "fixed-check",
                "pwsh -NoProfile -Command \"if (Test-Path 'fixed.txt') { exit 0 } else { exit 1 }\"")
            .Planner($"# Task\n{contract}\n# TaskRelatedIntent\nIDD-0008")
            .Execute(contract, invocation =>
            {
                Assert.Contains("--- IDD-0008 ---", invocation.Input, StringComparison.Ordinal);
                File.WriteAllText(Path.Combine(scenario.WorkspacePath, "first-attempt.txt"), "first");
                return "First implementation attempt.";
            })
            .Execute(contract, invocation =>
            {
                Assert.Contains("--- IDD-0008 ---", invocation.Input, StringComparison.Ordinal);
                File.WriteAllText(Path.Combine(scenario.WorkspacePath, "fixed.txt"), "fixed");
                return "Corrected from verification evidence.";
            })
            .Done();

        var result = await scenario.Run();

        result.ShouldComplete();
        result.ShouldHaveAttemptCount("W000001", 2);
        result.ShouldHavePlanningCycles(2);
        Assert.Equal(["IDD-0008"], Assert.Single(result.State.Completed).TaskRelatedIntentIds);
    }

    [Fact]
    public async Task RetryAfterBudgetExtensionReloadsCurrentIntentContentsWithoutReselection()
    {
        const string contract = "Implement current durable truth.";
        const string intentPath = ".idd/intent/IDD-0009.spec-current.md";
        using var scenario = FactoryScenario.Create()
            .WithFile(intentPath, "ORIGINAL-INTENT-CONTENT")
            .Planner($"# Task\n{contract}\n# TaskRelatedIntent\nIDD-0009")
            .CommandFailure(AgentTerminationKind.CommandTimeout, "timeout 1")
            .CommandFailure(AgentTerminationKind.CommandTimeout, "timeout 2")
            .CommandFailure(AgentTerminationKind.CommandTimeout, "timeout 3")
            .CommandFailure(AgentTerminationKind.CommandTimeout, "timeout 4")
            .Execute(contract, invocation =>
            {
                Assert.Contains("UPDATED-INTENT-CONTENT", invocation.Input, StringComparison.Ordinal);
                Assert.DoesNotContain("ORIGINAL-INTENT-CONTENT", invocation.Input, StringComparison.Ordinal);
                return "Implemented after budget extension.";
            })
            .Done();

        var exhausted = await scenario.Run();
        exhausted.ShouldBeBlockedBy("RETRY_BUDGET_EXHAUSTED");
        Assert.Equal(4, exhausted.Invocations.Count(x => x.WorkItemId == "W000001"));
        Assert.All(
            exhausted.Invocations.Where(x => x.WorkItemId == "W000001"),
            invocation => Assert.Contains("ORIGINAL-INTENT-CONTENT", invocation.Input, StringComparison.Ordinal));

        scenario.WithFile(intentPath, "UPDATED-INTENT-CONTENT");
        var result = await scenario.RetryExhausted(1);

        result.ShouldComplete();
        result.ShouldHaveAttemptCount("W000001", 5);
        result.ShouldHavePlanningCycles(2);
        Assert.Equal(["IDD-0009"], Assert.Single(result.State.Completed).TaskRelatedIntentIds);
    }

    [Fact]
    public async Task StateRoundTripPreservesCurrentAndRemainingRelatedIntent()
    {
        using var temp = new TestWorkspace();
        var store = new FileFactoryStateStore(temp.Path, new FactoryStateValidator());
        var state = StateStoreTests.State();
        state.Current = StateStoreTests.Planned("W000001") with { TaskRelatedIntentIds = ["IDD-0002", "IDD-0001"] };
        state.CurrentPhase = CurrentWorkPhase.Ready;
        state.Remaining.Add(StateStoreTests.Planned("W000002") with { TaskRelatedIntentIds = ["IDD-0003"] });
        state.PlanningCycleCount = 1;
        await store.CreateAsync(state, default);

        var loaded = (await store.LoadAsync(default))!;

        Assert.Equal(["IDD-0002", "IDD-0001"], loaded.Current!.TaskRelatedIntentIds);
        Assert.Equal(["IDD-0003"], Assert.Single(loaded.Remaining).TaskRelatedIntentIds);
    }

    [Fact]
    public async Task RelatedIntentSequenceCannotMutateAfterMaterialization()
    {
        using var temp = new TestWorkspace();
        var store = new FileFactoryStateStore(temp.Path, new FactoryStateValidator());
        var state = StateStoreTests.State();
        state.Remaining.Add(StateStoreTests.Planned("W000001") with { TaskRelatedIntentIds = ["IDD-0001", "IDD-0002"] });
        state.PlanningCycleCount = 1;
        await store.CreateAsync(state, default);

        state.Remaining[0].TaskRelatedIntentIds.Reverse();
        var error = await Assert.ThrowsAsync<FactoryStateException>(() => store.SaveAsync(state, 0, default));

        Assert.Equal("CORRUPT_FACTORY_STATE", error.Code);
        Assert.Contains("immutable", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompletedRelatedIntentSequenceIsImmutable()
    {
        using var temp = new TestWorkspace();
        var store = new FileFactoryStateStore(temp.Path, new FactoryStateValidator());
        var state = StateStoreTests.State();
        state.Completed.Add(StateStoreTests.Completed("W000001") with { TaskRelatedIntentIds = ["IDD-0001"] });
        await store.CreateAsync(state, default);

        state.Completed[0].TaskRelatedIntentIds.Add("IDD-0002");
        var error = await Assert.ThrowsAsync<FactoryStateException>(() => store.SaveAsync(state, 0, default));

        Assert.Equal("CORRUPT_FACTORY_STATE", error.Code);
    }

    [Fact]
    public async Task PreviousSchemaWithoutRelatedIntentMetadataRemainsLegacy()
    {
        using var temp = new TestWorkspace();
        var path = Path.Combine(temp.Path, "state.json");
        await File.WriteAllTextAsync(path,
            "{\"schemaVersion\":12,\"remaining\":[{\"id\":\"W000001\",\"contractPath\":\"work-items/W000001/contract.md\"}]}");

        var error = await Assert.ThrowsAsync<FactoryStateException>(() =>
            new FileFactoryStateStore(temp.Path, new FactoryStateValidator()).LoadAsync(default));

        Assert.Equal("LEGACY_FACTORY_STATE", error.Code);
    }

    [Fact]
    public async Task LaterPlanningCycleMaySelectDifferentIntentForNewTask()
    {
        using var scenario = FactoryScenario.Create()
            .WithFile(".idd/intent/IDD-0001.spec-a.md", "intent A")
            .WithFile(".idd/intent/IDD-0002.spec-b.md", "intent B")
            .Planner("# Task\nImplement A.\n# TaskRelatedIntent\nIDD-0001")
            .Execute("Implement A.")
            .Planner("# Task\nImplement B.\n# TaskRelatedIntent\nIDD-0002")
            .Execute("Implement B.")
            .Done();

        var result = await scenario.Run();

        result.ShouldComplete();
        Assert.Equal(["IDD-0001"], result.State.Completed[0].TaskRelatedIntentIds);
        Assert.Equal(["IDD-0002"], result.State.Completed[1].TaskRelatedIntentIds);
    }
}
