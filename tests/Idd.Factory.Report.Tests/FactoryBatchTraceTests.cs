using Idd.Factory.Report;
using Xunit;

namespace Idd.Factory.Report.Tests;

public sealed class FactoryBatchTraceTests
{
    [Theory]
    [InlineData("idd_factory_root_b0001", "root", 1, null)]
    [InlineData("idd_factory_root_b12345", "root", 12345, null)]
    [InlineData("idd_factory_planner_b0002_0001", "planner", 2, 1)]
    [InlineData("idd_factory_worker_b0002_10101", "worker", 2, 10101)]
    [InlineData("idd_factory_planner_01", "planner", 0, null)]
    [InlineData("idd_factory_worker_99", "worker", 0, null)]
    public void RecognizesBatchScopedAndLegacyNativeNames(
        string taskName, string role, int batch, int? attempt)
    {
        var parsed = Assert.IsType<FactoryBatchTrace.Name>(
            FactoryBatchTrace.ParseName(taskName));
        Assert.Equal(role, parsed.Role);
        Assert.Equal(batch, parsed.BatchOrdinal);
        Assert.Equal(attempt, parsed.AttemptOrdinal);
    }

    [Theory]
    [InlineData("idd_factory_worker_b0001")]
    [InlineData("idd_factory_root_b0000")]
    [InlineData("idd_factory_planner_b0000_0001")]
    [InlineData("idd_factory_planner_b0001_0000")]
    [InlineData("idd_factory_worker_b0001_0001_extra")]
    [InlineData("idd_factory_root_01")]
    public void RejectsMalformedOrAmbiguousNativeNames(string name) =>
        Assert.Null(FactoryBatchTrace.ParseName(name));

    [Theory]
    [InlineData("max_workers_per_batch = 3", 3)]
    [InlineData("Root assignment:\nmax_workers_per_batch: 8\nworktree: /tmp/repo", 8)]
    [InlineData("{\"max_workers_per_batch\": 3}", 3)]
    [InlineData("max_workers_per_batch=3\nmax_workers_per_batch = 3", 3)]
    [InlineData("max_workers_per_batch = 0", null)]
    [InlineData("max_workers_per_batch = -1", null)]
    [InlineData("max_workers_per_batch = 1.5", null)]
    [InlineData("max_workers_per_batch=2\nmax_workers_per_batch=3", null)]
    [InlineData("max_workers_per_batch=3\nmax_workers_per_batch=-1", null)]
    [InlineData("max_workers_per_batch=3\nmax_workers_per_batch=2.5", null)]
    [InlineData("max_workers_per_batch=3\nmax_workers_per_batch=unknown", null)]
    [InlineData("max_workers_per_batch=2147483648", null)]
    [InlineData("No explicit limit", null)]
    public void BatchBudgetIsFromSingleUnambiguousNativeAssignment(string assignment, int? expected) =>
        Assert.Equal(expected, FactoryBatchTrace.ParseBatchLimit(assignment));

    [Theory]
    [InlineData("CONTINUE", 3, "BATCH_LIMIT", true)]
    [InlineData("CONTINUE", 4, "BATCH_LIMIT", true)]
    [InlineData("CONTINUE", 9, "BATCH_LIMIT", true)]
    [InlineData("CONTINUE", 2, "BATCH_LIMIT", false)]
    [InlineData("CONTINUE", 0, "BATCH_LIMIT", false)]
    [InlineData("CONTINUE", 3, "ERROR", false)]
    [InlineData("COMPLETED", 0, "DONE", true)]
    [InlineData("COMPLETED", 4, "DONE", true)]
    [InlineData("BLOCKED", 1, "CHILD_UNCONFIRMED", true)]
    [InlineData("BLOCKED", 9, "CHECKPOINT_INCONSISTENT", true)]
    [InlineData("NEEDS_INPUT", 2, "PLANNER_QUESTION", true)]
    public void StrictBatchTerminalStatus(string status, int count, string reason, bool expected)
    {
        var message = $"Status: {status}\nCompletedWorkers: {count}\nReason: {reason}";
        Assert.Equal(expected, FactoryBatchTrace.TryParseTerminal(message, 3, out _));
    }

    [Theory]
    [InlineData("Status: CONTINUE\nCompletedWorkers: 3\nReason: BATCH_LIMIT\nStatus: BLOCKED")]
    [InlineData("Status: CONTINUE\nCompletedWorkers: 3\nReason: BATCH_LIMIT\nCompletedWorkers: 3")]
    [InlineData("Status: CONTINUE\nCompletedWorkers: -1\nReason: BATCH_LIMIT")]
    [InlineData("Status: COMPLETED\nCompletedWorkers: 0")]
    [InlineData("Status: NEEDS_INPUT\nCompletedWorkers: 0\nReason: PLANNER_QUESTION\nQuestionFile: /fake/question.md")]
    [InlineData("Status: CONTINUE\nCompletedWorkers: 3\nReason: BATCH_LIMIT\nQuestionFile: .idd/factory/current/question.md")]
    [InlineData("Status: CONTINUE\nCompletedWorkers: 2147483648\nReason: BATCH_LIMIT")]
    [InlineData("Status: CONTINUE\nCompletedWorkers: 99999999999999999999\nReason: BATCH_LIMIT")]
    [InlineData("Status: CONTINUE\nCompletedWorkers: 2\nReason: BATCH_LIMIT")]
    public void TerminalContractRejectsAmbiguityAndInvalidCheckpoint(string text)
    {
        Assert.False(FactoryBatchTrace.TryParseTerminal(text, 3, out _));
    }

    [Fact]
    public void AcceptsExactPersistedQuestionReference()
    {
        const string message = "Status: NEEDS_INPUT\nCompletedWorkers: 1\nReason: PLANNER_QUESTION\nQuestionFile: .idd/factory/current/question.md";
        Assert.True(FactoryBatchTrace.TryParseTerminal(message, 3, out var result));
        Assert.Equal(".idd/factory/current/question.md", result?.QuestionFile);
    }
}
