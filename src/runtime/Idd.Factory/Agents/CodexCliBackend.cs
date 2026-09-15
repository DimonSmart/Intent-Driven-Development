using System.Text;
using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Processes;

namespace Idd.Factory.Agents;

public sealed class CodexCliBackend : IAgentBackend
{
    private static readonly ProcessTerminationOptions CodexTermination = new(
        TryGracefulClose: true,
        GracefulCloseDelay: TimeSpan.FromMilliseconds(1500),
        TerminationTimeout: TimeSpan.FromSeconds(5));
    private static readonly UTF8Encoding TransportUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly TimeSpan DefaultPostResultGrace = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumWaitPollInterval = TimeSpan.FromMilliseconds(100);

    private readonly Lazy<CodexCommand> command;
    private readonly string pluginRoot;
    private readonly AgentExecutionConfiguration executionConfiguration;
    private readonly AgentCapabilityPolicy capabilityPolicy;
    private readonly CodexHomePreparation homePreparation;
    private readonly TimeSpan postResultGrace;
    private readonly ProcessExecutor processExecutor = ProcessExecutor.Shared;
    private readonly Dictionary<string, RunningExecution> executions = new(StringComparer.Ordinal);

    public CodexCliBackend(
        string pluginRoot,
        string? executable = null,
        AgentExecutionConfiguration? executionConfiguration = null,
        AgentCapabilityPolicy? capabilityPolicy = null)
        : this(
            pluginRoot,
            executable,
            executionConfiguration,
            capabilityPolicy,
            DefaultPostResultGrace)
    {
    }

    internal CodexCliBackend(
        string pluginRoot,
        string? executable,
        AgentExecutionConfiguration? executionConfiguration,
        AgentCapabilityPolicy? capabilityPolicy,
        TimeSpan postResultGrace)
    {
        if (postResultGrace <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(postResultGrace));

        this.pluginRoot = Path.GetFullPath(pluginRoot);
        this.executionConfiguration = executionConfiguration ?? new();
        this.capabilityPolicy = capabilityPolicy ?? AgentCapabilityPolicy.ProductionDefault;
        this.postResultGrace = postResultGrace;
        homePreparation = new CodexHomePreparation(this.pluginRoot, this.capabilityPolicy);
        command = new Lazy<CodexCommand>(() =>
            executable is null ? CodexExecutableResolver.Resolve() : new(executable, []));
    }

    public async Task<AgentRunHandle> StartAsync(
        AgentInvocation invocation,
        CancellationToken cancellationToken)
    {
        CodexCommand resolvedCommand;
        try
        {
            resolvedCommand = command.Value;
        }
        catch (FileNotFoundException exception)
        {
            throw new AgentProtocolException(
                "AGENT_BACKEND_UNAVAILABLE",
                $"Codex CLI could not be located: {exception.Message}");
        }

        var attemptDirectory = Path.GetDirectoryName(invocation.SemanticOutputPath)!;
        var privateHome = homePreparation.Prepare(
            invocation.RunId,
            invocation.AttemptId,
            invocation.SkillName);
        var codexHome = privateHome.Path;
        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "idd-factory",
            "codex-temp",
            invocation.RunId,
            invocation.AttemptId);
        CodexHomePreparation.CleanupDirectory(tempDirectory);
        Directory.CreateDirectory(tempDirectory);

        string skillInstructions;
        try
        {
            skillInstructions = homePreparation.ReadSkillInstructions(invocation);
        }
        catch
        {
            CodexHomePreparation.CleanupDirectory(codexHome);
            CodexHomePreparation.CleanupDirectory(tempDirectory);
            throw;
        }

