using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Runtime;
using Idd.Factory.Verification;

namespace Idd.Factory.Tests;

internal sealed class FactoryScenario : IDisposable
{
    private readonly TestWorkspace workspace = new();
    private readonly FakeAgentBackend backend = new();
    private VerificationEngine? verification;
    private FactoryRuntime? runtime;

    public static FactoryScenario Create() => new();

    public string WorkspacePath => workspace.Path;
    public FakeAgentBackend Backend => backend;
    public FactoryRuntime Runtime => runtime ??= FactoryRuntimeTestHarness.CreateRuntime(workspace.Path, backend, verification: verification);

    public FactoryScenario Planner(string output)
    {
        backend.Enqueue(invocation =>
        {
            Assert.Equal("planning", invocation.Capability);
            Assert.Equal("planner", invocation.Role);
            return output;
        });
        return this;
    }

    public FactoryScenario Plan(params string[] tasks) =>
        Planner(string.Join("\n\n", tasks.Select(task => $"# Task\n\n{task.Trim()}")));

    public FactoryScenario Question(string question) => Planner($"# Question\n\n{question.Trim()}");

    public FactoryScenario Done() => Planner("# Done");

    public FactoryScenario Execute(string task, string result = "Implemented the requested task.") =>
        Execute(task, _ => result);

    public FactoryScenario Execute(string task, Func<AgentInvocation, string> result)
    {
        backend.Enqueue(invocation =>
        {
            Assert.Equal("implementation", invocation.Capability);
            Assert.Equal("executor", invocation.Role);
            Assert.NotNull(invocation.WorkItemId);
            var contract = File.ReadAllText(Path.Combine(
                workspace.Path,
                ".idd",
                "factory",
                "current",
                "work-items",
                invocation.WorkItemId!,
                "contract.md"));
            Assert.Equal(task.Trim(), contract.Trim());
            return result(invocation);
        });
        return this;
    }

    public FactoryScenario Agent(Func<AgentInvocation, string> result)
    {
        backend.Enqueue(result);
        return this;
    }

    public FactoryScenario CommandFailure(AgentTerminationKind terminationKind, string diagnostic)
    {
        backend.EnqueueCommandFailure(terminationKind, diagnostic);
        return this;
    }

    public FactoryScenario WithFile(string relativePath, string content)
    {
        workspace.Write(relativePath, content);
        return this;
    }

    public FactoryScenario WithVerification(string yaml)
    {
        workspace.Write(".idd/verification.yaml", yaml);
        return this;
    }

    public FactoryScenario WithVerificationCheck(
        string id,
        string command,
        string context = "subtask",
        string? timeout = null) =>
        WithVerification(VerificationPolicyFixture.SingleCheck(id, command, context, timeout));

    public FactoryScenario WithVerification(VerificationEngine engine)
    {
        Assert.Null(runtime);
        verification = engine;
        return this;
    }

    public async Task<ScenarioResult> Run(string request = "Complete the requested change.") =>
        await CaptureAsync(await Runtime.RunRequestAsync(request, "test", default));

    public async Task<ScenarioResult> Continue(
        VerificationConfirmation confirmation = VerificationConfirmation.None,
        bool? verificationPassed = null,
        string? userAnswer = null) =>
        await CaptureAsync(await Runtime.ContinueAsync(default, confirmation, verificationPassed, userAnswer));

    public async Task<ScenarioResult> RetryExhausted(int additionalAttempts = 1) =>
        await CaptureAsync(await Runtime.RetryExhaustedAsync(additionalAttempts, default));

    private async Task<ScenarioResult> CaptureAsync(FactoryCliOutcome outcome)
    {
        var runDirectory = outcome.ResultDirectory ?? Path.Combine(workspace.Path, ".idd", "factory", "current");
        var state = JsonSerializer.Deserialize<FactoryState>(
            await File.ReadAllTextAsync(Path.Combine(runDirectory, "state.json")),
            FactoryJson.Options)!;
        return new ScenarioResult(outcome, state, backend.Invocations.ToArray(), workspace.Path, runDirectory);
    }

    public void Dispose() => workspace.Dispose();
}

internal sealed class ScenarioResult(
    FactoryCliOutcome outcome,
    FactoryState state,
    IReadOnlyList<AgentInvocation> invocations,
    string workspacePath,
    string runDirectory)
{
    public FactoryCliOutcome Outcome { get; } = outcome;
    public FactoryState State { get; } = state;
    public IReadOnlyList<AgentInvocation> Invocations { get; } = invocations;
    public string WorkspacePath { get; } = workspacePath;
    public string RunDirectory { get; } = runDirectory;

    public void ShouldComplete() => Assert.Equal("COMPLETED", Outcome.FactoryOutcome);

    public void ShouldStopWith(string code) => Assert.Equal(code, Outcome.FactoryOutcome);

    public void ShouldBeBlockedBy(string code)
    {
        Assert.Equal(code, Outcome.FactoryOutcome);
        Assert.Equal(code, State.Blocker?.Code);
    }

    public void ShouldExecute(params string[] expectedTasks)
    {
        var actual = Invocations
            .Where(invocation => invocation.Capability == "implementation")
            .Select(invocation => ReadContract(invocation.WorkItemId!))
            .ToArray();
        Assert.Equal(expectedTasks.Select(task => task.Trim()).ToArray(), actual);
    }

    public void ShouldHavePlanningCycles(int count) =>
        Assert.Equal(count, Invocations.Count(invocation => invocation.Capability == "planning"));

    public void ShouldHaveAttemptCount(string workItemId, int count) =>
        Assert.Equal(count, Invocations.Count(invocation => invocation.WorkItemId == workItemId));

    public void ShouldHaveEvidence(string checkId)
    {
        var evidence = State.VerificationEvidenceRefs
            .Select(reference => Path.Combine(RunDirectory, reference.Replace('/', Path.DirectorySeparatorChar)))
            .Where(File.Exists)
            .Select(path => VerificationEngine.Read(File.ReadAllText(path)))
            .Where(item => item is not null)
            .ToArray();
        Assert.Contains(evidence, item => item!.CheckId == checkId);
    }

    public void ShouldHaveContinuation(bool resumable, string? context = null)
    {
        Assert.NotNull(State.PendingContinuation);
        Assert.Equal(resumable, State.PendingContinuation!.IsResumable);
        if (context is not null) Assert.Equal(context, State.PendingContinuation.VerificationContext);
    }

    public void ShouldHaveNoAgentCalls() => Assert.Empty(Invocations);

    public string ReadArtifact(string relativePath) =>
        File.ReadAllText(Path.Combine(RunDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private string ReadContract(string workItemId) =>
        File.ReadAllText(Path.Combine(RunDirectory, "work-items", workItemId, "contract.md")).Trim();
}
