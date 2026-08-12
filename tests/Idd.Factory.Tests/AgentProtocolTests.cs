using Idd.Factory.Agents;
using Idd.Factory.Domain;

namespace Idd.Factory.Tests;

public sealed class AgentProtocolTests
{
    [Fact] public void ValidResultPasses()
    {
        var invocation = Invocation(); var result = Result();
        Assert.Same(result, new AgentResultValidator().Validate(invocation, result));
    }

    [Theory]
    [InlineData("run", "wrong", "AGENT_RESULT_IDENTITY_MISMATCH")]
    [InlineData("outcome", "approved", "UNSUPPORTED_AGENT_OUTCOME")]
    [InlineData("role", "final-reviewer", "AGENT_RESULT_IDENTITY_MISMATCH")]
    public void IdentityAndOutcomeAreValidated(string field, string value, string code)
    {
        var source = Result(); var result = source with { RunId = field == "run" ? value : source.RunId, Outcome = field == "outcome" ? value : source.Outcome, Role = field == "role" ? value : source.Role };
        Assert.Equal(code, Assert.Throws<AgentProtocolException>(() => new AgentResultValidator().Validate(Invocation(), result)).Code);
    }

    [Fact] public void ProtocolVersionIsValidated()
    { Assert.Equal("UNSUPPORTED_AGENT_PROTOCOL", Assert.Throws<AgentProtocolException>(() => new AgentResultValidator().Validate(Invocation(), Result() with { ProtocolVersion = 2 })).Code); }

    [Fact] public async Task WorkerCannotChangeRunnerOwnedState()
    {
        using var temp = new TestWorkspace(); var resultPath = temp.Write(".idd/factory/current/attempts/A000001/placeholder", "x"); resultPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(resultPath)!, "result.json");
        var statePath = temp.Write(".idd/factory/current/state.json", "before"); temp.Write(".idd/factory/current/request.md", "request");
        var invocation = Invocation() with { Workspace = temp.Path, ResultPath = resultPath };
        var backend = new MutatingBackend(invocation, statePath);
        var exception = await Assert.ThrowsAsync<AgentProtocolException>(() => new AgentExecutor(backend, new AgentResultValidator()).ExecuteAsync(invocation, default));
        Assert.Equal("WORKER_CHANGED_RUNNER_STATE", exception.Code);
    }

    [Fact] public void CodexResolverPrefersPackagedNativeExecutableOnWindows()
    {
        using var temp = new TestWorkspace(); var native = temp.Write("node_modules/@openai/codex/node_modules/@openai/codex-win32-x64/vendor/bin/codex.exe", "");
        Assert.Equal(native.Replace('/', Path.DirectorySeparatorChar), CodexExecutableResolver.ResolveFromPath(temp.Path, true).Executable);
    }

    private static AgentInvocation Invocation() => new() { RunId = "run", AttemptId = "A000001", Role = "implementer", Workspace = "w", ResultPath = "r", Prompt = "p", StartedAt = DateTimeOffset.UnixEpoch, WorkspaceFingerprint = "f" };
    private static AgentResultEnvelope Result() => new() { ProtocolVersion = 1, RunId = "run", AttemptId = "A000001", Role = "implementer", Outcome = "completed" };

    private sealed class MutatingBackend(AgentInvocation invocation, string statePath) : IAgentBackend
    {
        public Task<AgentRunHandle> StartAsync(AgentInvocation _, CancellationToken cancellationToken) { File.WriteAllText(statePath, "changed"); File.WriteAllText(invocation.ResultPath, System.Text.Json.JsonSerializer.Serialize(Result(), FactoryJson.Options)); return Task.FromResult(new AgentRunHandle(invocation.AttemptId, 1, invocation.AttemptId)); }
        public Task<AgentProcessResult> WaitAsync(AgentRunHandle handle, CancellationToken cancellationToken) => Task.FromResult(new AgentProcessResult(0, "", "", false));
        public Task CancelAsync(AgentRunHandle handle, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
