using Idd.Factory.Agents;
using Idd.Factory.Domain;
using System.Text.Json;

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

    [Theory]
    [InlineData("task-decomposer", "idd-factory-decompose-task", AgentExecutionProfile.ReadOnly)]
    [InlineData("implementer", "idd-factory-execute-subtask", AgentExecutionProfile.WorkspaceWrite)]
    [InlineData("checkpoint-reviewer", "idd-factory-review-checkpoint", AgentExecutionProfile.ReadOnly)]
    [InlineData("final-reviewer", "idd-factory-review-task", AgentExecutionProfile.ReadOnly)]
    [InlineData("factory-replanner", "idd-factory-replan", AgentExecutionProfile.ReadOnly)]
    public void FactoryRolesResolveToSemanticAgentContracts(string role, string skillName, AgentExecutionProfile executionProfile)
    {
        Assert.Equal(new FactoryAgentContract(role, skillName, executionProfile), FactoryAgentCatalog.Resolve(role));
    }

    [Fact] public void InvocationSerializesBackendNeutralSemanticContract()
    {
        var json = JsonSerializer.Serialize(Invocation(), FactoryJson.Options);
        using var document = JsonDocument.Parse(json); var root = document.RootElement;

        Assert.Equal(AgentInvocation.CurrentProtocolVersion, root.GetProperty("protocolVersion").GetInt32());
        Assert.Equal("implementer", root.GetProperty("role").GetString());
        Assert.Equal("idd-factory-execute-subtask", root.GetProperty("skillName").GetString());
        Assert.Equal("workspace-write", root.GetProperty("executionProfile").GetString());
        Assert.Equal("input", root.GetProperty("input").GetString());
        Assert.False(root.TryGetProperty("skillReferences", out _));
        Assert.False(root.TryGetProperty("prompt", out _));

        Assert.Equal(Invocation(), JsonSerializer.Deserialize<AgentInvocation>(json, FactoryJson.Options));
    }

    [Fact] public void CodexAdapterMapsProfileWithoutRoleKnowledge()
    {
        Assert.Equal("read-only", CodexCliBackend.Sandbox(AgentExecutionProfile.ReadOnly));
        Assert.Equal("workspace-write", CodexCliBackend.Sandbox(AgentExecutionProfile.WorkspaceWrite));
    }

    [Fact] public void CodexAdapterOwnsExplicitSkillBootstrap()
    {
        var prompt = CodexCliBackend.BuildBootstrapPrompt(Invocation());
        Assert.StartsWith("Use $idd-factory-execute-subtask.", prompt);
        Assert.Contains("input", prompt);
        Assert.Contains("protocolVersion=1", prompt);
    }

    [Fact] public void CodexAdapterReportsRequiredAttemptTelemetry()
    {
        var telemetry = CodexCliBackend.BuildTelemetry(Invocation());
        Assert.Equal("implementer", telemetry.Role); Assert.Equal("idd-factory-execute-subtask", telemetry.SkillName);
        Assert.Equal("codex-cli", telemetry.Backend); Assert.Equal(AgentExecutionProfile.WorkspaceWrite, telemetry.ExecutionProfile);
        Assert.Equal("bootstrap", telemetry.SkillInvocationMode); Assert.Equal("input".Length, telemetry.InputChars);
        Assert.Equal("default/unpinned", telemetry.RequestedModel);
        Assert.Equal("default/unpinned", telemetry.RequestedReasoningEffort);
        Assert.Equal("unknown", telemetry.EffectiveModel);
        Assert.Equal("unknown", telemetry.EffectiveReasoningEffort);
    }

    [Fact] public void CodexAdapterReportsSafeWindowsSandboxTelemetry()
    {
        var telemetry = CodexCliBackend.BuildTelemetry(Invocation(), windowsSandbox: "unelevated", windowsAppsPathEntriesRemoved: 2);
        Assert.Equal("unelevated", telemetry.WindowsSandbox);
        Assert.Equal(2, telemetry.WindowsAppsPathEntriesRemoved);
    }

    [Fact]
    public void WindowsSandboxPathRemovesAppExecutionAliasDirectories()
    {
        var path = string.Join(';',
            @"C:\Program Files\Git\cmd",
            " ",
            @" C:\Users\u\AppData\Local\Microsoft\WindowsApps ",
            @"C:\PROGRAM FILES\WINDOWSAPPS\PowerShell",
            @"C:\Program Files\dotnet");

        var result = CodexProcessEnvironment.PrepareSandboxCompatiblePath(path, isWindows: true);

        Assert.Equal(string.Join(';', @"C:\Program Files\Git\cmd", @"C:\Program Files\dotnet"), result.Path);
        Assert.Equal(2, result.WindowsAppsPathEntriesRemoved);
    }

    [Fact]
    public void WindowsSandboxPathPreservesOrderWhenNoAliasDirectoryExists()
    {
        var path = string.Join(';', @"C:\Program Files\Git\cmd", @"C:\Program Files\dotnet");
        var result = CodexProcessEnvironment.PrepareSandboxCompatiblePath(path, isWindows: true);
        Assert.Equal(path, result.Path);
        Assert.Equal(0, result.WindowsAppsPathEntriesRemoved);
    }

    [Fact]
    public void NonWindowsPathIsUnchanged()
    {
        const string path = "/usr/bin:/opt/WindowsApps/bin:: /custom/bin ";
        var result = CodexProcessEnvironment.PrepareSandboxCompatiblePath(path, isWindows: false);
        Assert.Equal(path, result.Path);
        Assert.Equal(0, result.WindowsAppsPathEntriesRemoved);
    }

    [Fact] public void CodexAdapterMapsPinnedExecutionConfigurationToCommandLine()
    {
        var arguments = CodexCliBackend.BuildArguments(Invocation(), new("gpt-test", "high"), isWindows: false);
        Assert.Contains("--model", arguments);
        Assert.Equal("gpt-test", arguments[arguments.ToList().IndexOf("--model") + 1]);
        Assert.Contains("model_reasoning_effort=high", arguments);
    }

    [Fact] public void CodexAdapterOmitsUnpinnedExecutionConfiguration()
    {
        var arguments = CodexCliBackend.BuildArguments(Invocation(), new(), isWindows: false);
        Assert.DoesNotContain("--model", arguments);
        Assert.DoesNotContain(arguments, value => value.StartsWith("model_reasoning_effort=", StringComparison.Ordinal));
    }

    [Fact] public void CodexAdapterMapsConfiguredWindowsSandbox()
    {
        var arguments = CodexCliBackend.BuildArguments(Invocation(), new(WindowsSandbox: "elevated"), isWindows: true);
        Assert.Contains("windows.sandbox=\"elevated\"", arguments);
    }

    [Fact] public void CodexAdapterDoesNotInventWindowsSandbox()
    {
        var arguments = CodexCliBackend.BuildArguments(Invocation(), new(), isWindows: true);
        Assert.DoesNotContain(arguments, value => value.StartsWith("windows.sandbox=", StringComparison.Ordinal));
    }

    [Fact] public void CodexAdapterRejectsUnknownWindowsSandbox()
    {
        Assert.Throws<ArgumentException>(() => CodexCliBackend.BuildArguments(Invocation(), new(WindowsSandbox: "invalid"), isWindows: true));
    }

    [Fact] public void SelectedFactorySkillIsNeverInheritedFromUserHome()
    {
        Assert.False(CodexCliBackend.ShouldInheritSkill("idd-factory-execute-subtask", "idd-factory-execute-subtask"));
        Assert.True(CodexCliBackend.ShouldInheritSkill("domain-skill", "idd-factory-execute-subtask"));
    }

    [Fact] public void ProjectSkillCollisionFailsExplicitly()
    {
        using var temp = new TestWorkspace();
        var plugin = temp.Write("plugin/skills/idd-factory-execute-subtask/SKILL.md", "factory");
        temp.Write("workspace/.agents/skills/idd-factory-execute-subtask/SKILL.md", "collision");
        var invocation = Invocation() with { Workspace = Path.Combine(temp.Path, "workspace") };
        var exception = Assert.Throws<AgentProtocolException>(() => CodexCliBackend.ValidateSkillIdentity(Path.GetFullPath(Path.Combine(temp.Path, "plugin")), invocation));
        Assert.Equal("FACTORY_SKILL_COLLISION", exception.Code);
    }

    [Fact] public void ControlledCapabilityTelemetryReportsNoUserSkillInheritance()
    {
        var telemetry = CodexCliBackend.BuildTelemetry(Invocation(), new("model", "low"), new(false, "release-eval-controlled"));
        Assert.Equal("isolated", telemetry.UserSkillInheritancePolicy);
        Assert.Equal(0, telemetry.InheritedUserSkillCount);
        Assert.Equal("release-eval-controlled", telemetry.CapabilityProfile);
    }

    [Fact] public async Task WorkerCannotChangeRunnerOwnedState()
    {
        using var temp = new TestWorkspace(); var resultPath = temp.Write(".idd/factory/current/attempts/A000001/placeholder", "x"); resultPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(resultPath)!, "result.json");
        var statePath = temp.Write(".idd/factory/current/state.json", "before"); temp.Write(".idd/factory/current/request.md", "request");
        var invocation = Invocation() with { Workspace = temp.Path, ResultPath = resultPath };
        var backend = new MutatingBackend(invocation, statePath);
        var exception = await Assert.ThrowsAsync<AgentProtocolException>(() => new AgentExecutor(backend, new AgentResultValidator()).ExecuteAsync(invocation, default));
        Assert.Equal("WORKER_CHANGED_RUNNER_STATE", exception.Code);
    }

    [Theory]
    [InlineData(AgentTerminationKind.CleanExit, 0, false)]
    [InlineData(AgentTerminationKind.ForcedAfterResult, -1, true)]
    public async Task CompleteResultIsAcceptedWithExplicitTermination(AgentTerminationKind kind, int exitCode, bool killed)
    {
        using var temp = new TestWorkspace();
        var invocation = PreparedInvocation(temp);
        var execution = await new AgentExecutor(new ResultBackend(invocation, new(exitCode, "", "", true, killed, kind), produceResult: true), new AgentResultValidator()).ExecuteAsync(invocation, default);
        Assert.Equal(kind, execution.Process.TerminationKind);
        Assert.Equal(exitCode, execution.Process.ExitCode);
        Assert.Equal(killed, execution.Process.KillRequired);
    }

    [Fact] public async Task NonZeroExitBeforeResultIsTransportFailure()
    {
        using var temp = new TestWorkspace();
        var invocation = PreparedInvocation(temp);
        var exception = await Assert.ThrowsAsync<AgentProtocolException>(() => new AgentExecutor(new ResultBackend(invocation, new(17, "", "failure", false, false, AgentTerminationKind.TransportFailure), produceResult: false), new AgentResultValidator()).ExecuteAsync(invocation, default));
        Assert.Equal("AGENT_TRANSPORT_FAILURE", exception.Code);
    }

    [Fact] public async Task CancelledProcessPropagatesCancellation()
    {
        using var temp = new TestWorkspace();
        var invocation = PreparedInvocation(temp);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AgentExecutor(new ResultBackend(invocation, new(-1, "", "", false, true, AgentTerminationKind.Cancelled), produceResult: false), new AgentResultValidator()).ExecuteAsync(invocation, new CancellationToken(canceled: true)));
    }

    [Fact] public void CodexResolverPrefersPackagedNativeExecutableOnWindows()
    {
        using var temp = new TestWorkspace(); var native = temp.Write("node_modules/@openai/codex/node_modules/@openai/codex-win32-x64/vendor/bin/codex.exe", "");
        Assert.Equal(native.Replace('/', Path.DirectorySeparatorChar), CodexExecutableResolver.ResolveFromPath(temp.Path, true).Executable);
    }

    private static AgentInvocation Invocation() => new() { RunId = "run", AttemptId = "A000001", Role = "implementer", Workspace = "w", ResultPath = "r", SkillName = "idd-factory-execute-subtask", ExecutionProfile = AgentExecutionProfile.WorkspaceWrite, Input = "input", StartedAt = DateTimeOffset.UnixEpoch, WorkspaceFingerprint = "f" };
    private static AgentInvocation PreparedInvocation(TestWorkspace temp)
    {
        temp.Write(".idd/factory/current/state.json", "state");
        temp.Write(".idd/factory/current/request.md", "request");
        var placeholder = temp.Write(".idd/factory/current/attempts/A000001/placeholder", "x");
        return Invocation() with { Workspace = temp.Path, ResultPath = Path.Combine(Path.GetDirectoryName(placeholder)!, "result.json") };
    }
    private static AgentResultEnvelope Result() => new() { ProtocolVersion = 1, RunId = "run", AttemptId = "A000001", Role = "implementer", Outcome = "completed" };

    private sealed class MutatingBackend(AgentInvocation invocation, string statePath) : IAgentBackend
    {
        public Task<AgentRunHandle> StartAsync(AgentInvocation _, CancellationToken cancellationToken) { File.WriteAllText(statePath, "changed"); File.WriteAllText(invocation.ResultPath, System.Text.Json.JsonSerializer.Serialize(Result(), FactoryJson.Options)); return Task.FromResult(new AgentRunHandle(invocation.AttemptId, 1, invocation.AttemptId)); }
        public Task<AgentProcessResult> WaitAsync(AgentRunHandle handle, CancellationToken cancellationToken) => Task.FromResult(new AgentProcessResult(0, "", "", true, false, AgentTerminationKind.CleanExit));
        public Task CancelAsync(AgentRunHandle handle, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ResultBackend(AgentInvocation invocation, AgentProcessResult process, bool produceResult) : IAgentBackend
    {
        public Task<AgentRunHandle> StartAsync(AgentInvocation _, CancellationToken cancellationToken)
        {
            if (produceResult) File.WriteAllText(invocation.ResultPath, JsonSerializer.Serialize(Result(), FactoryJson.Options));
            return Task.FromResult(new AgentRunHandle(invocation.AttemptId, 1, invocation.AttemptId));
        }
        public Task<AgentProcessResult> WaitAsync(AgentRunHandle handle, CancellationToken cancellationToken) => Task.FromResult(process);
        public Task CancelAsync(AgentRunHandle handle, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