        var stdoutPath = Path.Combine(attemptDirectory, "stdout.log");
        var stderrPath = Path.Combine(attemptDirectory, "stderr.log");
        var sqliteDirectory = Path.Combine(codexHome, "state");
        Directory.CreateDirectory(sqliteDirectory);
        var pathPreparation = CodexProcessEnvironment.PrepareSandboxCompatiblePath(
            Environment.GetEnvironmentVariable("PATH") ?? string.Empty,
            OperatingSystem.IsWindows());
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["CODEX_HOME"] = codexHome,
            ["CODEX_SQLITE_HOME"] = sqliteDirectory,
            ["TEMP"] = tempDirectory,
            ["TMP"] = tempDirectory,
            ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0",
            ["MSBUILDUSESERVER"] = "0",
            ["MSBUILDDISABLENODEREUSE"] = "1"
        };
        if (OperatingSystem.IsWindows())
            environment["PATH"] = pathPreparation.Path;
        var arguments = CodexCommandProtocol.BuildArguments(
            invocation,
            executionConfiguration,
            resolvedCommand.PrefixArguments,
            OperatingSystem.IsWindows());

        await File.WriteAllTextAsync(
            Path.Combine(attemptDirectory, "attempt-telemetry.json"),
            JsonSerializer.Serialize(
                CodexCommandProtocol.BuildTelemetry(
                    invocation,
                    executionConfiguration,
                    capabilityPolicy,
                    privateHome.InheritedSkillCount,
                    homePreparation.ReadSkillSourceVersion(),
                    pluginRoot,
                    OperatingSystem.IsWindows() ? executionConfiguration.WindowsSandbox : null,
                    pathPreparation.WindowsAppsPathEntriesRemoved),
                FactoryJson.Options),
            cancellationToken);

        var commandTracker = new CommandExecutionTracker();
        var prompt = CodexCommandProtocol.BuildBootstrapPrompt(invocation, skillInstructions);
        ProcessExecutionHandle process;
        try
        {
            process = await processExecutor.StartAsync(
                new(resolvedCommand.Executable, arguments, invocation.Workspace)
                {
                    EnvironmentOverrides = environment,
                    StandardInput = prompt,
                    StandardInputEncoding = TransportUtf8,
                    StandardOutputEncoding = TransportUtf8,
                    StandardErrorEncoding = TransportUtf8,
                    TerminationOptions = CodexTermination,
                    OutputDrainTimeout = TimeSpan.FromSeconds(5),
                    StandardOutput = new()
                    {
                        Capture = true,
                        FilePath = stdoutPath,
                        FileEncoding = TransportUtf8,
                        LineObserver = line => commandTracker.Observe(line, DateTimeOffset.UtcNow)
                    },
                    StandardError = new()
                    {
                        Capture = true,
                        FilePath = stderrPath,
                        FileEncoding = TransportUtf8
                    }
                },
                cancellationToken);
        }
        catch (ProcessExecutionException exception)
        {
            CodexHomePreparation.CleanupDirectory(codexHome);
            CodexHomePreparation.CleanupDirectory(tempDirectory);
            if (exception.Result.CompletionReason == ProcessCompletionReason.Cancelled)
                throw new OperationCanceledException(cancellationToken);
            throw new AgentProtocolException(
                "AGENT_BACKEND_UNAVAILABLE",
                $"Codex CLI could not start: {exception.Result.Failure?.Message ?? exception.Message}");
        }

        executions.Add(
            invocation.AttemptId,
            new(
                process,
                stdoutPath,
                stderrPath,
                invocation.SemanticOutputPath,
                codexHome,
                tempDirectory,
                commandTracker));
        return new(invocation.AttemptId, process.ProcessId, invocation.AttemptId);
    }

    public async Task<AgentProcessResult> WaitAsync(
        AgentRunHandle handle,
        CancellationToken cancellationToken)
    {
        if (!executions.Remove(handle.BackendHandle, out var running))
        {
            return new(
                -1,
                "",
                "The backend handle is not active in this runtime process.",
                false,
                false,
                AgentTerminationKind.TransportFailure);
        }

        var cleanupTempDirectory = true;
        try
        {
            var completedResultWasObserved = false;
            var killRequired = false;
            DateTimeOffset? quiescentSince = null;
            var commandTimeout = executionConfiguration.EffectiveCommandTimeout;
            if (commandTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(
                    nameof(commandTimeout),
                    "Semantic-command timeout must be positive.");
            var waitPollInterval = TimeSpan.FromMilliseconds(
                Math.Max(1, Math.Min(MaximumWaitPollInterval.TotalMilliseconds, commandTimeout.TotalMilliseconds)));

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (running.Process.HasExited)
                    break;

                var now = DateTimeOffset.UtcNow;
                if (running.CommandTracker.FindTimedOut(now, commandTimeout) is { } timedOutCommand)
                {
                    cleanupTempDirectory = false;
                    return await TerminateForCommandFailureAsync(
                        running,
                        CodexCommandProtocol.BuildCommandTimeoutDiagnostic(
                            timedOutCommand,
                            commandTimeout),
                        AgentTerminationKind.CommandTimeout);
                }

                if (!completedResultWasObserved && IsCompleteResult(running.ResultPath))
                    completedResultWasObserved = true;

                if (completedResultWasObserved)
                {
                    if (running.CommandTracker.GetActive().Count > 0)
                    {
                        quiescentSince = null;
                    }
                    else
                    {
                        quiescentSince ??= now;
                        if (now - quiescentSince >= postResultGrace)
                        {
                            if (running.Process.HasExited)
                                break;
                            if (running.CommandTracker.GetActive().Count > 0)
                            {
                                quiescentSince = null;
                            }
                            else
                            {
                                killRequired = true;
                                await running.Process.TerminateAsync(CodexTermination);
                                break;
                            }
                        }
                    }
                }

                await Task.Delay(waitPollInterval, cancellationToken);
            }

            var technical = await running.Process.CompleteAsync();
            completedResultWasObserved |= IsCompleteResult(running.ResultPath);
            var stdout = technical.StandardOutput;
            var stderr = technical.StandardError;
            var exitCode = technical.ExitCode;
            var termination = killRequired
                ? AgentTerminationKind.ForcedAfterResult
                : exitCode == 0
                    ? AgentTerminationKind.CleanExit
                    : AgentTerminationKind.TransportFailure;

            var incompleteCommands = running.CommandTracker.GetActive();
            if (incompleteCommands.Count > 0)
            {
                cleanupTempDirectory = false;
                completedResultWasObserved = false;
                termination = AgentTerminationKind.IncompleteCommand;
                var diagnostic = CodexCommandProtocol.BuildIncompleteCommandDiagnostic(incompleteCommands);
                stderr = string.IsNullOrWhiteSpace(stderr)
                    ? diagnostic
                    : stderr.TrimEnd() + Environment.NewLine + diagnostic;
                if (File.Exists(running.ResultPath)) File.Delete(running.ResultPath);
                await File.WriteAllTextAsync(
                    running.StderrPath,
                    stderr,
                    CancellationToken.None);
            }
            return new AgentProcessResult(
                exitCode,
                stdout,
                stderr,
                completedResultWasObserved,
                killRequired,
                termination);
        }
        catch (OperationCanceledException)
        {
            await running.Process.TerminateAsync(CodexTermination);
            var technical = await running.Process.CompleteAsync(ProcessCompletionReason.Cancelled);
            return new(
                technical.ExitCode,
                technical.StandardOutput,
                technical.StandardError,
                IsCompleteResult(running.ResultPath),
                true,
                AgentTerminationKind.Cancelled);
        }
        finally
        {
            CodexHomePreparation.TryCleanupDirectory(
                running.CodexHome,
                running.ResultPath);
            if (cleanupTempDirectory)
                CodexHomePreparation.TryCleanupDirectory(
                    running.TempDirectory,
                    running.ResultPath);
            await running.Process.DisposeAsync();
        }
    }

    public async Task CancelAsync(
        AgentRunHandle handle,
        CancellationToken cancellationToken)
    {
        if (!executions.Remove(handle.BackendHandle, out var running)) return;
        try
        {
            await running.Process.TerminateAsync(CodexTermination);
            await running.Process.CompleteAsync(ProcessCompletionReason.Cancelled);
        }
        finally
        {
            CodexHomePreparation.TryCleanupDirectory(
                running.CodexHome,
                running.ResultPath);
            CodexHomePreparation.TryCleanupDirectory(
                running.TempDirectory,
                running.ResultPath);
            await running.Process.DisposeAsync();
        }
    }

    private static bool IsCompleteResult(string path) => File.Exists(path);

    private static async Task<AgentProcessResult> TerminateForCommandFailureAsync(
        RunningExecution running,
        string diagnostic,
        AgentTerminationKind terminationKind)
    {
        var termination = await running.Process.TerminateAsync(CodexTermination);
        diagnostic += $" Process-tree termination succeeded: {termination.Succeeded.ToString().ToLowerInvariant()}.";
        var technical = await running.Process.CompleteAsync();
        var stdout = technical.StandardOutput;
        var stderr = technical.StandardError;
        stderr = string.IsNullOrWhiteSpace(stderr)
            ? diagnostic
            : stderr.TrimEnd() + Environment.NewLine + diagnostic;
        if (File.Exists(running.ResultPath)) File.Delete(running.ResultPath);
        await File.WriteAllTextAsync(running.StderrPath, stderr, CancellationToken.None);
        return new AgentProcessResult(
            technical.ExitCode,
            stdout,
            stderr,
            false,
            true,
            terminationKind);
    }

    private sealed record RunningExecution(
        ProcessExecutionHandle Process,
        string StdoutPath,
        string StderrPath,
        string ResultPath,
        string CodexHome,
        string TempDirectory,
        CommandExecutionTracker CommandTracker);
}
