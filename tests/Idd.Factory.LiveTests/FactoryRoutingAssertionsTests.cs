using Idd.Factory.Report;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class FactoryRoutingAssertionsTests
{
    private const string RemovedModel = "removed-model";
    private const string Plan = "# Task\nImplement ProductCode.\n# ExecutionProfile\nstandard\n";
    private static readonly IReadOnlyDictionary<string, WorkerExecutionSettings> Mappings =
        new Dictionary<string, WorkerExecutionSettings>
        {
            ["economy"] = new("model-a", "medium"),
            ["standard"] = new("model-b", "medium"),
            ["strong"] = new("model-c", "medium")
        };

    [Fact]
    public void Verify_AcceptsConfirmedCorrectRouting() =>
        FactoryRoutingAssertions.Verify(Report(), Mappings);

    [Fact]
    public void VerifyUnavailableModel_AcceptsBlockedRejectionWithoutAWorker()
    {
        var (report, root) = UnavailableModelReport();
        FactoryRoutingAssertions.VerifyUnavailableModel(report, root, RemovedModel, "medium", Plan);
    }

    [Theory]
    [InlineData("no-spawn")]
    [InlineData("unknown-outcome")]
    [InlineData("wrong-model")]
    [InlineData("wrong-effort")]
    [InlineData("accepted-substitute")]
    [InlineData("other-factory-attempt")]
    [InlineData("no-fork")]
    [InlineData("no-planner")]
    [InlineData("no-plan")]
    [InlineData("no-reason")]
    [InlineData("completed")]
    [InlineData("completed-worker")]
    [InlineData("unknown-worker")]
    public void VerifyUnavailableModel_FailsClosedOnMissingEvidenceOrFallback(string fault)
    {
        var (report, root) = UnavailableModelReport();
        string plan = Plan;
        if (fault == "no-spawn") root.SpawnRecords.Clear();
        if (fault == "unknown-outcome") root.SpawnRecords["rejected"].SpawnOutcome = "unknown";
        if (fault == "wrong-model") root.SpawnRecords["rejected"].RequestedModel = "fallback";
        if (fault == "wrong-effort") root.SpawnRecords["rejected"].RequestedReasoningEffort = "high";
        if (fault == "accepted-substitute") root.SpawnRecords["rejected"].SpawnOutcome = "accepted";
        if (fault == "other-factory-attempt")
            root.SpawnRecords["fallback"] = new SpawnRecord
            {
                RequestedTaskName = "idd_factory_worker_02", RequestedModel = "model-b",
                SpawnOutcome = "accepted", Timestamp = DateTimeOffset.Parse("2026-10-16T10:00:03Z")
            };
        if (fault == "no-fork") root.Events.Clear();
        if (fault == "no-planner") report.Agents.Clear();
        if (fault == "no-plan") plan = "# Task\nImplement ProductCode.";
        if (fault is "no-reason" or "completed")
            report = new FactoryRunReport
            {
                Run = new RunInfo
                {
                    RootThreadId = "root", Result = fault == "completed" ? "completed" : "blocked",
                    Reason = fault == "no-reason" ? "Something failed" : report.Run.Reason,
                    StartedAt = report.Run.StartedAt
                },
                Completion = report.Completion, FactoryProjectState = report.FactoryProjectState,
                Agents = report.Agents, Tasks = report.Tasks
            };
        if (fault == "completed-worker")
            report.Tasks.Add(new TaskReport { Status = "completed" });
        if (fault == "unknown-worker")
            report.Agents.Add(new AgentReport { Role = "unknown", ThreadId = "maybe-worker", ParentThreadId = "root" });
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            FactoryRoutingAssertions.VerifyUnavailableModel(report, root, RemovedModel, "medium", plan));
    }

    [Fact]
    public void VerifyUnavailableModel_AllowsProvenUnrelatedChild()
    {
        var (report, root) = UnavailableModelReport();
        root.SpawnRecords["helper"] = new SpawnRecord
        {
            CallId = "helper", Task = "You are a helper. Explain this term.",
            Timestamp = DateTimeOffset.Parse("2026-10-16T10:00:03Z"),
            ChildThreadId = "helper-thread"
        };
        report.Agents.Add(new AgentReport { Role = "unknown", ThreadId = "helper-thread", ParentThreadId = "root" });
        FactoryRoutingAssertions.VerifyUnavailableModel(report, root, RemovedModel, "medium", Plan);
    }

    [Theory]
    [InlineData("wrong-profile")]
    [InlineData("swapped-mappings")]
    [InlineData("root-default")]
    [InlineData("missing-spawn-settings")]
    [InlineData("missing-profile")]
    [InlineData("wrong-actual")]
    [InlineData("missing-actual-model")]
    [InlineData("missing-actual-reasoning")]
    [InlineData("reused-thread")]
    [InlineData("unfinished")]
    [InlineData("diagnostic-conflict")]
    public void Verify_RejectsBrokenRouting(string fault)
    {
        var report = Report();
        var first = report.Tasks[0];
        report.Tasks[0] = new TaskReport
        {
            AgentThreadId = fault == "reused-thread" ? report.Tasks[1].AgentThreadId : first.AgentThreadId,
            Status = fault == "unfinished" ? "interrupted" : first.Status,
            ExecutionProfile = fault == "wrong-profile" ? "economy" : first.ExecutionProfile,
            RequestedModel = fault switch
            {
                "missing-spawn-settings" => null,
                "swapped-mappings" => "model-c",
                "root-default" => "model-a",
                _ => first.RequestedModel
            },
            RequestedReasoningEffort = fault == "missing-spawn-settings" ? null : first.RequestedReasoningEffort,
            ActualModel = fault switch
            {
                "wrong-actual" => "unexpected-model",
                "missing-actual-model" => null,
                _ => first.ActualModel
            },
            ActualReasoningEffort = fault == "missing-actual-reasoning" ? null : first.ActualReasoningEffort
        };
        if (fault == "missing-profile")
            report.Tasks.RemoveAt(2);
        if (fault == "diagnostic-conflict")
            report.Diagnostics.Add(new Diagnostic { Code = "agent_role_conflict" });
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => FactoryRoutingAssertions.Verify(report, Mappings));
    }

    private static (FactoryRunReport Report, CodexRollout Root) UnavailableModelReport()
    {
        var root = new CodexRollout { ThreadId = "root" };
        root.SpawnRecords["rejected"] = new SpawnRecord
        {
            CallId = "rejected", RequestedTaskName = "idd_factory_worker_01",
            RequestedModel = RemovedModel, RequestedReasoningEffort = "medium",
            SpawnOutcome = "rejected", RejectionCode = "model_not_found",
            RejectionMessage = "Model removed-model is unavailable",
            Timestamp = DateTimeOffset.Parse("2026-10-16T10:00:02Z")
        };
        root.Events.Add(new CodexEvent
        {
            ToolId = "rejected", ToolName = "spawn_agent",
            ToolArguments = "{\"fork_turns\":\"none\"}"
        });
        return (new FactoryRunReport
        {
            Run = new RunInfo
            {
                RootThreadId = "root", Result = "blocked",
                Reason = "Host rejected model 'removed-model' because it is unavailable.",
                StartedAt = DateTimeOffset.Parse("2026-10-16T10:00:00Z")
            },
            Completion = new CompletionReport { DeclaredResult = "blocked" },
            FactoryProjectState = new FactoryProjectState
            {
                CurrentRequestPresent = true, CurrentPlanPresent = true
            },
            Agents = [new AgentReport { Role = "planner", PlannerOutcome = "Tasks", ThreadId = "planner", ParentThreadId = "root" }]
        }, root);
    }

    private static FactoryRunReport Report() => new()
    {
        Tasks = new[] { "standard", "strong", "economy" }.Select((profile, index) => new TaskReport
        {
            AgentThreadId = $"worker-{index}",
            ExecutionProfile = profile,
            Status = "completed",
            RequestedModel = Mappings[profile].Model,
            RequestedReasoningEffort = Mappings[profile].ReasoningEffort,
            ActualModel = Mappings[profile].Model,
            ActualReasoningEffort = Mappings[profile].ReasoningEffort
        }).ToList()
    };
}
