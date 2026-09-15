using Idd.Factory.Agents;
using Idd.Factory.Domain;

namespace Idd.Factory.Tests;

public sealed class CodexCliBackendCommandLifecycleTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"idd-codex-lifecycle-tests-{Guid.NewGuid():N}");

    public CodexCliBackendCommandLifecycleTests()
    {
        Directory.CreateDirectory(directory);
    }

    [Fact]
    public async Task A000002OverlapCompletesWithoutFailureOrRetrySignal()
    {
        var execution = await RunScenarioAsync("test-a000002");

        Assert.Equal(AgentTerminationKind.CleanExit, execution.Process.TerminationKind);
        Assert.True(execution.Process.CompleteResultObserved);
        Assert.False(execution.Process.KillRequired);
        Assert.True(File.Exists(execution.ResultPath));
        Assert.DoesNotContain("incomplete shell command", execution.Process.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResultDoesNotStartForcedShutdownWhileCommandIsActive()
    {
        var execution = await RunScenarioAsync(
            "test-result-active-command",
            postResultGrace: TimeSpan.FromMilliseconds(75));

        Assert.Equal(AgentTerminationKind.CleanExit, execution.Process.TerminationKind);
        Assert.True(execution.Process.CompleteResultObserved);
        Assert.False(execution.Process.KillRequired);
    }

    [Fact]
    public async Task CommandStartingAfterResultObservationCancelsQuiescentGrace()
    {
        var execution = await RunScenarioAsync(
            "test-command-after-result",
            postResultGrace: TimeSpan.FromMilliseconds(300));

        Assert.Equal(AgentTerminationKind.CleanExit, execution.Process.TerminationKind);
        Assert.True(execution.Process.CompleteResultObserved);
        Assert.False(execution.Process.KillRequired);
    }

    [Fact]
    public async Task OneParallelCommandTimingOutRejectsResultAndIdentifiesThatCommand()
    {
        var execution = await RunScenarioAsync(
            "test-command-timeout",
            commandTimeout: TimeSpan.FromMilliseconds(175),
            postResultGrace: TimeSpan.FromMilliseconds(75));

        Assert.Equal(AgentTerminationKind.CommandTimeout, execution.Process.TerminationKind);
        Assert.False(execution.Process.CompleteResultObserved);
        Assert.True(execution.Process.KillRequired);
        Assert.False(File.Exists(execution.ResultPath));
        Assert.Contains("Shell command [A] exceeded", execution.Process.Stderr, StringComparison.Ordinal);
        Assert.Contains("hung command", execution.Process.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("Shell command [B] exceeded", execution.Process.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessExitWithIncompleteCommandRejectsSemanticResultAfterDrain()
    {
        var execution = await RunScenarioAsync("test-incomplete-exit");

        Assert.Equal(AgentTerminationKind.IncompleteCommand, execution.Process.TerminationKind);
        Assert.False(execution.Process.CompleteResultObserved);
        Assert.False(execution.Process.KillRequired);
        Assert.False(File.Exists(execution.ResultPath));
        Assert.Contains("[A] abandoned command", execution.Process.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BufferedCompletionIsDrainedBeforeFinalIncompleteDecision()
    {
        var execution = await RunScenarioAsync("test-buffered-completion");

        Assert.Equal(AgentTerminationKind.CleanExit, execution.Process.TerminationKind);
        Assert.True(execution.Process.CompleteResultObserved);
        Assert.False(execution.Process.KillRequired);
        Assert.True(File.Exists(execution.ResultPath));
    }

    [Fact]
    public async Task ForcedAfterResultStillBoundsWorkerWhenNoCommandsAreActive()
    {
        var execution = await RunScenarioAsync(
            "test-forced-after-result",
            postResultGrace: TimeSpan.FromMilliseconds(100));

        Assert.Equal(AgentTerminationKind.ForcedAfterResult, execution.Process.TerminationKind);
        Assert.True(execution.Process.CompleteResultObserved);
        Assert.True(execution.Process.KillRequired);
        Assert.True(File.Exists(execution.ResultPath));
    }

    private async Task<ScenarioExecution> RunScenarioAsync(
        string scenario,
        TimeSpan? commandTimeout = null,
        TimeSpan? postResultGrace = null)
    {
        var runId = $"run-{Guid.NewGuid():N}";
        var attemptId = $"attempt-{Guid.NewGuid():N}";
        var workspace = Path.Combine(directory, "workspace");
        var attemptDirectory = Path.Combine(directory, attemptId);
        var resultPath = Path.Combine(attemptDirectory, "result.md");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(attemptDirectory);

        var backend = new CodexCliBackend(
            CreatePluginRoot(),
            HelperExecutable(),
            new AgentExecutionConfiguration(
                Model: scenario,
                CommandTimeout: commandTimeout ?? TimeSpan.FromSeconds(3)),
            new AgentCapabilityPolicy(false, "test"),
            postResultGrace ?? TimeSpan.FromMilliseconds(250));
        var invocation = new AgentInvocation
        {
            RunId = runId,
            AttemptId = attemptId,
            Capability = "implementation",
            Role = "executor",
            WorkItemId = "W000001",
            Workspace = workspace,
            SemanticOutputPath = resultPath,
            SkillName = "idd-factory-execute-subtask",
            ExecutionProfile = AgentExecutionProfile.WorkspaceWrite,
            Input = "Run the lifecycle test scenario.",
            StartedAt = DateTimeOffset.UtcNow
        };

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var handle = await backend.StartAsync(invocation, cancellation.Token);
        var process = await backend.WaitAsync(handle, cancellation.Token);
        return new(process, resultPath);
    }

    private string CreatePluginRoot()
    {
        var pluginRoot = Path.Combine(directory, "plugin");
        var skillDirectory = Path.Combine(pluginRoot, "skills", "idd-factory-execute-subtask");
        Directory.CreateDirectory(skillDirectory);
        File.WriteAllText(
            Path.Combine(skillDirectory, "SKILL.md"),
            "# Test executor skill\n\nExecute the assigned test scenario.");
        return pluginRoot;
    }

    private static string HelperExecutable()
    {
        var root = RepositoryRoot();
        var baseDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = baseDirectory.Parent?.Name ?? "Debug";
        var executable = OperatingSystem.IsWindows()
            ? "Idd.Factory.ProcessTestHelper.exe"
            : "Idd.Factory.ProcessTestHelper";
        var path = Path.Combine(
            root,
            "tests",
            "Idd.Factory.ProcessTestHelper",
            "bin",
            configuration,
            "net10.0",
            executable);
        Assert.True(File.Exists(path), $"Process test helper was not built: {path}");
        return path;
    }

    private static string RepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
             current is not null;
             current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "Intent-Driven-Development.slnx")))
                return current.FullName;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record ScenarioExecution(AgentProcessResult Process, string ResultPath);
}
