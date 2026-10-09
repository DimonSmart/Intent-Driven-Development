using Idd.Factory.Report;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class FactoryRoutingAssertionsTests
{
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
        FactoryRoutingAssertions.VerifyUnavailableModel(report, root, "removed-model");
    }

    [Fact]
    public void VerifyUnavailableModel_RejectsBlockedWithoutAnAttemptedSpawn()
    {
        var (report, root) = UnavailableModelReport();
        root.SpawnRecords.Clear();
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            FactoryRoutingAssertions.VerifyUnavailableModel(report, root, "removed-model"));
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("missing-reason")]
    [InlineData("fallback-model")]
    [InlineData("fallback-modern")]
    [InlineData("inherited-fallback")]
    [InlineData("completed-worker")]
    public void VerifyUnavailableModel_RejectsSilentFallbackOrFalseSuccess(string fault)
    {
        var (blocked, root) = UnavailableModelReport();
        var report = new FactoryRunReport
        {
            Run = new RunInfo
            {
                Result = fault == "completed" ? "completed" : "blocked",
                Reason = fault == "missing-reason" ? "An error occurred." : blocked.Run.Reason
            },
            Completion = blocked.Completion
        };
        if (fault is "fallback-model" or "fallback-modern" or "inherited-fallback")
            root.SpawnRecords["fallback"] = new SpawnRecord
            {
                Task = fault == "fallback-modern"
                    ? "You are the Factory worker.\\n--- Factory worker assignment ---"
                    : "Use idd-factory-execute-subtask.",
                RequestedModel = fault == "inherited-fallback" ? null : "root-default"
            };
        if (fault == "completed-worker")
            report.Tasks.Add(new TaskReport
            {
                ExecutionProfile = "standard", RequestedModel = "removed-model",
                ActualModel = "removed-model", Status = "completed"
            });
        var exception = Record.Exception(() =>
            FactoryRoutingAssertions.VerifyUnavailableModel(report, root, "removed-model"));
        Assert.IsAssignableFrom<Xunit.Sdk.XunitException>(exception);
    }

    private static (FactoryRunReport Report, CodexRollout Root) UnavailableModelReport()
    {
        var root = new CodexRollout();
        root.SpawnRecords["rejected"] = new SpawnRecord { RequestedModel = "removed-model" };
        return (new FactoryRunReport
        {
            Run = new RunInfo { Result = "blocked", Reason = "Model 'removed-model' has been removed; rerun idd-factory-configure." },
            Completion = new CompletionReport { DeclaredResult = "blocked" }
        }, root);
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
    public void Verify_RejectsBrokenRouting(string fault)
    {
        var report = Report();
        var economy = report.Tasks[0];
        report.Tasks[0] = new TaskReport
        {
            ExecutionProfile = fault == "wrong-profile" ? "standard" : "economy",
            RequestedModel = fault switch
            {
                "missing-spawn-settings" => null,
                "swapped-mappings" => "model-b",
                _ => economy.RequestedModel
            },
            RequestedReasoningEffort = fault == "missing-spawn-settings" ? null : economy.RequestedReasoningEffort,
            ActualModel = fault switch
            {
                "wrong-actual" => "unexpected-model",
                "missing-actual-model" => null,
                _ => economy.ActualModel
            },
            ActualReasoningEffort = fault == "missing-actual-reasoning" ? null : economy.ActualReasoningEffort
        };
        if (fault == "wrong-profile")
            report.Tasks[1] = new TaskReport
            {
                ExecutionProfile = "economy", RequestedModel = "model-b", RequestedReasoningEffort = "medium"
            };
        if (fault == "root-default")
            for (var index = 0; index < report.Tasks.Count; index++)
            {
                report.Tasks[index] = new TaskReport
                {
                    ExecutionProfile = report.Tasks[index].ExecutionProfile,
                    RequestedModel = "model-a", RequestedReasoningEffort = "medium"
                };
            }
        if (fault == "missing-profile")
            report.Tasks.RemoveAt(2);
        var exception = Record.Exception(() => FactoryRoutingAssertions.Verify(report, Mappings));
        Assert.IsAssignableFrom<Xunit.Sdk.XunitException>(exception);
    }

    private static FactoryRunReport Report() => new()
    {
        Tasks = Mappings.Select(mapping => new TaskReport
        {
            ExecutionProfile = mapping.Key,
            RequestedModel = mapping.Value.Model,
            RequestedReasoningEffort = mapping.Value.ReasoningEffort,
            ActualModel = mapping.Value.Model,
            ActualReasoningEffort = mapping.Value.ReasoningEffort
        }).ToList()
    };
}
