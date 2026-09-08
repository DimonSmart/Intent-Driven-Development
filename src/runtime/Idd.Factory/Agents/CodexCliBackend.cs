using System.Diagnostics;
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

    private readonly Lazy<CodexCommand> command;
    private readonly string pluginRoot;
    private readonly AgentExecutionConfiguration executionConfiguration;
    private readonly AgentCapabilityPolicy capabilityPolicy;
    private readonly CodexHomePreparation homePreparation;
    private readonly ProcessSupervisor processSupervisor = ProcessSupervisor.Shared;
    private readonly Dictionary<string, RunningProcess> processes = new(StringComparer.Ordinal);

    public CodexCliBackend(
        string pluginRoot,
        string? executable = null,
        AgentExecutionConfiguration? executionConfiguration = null,
        AgentCapabilityPolicy? capabilityPolicy = null)
    {
        this.pluginRoot = Path.GetFullPath(pluginRoot);
        this.executionConfiguration = executionConfiguration ?? new();
        this.capabilityPolicy = capabilityPolicy ?? AgentCapabilityPolicy.ProductionDefault;
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
        var start = CreateProcessStartInfo(resolvedCommand.Executable, invocation.Workspace);
        start.Environment["CODEX_HOME"] = codexHome;
        start.Environment["CODEX_SQLITE_HOME"] = sqliteDirectory;
        start.Environment["TEMP"] = tempDirectory;
        start.Environment["TMP"] = tempDirectory;
        start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        start.Environment["MSBUILDUSESERVER"] = "0";
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        var pathPreparation = CodexProcessEnvironment.PrepareSandboxCompatiblePath(
            start.Environment["PATH"] ?? string.Empty,
            OperatingSystem.IsWindows());
        if (OperatingSystem.IsWindows())
            start.Environment["PATH"] = pathPreparation.Path;
        foreach (var argument in CodexCommandProtocol.BuildArguments(
                     invocation,
                     executionConfiguration,
                     resolvedCommand.PrefixArguments,
                     OperatingSystem.IsWindows()))
        {
            start.ArgumentList.Add(argument);
        }

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

        Process? process;
        try
        {
            process = processSupervisor.Start(start);
            if (process is null)
                throw new AgentProtocolException(
                    "AGENT_BACKEND_UNAVAILABLE",
                    "Codex CLI did not start.");
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            CodexHomePreparation.CleanupDirectory(codexHome);
            CodexHomePreparation.CleanupDirectory(tempDirectory);
            throw new AgentProtocolException(
                "AGENT_BACKEND_UNAVAILABLE",
                $"Codex CLI could not start: {exception.Message}");
        }

        var commandTracker = new CommandExecutionTracker();
        var stdout = processSupervisor.CaptureLinesAsync(
            process.StandardOutput,
            stdoutPath,
            line => commandTracker.Observe(line, DateTimeOffset.UtcNow),
            cancellationToken);
        var stderr = processSupervisor.CaptureLinesAsync(
            process.StandardError,
            stderrPath,
            observeLine: null,
            cancellationToken);
        var prompt = CodexCommandProtocol.BuildBootstrapPrompt(invocation, skillInstructions);
        await process.StandardInput.WriteAsync(prompt.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
        process.StandardInput.Close();
        processes.Add(
            invocation.AttemptId,
            new(
                process,
                stdout,
                stderr,
                stdoutPath,
                stderrPath,
                invocation.SemanticOutputPath,
                tempDirectory,
                commandTracker));
        return new(invocation.AttemptId, process.Id, invocation.AttemptId);
    }

    public async Task<AgentProcessResult> WaitAsync(
        AgentRunHandle handle,
        CancellationToken cancellationToken)
    {
        if (!processes.Remove(handle.BackendHandle, out var running))
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
            using var resultWatcherCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var processExit = processSupervisor.WaitForExitAsync(
                running.Process,
                timeout: null,
                cancellationToken);
            var resultReady = WaitForCompleteResultAsync(
                running.ResultPath,
                resultWatcherCancellation.Token);
            var commandTimeout = WaitForCommandTimeoutAsync(
                running.CommandTracker,
                executionConfiguration.EffectiveCommandTimeout,
                resultWatcherCancellation.Token);
            var commandOverlap = WaitForCommandOverlapAsync(
                running.CommandTracker,
                resultWatcherCancellation.Token);
            var first = await Task.WhenAny(
                processExit,
                resultReady,
                commandTimeout,
                commandOverlap);

            if (first == commandOverlap)
            {
                var overlap = await commandOverlap;
                cleanupTempDirectory = false;
                resultWatcherCancellation.Cancel();
                return await TerminateForCommandFailureAsync(
                    running,
                    CodexCommandProtocol.BuildCommandOverlapDiagnostic(overlap),
                    AgentTerminationKind.IncompleteCommand);
            }

            if (first == commandTimeout)
            {
                var timedOutCommand = await commandTimeout;
                cleanupTempDirectory = false;
                resultWatcherCancellation.Cancel();
                return await TerminateForCommandFailureAsync(
                    running,
                    CodexCommandProtocol.BuildCommandTimeoutDiagnostic(
                        timedOutCommand,
                        executionConfiguration.EffectiveCommandTimeout),
                    AgentTerminationKind.CommandTimeout);
            }

            var completedResultWasObserved = first == resultReady && await resultReady;
            if (!completedResultWasObserved && first == processExit)
            {
                await processExit;
                completedResultWasObserved = IsCompleteResult(running.ResultPath);
            }
            resultWatcherCancellation.Cancel();

            var killRequired = false;
            if (completedResultWasObserved && !running.Process.HasExited)
            {
                var exited = await processSupervisor.WaitForExitAsync(
                    running.Process,
                    TimeSpan.FromSeconds(5),
                    cancellationToken);
                if (!exited)
                {
                    killRequired = true;
                    await CancelProcessAsync(running.Process);
                }
            }
            else
            {
                await processExit;
            }

            var stdout = await running.Stdout;
            var stderr = await running.Stderr;
            int? exitCode = running.Process.HasExited ? running.Process.ExitCode : null;
            var termination = killRequired
                ? AgentTerminationKind.ForcedAfterResult
                : exitCode == 0
                    ? AgentTerminationKind.CleanExit
                    : AgentTerminationKind.TransportFailure;
            var incompleteCommands = CodexCommandProtocol.FindIncompleteCommandExecutions(stdout);
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
            await CancelProcessAsync(running.Process);
            string stdout = "";
            string stderr = "";
            try
            {
                stdout = await running.Stdout;
                stderr = await running.Stderr;
            }
            catch (OperationCanceledException)
            {
            }
            return new(
                running.Process.HasExited ? running.Process.ExitCode : null,
                stdout,
                stderr,
                IsCompleteResult(running.ResultPath),
                true,
                AgentTerminationKind.Cancelled);
        }
        finally
        {
            CodexHomePreparation.TryCleanupDirectory(
                running.Process.StartInfo.Environment["CODEX_HOME"]!,
                running.ResultPath);
            if (cleanupTempDirectory)
                CodexHomePreparation.TryCleanupDirectory(
                    running.TempDirectory,
                    running.ResultPath);
            running.Process.Dispose();
        }
    }

    public async Task CancelAsync(
        AgentRunHandle handle,
        CancellationToken cancellationToken)
    {
        if (!processes.Remove(handle.BackendHandle, out var running)) return;
        try
        {
            await CancelProcessAsync(running.Process);
            await Task.WhenAll(running.Stdout, running.Stderr);
        }
        finally
        {
            CodexHomePreparation.TryCleanupDirectory(
                running.Process.StartInfo.Environment["CODEX_HOME"]!,
                running.ResultPath);
            CodexHomePreparation.TryCleanupDirectory(
                running.TempDirectory,
                running.ResultPath);
            running.Process.Dispose();
        }
    }

    internal static ProcessStartInfo CreateProcessStartInfo(
        string executable,
        string workingDirectory)
    {
        var utf8 = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);
        return new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = utf8,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    private static async Task<IncompleteCommandExecution> WaitForCommandTimeoutAsync(
        CommandExecutionTracker tracker,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "Semantic-command timeout must be positive.");
        while (true)
        {
            if (tracker.FindTimedOut(DateTimeOffset.UtcNow, timeout) is { } command)
                return command;
            await Task.Delay(
                TimeSpan.FromMilliseconds(Math.Min(250, timeout.TotalMilliseconds)),
                cancellationToken);
        }
    }

    private static async Task<CommandExecutionOverlap> WaitForCommandOverlapAsync(
        CommandExecutionTracker tracker,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            if (tracker.FindOverlap() is { } overlap) return overlap;
            await Task.Delay(100, cancellationToken);
        }
    }

    private static async Task<bool> WaitForCompleteResultAsync(
        string path,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (File.Exists(path)) return true;
            await Task.Delay(100, cancellationToken);
        }
        return false;
    }

    private static bool IsCompleteResult(string path) => File.Exists(path);

    private static async Task<string> ReadCapturedOutputAsync(
        Task<string> capture,
        string path)
    {
        if (await ProcessSupervisor.Shared.WaitAsync(capture, TimeSpan.FromSeconds(5)))
            return await capture;
        return File.Exists(path) ? await File.ReadAllTextAsync(path) : string.Empty;
    }

    private async Task<AgentProcessResult> TerminateForCommandFailureAsync(
        RunningProcess running,
        string diagnostic,
        AgentTerminationKind terminationKind)
    {
        var terminated = await CancelProcessAsync(running.Process);
        diagnostic += $" Process-tree termination succeeded: {terminated.ToString().ToLowerInvariant()}.";
        var stdout = await ReadCapturedOutputAsync(running.Stdout, running.StdoutPath);
        var stderr = await ReadCapturedOutputAsync(running.Stderr, running.StderrPath);
        stderr = string.IsNullOrWhiteSpace(stderr)
            ? diagnostic
            : stderr.TrimEnd() + Environment.NewLine + diagnostic;
        if (File.Exists(running.ResultPath)) File.Delete(running.ResultPath);
        await File.WriteAllTextAsync(running.StderrPath, stderr, CancellationToken.None);
        return new AgentProcessResult(
            running.Process.HasExited ? running.Process.ExitCode : null,
            stdout,
            stderr,
            false,
            true,
            terminationKind);
    }

    private async Task<bool> CancelProcessAsync(Process process) =>
        (await processSupervisor.TerminateAsync(process, CodexTermination)).Succeeded;

    private sealed record RunningProcess(
        Process Process,
        Task<string> Stdout,
        Task<string> Stderr,
        string StdoutPath,
        string StderrPath,
        string ResultPath,
        string TempDirectory,
        CommandExecutionTracker CommandTracker);
}
