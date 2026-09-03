using System.Text.Json;
using Idd.Factory.Domain;

namespace Idd.Factory.Tests;

public sealed class BatchRuntimeTests
{
    [Fact]
    public async Task RuntimeExecutesWholeBatchThenPlansAgainAndFinalizesWithoutReview()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(_ => "# Task\n\nImplement A.\n\n# Task\n\nImplement B.");
        backend.Enqueue(_ => "Implemented A in the current product.");
        backend.Enqueue(_ => "Implemented B and preserved the surrounding behavior.");
        backend.Enqueue(_ => "");

        var outcome = await FactoryRuntimeTestHarness.CreateRuntime(temp.Path, backend)
            .RunRequestAsync("Complete A and B.", "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal(["planning", "implementation", "implementation", "planning"], backend.Invocations.Select(x => x.Capability));
        Assert.All(backend.Invocations.Where(x => x.Capability == "implementation"), x => Assert.Equal("executor", x.Role));
        Assert.DoesNotContain(backend.Invocations, x => x.Capability.Contains("review", StringComparison.Ordinal));
        Assert.Equal("Implement A.", (await File.ReadAllTextAsync(Path.Combine(outcome.ResultDirectory!, "work-items", "W000001", "contract.md"))).Trim());
        Assert.Contains("Implemented B", await File.ReadAllTextAsync(Path.Combine(outcome.ResultDirectory!, "attempts", "A000003", "semantic-result.md")));
        using var completed = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outcome.ResultDirectory!, "completed-work.json")));
        Assert.False(completed.RootElement.GetProperty("completed")[0].TryGetProperty("capability", out _));
    }

    [Fact]
    public async Task ExecutorDiscoveryDoesNotInterruptBatchAndOnlyNextPlannerCreatesWork()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(_ => "# Task\n\nImplement A.\n\n# Task\n\nImplement B.");
        backend.Enqueue(_ => "Implemented A. Discovered additional-work-required is needed for C, but did not create it.");
        backend.Enqueue(_ => "Implemented B against the latest repository state.");
        backend.Enqueue(_ => "# Task\n\nImplement C discovered by the previous batch.");
        backend.Enqueue(_ => "Implemented C.");
        backend.Enqueue(_ => "");

        var outcome = await FactoryRuntimeTestHarness.CreateRuntime(temp.Path, backend)
            .RunRequestAsync("Complete the integrated change.", "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal(
            ["planning", "implementation", "implementation", "planning", "implementation", "planning"],
            backend.Invocations.Select(x => x.Capability));
        var contracts = Directory.GetFiles(Path.Combine(outcome.ResultDirectory!, "work-items"), "contract.md", SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.Ordinal)
            .Select(File.ReadAllText)
            .ToArray();
        Assert.Equal(3, contracts.Length);
        Assert.Contains("Implement C", contracts[2]);
    }

    [Fact]
    public async Task FailedFinalVerificationFeedsANewPlanningCycle()
    {
        using var temp = new TestWorkspace();
        temp.Write(".idd/verification.yaml", """
            version: 1
            checks:
              final-check:
                run: if (Test-Path marker.txt) { exit 0 } else { exit 1 }
            default:
              use: []
            final:
              use:
                - final-check
            """);
        var backend = new FakeAgentBackend();
        backend.Enqueue(_ => "");
        backend.Enqueue(invocation =>
        {
            Assert.Contains("Strict final verification failed", invocation.Input);
            return "# Task\n\nCreate the missing marker required by integrated verification.";
        });
        backend.Enqueue(_ =>
        {
            File.WriteAllText(Path.Combine(temp.Path, "marker.txt"), "ready");
            return "Created the missing marker.";
        });
        backend.Enqueue(_ => "");

        var outcome = await FactoryRuntimeTestHarness.CreateRuntime(temp.Path, backend)
            .RunRequestAsync("Produce a final-verifiable marker.", "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal(3, backend.Invocations.Count(x => x.Capability == "planning"));
        Assert.True(File.Exists(Path.Combine(temp.Path, "marker.txt")));
    }
}
