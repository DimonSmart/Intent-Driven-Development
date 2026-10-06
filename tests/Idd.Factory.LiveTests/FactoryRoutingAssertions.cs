using Idd.Factory.Report;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

internal static class FactoryRoutingAssertions
{
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
            if (task.ActualModel is not null)
                Assert.Equal(expected.Model, task.ActualModel);
            if (task.ActualReasoningEffort is not null)
                Assert.Equal(expected.ReasoningEffort, task.ActualReasoningEffort);
        }
        Assert.DoesNotContain(report.Diagnostics, diagnostic =>
            diagnostic.Code is "worker_execution_profile_mismatch" or "worker_execution_settings_mismatch");
    }
}
