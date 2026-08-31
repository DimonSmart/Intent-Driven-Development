using System.Text.Json;
using Idd.Factory.Agents;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.Runtime;
using Idd.Factory.State;
using Idd.Factory.Telemetry;
using Idd.Factory.Verification;
using Idd.Factory.Workflow;

namespace Idd.Factory.Tests;

public sealed class FactoryRuntimeTests
{
    [Fact] public async Task OneSubtaskHappyPathUsesNoCoordinatorAndFinalizes()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Implement the specified behavior."); var workflowPath = temp.Write("workflow.yaml", WorkflowTests.ValidText);
        var workflow = new WorkflowDefinitionLoader().Load(temp.Path, workflowPath); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"); var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", new { workItems = new[] { new { id = "one", sequence = 1, kind = "subtask", contractMarkdown = "# One\n\nImplement one.", dependencies = Array.Empty<string>(), coveredWorkItems = Array.Empty<string>(), verificationCheckIds = Array.Empty<string>() } } }));
        backend.Results.Enqueue(invocation => Envelope(invocation, "completed")); backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));
        var runtime = Create(temp.Path, workflow, current, backend); var outcome = await runtime.RunAsync(request, "test", default);
        Assert.Equal("COMPLETED", outcome.FactoryOutcome); Assert.Equal(new[] { "task-decomposer", "implementer", "final-reviewer" }, backend.Roles); Assert.DoesNotContain("factory-step-coordinator", backend.Roles);
        Assert.Collection(backend.Invocations,
            invocation => AssertInvocation(invocation, "task-decomposer", "idd-factory-decompose-task", AgentExecutionProfile.ReadOnly),
            invocation => AssertInvocation(invocation, "implementer", "idd-factory-execute-subtask", AgentExecutionProfile.WorkspaceWrite),
            invocation => AssertInvocation(invocation, "final-reviewer", "idd-factory-review-task", AgentExecutionProfile.ReadOnly));
        Assert.Empty(Directory.EnumerateFileSystemEntries(current)); Assert.True(File.Exists(System.IO.Path.Combine(outcome.ResultDirectory!, "factory-result.json")));
        Assert.True(File.Exists(System.IO.Path.Combine(outcome.ResultDirectory!, "decomposition", "decomposition.json")));
        Assert.Equal("# One\n\nImplement one.", File.ReadAllText(System.IO.Path.Combine(outcome.ResultDirectory!, "decomposition", "contracts", "001-one.md")));
        using var decomposition = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(outcome.ResultDirectory!, "decomposition", "decomposition.json")));
        var retainedItem = Assert.Single(decomposition.RootElement.GetProperty("workItems").EnumerateArray());
        Assert.Equal("one", retainedItem.GetProperty("id").GetString()); Assert.Equal("subtask", retainedItem.GetProperty("kind").GetString());
    }

    [Fact] public async Task LockedIdeArtifactDoesNotBlockRunStartup()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Implement the specified behavior."); var workflow = DefaultWorkflow(temp); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current");
        var index = temp.Write(".vs/test/FileContentIndex/index.vsidx", "locked"); using var held = new FileStream(index, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var backend = new FakeAgentBackend(); EnqueueHappyPath(backend);

        var outcome = await Create(temp.Path, workflow, current, backend).RunAsync(request, "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
    }

    [Fact] public async Task WorkflowChangeDuringRunIsDetected()
    {
        using var temp = new TestWorkspace(); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"); Directory.CreateDirectory(current);
        var state = StateStoreTests.State() with { WorkflowHash = "old" }; await new FileFactoryStateStore(current, new FactoryStateValidator()).CreateAsync(state, default);
        var workflow = new WorkflowDefinitionLoader().Load(temp.Path, temp.Write("workflow.yaml", WorkflowTests.ValidText)); var outcome = await Create(temp.Path, workflow, current, new FakeAgentBackend()).ContinueAsync(default);
        Assert.Equal("WORKFLOW_CHANGED", outcome.FactoryOutcome);
    }

    [Fact] public async Task LegacyStateIsNotMigrated()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/factory/current/001-old.ready.md", "old"); var workflow = new WorkflowDefinitionLoader().Load(temp.Path, temp.Write("workflow.yaml", WorkflowTests.ValidText));
        var runtime = Create(temp.Path, workflow, System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"), new FakeAgentBackend());
        Assert.Equal("LEGACY_FACTORY_STATE", (await Assert.ThrowsAsync<FactoryStateException>(() => runtime.RunAsync(temp.Write("request.md", "x"), "test", default))).Code);
    }

    [Fact] public async Task ContinueReusesPersistedValidWorkerResult()
    {
        using var temp = new TestWorkspace(); var workflow = new WorkflowDefinitionLoader().Load(temp.Path, temp.Write("workflow.yaml", WorkflowTests.ValidText));
        var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"); temp.Write(".idd/factory/current/request.md", "Resume task"); temp.Write(".idd/factory/current/work-items/001-one.md", "# One");
        var state = StateStoreTests.State() with { WorkflowHash = workflow.Hash, CurrentWorkflowStep = "execute", CurrentAttemptId = "A000001", AttemptSequence = 1 };
        state.WorkItems.Add(new WorkItemState { Id = "one", Sequence = 1, Kind = WorkItemKind.Subtask, Status = WorkItemStatus.Running, ContractPath = "work-items/001-one.md", CurrentAttemptId = "A000001", AttemptCount = 1 });
        await new FileFactoryStateStore(current, new FactoryStateValidator()).CreateAsync(state, default);
        var invocation = new AgentInvocation { RunId = state.RunId, AttemptId = "A000001", Role = "implementer", WorkItemId = "one", Workspace = temp.Path, ResultPath = System.IO.Path.Combine(current, "attempts", "A000001", "result.json"), SkillName = "idd-factory-execute-subtask", ExecutionProfile = AgentExecutionProfile.WorkspaceWrite, Input = "input", StartedAt = DateTimeOffset.UnixEpoch };
        temp.Write(".idd/factory/current/attempts/A000001/invocation.json", JsonSerializer.Serialize(invocation, FactoryJson.Options)); temp.Write(".idd/factory/current/attempts/A000001/result.json", JsonSerializer.Serialize(Envelope(invocation, "completed"), FactoryJson.Options));
        var backend = new FakeAgentBackend(); backend.Results.Enqueue(next => Envelope(next, "approved"));
        var outcome = await Create(temp.Path, workflow, current, backend).ContinueAsync(default);
        Assert.Equal("COMPLETED", outcome.FactoryOutcome); Assert.Equal(["final-reviewer"], backend.Roles);
    }

    [Fact] public async Task UnknownPersistedAttemptIsRejected()
    {
        using var temp = new TestWorkspace(); var workflow = new WorkflowDefinitionLoader().Load(temp.Path, temp.Write("workflow.yaml", WorkflowTests.ValidText)); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current");
        var state = StateStoreTests.State() with { WorkflowHash = workflow.Hash, CurrentAttemptId = "A000001" }; await new FileFactoryStateStore(current, new FactoryStateValidator()).CreateAsync(state, default);
        Assert.Equal("UNKNOWN_ATTEMPT", (await Assert.ThrowsAsync<AgentProtocolException>(() => Create(temp.Path, workflow, current, new FakeAgentBackend()).ContinueAsync(default))).Code);
    }

    [Fact] public async Task IntentGatePreservesMissingIntentDecisionsAndResumesAfterDurableIntentChanges()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/intent/spec.md", "before"); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"); var backend = new FakeAgentBackend();
        var details = new
        {
            missingIntentDecisions = new[]
            {
                new
                {
                    area = "Staged registration",
                    whyBlocking = "The decomposition cannot define safe stage boundaries without durable registration semantics.",
                    requiredDecisions = new[] { "Define the staged registration contract.", "Define idempotency and lost-response recovery rules." },
                    intentReferences = new[] { "IDD-0002", "IDD-0006" },
                    recommendedNextWorkflow = "idd-intent-change"
                }
            }
        };
        backend.Results.Enqueue(invocation => Envelope(invocation, "intent-required", details, "Registration semantics are incomplete."));
        var runtime = Create(temp.Path, workflow, current, backend);

        var first = await runtime.RunAsync(request, "test", default);

        Assert.Equal("INTENT_REQUIRED", first.FactoryOutcome); Assert.Equal("Registration semantics are incomplete.", first.Reason);
        Assert.Equal("Update the listed durable intent decisions in .idd/intent, then run continue.", first.ResumeWhen); AssertIntentRequiredPayload(first.Payload);
        var persisted = await new FileFactoryStateStore(current, new FactoryStateValidator()).LoadAsync(default);
        Assert.NotNull(persisted); Assert.Equal("INTENT_REQUIRED", persisted.Blocker!.Code); AssertIntentRequiredPayload(persisted.Blocker.Payload);
        var count = backend.Roles.Count; var repeated = await runtime.ContinueAsync(default);
        Assert.Equal("INTENT_REQUIRED", repeated.FactoryOutcome); Assert.Equal(first.Reason, repeated.Reason); Assert.Equal(first.ResumeWhen, repeated.ResumeWhen); AssertIntentRequiredPayload(repeated.Payload); Assert.Equal(count, backend.Roles.Count);

        temp.Write(".idd/intent/spec.md", "after"); EnqueueHappyPath(backend);
        Assert.Equal("COMPLETED", (await runtime.ContinueAsync(default)).FactoryOutcome);
    }

    [Fact] public async Task IntentRequiredWithoutStructuredDecisionsIsRejected()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"); var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "intent-required", new { missingIntentDecisions = Array.Empty<object>() }));

        var outcome = await Create(temp.Path, workflow, current, backend).RunAsync(request, "test", default);

        Assert.Equal("MALFORMED_AGENT_RESULT", outcome.FactoryOutcome); Assert.Contains("non-empty payload.missingIntentDecisions", outcome.Reason);
    }

    [Fact] public async Task ClarificationRequiresAndPersistsAnswer()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"); var backend = new FakeAgentBackend();
        var details = new { question = "Which storage mode should be used?", options = new[] { "memory", "file" } };
        backend.Results.Enqueue(invocation => Envelope(invocation, "needs-clarification", details, "Choose storage mode.")); var runtime = Create(temp.Path, workflow, current, backend);
        var first = await runtime.RunAsync(request, "test", default);
        Assert.Equal("NEEDS_CLARIFICATION", first.FactoryOutcome); Assert.Equal("Choose storage mode.", first.Reason); AssertClarificationPayload(first.Payload);
        var persisted = await new FileFactoryStateStore(current, new FactoryStateValidator()).LoadAsync(default);
        Assert.NotNull(persisted); Assert.Equal("NEEDS_CLARIFICATION", persisted.Blocker!.Code); Assert.Equal(first.Reason, persisted.Blocker.Reason); AssertClarificationPayload(persisted.Blocker.Payload);
        var count = backend.Roles.Count; var repeated = await runtime.ContinueAsync(default);
        Assert.Equal("NEEDS_CLARIFICATION", repeated.FactoryOutcome); Assert.Equal(first.Reason, repeated.Reason); Assert.Equal(first.ResumeWhen, repeated.ResumeWhen); AssertClarificationPayload(repeated.Payload); Assert.Equal(count, backend.Roles.Count);
        EnqueueHappyPath(backend); var answer = temp.Write("answer.md", "Use option A.");
        var completed = await runtime.ContinueAsync(default, answer);
        Assert.Equal("COMPLETED", completed.FactoryOutcome); Assert.Equal(first.RunId, completed.RunId);
    }

    [Fact] public async Task ImplementerBlockedPreservesSemanticReasonAndPayload()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"); var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem([])));
        backend.Results.Enqueue(invocation => Envelope(invocation, "blocked", new { dependency = "storage-service" }, "Storage service is unavailable."));

        var outcome = await Create(temp.Path, workflow, current, backend).RunAsync(request, "test", default);

        Assert.Equal("BLOCKED", outcome.FactoryOutcome); Assert.Equal("Storage service is unavailable.", outcome.Reason);
        Assert.Equal("storage-service", outcome.Payload!.Value.GetProperty("dependency").GetString());
        var persisted = await new FileFactoryStateStore(current, new FactoryStateValidator()).LoadAsync(default);
        Assert.Equal("storage-service", persisted!.Blocker!.Payload!.Value.GetProperty("dependency").GetString());
    }

    [Fact] public async Task ContinueRetriesBlockedSemanticWorkItem()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"); var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem([])));
        backend.Results.Enqueue(invocation => Envelope(invocation, "blocked", new { dependency = "api" }, "API is unavailable."));
        var runtime = Create(temp.Path, workflow, current, backend);
        Assert.Equal("BLOCKED", (await runtime.RunAsync(request, "test", default)).FactoryOutcome);
        backend.Results.Enqueue(invocation => Envelope(invocation, "completed")); backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));
        Assert.Equal("COMPLETED", (await runtime.ContinueAsync(default)).FactoryOutcome);
        Assert.Equal(2, backend.Roles.Count(role => role == "implementer"));
    }

    [Fact] public async Task ContinueResumesVerificationWithoutRepeatingImplementation()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current");
        temp.Write("gate.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>"); temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  gate:\n    instructions: Confirm gate externally.\ndefault:\n  use:\n    - gate\n");
        var workflow = DefaultWorkflow(temp); var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem(["gate"]))); backend.Results.Enqueue(invocation => Envelope(invocation, "completed"));
        var runtime = Create(temp.Path, workflow, current, backend);
        Assert.Equal("VERIFICATION_RESULT_REQUIRED", (await runtime.RunAsync(request, "test", default)).FactoryOutcome);
        temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  gate:\n    run: dotnet build gate.csproj --nologo\ndefault:\n  use:\n    - gate\n");
        backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));
        Assert.Equal("VERIFICATION_POLICY_CHANGED", (await runtime.ContinueAsync(default, verificationPassed: true)).FactoryOutcome);
        Assert.Equal(1, backend.Roles.Count(role => role == "implementer"));
    }

    [Fact] public async Task CheckpointVerificationResumeRunsGateOnceThenReviewer()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"); var workflow = DefaultWorkflow(temp);
        temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  gate:\n    instructions: Confirm checkpoint.\ndefault:\n  use: []\ncheckpoint:\n  use:\n    - gate\n");
        var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", new { workItems = new object[]
        {
            new { id = "one", sequence = 1, kind = "subtask", contractMarkdown = "# One", dependencies = Array.Empty<string>(), coveredWorkItems = Array.Empty<string>(), verificationCheckIds = Array.Empty<string>() },
            new { id = "review", sequence = 2, kind = "review-checkpoint", contractMarkdown = "# Review", dependencies = new[] { "one" }, coveredWorkItems = new[] { "one" }, verificationCheckIds = Array.Empty<string>() }
        } }));
        backend.Results.Enqueue(invocation => Envelope(invocation, "completed"));
        var runtime = Create(temp.Path, workflow, current, backend);
        Assert.Equal("VERIFICATION_RESULT_REQUIRED", (await runtime.RunAsync(request, "test", default)).FactoryOutcome);
        backend.Results.Enqueue(invocation => Envelope(invocation, "approved")); backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));
        var outcome = await runtime.ContinueAsync(default, verificationPassed: true);
        Assert.Equal("COMPLETED", outcome.FactoryOutcome); Assert.Equal(1, backend.Roles.Count(role => role == "implementer")); Assert.Equal(1, backend.Roles.Count(role => role == "checkpoint-reviewer"));
        Assert.Single(Directory.GetFiles(System.IO.Path.Combine(outcome.ResultDirectory!, "verification"), "*.json").Select(ReadEvidence), x => x.CheckId == "gate" && x.Status == "passed");
    }

    [Fact] public async Task FinalVerificationResumeRunsGateOnceThenReviewer()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"); var workflow = DefaultWorkflow(temp);
        temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  gate:\n    instructions: Confirm final.\ndefault:\n  use: []\nfinal:\n  use:\n    - gate\n");
        var backend = new FakeAgentBackend(); backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem([]))); backend.Results.Enqueue(invocation => Envelope(invocation, "completed"));
        var runtime = Create(temp.Path, workflow, current, backend);
        Assert.Equal("VERIFICATION_RESULT_REQUIRED", (await runtime.RunAsync(request, "test", default)).FactoryOutcome);
        backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));
        var outcome = await runtime.ContinueAsync(default, verificationPassed: true);
        Assert.Equal("COMPLETED", outcome.FactoryOutcome); Assert.Equal(1, backend.Roles.Count(role => role == "final-reviewer"));
        Assert.Single(Directory.GetFiles(System.IO.Path.Combine(outcome.ResultDirectory!, "verification"), "*.json").Select(ReadEvidence), x => x.CheckId == "gate" && x.Status == "passed");
    }

    [Fact] public async Task BlockedCheckpointReviewerResumesWithoutRepeatingGate()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"); var workflow = DefaultWorkflow(temp);
        temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  gate:\n    run: dotnet --version\ndefault:\n  use: []\ncheckpoint:\n  use:\n    - gate\n");
        var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", CheckpointedItem()));
        backend.Results.Enqueue(invocation => Envelope(invocation, "completed"));
        backend.Results.Enqueue(invocation => Envelope(invocation, "blocked", reason: "Reviewer dependency unavailable."));
        var runtime = Create(temp.Path, workflow, current, backend);

        Assert.Equal("BLOCKED", (await runtime.RunAsync(request, "test", default)).FactoryOutcome);
        backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));
        backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));
        var outcome = await runtime.ContinueAsync(default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal(2, backend.Roles.Count(role => role == "checkpoint-reviewer"));
        Assert.Single(Directory.GetFiles(System.IO.Path.Combine(outcome.ResultDirectory!, "verification"), "*.json").Select(ReadEvidence), x => x.CheckId == "gate");
    }

    [Fact] public async Task BlockedFinalReviewerResumesWithoutRepeatingGate()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"); var workflow = DefaultWorkflow(temp);
        temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  gate:\n    run: dotnet --version\ndefault:\n  use: []\nfinal:\n  use:\n    - gate\n");
        var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem([])));
        backend.Results.Enqueue(invocation => Envelope(invocation, "completed"));
        backend.Results.Enqueue(invocation => Envelope(invocation, "blocked", reason: "Reviewer dependency unavailable."));
        var runtime = Create(temp.Path, workflow, current, backend);

        Assert.Equal("BLOCKED", (await runtime.RunAsync(request, "test", default)).FactoryOutcome);
        backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));
        var outcome = await runtime.ContinueAsync(default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal(2, backend.Roles.Count(role => role == "final-reviewer"));
        Assert.Single(Directory.GetFiles(System.IO.Path.Combine(outcome.ResultDirectory!, "verification"), "*.json").Select(ReadEvidence), x => x.CheckId == "gate");
    }

    [Fact] public async Task VerificationExceptionResumesAsVerificationGate()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"); var workflow = DefaultWorkflow(temp);
        var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem([]))); backend.Results.Enqueue(invocation => Envelope(invocation, "completed"));
        var verification = new ThrowOnceVerificationEngine(temp.Path, current); var runtime = Create(temp.Path, workflow, current, backend, verification);
        Assert.Equal("TEST_VERIFICATION_EXCEPTION", (await runtime.RunAsync(request, "test", default)).FactoryOutcome);
        var persisted = await new FileFactoryStateStore(current, new FactoryStateValidator()).LoadAsync(default);
        Assert.Equal(ContinuationKind.VerificationGate, persisted!.PendingContinuation!.Kind); Assert.Equal("subtask", persisted.PendingContinuation.VerificationContext); Assert.Equal("one", persisted.PendingContinuation.WorkItemId);
        verification.Fail = false;
        backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));
        Assert.Equal("COMPLETED", (await runtime.ContinueAsync(default)).FactoryOutcome); Assert.Equal(1, backend.Roles.Count(role => role == "implementer"));
    }

    [Fact] public async Task ReplanBudgetExhaustionIsTerminal()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current");
        var baseWorkflow = DefaultWorkflow(temp); var workflow = baseWorkflow with { Limits = baseWorkflow.Limits with { MaxReplans = 0 } }; var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem([]))); backend.Results.Enqueue(invocation => Envelope(invocation, "needs-replan", new { defect = "missing work" }, "Plan is incomplete."));
        var runtime = Create(temp.Path, workflow, current, backend);
        var first = await runtime.RunAsync(request, "test", default);
        Assert.Equal("REPLAN_BUDGET_EXHAUSTED", first.FactoryOutcome); Assert.Contains("Cancel and restart", first.ResumeWhen);
        var calls = backend.Roles.Count; Assert.Equal("REPLAN_BUDGET_EXHAUSTED", (await runtime.ContinueAsync(default)).FactoryOutcome); Assert.Equal(calls, backend.Roles.Count);
    }

    [Fact] public async Task FinalReviewReplanIncludesPersistedTriggerWhenNoMutableWorkRemains()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"); var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem([]))); backend.Results.Enqueue(invocation => Envelope(invocation, "completed"));
        backend.Results.Enqueue(invocation => Envelope(invocation, "needs-replan", new { finding = "integration gap" }, "Final integration gap."));
        backend.Results.Enqueue(invocation => { Assert.Contains("Final integration gap.", invocation.Input); Assert.Contains("integration gap", invocation.Input); Assert.Contains("final-reviewer", invocation.Input); return Envelope(invocation, "replan-proposed", new { operations = Array.Empty<object>() }); });
        backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));
        Assert.Equal("COMPLETED", (await Create(temp.Path, workflow, current, backend).RunAsync(request, "test", default)).FactoryOutcome);
    }

    [Fact] public async Task CheckpointCorrectionGraphCanBeReplanned()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"); var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", new { workItems = new object[]
        {
            new { id = "one", sequence = 1, kind = "subtask", contractMarkdown = "# One", dependencies = Array.Empty<string>(), coveredWorkItems = Array.Empty<string>(), verificationCheckIds = Array.Empty<string>() },
            new { id = "review", sequence = 2, kind = "review-checkpoint", contractMarkdown = "# Review", dependencies = new[] { "one" }, coveredWorkItems = new[] { "one" }, verificationCheckIds = Array.Empty<string>() }
        } }));
        backend.Results.Enqueue(invocation => Envelope(invocation, "completed"));
        backend.Results.Enqueue(invocation => Envelope(invocation, "needs-fix", new { correctiveSubtask = new { id = "fix", contractMarkdown = "# Fix", verificationCheckIds = Array.Empty<string>() } }));
        backend.Results.Enqueue(invocation => Envelope(invocation, "completed"));
        backend.Results.Enqueue(invocation => Envelope(invocation, "needs-replan", new { defect = "coverage" }, "Review coverage must change."));
        backend.Results.Enqueue(invocation => Envelope(invocation, "replan-proposed", new { operations = Array.Empty<object>() }));
        backend.Results.Enqueue(invocation => Envelope(invocation, "approved")); backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));
        Assert.Equal("COMPLETED", (await Create(temp.Path, workflow, current, backend).RunAsync(request, "test", default)).FactoryOutcome);
    }

    [Fact] public async Task ReplanTriggerSurvivesClarificationStop()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var answer = temp.Write("answer.md", "Proceed."); var workflow = DefaultWorkflow(temp); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"); var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem([]))); backend.Results.Enqueue(invocation => Envelope(invocation, "needs-replan", new { defect = "missing work" }, "Initial reason."));
        backend.Results.Enqueue(invocation => Envelope(invocation, "needs-clarification", new { question = "Scope?", options = new[] { "a" } }, "Choose scope."));
        var runtime = Create(temp.Path, workflow, current, backend); Assert.Equal("NEEDS_CLARIFICATION", (await runtime.RunAsync(request, "test", default)).FactoryOutcome);
        backend.Results.Enqueue(invocation => { Assert.Contains("Initial reason.", invocation.Input); Assert.Contains("missing work", invocation.Input); return Envelope(invocation, "replan-proposed", new { operations = Array.Empty<object>() }); });
        backend.Results.Enqueue(invocation => Envelope(invocation, "completed")); backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));
        Assert.Equal("COMPLETED", (await runtime.ContinueAsync(default, answer)).FactoryOutcome);
    }

    [Fact] public async Task ReplanTriggerSurvivesIntentRequiredStop()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/intent/spec.md", "before"); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"); var backend = new FakeAgentBackend();
        var details = new { missingIntentDecisions = new[] { new { area = "Planning", whyBlocking = "The replan needs a durable decision.", requiredDecisions = new[] { "Define the missing constraint." }, intentReferences = new[] { "IDD-0001" }, recommendedNextWorkflow = "idd-intent-change" } } };
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem([])));
        backend.Results.Enqueue(invocation => Envelope(invocation, "needs-replan", new { defect = "missing work" }, "Original replan reason."));
        backend.Results.Enqueue(invocation => Envelope(invocation, "intent-required", details, "Replan requires intent."));
        var runtime = Create(temp.Path, workflow, current, backend);
        Assert.Equal("INTENT_REQUIRED", (await runtime.RunAsync(request, "test", default)).FactoryOutcome);
        temp.Write(".idd/intent/spec.md", "after");
        backend.Results.Enqueue(invocation => { Assert.Equal("factory-replanner", invocation.Role); Assert.Contains("Original replan reason.", invocation.Input); Assert.Contains("missing work", invocation.Input); Assert.Contains("attempts/A000002/result.json", invocation.Input); return Envelope(invocation, "replan-proposed", new { operations = Array.Empty<object>() }); });
        backend.Results.Enqueue(invocation => Envelope(invocation, "completed")); backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));
        Assert.Equal("COMPLETED", (await runtime.ContinueAsync(default)).FactoryOutcome);
    }

    [Fact] public async Task ExecuteIntentRequiredPersistsTriggerForReplan()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/intent/spec.md", "before"); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = Path.Combine(temp.Path, ".idd", "factory", "current"); var backend = new FakeAgentBackend();
        var details = new { missingIntentDecisions = new[] { new { area = "Execution", whyBlocking = "Implementation needs a durable choice.", requiredDecisions = new[] { "Choose the constraint." }, intentReferences = new[] { "IDD-0001" }, recommendedNextWorkflow = "idd-intent-change" } } };
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem([])));
        backend.Results.Enqueue(invocation => Envelope(invocation, "intent-required", details, "Implementation requires intent."));
        var runtime = Create(temp.Path, workflow, current, backend);

        Assert.Equal("INTENT_REQUIRED", (await runtime.RunAsync(request, "test", default)).FactoryOutcome);
        temp.Write(".idd/intent/spec.md", "after");
        backend.Results.Enqueue(invocation => { Assert.Equal("factory-replanner", invocation.Role); Assert.Contains("Implementation requires intent.", invocation.Input); Assert.Contains("Execution", invocation.Input); return Envelope(invocation, "replan-proposed", new { operations = Array.Empty<object>() }); });
        backend.Results.Enqueue(invocation => Envelope(invocation, "completed")); backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));

        Assert.Equal("COMPLETED", (await runtime.ContinueAsync(default)).FactoryOutcome);
    }

    [Fact] public async Task ReplanCannotSupersedePrerequisiteOfRemainingWork()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = Path.Combine(temp.Path, ".idd", "factory", "current"); var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", new { workItems = new object[]
        {
            new { id = "a", sequence = 1, kind = "subtask", contractMarkdown = "# A", dependencies = Array.Empty<string>(), coveredWorkItems = Array.Empty<string>(), verificationCheckIds = Array.Empty<string>() },
            new { id = "b", sequence = 2, kind = "subtask", contractMarkdown = "# B", dependencies = new[] { "a" }, coveredWorkItems = Array.Empty<string>(), verificationCheckIds = Array.Empty<string>() }
        } }));
        backend.Results.Enqueue(invocation => Envelope(invocation, "needs-replan", new { defect = "replace A" }, "A is obsolete."));
        backend.Results.Enqueue(invocation => Envelope(invocation, "replan-proposed", new { operations = new[] { new { kind = "supersede-ready-subtask", id = "a" } } }));

        var outcome = await Create(temp.Path, workflow, current, backend).RunAsync(request, "test", default);

        Assert.Equal("INVALID_RUNTIME_GRAPH", outcome.FactoryOutcome);
        var state = await new FileFactoryStateStore(current, new FactoryStateValidator()).LoadAsync(default);
        Assert.Equal(0, state!.ReplanCount); Assert.Equal(WorkItemStatus.Ready, state.WorkItems.Single(x => x.Id == "a").Status); Assert.Equal(WorkItemStatus.Planned, state.WorkItems.Single(x => x.Id == "b").Status); Assert.NotNull(state.PendingReplanTrigger);
    }

    [Fact] public async Task ReplanCannotReplacePrerequisiteWithoutRewiringDependents()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = Path.Combine(temp.Path, ".idd", "factory", "current"); var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", new { workItems = new object[]
        {
            new { id = "a", sequence = 1, kind = "subtask", contractMarkdown = "# A", dependencies = Array.Empty<string>(), coveredWorkItems = Array.Empty<string>(), verificationCheckIds = Array.Empty<string>() },
            new { id = "b", sequence = 2, kind = "subtask", contractMarkdown = "# B", dependencies = new[] { "a" }, coveredWorkItems = Array.Empty<string>(), verificationCheckIds = Array.Empty<string>() }
        } }));
        backend.Results.Enqueue(invocation => Envelope(invocation, "needs-replan", new { defect = "replace A" }, "A is obsolete."));
        backend.Results.Enqueue(invocation => Envelope(invocation, "replan-proposed", new
        {
            operations = new object[]
            {
                new
                {
                    kind = "replace-ready-subtask",
                    id = "a",
                    subtask = new { id = "replacement", sequence = 3, kind = "subtask", contractMarkdown = "# Replacement", dependencies = Array.Empty<string>(), coveredWorkItems = Array.Empty<string>(), verificationCheckIds = Array.Empty<string>() }
                }
            }
        }));

        var outcome = await Create(temp.Path, workflow, current, backend).RunAsync(request, "test", default);

        Assert.Equal("INVALID_RUNTIME_GRAPH", outcome.FactoryOutcome);
        var state = await new FileFactoryStateStore(current, new FactoryStateValidator()).LoadAsync(default);
        Assert.Equal(0, state!.ReplanCount); Assert.DoesNotContain(state.WorkItems, x => x.Id == "replacement"); Assert.Equal(WorkItemStatus.Ready, state.WorkItems.Single(x => x.Id == "a").Status); Assert.NotNull(state.PendingReplanTrigger);
    }

    [Fact] public async Task ResumedVerificationFixNeedsReplanUsesExecuteTransition()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        temp.Write("gate.csproj", "<Project"); temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  gate:\n    run: dotnet build gate.csproj --nologo\ndefault:\n  use:\n    - gate\n");
        var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem(["gate"]))); backend.Results.Enqueue(invocation => Envelope(invocation, "completed"));
        backend.Results.Enqueue(invocation => Envelope(invocation, "blocked", reason: "Repair is paused."));
        var runtime = Create(temp.Path, workflow, current, backend);
        Assert.Equal("BLOCKED", (await runtime.RunAsync(request, "test", default)).FactoryOutcome);
        backend.Results.Enqueue(invocation => { temp.Write("gate.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>"); return Envelope(invocation, "needs-replan", new { defect = "repair scope" }, "Repair requires replanning."); });
        backend.Results.Enqueue(invocation => { Assert.Equal("factory-replanner", invocation.Role); Assert.Contains("Repair requires replanning.", invocation.Input); return Envelope(invocation, "replan-proposed", new { operations = new[] { new { kind = "supersede-ready-subtask", id = "one" } } }); });
        backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));

        Assert.Equal("COMPLETED", (await runtime.ContinueAsync(default)).FactoryOutcome);
        Assert.Contains("factory-replanner", backend.Roles);
    }

    [Fact] public async Task VerificationFixResumeUsesIndependentBudget()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var baseWorkflow = DefaultWorkflow(temp); var workflow = baseWorkflow with { Limits = baseWorkflow.Limits with { MaxAgentAttempts = 2 } }; var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        temp.Write("gate.csproj", "<Project"); temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  gate:\n    run: dotnet build gate.csproj --nologo\ndefault:\n  use:\n    - gate\n");
        var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem(["gate"]))); backend.Results.Enqueue(invocation => Envelope(invocation, "completed")); backend.Results.Enqueue(invocation => Envelope(invocation, "blocked", reason: "Repair is paused."));
        var runtime = Create(temp.Path, workflow, current, backend);

        Assert.Equal("BLOCKED", (await runtime.RunAsync(request, "test", default)).FactoryOutcome);
        backend.Results.Enqueue(invocation => { temp.Write("gate.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>"); return Envelope(invocation, "completed"); });
        backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));
        Assert.Equal("COMPLETED", (await runtime.ContinueAsync(default)).FactoryOutcome);
        Assert.Equal(3, backend.Roles.Count(role => role == "implementer"));
    }

    [Fact] public async Task RestartBeforeResumedVerificationFixDispatchKeepsVerificationFixAuthoritative()
    {
        using var temp = new TestWorkspace(); var workflow = DefaultWorkflow(temp); var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        var state = await SeedVerificationFixContinuationAsync(temp, workflow, current);
        state.RunStatus = FactoryRunStatus.Running; state.Blocker = null;
        await new FileFactoryStateStore(current, new FactoryStateValidator()).SaveAsync(state, state.Revision, default);
        var backend = new FakeAgentBackend(); var observedFixCount = -1;
        backend.Results.Enqueue(invocation =>
        {
            var persisted = new FileFactoryStateStore(current, new FactoryStateValidator()).LoadAsync(default).GetAwaiter().GetResult()!;
            observedFixCount = persisted.WorkItems.Single().VerificationFixAttemptCount;
            Assert.Equal(SemanticOperationKind.SubtaskVerificationFix, persisted.PendingContinuation!.Operation);
            Assert.Contains("verification-fix", invocation.Input);
            return Envelope(invocation, "completed");
        });
        backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));

        Assert.Equal("COMPLETED", (await Create(temp.Path, workflow, current, backend).ContinueAsync(default)).FactoryOutcome);
        Assert.Equal(1, observedFixCount); Assert.Equal(1, backend.Invocations.Count(x => x.Role == "implementer" && x.Input.Contains("verification-fix")));
        Assert.DoesNotContain(backend.Invocations, x => x.Role == "implementer" && !x.Input.Contains("verification-fix"));
    }

    [Fact] public async Task RestartWithResumedVerificationFixAttemptInFlightReconcilesThatFix()
    {
        using var temp = new TestWorkspace(); var workflow = DefaultWorkflow(temp); var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        var state = await SeedVerificationFixContinuationAsync(temp, workflow, current);
        await WritePersistedAttemptAsync(temp, current, state, "A000007", result: null);
        var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => { Assert.Contains("verification-fix", invocation.Input); Assert.NotEqual("A000007", invocation.AttemptId); return Envelope(invocation, "completed"); });
        backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));

        Assert.Equal("COMPLETED", (await Create(temp.Path, workflow, current, backend).ContinueAsync(default)).FactoryOutcome);
        Assert.DoesNotContain(backend.Invocations, x => x.Role == "implementer" && !x.Input.Contains("verification-fix"));
    }

    [Fact] public async Task RestartAfterResumedVerificationFixCompletedContinuesVerificationGate()
    {
        using var temp = new TestWorkspace(); var workflow = DefaultWorkflow(temp); var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        var state = await SeedVerificationFixContinuationAsync(temp, workflow, current);
        await WritePersistedAttemptAsync(temp, current, state, "A000007", "completed");
        var backend = new FakeAgentBackend(); backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));

        Assert.Equal("COMPLETED", (await Create(temp.Path, workflow, current, backend).ContinueAsync(default)).FactoryOutcome);
        Assert.Equal(["final-reviewer"], backend.Roles);
    }

    [Fact] public async Task RestartAfterResumedVerificationFixNeedsReplanPreservesTriggerAndReplans()
    {
        using var temp = new TestWorkspace(); var workflow = DefaultWorkflow(temp); var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        var state = await SeedVerificationFixContinuationAsync(temp, workflow, current);
        await WritePersistedAttemptAsync(temp, current, state, "A000007", "needs-replan", new { defect = "repair scope" }, "Repair requires replanning.");
        var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation =>
        {
            Assert.Equal("factory-replanner", invocation.Role);
            var persisted = new FileFactoryStateStore(current, new FactoryStateValidator()).LoadAsync(default).GetAwaiter().GetResult()!;
            Assert.Equal("Repair requires replanning.", persisted.PendingReplanTrigger!.Reason);
            Assert.Equal("repair scope", persisted.PendingReplanTrigger.Payload!.Value.GetProperty("defect").GetString());
            return Envelope(invocation, "replan-proposed", new { operations = new[] { new { kind = "supersede-ready-subtask", id = "one" } } });
        });
        backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));

        Assert.Equal("COMPLETED", (await Create(temp.Path, workflow, current, backend).ContinueAsync(default)).FactoryOutcome);
        Assert.Equal(1, backend.Roles.Count(x => x == "factory-replanner"));
        Assert.DoesNotContain(backend.Roles, x => x == "implementer");
    }

    [Fact] public async Task RestartAfterResumedVerificationFixIntentRequiredRestoresIntentGate()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/intent/spec.md", "before"); var workflow = DefaultWorkflow(temp); var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        var state = await SeedVerificationFixContinuationAsync(temp, workflow, current);
        await WritePersistedAttemptAsync(temp, current, state, "A000007", "intent-required", IntentRequiredDetails(), "Recovered verification repair needs intent.");
        var backend = new FakeAgentBackend(); var runtime = Create(temp.Path, workflow, current, backend);

        Assert.Equal("INTENT_REQUIRED", (await runtime.ContinueAsync(default)).FactoryOutcome);
        var persisted = await new FileFactoryStateStore(current, new FactoryStateValidator()).LoadAsync(default);
        Assert.Equal(ContinuationKind.IntentGate, persisted!.PendingContinuation!.Kind);
        Assert.Equal("runtime-intent", persisted.PendingContinuation.WorkflowStep);
        Assert.Equal("Recovered verification repair needs intent.", persisted.Blocker!.Reason);
        Assert.NotNull(persisted.PendingReplanTrigger); Assert.Equal("implementer", persisted.PendingReplanTrigger!.SourceRole); Assert.Equal("one", persisted.PendingReplanTrigger.SourceWorkItemId);
        Assert.Equal("attempts/A000007/result.json", persisted.PendingReplanTrigger.ResultRef);
        Assert.Empty(backend.Invocations);

        Assert.Equal("INTENT_REQUIRED", (await runtime.ContinueAsync(default)).FactoryOutcome);
        Assert.Empty(backend.Invocations);
    }

    [Fact] public async Task FailedVerificationUsesOneImplementerFixAndReverifies()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current");
        temp.Write("gate.csproj", "<Project");
        temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  gate:\n    run: dotnet build gate.csproj --nologo\ndefault:\n  use:\n    - gate\n");
        var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem(["gate"])));
        backend.Results.Enqueue(invocation => Envelope(invocation, "completed"));
        backend.Results.Enqueue(invocation => { Assert.Contains("verification-fix", invocation.Input); temp.Write("gate.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>"); return Envelope(invocation, "completed"); });
        backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));
        var outcome = await Create(temp.Path, workflow, current, backend).RunAsync(request, "test", default);
        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal(new[] { "task-decomposer", "implementer", "implementer", "final-reviewer" }, backend.Roles);
    }

    [Fact] public async Task BlockedSubtaskVerificationFixResumesSameRepairCycle()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current");
        temp.Write("gate.csproj", "<Project");
        temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  gate:\n    run: dotnet build gate.csproj --nologo\ndefault:\n  use:\n    - gate\n");
        var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem(["gate"])));
        backend.Results.Enqueue(invocation => Envelope(invocation, "completed"));
        backend.Results.Enqueue(invocation => { Assert.Contains("Mode:\nverification-fix", invocation.Input); return Envelope(invocation, "blocked", reason: "Restore the build toolchain."); });
        var runtime = Create(temp.Path, workflow, current, backend);

        Assert.Equal("BLOCKED", (await runtime.RunAsync(request, "test", default)).FactoryOutcome);
        var blocked = await new FileFactoryStateStore(current, new FactoryStateValidator()).LoadAsync(default);
        Assert.Equal(SemanticOperationKind.SubtaskVerificationFix, blocked!.PendingContinuation!.Operation);
        Assert.Equal(1, blocked.WorkItems.Single().VerificationFixAttemptCount);
        temp.Write("gate.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        backend.Results.Enqueue(invocation => { Assert.Contains("Mode:\nverification-fix", invocation.Input); return Envelope(invocation, "completed"); });
        backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));

        Assert.Equal("COMPLETED", (await runtime.ContinueAsync(default)).FactoryOutcome);
        Assert.Equal(1, backend.Invocations.Count(x => x.Role == "implementer" && !x.Input.Contains("verification-fix")));
        Assert.Equal(2, backend.Invocations.Count(x => x.Role == "implementer" && x.Input.Contains("verification-fix")));
    }

    [Fact] public async Task VerificationFixIntentRequiredWaitsForIntentThenReplansWithoutRepeatingTheFix()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/intent/spec.md", "before"); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        temp.Write("gate.csproj", "<Project");
        temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  gate:\n    run: dotnet build gate.csproj --nologo\ndefault:\n  use:\n    - gate\n");
        var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem(["gate"])));
        backend.Results.Enqueue(invocation => Envelope(invocation, "completed"));
        backend.Results.Enqueue(invocation => Envelope(invocation, "intent-required", IntentRequiredDetails(), "Verification repair needs a durable decision."));
        var runtime = Create(temp.Path, workflow, current, backend);

        Assert.Equal("INTENT_REQUIRED", (await runtime.RunAsync(request, "test", default)).FactoryOutcome);
        var persisted = await new FileFactoryStateStore(current, new FactoryStateValidator()).LoadAsync(default);
        Assert.Equal(ContinuationKind.IntentGate, persisted!.PendingContinuation!.Kind);
        Assert.Equal("runtime-intent", persisted.PendingContinuation.WorkflowStep);
        Assert.Equal("INTENT_REQUIRED", persisted.Blocker!.Code); AssertIntentRequiredPayload(persisted.Blocker.Payload);
        Assert.NotNull(persisted.PendingReplanTrigger); Assert.Equal("implementer", persisted.PendingReplanTrigger!.SourceRole); Assert.Equal("one", persisted.PendingReplanTrigger.SourceWorkItemId);
        Assert.Equal("Verification repair needs a durable decision.", persisted.PendingReplanTrigger.Reason); AssertIntentRequiredPayload(persisted.PendingReplanTrigger.Payload); Assert.NotEmpty(persisted.PendingReplanTrigger.EvidenceRefs);

        var callsBeforeUnchangedContinue = backend.Roles.Count;
        Assert.Equal("INTENT_REQUIRED", (await runtime.ContinueAsync(default)).FactoryOutcome);
        Assert.Equal(callsBeforeUnchangedContinue, backend.Roles.Count);

        temp.Write(".idd/intent/spec.md", "after"); temp.Write("gate.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        backend.Results.Enqueue(invocation =>
        {
            Assert.Equal("factory-replanner", invocation.Role);
            Assert.Contains("Verification repair needs a durable decision.", invocation.Input);
            return Envelope(invocation, "replan-proposed", new { operations = new[] { new { kind = "supersede-ready-subtask", id = "one" } } });
        });
        backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));

        Assert.Equal("COMPLETED", (await runtime.ContinueAsync(default)).FactoryOutcome);
        Assert.Equal(1, backend.Roles.Count(x => x == "factory-replanner"));
        Assert.Equal(1, backend.Invocations.Count(x => x.Role == "implementer" && x.Input.Contains("verification-fix")));
    }

    [Fact] public async Task InvalidDecompositionVerificationDoesNotPartiallyApplyWork()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current");
        temp.Write(".idd/verification.yaml", "version: 1\nchecks: {}\ndefault:\n  use: []\n");
        var backend = new FakeAgentBackend(); backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem(["gate"])));
        var runtime = Create(temp.Path, workflow, current, backend);

        Assert.Equal("UNKNOWN_VERIFICATION_CHECK", (await runtime.RunAsync(request, "test", default)).FactoryOutcome);
        var blocked = await new FileFactoryStateStore(current, new FactoryStateValidator()).LoadAsync(default);
        Assert.Empty(blocked!.WorkItems); Assert.Equal(SemanticOperationKind.Decomposition, blocked.PendingContinuation!.Operation);
        Assert.Empty(Directory.EnumerateFiles(System.IO.Path.Combine(current, "work-items")));
        temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  gate:\n    run: dotnet --version\ndefault:\n  use: []\n");
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem(["gate"])));
        backend.Results.Enqueue(invocation => Envelope(invocation, "completed")); backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));

        Assert.Equal("COMPLETED", (await runtime.ContinueAsync(default)).FactoryOutcome);
        Assert.Equal(2, backend.Roles.Count(x => x == "task-decomposer"));
    }

    [Fact] public async Task InvalidReplanVerificationDoesNotPartiallyApplyGraph()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current");
        temp.Write(".idd/verification.yaml", "version: 1\nchecks: {}\ndefault:\n  use: []\n");
        var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem([])));
        backend.Results.Enqueue(invocation => Envelope(invocation, "needs-replan", new { defect = "missing step" }, "Plan needs another step."));
        backend.Results.Enqueue(invocation => Envelope(invocation, "replan-proposed", new { operations = new object[] { new { kind = "insert-subtask", subtask = new { id = "extra", sequence = 2, kind = "subtask", contractMarkdown = "# Extra", dependencies = new[] { "one" }, coveredWorkItems = Array.Empty<string>(), verificationCheckIds = new[] { "gate" } } } } }));
        var runtime = Create(temp.Path, workflow, current, backend);

        Assert.Equal("UNKNOWN_VERIFICATION_CHECK", (await runtime.RunAsync(request, "test", default)).FactoryOutcome);
        var blocked = await new FileFactoryStateStore(current, new FactoryStateValidator()).LoadAsync(default);
        Assert.Single(blocked!.WorkItems); Assert.Equal(0, blocked.ReplanCount); Assert.NotNull(blocked.PendingReplanTrigger);
        Assert.Equal(SemanticOperationKind.Replan, blocked.PendingContinuation!.Operation);
        temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  gate:\n    run: dotnet --version\ndefault:\n  use: []\n");
        backend.Results.Enqueue(invocation => Envelope(invocation, "replan-proposed", new { operations = Array.Empty<object>() }));
        backend.Results.Enqueue(invocation => Envelope(invocation, "completed")); backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));

        Assert.Equal("COMPLETED", (await runtime.ContinueAsync(default)).FactoryOutcome);
        Assert.Equal(2, backend.Roles.Count(x => x == "factory-replanner"));
    }

    [Fact] public async Task MissingPolicySubtaskUsesRepositoryFallback()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current");
        temp.Write("scripts/Check.ps1", """
            $countPath = 'fallback-count.txt'
            $count = if (Test-Path -LiteralPath $countPath) { [int](Get-Content -Raw -LiteralPath $countPath) } else { 0 }
            $count++
            Set-Content -LiteralPath $countPath -Value $count
            if ($count -eq 1) {
                $state = Get-Content -Raw -LiteralPath '.idd/factory/current/state.json' | ConvertFrom-Json
                Set-Content -LiteralPath 'first-fallback-status.txt' -Value $state.workItems[0].status
            }
            """);
        var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem([])));
        backend.Results.Enqueue(invocation => Envelope(invocation, "completed"));
        backend.Results.Enqueue(invocation =>
        {
            var state = JsonSerializer.Deserialize<FactoryState>(File.ReadAllText(System.IO.Path.Combine(current, "state.json")), FactoryJson.Options)!;
            Assert.Contains(state.WorkItems[0].VerificationEvidenceRefs, path => ReadEvidence(current, path).CheckId == "repository-fallback");
            return Envelope(invocation, "approved");
        });

        var outcome = await Create(temp.Path, workflow, current, backend).RunAsync(request, "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal("AwaitingVerification", File.ReadAllText(System.IO.Path.Combine(temp.Path, "first-fallback-status.txt")).Trim());
        var evidence = Directory.GetFiles(System.IO.Path.Combine(outcome.ResultDirectory!, "verification"), "*.json").Select(ReadEvidence).ToArray();
        Assert.Contains(evidence, item => item.CheckId == "repository-fallback" && item.Status == "passed");
    }

    [Fact] public async Task MissingPolicyFailedSubtaskFallbackUsesVerificationFix()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current");
        temp.Write("scripts/Check.ps1", """
            $countPath = 'fallback-count.txt'
            $count = if (Test-Path -LiteralPath $countPath) { [int](Get-Content -Raw -LiteralPath $countPath) } else { 0 }
            $count++
            Set-Content -LiteralPath $countPath -Value $count
            if (-not (Test-Path -LiteralPath 'fallback-fixed.txt')) { exit 7 }
            """);
        var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem([])));
        backend.Results.Enqueue(invocation => Envelope(invocation, "completed"));
        backend.Results.Enqueue(invocation =>
        {
            Assert.Equal("implementer", invocation.Role); Assert.Contains("verification-fix", invocation.Input);
            temp.Write("fallback-fixed.txt", "yes"); return Envelope(invocation, "completed");
        });
        backend.Results.Enqueue(invocation =>
        {
            var state = JsonSerializer.Deserialize<FactoryState>(File.ReadAllText(System.IO.Path.Combine(current, "state.json")), FactoryJson.Options)!;
            Assert.Equal(WorkItemStatus.Completed, state.WorkItems[0].Status); return Envelope(invocation, "approved");
        });

        var outcome = await Create(temp.Path, workflow, current, backend).RunAsync(request, "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal(new[] { "task-decomposer", "implementer", "implementer", "final-reviewer" }, backend.Roles);
        Assert.True(int.Parse(File.ReadAllText(System.IO.Path.Combine(temp.Path, "fallback-count.txt"))) >= 3);
        var evidence = Directory.GetFiles(System.IO.Path.Combine(outcome.ResultDirectory!, "verification"), "*.json").Select(ReadEvidence).Where(item => item.CheckId == "repository-fallback").ToArray();
        Assert.Contains(evidence, item => item.Status == "failed"); Assert.Contains(evidence, item => item.Status == "passed");
    }

    [Fact] public async Task ExistingMalformedPolicyDoesNotFallback()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current");
        temp.Write(".idd/verification.yaml", "version: 2\nchecks: {}\ndefault:\n  use: []\n");
        temp.Write("scripts/Check.ps1", "Set-Content -LiteralPath fallback-ran.txt -Value yes\n");
        var backend = new FakeAgentBackend(); backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem([])));

        var outcome = await Create(temp.Path, workflow, current, backend).RunAsync(request, "test", default);

        Assert.Equal("INVALID_VERIFICATION_POLICY", outcome.FactoryOutcome);
        Assert.Equal(["task-decomposer"], backend.Roles);
        Assert.False(File.Exists(System.IO.Path.Combine(temp.Path, "fallback-ran.txt")));
    }

    [Fact] public async Task RepeatedVerificationFailureBlocksWithoutHiddenAgentRetry()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current");
        temp.Write("gate.csproj", "<Project");
        temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  gate:\n    run: dotnet build gate.csproj --nologo\ndefault:\n  use:\n    - gate\n");
        var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem(["gate"])));
        backend.Results.Enqueue(invocation => Envelope(invocation, "completed"));
        backend.Results.Enqueue(invocation => Envelope(invocation, "completed"));
        var outcome = await Create(temp.Path, workflow, current, backend).RunAsync(request, "test", default);
        Assert.Equal("VERIFICATION_FIX_BUDGET_EXHAUSTED", outcome.FactoryOutcome);
        Assert.Equal(new[] { "task-decomposer", "implementer", "implementer" }, backend.Roles);
    }

    [Fact] public async Task CheckpointReviewerRunsOnlyAfterRuntimeGatePasses()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current");
        temp.Write("gate.csproj", "<Project");
        temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  gate:\n    run: dotnet build gate.csproj --nologo\ndefault:\n  use:\n    - gate\ncheckpoint:\n  use:\n    - gate\n");
        var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", new { workItems = new object[]
        {
            new { id = "one", sequence = 1, kind = "subtask", contractMarkdown = "# One", dependencies = Array.Empty<string>(), coveredWorkItems = Array.Empty<string>(), verificationCheckIds = Array.Empty<string>() },
            new { id = "review", sequence = 2, kind = "review-checkpoint", contractMarkdown = "# Review", dependencies = new[] { "one" }, coveredWorkItems = new[] { "one" }, verificationCheckIds = new[] { "gate" } }
        } }));
        backend.Results.Enqueue(invocation => Envelope(invocation, "completed"));
        backend.Results.Enqueue(invocation => { Assert.Equal("implementer", invocation.Role); Assert.Contains("verification-fix", invocation.Input); temp.Write("gate.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>"); return Envelope(invocation, "completed"); });
        backend.Results.Enqueue(invocation => { Assert.Equal("checkpoint-reviewer", invocation.Role); return Envelope(invocation, "approved"); });
        backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));
        var outcome = await Create(temp.Path, workflow, current, backend).RunAsync(request, "test", default);
        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal(new[] { "task-decomposer", "implementer", "implementer", "checkpoint-reviewer", "final-reviewer" }, backend.Roles);
    }

    [Fact] public async Task ConfirmationApproveRunsExactCheckOnceAndContinues()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  gate:\n    run: dotnet --version\n    confirmation: required\ndefault:\n  use: []\nsubtask:\n  use:\n    - gate\nfinal:\n  use: []\n");
        var backend = new FakeAgentBackend(); backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem(["gate"]))); backend.Results.Enqueue(invocation => Envelope(invocation, "completed"));
        var runtime = Create(temp.Path, workflow, current, backend);
        Assert.Equal("VERIFICATION_CONFIRMATION_REQUIRED", (await runtime.RunAsync(request, "test", default)).FactoryOutcome);
        backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));
        var outcome = await runtime.ContinueAsync(default, confirmation: VerificationConfirmation.Approve);
        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Single(Directory.GetFiles(Path.Combine(outcome.ResultDirectory!, "verification"), "*.json").Select(ReadEvidence), evidence => evidence.CheckId == "gate" && evidence.Status == "passed");
    }

    [Fact] public async Task ConfirmationDeclinePersistsValidTerminalStateWithoutRunningCommand()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  gate:\n    run: dotnet --version > confirmation-ran.txt\n    confirmation: required\ndefault:\n  use: []\nsubtask:\n  use:\n    - gate\n");
        var backend = new FakeAgentBackend(); backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem(["gate"]))); backend.Results.Enqueue(invocation => Envelope(invocation, "completed"));
        var runtime = Create(temp.Path, workflow, current, backend);
        Assert.Equal("VERIFICATION_CONFIRMATION_REQUIRED", (await runtime.RunAsync(request, "test", default)).FactoryOutcome);
        Assert.Equal("VERIFICATION_DECLINED", (await runtime.ContinueAsync(default, confirmation: VerificationConfirmation.Decline)).FactoryOutcome);
        var persisted = await new FileFactoryStateStore(current, new FactoryStateValidator()).LoadAsync(default);
        Assert.Null(persisted!.PendingVerificationSession); Assert.False(persisted.PendingContinuation!.IsResumable);
        Assert.Contains(persisted.VerificationEvidenceRefs, path => ReadEvidence(current, path).Status == "not-verified");
        Assert.False(File.Exists(Path.Combine(temp.Path, "confirmation-ran.txt")));
        Assert.Equal("VERIFICATION_DECLINED", (await runtime.ContinueAsync(default)).FactoryOutcome);
        Assert.False(File.Exists(Path.Combine(temp.Path, "confirmation-ran.txt")));
    }

    [Fact] public async Task ManualPassedAdvancesCursorToNextCheck()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  manual:\n    instructions: Confirm behavior.\n  automatic:\n    run: dotnet --version\ndefault:\n  use: []\nsubtask:\n  use:\n    - manual\n    - automatic\nfinal:\n  use: []\n");
        var backend = new FakeAgentBackend(); backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem(["manual", "automatic"]))); backend.Results.Enqueue(invocation => Envelope(invocation, "completed"));
        var runtime = Create(temp.Path, workflow, current, backend);
        Assert.Equal("VERIFICATION_RESULT_REQUIRED", (await runtime.RunAsync(request, "test", default)).FactoryOutcome);
        backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));
        var outcome = await runtime.ContinueAsync(default, verificationPassed: true);
        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        var evidence = Directory.GetFiles(Path.Combine(outcome.ResultDirectory!, "verification"), "*.json").Select(ReadEvidence).ToArray();
        Assert.Single(evidence, item => item.CheckId == "manual" && item.Status == "passed");
        Assert.Single(evidence, item => item.CheckId == "automatic" && item.Status == "passed");
    }

    [Fact] public async Task ManualFailureRepairRequiresFreshManualResult()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  manual:\n    instructions: Confirm behavior.\ndefault:\n  use: []\nsubtask:\n  use:\n    - manual\nfinal:\n  use: []\n");
        var backend = new FakeAgentBackend(); backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem(["manual"]))); backend.Results.Enqueue(invocation => Envelope(invocation, "completed"));
        var runtime = Create(temp.Path, workflow, current, backend);
        Assert.Equal("VERIFICATION_RESULT_REQUIRED", (await runtime.RunAsync(request, "test", default)).FactoryOutcome);
        backend.Results.Enqueue(invocation => { temp.Write("src/repaired.cs", "fixed"); return Envelope(invocation, "completed"); });
        Assert.Equal("VERIFICATION_RESULT_REQUIRED", (await runtime.ContinueAsync(default, verificationPassed: false)).FactoryOutcome);
        backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));
        Assert.Equal("COMPLETED", (await runtime.ContinueAsync(default, verificationPassed: true)).FactoryOutcome);
        Assert.Equal(2, backend.Roles.Count(role => role == "implementer"));
    }

    [Fact] public async Task ResumedVerificationFixNewPathRecomputesRulesAndDispatchesPersistedReplan()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        temp.Write("broken.csproj", "<Project");
        temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  area-a:\n    run: dotnet build broken.csproj --nologo\n  area-b:\n    run: dotnet --version\ndefault:\n  use: []\nsubtask:\n  rules:\n    - paths:\n        - src/A/**\n      use:\n        - area-a\n    - fallback: true\n      use:\n        - area-b\nfinal:\n  use: []\n");
        var backend = new FakeAgentBackend(); backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem(["area-a"])));
        backend.Results.Enqueue(invocation => { temp.Write("src/A/a.cs", "a"); return Envelope(invocation, "completed"); });
        backend.Results.Enqueue(invocation => Envelope(invocation, "blocked", reason: "Repair is paused."));
        var runtime = Create(temp.Path, workflow, current, backend);
        Assert.Equal("BLOCKED", (await runtime.RunAsync(request, "test", default)).FactoryOutcome);
        backend.Results.Enqueue(invocation => { temp.Write("src/B/b.cs", "b"); return Envelope(invocation, "completed"); });
        backend.Results.Enqueue(invocation =>
        {
            Assert.Equal("factory-replanner", invocation.Role);
            Assert.Contains("different verification selection", invocation.Input);
            var persisted = new FileFactoryStateStore(current, new FactoryStateValidator()).LoadAsync(default).GetAwaiter().GetResult()!;
            Assert.Equal("replan", persisted.CurrentWorkflowStep);
            Assert.Equal(ContinuationKind.SemanticInvocation, persisted.PendingContinuation!.Kind);
            Assert.Equal(SemanticOperationKind.Replan, persisted.PendingContinuation.Operation);
            Assert.Equal("replan", persisted.PendingContinuation.WorkflowStep);
            Assert.Contains("src/B/b.cs", persisted.WorkItems.Single().ChangedPaths);
            return Envelope(invocation, "blocked", reason: "Stop after observing replan.");
        });

        var outcome = await runtime.ContinueAsync(default);
        Assert.Equal("BLOCKED", outcome.FactoryOutcome); Assert.NotEqual("NEEDS_REPLAN", outcome.FactoryOutcome);
        Assert.Equal("factory-replanner", backend.Roles[^1]);
        var state = await new FileFactoryStateStore(current, new FactoryStateValidator()).LoadAsync(default);
        Assert.Contains("src/B/b.cs", state!.WorkItems.Single().ChangedPaths);
        Assert.Equal(2, backend.Invocations.Count(x => x.Role == "implementer" && x.Input.Contains("verification-fix")));
    }

    [Fact] public async Task ResumedManualVerificationFailureRoutesVerificationFixReplanWithoutRepeatingChecks()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        temp.Write("broken.csproj", "<Project");
        temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  manual:\n    instructions: Confirm behavior.\n  automatic:\n    run: dotnet build broken.csproj --nologo\ndefault:\n  use: []\nsubtask:\n  use:\n    - manual\n    - automatic\nfinal:\n  use: []\n");
        var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem(["manual", "automatic"])));
        backend.Results.Enqueue(invocation => Envelope(invocation, "completed"));
        var runtime = Create(temp.Path, workflow, current, backend);
        Assert.Equal("VERIFICATION_RESULT_REQUIRED", (await runtime.RunAsync(request, "test", default)).FactoryOutcome);
        backend.Results.Enqueue(invocation => Envelope(invocation, "needs-replan", new { defect = "automatic verification repair requires replanning" }, "Replan the failed automatic verification repair."));
        backend.Results.Enqueue(invocation =>
        {
            Assert.Equal("factory-replanner", invocation.Role);
            var persisted = new FileFactoryStateStore(current, new FactoryStateValidator()).LoadAsync(default).GetAwaiter().GetResult()!;
            Assert.Equal("replan", persisted.CurrentWorkflowStep);
            Assert.Equal(ContinuationKind.SemanticInvocation, persisted.PendingContinuation!.Kind);
            Assert.Equal(SemanticOperationKind.Replan, persisted.PendingContinuation.Operation);
            Assert.Equal("Replan the failed automatic verification repair.", persisted.PendingReplanTrigger!.Reason);
            return Envelope(invocation, "blocked", reason: "Stop after observing replan.");
        });

        var outcome = await runtime.ContinueAsync(default, verificationPassed: true);

        Assert.Equal("BLOCKED", outcome.FactoryOutcome); Assert.NotEqual("NEEDS_REPLAN", outcome.FactoryOutcome);
        Assert.Single(backend.Invocations, x => x.Role == "implementer" && x.Input.Contains("verification-fix"));
        Assert.Single(backend.Invocations, x => x.Role == "factory-replanner");
        var evidence = (await new FileFactoryStateStore(current, new FactoryStateValidator()).LoadAsync(default))!.VerificationEvidenceRefs.Select(path => ReadEvidence(current, path)).ToArray();
        Assert.Single(evidence, item => item.CheckId == "manual" && item.Status == "passed");
        Assert.Single(evidence, item => item.CheckId == "automatic" && item.Status == "failed");
    }

    [Fact] public async Task CheckpointRepairPathsParticipateInFreshCheckpointScope()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        temp.Write("broken.csproj", "<Project");
        temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  area-a:\n    run: dotnet build broken.csproj --nologo\n  combined:\n    run: dotnet --version\ndefault:\n  use: []\ncheckpoint:\n  rules:\n    - paths:\n        - src/A/**\n      use:\n        - area-a\n    - fallback: true\n      use:\n        - combined\nfinal:\n  use: []\n");
        var backend = new FakeAgentBackend(); backend.Results.Enqueue(invocation => Envelope(invocation, "ready", CheckpointedItem()));
        backend.Results.Enqueue(invocation => { temp.Write("src/A/a.cs", "a"); return Envelope(invocation, "completed"); });
        backend.Results.Enqueue(invocation => { temp.Write("src/B/b.cs", "b"); return Envelope(invocation, "completed"); });
        backend.Results.Enqueue(invocation =>
        {
            var state = JsonSerializer.Deserialize<FactoryState>(File.ReadAllText(Path.Combine(current, "state.json")), FactoryJson.Options)!;
            Assert.Contains("src/B/b.cs", state.WorkItems.Single(item => item.Id == "review").ChangedPaths); return Envelope(invocation, "approved");
        });
        backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));
        var outcome = await Create(temp.Path, workflow, current, backend).RunAsync(request, "test", default);
        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        var evidence = Directory.GetFiles(Path.Combine(outcome.ResultDirectory!, "verification"), "*.json").Select(ReadEvidence).ToArray();
        Assert.Contains(evidence, item => item.CheckId == "area-a" && item.Status == "failed"); Assert.Contains(evidence, item => item.CheckId == "combined" && item.Status == "passed");
    }

    [Fact] public async Task FinalVerificationRepairReplanCreatesFreshSessionAfterImplementation()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        temp.Write("final.csproj", "<Project");
        temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  final-gate:\n    run: dotnet build final.csproj --nologo\ndefault:\n  use: []\nfinal:\n  use:\n    - final-gate\n");
        var backend = new FakeAgentBackend(); backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem([]))); backend.Results.Enqueue(invocation => Envelope(invocation, "completed"));
        backend.Results.Enqueue(invocation => Envelope(invocation, "needs-replan", new { defect = "final repair needs implementation" }, "Replan final repair."));
        backend.Results.Enqueue(invocation => Envelope(invocation, "replan-proposed", new { operations = new object[] { new { kind = "insert-subtask", subtask = new { id = "final-fix", sequence = 2, kind = "subtask", contractMarkdown = "# Fix", dependencies = new[] { "one" }, coveredWorkItems = Array.Empty<string>(), verificationCheckIds = Array.Empty<string>() } } } }));
        backend.Results.Enqueue(invocation => { temp.Write("final.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>"); return Envelope(invocation, "completed"); });
        backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));
        var outcome = await Create(temp.Path, workflow, current, backend).RunAsync(request, "test", default);
        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        var evidence = Directory.GetFiles(Path.Combine(outcome.ResultDirectory!, "verification"), "*.json").Select(ReadEvidence).Where(item => item.CheckId == "final-gate").ToArray();
        Assert.Contains(evidence, item => item.Status == "failed"); Assert.Contains(evidence, item => item.Status == "passed");
    }

    [Fact] public async Task RestartRecoversWorkspaceChangesBeforeReusingPersistedResult()
    {
        using var temp = new TestWorkspace(); var workflow = DefaultWorkflow(temp); var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        temp.Write(".idd/factory/current/request.md", "Resume task"); temp.Write(".idd/factory/current/work-items/001-one.md", "# One"); temp.Write("product.txt", "before");
        var state = StateStoreTests.State() with { WorkflowHash = workflow.Hash, CurrentWorkflowStep = "execute", CurrentAttemptId = "A000001", AttemptSequence = 1 };
        state.WorkItems.Add(new WorkItemState { Id = "one", Sequence = 1, Kind = WorkItemKind.Subtask, Status = WorkItemStatus.Running, ContractPath = "work-items/001-one.md", CurrentAttemptId = "A000001", AttemptCount = 1 });
        await new FileFactoryStateStore(current, new FactoryStateValidator()).CreateAsync(state, default);
        var invocation = PersistedWorkspaceWriteInvocation(temp, current, state); WriteWorkspaceBaseline(temp, "before");
        temp.Write("product.txt", "after"); temp.Write(".idd/factory/current/attempts/A000001/result.json", JsonSerializer.Serialize(Envelope(invocation, "completed"), FactoryJson.Options));
        var backend = new FakeAgentBackend(); backend.Results.Enqueue(next =>
        {
            var recovered = JsonSerializer.Deserialize<FactoryState>(File.ReadAllText(Path.Combine(current, "state.json")), FactoryJson.Options)!;
            Assert.Contains("product.txt", recovered.WorkItems.Single().ChangedPaths); Assert.Contains("product.txt", recovered.FactoryRunChangedPaths); return Envelope(next, "approved");
        });
        Assert.Equal("COMPLETED", (await Create(temp.Path, workflow, current, backend).ContinueAsync(default)).FactoryOutcome);
    }

    [Fact] public async Task InterruptedWorkspaceWriteAttemptRetainsChangesBeforeRetry()
    {
        using var temp = new TestWorkspace(); var workflow = DefaultWorkflow(temp); var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        temp.Write(".idd/factory/current/request.md", "Resume task"); temp.Write(".idd/factory/current/work-items/001-one.md", "# One"); temp.Write("product.txt", "before");
        var state = StateStoreTests.State() with { WorkflowHash = workflow.Hash, CurrentWorkflowStep = "execute", CurrentAttemptId = "A000001", AttemptSequence = 1 };
        state.WorkItems.Add(new WorkItemState { Id = "one", Sequence = 1, Kind = WorkItemKind.Subtask, Status = WorkItemStatus.Running, ContractPath = "work-items/001-one.md", CurrentAttemptId = "A000001", AttemptCount = 1 });
        await new FileFactoryStateStore(current, new FactoryStateValidator()).CreateAsync(state, default);
        PersistedWorkspaceWriteInvocation(temp, current, state); WriteWorkspaceBaseline(temp, "before"); temp.Write("product.txt", "interrupted change");
        var backend = new FakeAgentBackend(); backend.Results.Enqueue(next =>
        {
            var recovered = JsonSerializer.Deserialize<FactoryState>(File.ReadAllText(Path.Combine(current, "state.json")), FactoryJson.Options)!;
            Assert.Contains("product.txt", recovered.WorkItems.Single().ChangedPaths); Assert.Contains("product.txt", recovered.FactoryRunChangedPaths); return Envelope(next, "completed");
        }); backend.Results.Enqueue(next => Envelope(next, "approved"));
        Assert.Equal("COMPLETED", (await Create(temp.Path, workflow, current, backend).ContinueAsync(default)).FactoryOutcome);
    }

    [Fact] public async Task NestedBuildArtifactsDoNotEnterChangedScope()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = Path.Combine(temp.Path, ".idd", "factory", "current"); var backend = new FakeAgentBackend();
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem([])));
        backend.Results.Enqueue(invocation => { temp.Write("src/App/product.cs", "product"); temp.Write("src/App/bin/generated.dll", "bin"); temp.Write("src/App/obj/generated.cs", "obj"); return Envelope(invocation, "completed"); });
        backend.Results.Enqueue(invocation =>
        {
            var state = JsonSerializer.Deserialize<FactoryState>(File.ReadAllText(Path.Combine(current, "state.json")), FactoryJson.Options)!;
            Assert.Equal(["src/App/product.cs"], state.WorkItems.Single().ChangedPaths); return Envelope(invocation, "approved");
        });
        Assert.Equal("COMPLETED", (await Create(temp.Path, workflow, current, backend).RunAsync(request, "test", default)).FactoryOutcome);
    }

    [Fact] public async Task ConfirmationDetectsPathRuleOnlyPolicyMutation()
    {
        using var temp = new TestWorkspace(); var request = temp.Write("task.md", "Task"); var workflow = DefaultWorkflow(temp); var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        var policy = "version: 1\nchecks:\n  gate:\n    run: dotnet --version > policy-ran.txt\n    confirmation: required\ndefault:\n  use: []\nsubtask:\n  rules:\n    - paths:\n        - src/A/**\n      use:\n        - gate\n    - fallback: true\n      use:\n        - gate\n";
        temp.Write(".idd/verification.yaml", policy); var backend = new FakeAgentBackend(); backend.Results.Enqueue(invocation => Envelope(invocation, "ready", OneItem(["gate"]))); backend.Results.Enqueue(invocation => { temp.Write("src/A/a.cs", "a"); return Envelope(invocation, "completed"); });
        var runtime = Create(temp.Path, workflow, current, backend);
        Assert.Equal("VERIFICATION_CONFIRMATION_REQUIRED", (await runtime.RunAsync(request, "test", default)).FactoryOutcome);
        temp.Write(".idd/verification.yaml", policy.Replace("src/A/**", "src/B/**"));
        Assert.Equal("VERIFICATION_POLICY_CHANGED", (await runtime.ContinueAsync(default, confirmation: VerificationConfirmation.Approve)).FactoryOutcome);
        Assert.False(File.Exists(Path.Combine(temp.Path, "policy-ran.txt")));
    }

    private static FactoryRuntime Create(string workspace, WorkflowDefinition workflow, string current, IAgentBackend backend, VerificationEngine? verification = null)
    { var validator = new FactoryStateValidator(); var clock = new FakeClock(); return new(workspace, workflow, new FileFactoryStateStore(current, validator), new AgentExecutor(backend, new AgentResultValidator()), verification ?? new VerificationEngine(workspace, current), new FactoryEventWriter(current, clock), clock); }
    private static AgentResultEnvelope Envelope(AgentInvocation invocation, string outcome, object? payload = null, string? reason = null)
    { JsonElement? element = payload is null ? null : JsonSerializer.SerializeToElement(payload, FactoryJson.Options); return new() { ProtocolVersion = AgentInvocation.CurrentProtocolVersion, RunId = invocation.RunId, AttemptId = invocation.AttemptId, Role = invocation.Role, Outcome = outcome, Reason = reason, Payload = element }; }
    private static void AssertClarificationPayload(JsonElement? payload)
    { Assert.Equal("Which storage mode should be used?", payload!.Value.GetProperty("question").GetString()); Assert.Equal(new[] { "memory", "file" }, payload.Value.GetProperty("options").EnumerateArray().Select(x => x.GetString())); }
    private static void AssertIntentRequiredPayload(JsonElement? payload)
    {
        var decision = Assert.Single(payload!.Value.GetProperty("missingIntentDecisions").EnumerateArray());
        Assert.Equal("Staged registration", decision.GetProperty("area").GetString());
        Assert.Contains("stage boundaries", decision.GetProperty("whyBlocking").GetString());
        Assert.Equal(new[] { "Define the staged registration contract.", "Define idempotency and lost-response recovery rules." }, decision.GetProperty("requiredDecisions").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(new[] { "IDD-0002", "IDD-0006" }, decision.GetProperty("intentReferences").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("idd-intent-change", decision.GetProperty("recommendedNextWorkflow").GetString());
    }
    private static void AssertInvocation(AgentInvocation invocation, string role, string skillName, AgentExecutionProfile executionProfile)
    { Assert.Equal(role, invocation.Role); Assert.Equal(skillName, invocation.SkillName); Assert.Equal(executionProfile, invocation.ExecutionProfile); Assert.False(string.IsNullOrWhiteSpace(invocation.Input)); }
    private static WorkflowDefinition DefaultWorkflow(TestWorkspace temp)
    { var source = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "runtime", "Idd.Factory", "factory-workflow.yaml")); return new WorkflowDefinitionLoader().Load(temp.Path, source); }
    private static void EnqueueHappyPath(FakeAgentBackend backend)
    {
        backend.Results.Enqueue(invocation => Envelope(invocation, "ready", new { workItems = new[] { new { id = "one", sequence = 1, kind = "subtask", contractMarkdown = "# One", dependencies = Array.Empty<string>(), coveredWorkItems = Array.Empty<string>(), verificationCheckIds = Array.Empty<string>() } } }));
        backend.Results.Enqueue(invocation => Envelope(invocation, "completed")); backend.Results.Enqueue(invocation => Envelope(invocation, "approved"));
    }
    private static object OneItem(string[] checks) => new { workItems = new[] { new { id = "one", sequence = 1, kind = "subtask", contractMarkdown = "# One", dependencies = Array.Empty<string>(), coveredWorkItems = Array.Empty<string>(), verificationCheckIds = checks } } };
    private static AgentInvocation PersistedWorkspaceWriteInvocation(TestWorkspace temp, string current, FactoryState state)
    {
        var invocation = new AgentInvocation { RunId = state.RunId, AttemptId = "A000001", Role = "implementer", WorkItemId = "one", Workspace = temp.Path, ResultPath = Path.Combine(current, "attempts", "A000001", "result.json"), SkillName = "idd-factory-execute-subtask", ExecutionProfile = AgentExecutionProfile.WorkspaceWrite, Input = "input", StartedAt = DateTimeOffset.UnixEpoch };
        temp.Write(".idd/factory/current/attempts/A000001/invocation.json", JsonSerializer.Serialize(invocation, FactoryJson.Options));
        return invocation;
    }
    private static void WriteWorkspaceBaseline(TestWorkspace temp, string content)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content)));
        temp.Write(".idd/factory/current/attempts/A000001/workspace-before.json", JsonSerializer.Serialize(new { schemaVersion = 1, files = new Dictionary<string, string> { ["product.txt"] = hash } }, FactoryJson.Options));
    }
    private static async Task<FactoryState> SeedVerificationFixContinuationAsync(TestWorkspace temp, WorkflowDefinition workflow, string current)
    {
        temp.Write(".idd/factory/current/request.md", "Resume verification fix"); temp.Write(".idd/factory/current/work-items/001-one.md", "# One");
        var state = StateStoreTests.State() with
        {
            WorkflowHash = workflow.Hash,
            CurrentWorkflowStep = "execute",
            RunStatus = FactoryRunStatus.Blocked,
            Blocker = new FactoryBlocker("BLOCKED", "Repair is paused.", "Resolve the reported condition and continue."),
            PendingContinuation = new PendingContinuation(ContinuationKind.SemanticInvocation, "execute", "one", "subtask", "BLOCKED", true,
                SemanticOperationKind.SubtaskVerificationFix, "Mode:\nverification-fix\n\nScope:\nwork item one")
        };
        state.WorkItems.Add(new WorkItemState { Id = "one", Sequence = 1, Kind = WorkItemKind.Subtask, Status = WorkItemStatus.Blocked, ContractPath = "work-items/001-one.md", AttemptCount = 1, VerificationFixAttemptCount = 1 });
        await new FileFactoryStateStore(current, new FactoryStateValidator()).CreateAsync(state, default);
        return state;
    }
    private static object IntentRequiredDetails() => new
    {
        missingIntentDecisions = new[]
        {
            new
            {
                area = "Staged registration",
                whyBlocking = "The decomposition cannot define safe stage boundaries without durable registration semantics.",
                requiredDecisions = new[] { "Define the staged registration contract.", "Define idempotency and lost-response recovery rules." },
                intentReferences = new[] { "IDD-0002", "IDD-0006" },
                recommendedNextWorkflow = "idd-intent-change"
            }
        }
    };
    private static async Task WritePersistedAttemptAsync(TestWorkspace temp, string current, FactoryState state, string attemptId, string? result, object? payload = null, string? reason = null)
    {
        state.CurrentAttemptId = attemptId; state.AttemptSequence = int.Parse(attemptId[1..]); state.WorkItems.Single().CurrentAttemptId = attemptId;
        await new FileFactoryStateStore(current, new FactoryStateValidator()).SaveAsync(state, state.Revision, default);
        var invocation = new AgentInvocation { RunId = state.RunId, AttemptId = attemptId, Role = "implementer", WorkItemId = "one", Workspace = temp.Path, ResultPath = Path.Combine(current, "attempts", attemptId, "result.json"), SkillName = "idd-factory-execute-subtask", ExecutionProfile = AgentExecutionProfile.WorkspaceWrite, Input = state.PendingContinuation!.OperationInput!, StartedAt = DateTimeOffset.UnixEpoch };
        temp.Write($".idd/factory/current/attempts/{attemptId}/invocation.json", JsonSerializer.Serialize(invocation, FactoryJson.Options));
        if (result is not null) temp.Write($".idd/factory/current/attempts/{attemptId}/result.json", JsonSerializer.Serialize(Envelope(invocation, result, payload, reason), FactoryJson.Options));
    }
    private static object CheckpointedItem() => new { workItems = new object[]
    {
        new { id = "one", sequence = 1, kind = "subtask", contractMarkdown = "# One", dependencies = Array.Empty<string>(), coveredWorkItems = Array.Empty<string>(), verificationCheckIds = Array.Empty<string>() },
        new { id = "review", sequence = 2, kind = "review-checkpoint", contractMarkdown = "# Review", dependencies = new[] { "one" }, coveredWorkItems = new[] { "one" }, verificationCheckIds = Array.Empty<string>() }
    } };
    private static VerificationEvidence ReadEvidence(string current, string relative) => ReadEvidence(System.IO.Path.Combine(current, relative));
    private static VerificationEvidence ReadEvidence(string path) => JsonSerializer.Deserialize<VerificationEvidence>(File.ReadAllText(path), FactoryJson.Options)!;

    private sealed class FakeAgentBackend : IAgentBackend
    {
        public Queue<Func<AgentInvocation, AgentResultEnvelope>> Results { get; } = new(); public List<string> Roles { get; } = [];
        public List<AgentInvocation> Invocations { get; } = [];
        public Task<AgentRunHandle> StartAsync(AgentInvocation invocation, CancellationToken cancellationToken) { Roles.Add(invocation.Role); Invocations.Add(invocation); var result = Results.Dequeue()(invocation); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(invocation.ResultPath)!); File.WriteAllText(invocation.ResultPath, JsonSerializer.Serialize(result, FactoryJson.Options)); return Task.FromResult(new AgentRunHandle(invocation.AttemptId, 1, invocation.AttemptId)); }
        public Task<AgentProcessResult> WaitAsync(AgentRunHandle handle, CancellationToken cancellationToken) => Task.FromResult(new AgentProcessResult(0, "", "", true, false, AgentTerminationKind.CleanExit));
        public Task CancelAsync(AgentRunHandle handle, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class FakeClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-01-01T00:00:00Z"); }
    private sealed class ThrowOnceVerificationEngine(string workspace, string current) : VerificationEngine(workspace, current)
    {
        public bool Fail { get; set; } = true;
        public override Task<VerificationResult> RunContextAsync(string context, IEnumerable<string> changedPaths, CancellationToken cancellationToken) =>
            Fail ? throw new VerificationException("TEST_VERIFICATION_EXCEPTION", "The verification configuration needs repair.") : base.RunContextAsync(context, changedPaths, cancellationToken);
    }
}
