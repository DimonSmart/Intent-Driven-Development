using Idd.Factory.Report;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

internal static class FactoryRoutingAssertions
{
    internal static void VerifyUnavailableModel(FactoryRunReport report, CodexRollout root, string unavailableModel)
    {
        Assert.Equal("blocked", report.Run.Result);
        Assert.Equal("blocked", report.Completion.DeclaredResult);
        Assert.NotNull(report.Run.Reason);
        Assert.Contains(unavailableModel, report.Run.Reason, StringComparison.Ordinal);
        Assert.NotEqual("passed", report.Completion.ProjectVerification);
        Assert.DoesNotContain(report.Agents, agent => agent.Role is "unknown" or "subagent");

        var workerSpawns = root.SpawnRecords.Values.Where(spawn =>
            spawn.RequestedModel is not null ||
            spawn.Task?.Contains("idd-factory-execute-subtask", StringComparison.Ordinal) == true ||
            spawn.Task?.Contains("You are the Factory worker.", StringComparison.Ordinal) == true ||
            spawn.Task?.Contains("--- Factory worker assignment ---", StringComparison.Ordinal) == true ||
            report.Agents.Any(agent => agent.Role == "worker" &&
                spawn.Children.Contains(agent.ThreadId, StringComparer.Ordinal))).ToArray();
        // A rejected native spawn may have no child rollout, but the attempted
        // worker spawn itself must be trace-visible. A mere BLOCKED message is not proof.
        Assert.NotEmpty(workerSpawns);
        Assert.All(workerSpawns, spawn => Assert.Equal(unavailableModel, spawn.RequestedModel));
        Assert.All(report.Tasks, task =>
        {
            Assert.Equal("standard", task.ExecutionProfile);
            Assert.Equal(unavailableModel, task.RequestedModel);
            Assert.True(task.ActualModel is null || task.ActualModel == unavailableModel,
                "An unavailable model must never be replaced by another model.");
            Assert.NotEqual("completed", task.Status);
        });
    }

    internal static void Verify(FactoryRunReport report,
        IReadOnlyDictionary<string, WorkerExecutionSettings> mappings)
    {
        LiveEvalSettings.ValidateProfiles(mappings);
        Assert.NotEmpty(report.Tasks);
        Assert.Equal(LiveEvalSettings.Profiles,
            report.Tasks.Select(task => task.ExecutionProfile).Distinct().OrderBy(profile => profile).ToArray());
        foreach (var task in report.Tasks)
        {
            Assert.NotNull(task.ExecutionProfile);
            var expected = mappings[task.ExecutionProfile];
            Assert.Equal(expected.Model, task.RequestedModel);
            Assert.Equal(expected.ReasoningEffort, task.RequestedReasoningEffort);
            Assert.Equal(expected.Model, task.ActualModel);
            Assert.Equal(expected.ReasoningEffort, task.ActualReasoningEffort);
        }
        Assert.DoesNotContain(report.Diagnostics, diagnostic =>
            diagnostic.Code is "worker_execution_profile_mismatch" or "worker_execution_settings_mismatch"
                or "worker_actual_settings_unavailable");
    }
}
