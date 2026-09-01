using System.Text.Json;
using Idd.Factory.Agents;
using Idd.Factory.Configuration;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.Runtime;
using Idd.Factory.State;
using Idd.Factory.Telemetry;
using Idd.Factory.Verification;
using static Idd.Factory.Tests.FactoryRuntimeTestHarness;

namespace Idd.Factory.Tests;

public sealed class VerificationRetryTests
{
    [Fact]
    public async Task FailedIntermediateVerificationPersistsReadyCurrentWithoutBlockingOrPlanMutation()
    {
        using var temp = new TestWorkspace();
        using var cancellation = new CancellationTokenSource();
        var backend = new FakeAgentBackend();
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = new[] { Work("Implement", "implementation") } }));
        backend.Enqueue(x =>
        {
            File.WriteAllText(Path.Combine(x.Workspace, "result.txt"), "changed");
            return Envelope(x, "completed");
        });
        var currentDirectory = Path.Combine(temp.Path, ".idd", "factory", "current");
        var fileStore = new FileFactoryStateStore(currentDirectory, new FactoryStateValidator());
        var stateStore = new CancelAfterFailedVerificationStore(fileStore, cancellation);
        var clock = new FakeClock();
        var runtime = new FactoryRuntime(temp.Path, CreateConfiguration(), stateStore,
            new FactoryAgentExecutor(backend, new FactoryAgentResultValidator()),
            new SequencedVerificationEngine(temp.Path, (VerificationStatus.Failed, "compile", 1, "compile failed")),
            new FactoryEventWriter(currentDirectory, clock), clock);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runtime.RunRequestAsync("Inspect the failed-verification transition", "test", cancellation.Token));
        var state = (await fileStore.LoadAsync(default))!;

        Assert.Equal(FactoryRunStatus.Running, state.RunStatus);
        Assert.Equal(CurrentWorkPhase.Ready, state.CurrentPhase);
        Assert.Equal("W000001", state.Current!.Id);
        Assert.Null(state.Blocker);
        Assert.Null(state.PendingContinuation);
        Assert.Null(state.PendingVerificationSession);
        Assert.Empty(state.Completed);
        Assert.Empty(state.Remaining);
        Assert.Equal(1, state.PlanRevision);
        Assert.Equal(1, state.Current.AttemptCount);
        Assert.Equal(state.Current.LastResultRef, Assert.Single(state.Current.PriorResultRefs));
        Assert.Single(state.Current.VerificationEvidenceRefs);
        Assert.Single(state.VerificationEvidenceRefs);
        Assert.Contains("result.txt", state.Current.ChangedPaths);
        Assert.Contains("result.txt", state.FactoryRunChangedPaths);
    }

    [Fact]
    public async Task FailedIntermediateVerificationRetriesSameCurrentWithPriorResultAndBoundedDiagnostics()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = new[] { Work("Implement", "implementation") } }));
        backend.Enqueue(_ => new SemanticAgentResult { Outcome = "completed", Summary = "first semantic result" });
        backend.Enqueue(_ => new SemanticAgentResult { Outcome = "completed", Summary = "corrected semantic result" });
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = Array.Empty<object>() }));
        backend.Enqueue(x => Envelope(x, "approved"));
        var diagnostic = "compiler error\n" + new string('x', 20_000) + "FULL_OUTPUT_END";
        var verification = new SequencedVerificationEngine(temp.Path,
            (VerificationStatus.Failed, "repository-fallback", 1, diagnostic),
            (VerificationStatus.Passed, "repository-fallback", 0, "passed"),
            (VerificationStatus.Passed, "repository-fallback", 0, "final passed"));

        var outcome = await CreateRuntime(temp.Path, backend, verification: verification)
            .RunRequestAsync("Implement and verify", "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        var implementations = backend.Invocations.Where(x => x.Capability == "implementation").ToArray();
        Assert.Equal(2, implementations.Length);
        Assert.All(implementations, invocation => Assert.Equal("W000001", invocation.WorkItemId));
        Assert.Contains("attempts/A000002/result.json", implementations[1].Input);
        Assert.Contains("Authoritative verification observations:", implementations[1].Input);
        Assert.Contains("Check: repository-fallback", implementations[1].Input);
        Assert.Contains("Status: failed", implementations[1].Input);
        Assert.Contains("Exit code: 1", implementations[1].Input);
        Assert.Contains("[verification output truncated; see evidence artifact]", implementations[1].Input);
        Assert.DoesNotContain("FULL_OUTPUT_END", implementations[1].Input);

        using var completed = JsonDocument.Parse(File.ReadAllText(Path.Combine(outcome.ResultDirectory!, "completed-work.json")));
        var work = Assert.Single(completed.RootElement.GetProperty("completed").EnumerateArray());
        Assert.Equal("W000001", work.GetProperty("id").GetString());
        Assert.Equal(2, work.GetProperty("verificationEvidenceRefs").GetArrayLength());
        var failedEvidence = JsonSerializer.Deserialize<VerificationEvidence>(
            File.ReadAllText(Path.Combine(outcome.ResultDirectory!, "verification", "V0001.json")), FactoryJson.Options)!;
        Assert.EndsWith("FULL_OUTPUT_END", failedEvidence.Output);
    }

    [Fact]
    public async Task RepeatedVerificationFailureStopsAtOrdinarySemanticAttemptBudgetWithLatestEvidence()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = new[] { Work("Implement", "implementation") } }));
        backend.Enqueue(x => Envelope(x, "completed"));
        backend.Enqueue(x => Envelope(x, "completed"));
        var verification = new SequencedVerificationEngine(temp.Path,
            (VerificationStatus.Failed, "compile", 1, "first failure"),
            (VerificationStatus.Failed, "compile", 2, "latest failure"));
        var configuration = new FactoryConfiguration(1, new FactoryLimits(2, 3, 5, 64), new FinalReviewPolicy(true),
            new HashSet<string>(["implementation", "research", "semantic-review"], StringComparer.Ordinal), "test-factory.yaml", "test-config-hash");

        var outcome = await CreateRuntime(temp.Path, backend, configuration: configuration, verification: verification)
            .RunRequestAsync("Keep trying within the normal budget", "test", default);
        var state = await LoadState(temp.Path);

        Assert.Equal("RETRY_BUDGET_EXHAUSTED", outcome.FactoryOutcome);
        Assert.Contains("after 2 semantic attempts", outcome.Reason);
        Assert.Contains("latest failure", outcome.Reason);
        Assert.Contains("verification/V0002.json", outcome.Reason);
        Assert.Equal(FactoryRunStatus.Blocked, state.RunStatus);
        Assert.Equal(CurrentWorkPhase.Ready, state.CurrentPhase);
        Assert.Equal(2, state.Current!.AttemptCount);
        Assert.Equal(2, state.Current.PriorResultRefs.Count);
        Assert.Equal(2, state.Current.VerificationEvidenceRefs.Count);
        Assert.Equal(2, state.VerificationEvidenceRefs.Count);
        Assert.Empty(state.Completed);
        Assert.Empty(state.Remaining);
    }

    [Fact]
    public async Task FailedFinalVerificationRemainsStrictAndDoesNotRetryCompletedImplementation()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = new[] { Work("Implement", "implementation") } }));
        backend.Enqueue(x => Envelope(x, "completed"));
        backend.Enqueue(x => Envelope(x, "ready", new { tasks = Array.Empty<object>() }));
        var verification = new SequencedVerificationEngine(temp.Path,
            (VerificationStatus.Passed, "subtask", 0, "passed"),
            (VerificationStatus.Failed, "final", 1, "strict final failure"));

        var outcome = await CreateRuntime(temp.Path, backend, verification: verification)
            .RunRequestAsync("Keep final verification strict", "test", default);
        var state = await LoadState(temp.Path);

        Assert.Equal("UNEXPECTED_VERIFICATION_FAILURE", outcome.FactoryOutcome);
        Assert.Single(backend.Invocations, x => x.Capability == "implementation");
        Assert.Null(state.Current);
        Assert.Single(state.Completed);
        Assert.False(state.FinalVerificationPassed);
        Assert.Equal(FactoryRunStatus.Blocked, state.RunStatus);
    }

    [Fact]
    public void MayFailClassificationAndProductionModelRemainUnchanged()
    {
        var item = StateStoreTests.Planned("W000001");
        item.VerificationExpectations["allowed"] = VerificationExpectation.MayFail;

        Assert.Equal(VerificationDecision.ExpectedFailure,
            FactoryRuntime.ClassifyVerification(item, "subtask", ["allowed"]));
        Assert.Equal(VerificationDecision.UnexpectedFailure,
            FactoryRuntime.ClassifyVerification(item, "subtask", ["required"]));
        Assert.Equal(VerificationDecision.UnexpectedFailure,
            FactoryRuntime.ClassifyVerification(null, "final", ["allowed"]));
        Assert.DoesNotContain(Enum.GetNames<SemanticOperationKind>(), name => name.Contains("VerificationFix", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(FactoryLimits).GetProperties(), property => property.Name.Contains("Verification", StringComparison.Ordinal));
        Assert.DoesNotContain(FactoryCapabilityCatalog.WorkItemCapabilities, capability => capability.Contains("verification", StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed class SequencedVerificationEngine : VerificationEngine
{
    private readonly string currentDirectory;
    private readonly Queue<(VerificationStatus Status, string CheckId, int ExitCode, string Output)> results;
    private int evidenceSequence;

    public SequencedVerificationEngine(
        string workspace,
        params (VerificationStatus Status, string CheckId, int ExitCode, string Output)[] results)
        : base(workspace, Path.Combine(workspace, ".idd", "factory", "current"))
    {
        currentDirectory = Path.Combine(workspace, ".idd", "factory", "current");
        this.results = new(results);
    }

    public override async Task<VerificationResult> RunContextAsync(
        string context,
        IEnumerable<string> changedPaths,
        CancellationToken cancellationToken)
    {
        var (status, checkId, exitCode, output) = results.Dequeue();
        var evidenceId = $"V{++evidenceSequence:0000}";
        var evidence = new VerificationEvidence(2, evidenceId, checkId, "definition", DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch, exitCode, status == VerificationStatus.Passed ? "passed" : "failed", output);
        var directory = Path.Combine(currentDirectory, "verification");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, evidenceId + ".json"),
            JsonSerializer.Serialize(evidence, FactoryJson.Options), cancellationToken);
        return new VerificationResult(status, [evidence]);
    }
}

internal sealed class CancelAfterFailedVerificationStore(
    IFactoryStateStore inner,
    CancellationTokenSource cancellation) : IFactoryStateStore
{
    public Task<FactoryState?> LoadAsync(CancellationToken cancellationToken) => inner.LoadAsync(cancellationToken);

    public Task CreateAsync(FactoryState state, CancellationToken cancellationToken) => inner.CreateAsync(state, cancellationToken);

    public async Task SaveAsync(FactoryState state, long expectedRevision, CancellationToken cancellationToken)
    {
        await inner.SaveAsync(state, expectedRevision, cancellationToken);
        if (state.CurrentPhase == CurrentWorkPhase.Ready &&
            state.Current?.LastVerificationDecision == VerificationDecision.UnexpectedFailure &&
            state.PendingVerificationSession is null)
            cancellation.Cancel();
    }
}
