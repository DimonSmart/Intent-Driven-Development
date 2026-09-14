using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace Idd.Factory.Processes;

internal enum ProcessCompletionReason
{
    Exited,
    TimedOut,
    Cancelled,
    StartFailed,
    InfrastructureFailed
}

internal sealed record ProcessTerminationOutcome(
    bool Requested,
    bool Succeeded,
    bool TimedOut = false,
    Exception? Error = null)
{
    public static ProcessTerminationOutcome NotRequested { get; } = new(false, false);
}

internal sealed record ProcessExecutionDiagnostic(
    string Kind,
    string Stage,
    string? Stream,
    string Message,
    Exception? Error = null);

internal static class ProcessExecutionDiagnosticKinds
{
    public const string OutputReadFailure = "output-read-failure";
    public const string OutputFileFailure = "output-file-failure";
    public const string OutputObserverFailure = "output-observer-failure";
    public const string DrainTimeout = "bounded-drain";
    public const string StdinFailure = "stdin-failure";
    public const string ExecutionFailure = "execution-failure";
}

internal sealed record ProcessOutputOptions
{
    public bool Capture { get; init; } = true;
    public string? FilePath { get; init; }
    public Encoding? FileEncoding { get; init; }
    public Action<string>? LineObserver { get; init; }
    public Func<ReadOnlyMemory<char>, CancellationToken, ValueTask>? ChunkObserver { get; init; }
}

internal sealed record ProcessExecutionRequest(
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory)
{
    public IReadOnlyDictionary<string, string?>? EnvironmentOverrides { get; init; }
    public string? StandardInput { get; init; }
    public Encoding? StandardInputEncoding { get; init; }
    public Encoding? StandardOutputEncoding { get; init; }
    public Encoding? StandardErrorEncoding { get; init; }
    public TimeSpan? Timeout { get; init; }
    public ProcessTerminationOptions TerminationOptions { get; init; } = ProcessExecutor.DefaultTerminationOptions;
    public TimeSpan OutputDrainTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public ProcessOutputOptions StandardOutput { get; init; } = new();
    public ProcessOutputOptions StandardError { get; init; } = new();
}

internal sealed record ProcessExecutionResult(
    int? ProcessId,
    int? ExitCode,
    ProcessCompletionReason CompletionReason,
    string StandardOutput,
    string StandardError,
    ProcessTerminationOutcome Termination,
    IReadOnlyList<ProcessExecutionDiagnostic> Diagnostics,
    Exception? Failure = null)
{
    public bool WasStarted => ProcessId is not null;
}

internal interface IProcessExecutor
{
    Task<ProcessExecutionResult> RunAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken);
}

internal sealed class ProcessExecutionException(
    ProcessExecutionResult result,
    Exception? innerException = null)
    : Exception("External process could not be started or initialized.", innerException ?? result.Failure)
{
    public ProcessExecutionResult Result { get; } = result;
}

internal sealed class ProcessExecutor : IProcessExecutor
{
    internal static readonly ProcessTerminationOptions DefaultTerminationOptions = new(
        TryGracefulClose: false,
        GracefulCloseDelay: TimeSpan.Zero,
        TerminationTimeout: TimeSpan.FromSeconds(5));

    public static ProcessExecutor Shared { get; } = new();

    private readonly ProcessSupervisor supervisor;

    internal ProcessExecutor(ProcessSupervisor? supervisor = null)
    {
        this.supervisor = supervisor ?? ProcessSupervisor.Shared;
    }

    public async Task<ProcessExecutionResult> RunAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var start = await TryStartAsync(request, cancellationToken);
        if (start.Failure is not null)
            return start.Failure;

        await using var handle = start.Handle!;
        var completionReason = ProcessCompletionReason.Exited;
        Exception? failure = null;
        try
        {
            var exited = await handle.WaitForExitAsync(request.Timeout, cancellationToken);
            if (!exited)
            {
                completionReason = ProcessCompletionReason.TimedOut;
                await handle.TerminateAsync();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            completionReason = ProcessCompletionReason.Cancelled;
            await handle.TerminateAsync();
        }
        catch (Exception exception)
        {
            completionReason = ProcessCompletionReason.InfrastructureFailed;
            failure = exception;
            handle.AddDiagnostic(new(
                ProcessExecutionDiagnosticKinds.ExecutionFailure,
                "wait",
                null,
                "External process execution failed while waiting for exit.",
                exception));
            await handle.TerminateAsync();
        }

        return await handle.CompleteAsync(completionReason, failure);
    }

    public async Task<ProcessExecutionHandle> StartAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var start = await TryStartAsync(request, cancellationToken);
        if (start.Failure is not null)
            throw new ProcessExecutionException(start.Failure, start.Failure.Failure);
        return start.Handle!;
    }

    private async Task<ProcessStartOutcome> TryStartAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken)
    {
        Process? process = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            process = supervisor.Start(CreateStartInfo(request));
            if (process is null)
            {
                var error = new InvalidOperationException(
                    $"External process '{request.Executable}' did not start.");
                return new(null, StartFailure(request, error));
            }
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            return new(null, new(
                null,
                null,
                ProcessCompletionReason.Cancelled,
                "",
                "",
                ProcessTerminationOutcome.NotRequested,
                [],
                exception));
        }
        catch (Exception exception)
        {
            process?.Dispose();
            return new(null, StartFailure(request, exception));
        }

        var handle = new ProcessExecutionHandle(process, request, supervisor);
        if (request.StandardInput is null)
            return new(handle, null);

        try
        {
            await process.StandardInput.WriteAsync(request.StandardInput.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
            process.StandardInput.Close();
            return new(handle, null);
        }
        catch (Exception exception)
        {
            handle.AddDiagnostic(new(
                ProcessExecutionDiagnosticKinds.StdinFailure,
                "write-stdin",
                "stdin",
                "Could not write or close redirected standard input.",
                exception));
            var reason = cancellationToken.IsCancellationRequested
                ? ProcessCompletionReason.Cancelled
                : ProcessCompletionReason.InfrastructureFailed;
            await handle.TerminateAsync();
            return new(null, await handle.CompleteAsync(reason, exception));
        }
    }

    private static ProcessStartInfo CreateStartInfo(ProcessExecutionRequest request)
    {
        var start = new ProcessStartInfo(request.Executable)
        {
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = request.StandardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (request.StandardInputEncoding is not null)
            start.StandardInputEncoding = request.StandardInputEncoding;
        if (request.StandardOutputEncoding is not null)
            start.StandardOutputEncoding = request.StandardOutputEncoding;
        if (request.StandardErrorEncoding is not null)
            start.StandardErrorEncoding = request.StandardErrorEncoding;

        foreach (var argument in request.Arguments)
            start.ArgumentList.Add(argument);

        if (request.EnvironmentOverrides is not null)
        {
            foreach (var (name, value) in request.EnvironmentOverrides)
            {
                if (value is null)
                    start.Environment.Remove(name);
                else
                    start.Environment[name] = value;
            }
        }

        return start;
    }

    private static ProcessExecutionResult StartFailure(
        ProcessExecutionRequest request,
        Exception exception) =>
        new(
            null,
            null,
            ProcessCompletionReason.StartFailed,
            "",
            "",
            ProcessTerminationOutcome.NotRequested,
            [new(
                ProcessExecutionDiagnosticKinds.ExecutionFailure,
                "start",
                null,
                $"Could not start external process '{request.Executable}'.",
                exception)],
            exception);

    private sealed record ProcessStartOutcome(
        ProcessExecutionHandle? Handle,
        ProcessExecutionResult? Failure);
}

internal sealed class ProcessExecutionHandle : IAsyncDisposable
{
    private readonly Process process;
    private readonly ProcessExecutionRequest request;
    private readonly ProcessSupervisor supervisor;
    private readonly CancellationTokenSource outputCancellation = new();
    private readonly ConcurrentQueue<ProcessExecutionDiagnostic> diagnostics = new();
    private readonly ProcessOutputPump stdout;
    private readonly ProcessOutputPump stderr;
    private readonly SemaphoreSlim terminationGate = new(1, 1);
    private readonly SemaphoreSlim completionGate = new(1, 1);
    private ProcessTerminationOutcome termination = ProcessTerminationOutcome.NotRequested;
    private ProcessExecutionResult? completedResult;
    private int? knownExitCode;
    private bool knownExited;
    private bool processDisposed;

    internal ProcessExecutionHandle(
        Process process,
        ProcessExecutionRequest request,
        ProcessSupervisor supervisor)
    {
        this.process = process;
        this.request = request;
        this.supervisor = supervisor;
        ProcessId = process.Id;
        stdout = new(
            "stdout",
            process.StandardOutput,
            request.StandardOutput,
            request.StandardOutputEncoding,
            diagnostics,
            outputCancellation.Token);
        stderr = new(
            "stderr",
            process.StandardError,
            request.StandardError,
            request.StandardErrorEncoding,
            diagnostics,
            outputCancellation.Token);
    }

    public int ProcessId { get; }

    public bool HasExited
    {
        get
        {
            RefreshExitState();
            return knownExited;
        }
    }

    public int? ExitCode
    {
        get
        {
            RefreshExitState();
            return knownExitCode;
        }
    }

    public async Task<bool> WaitForExitAsync(
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        if (HasExited)
            return true;
        var exited = await supervisor.WaitForExitAsync(process, timeout, cancellationToken);
        if (exited)
            RefreshExitState();
        return exited;
    }

    public async Task<ProcessTerminationOutcome> TerminateAsync(
        ProcessTerminationOptions? options = null)
    {
        await terminationGate.WaitAsync(CancellationToken.None);
        try
        {
            if (termination.Requested)
                return termination;

            if (HasExited)
            {
                termination = new(true, true);
                return termination;
            }

            var result = await supervisor.TerminateAsync(
                process,
                options ?? request.TerminationOptions);
            RefreshExitState();
            termination = new(
                Requested: true,
                Succeeded: result.Succeeded,
                TimedOut: result.TimedOut,
                Error: result.Error);
            return termination;
        }
        finally
        {
            terminationGate.Release();
        }
    }

    public async Task<ProcessExecutionResult> CompleteAsync(
        ProcessCompletionReason completionReason = ProcessCompletionReason.Exited,
        Exception? failure = null)
    {
        await completionGate.WaitAsync(CancellationToken.None);
        try
        {
            if (completedResult is not null)
                return completedResult;

            if (!HasExited)
                await TerminateAsync();

            RefreshExitState();
            await DrainOutputAsync();
            stdout.Freeze();
            stderr.Freeze();
            RefreshExitState();

            completedResult = new(
                ProcessId,
                knownExitCode,
                completionReason,
                stdout.CapturedText,
                stderr.CapturedText,
                termination,
                diagnostics.ToArray(),
                failure);
            DisposeProcess();
            return completedResult;
        }
        finally
        {
            completionGate.Release();
        }
    }

    internal void AddDiagnostic(ProcessExecutionDiagnostic diagnostic) =>
        diagnostics.Enqueue(diagnostic);

    public async ValueTask DisposeAsync()
    {
        if (completedResult is not null)
            return;

        var exitedBeforeDispose = HasExited;
        if (!exitedBeforeDispose)
            await TerminateAsync();
        await CompleteAsync(
            exitedBeforeDispose
                ? ProcessCompletionReason.Exited
                : ProcessCompletionReason.InfrastructureFailed);
    }

    private async Task DrainOutputAsync()
    {
        var drain = Task.WhenAll(stdout.Completion, stderr.Completion);
        if (await supervisor.WaitAsync(drain, request.OutputDrainTimeout))
        {
            try
            {
                await drain;
            }
            catch
            {
                // Pumps translate their failures into diagnostics.
            }
            return;
        }

        diagnostics.Enqueue(new(
            ProcessExecutionDiagnosticKinds.DrainTimeout,
            "drain-output",
            null,
            $"Output streams did not finish within {request.OutputDrainTimeout}."));
        stdout.Freeze();
        stderr.Freeze();
        outputCancellation.Cancel();
        try { process.StandardOutput.Dispose(); } catch { }
        try { process.StandardError.Dispose(); } catch { }

        try
        {
            if (await supervisor.WaitAsync(drain, TimeSpan.FromMilliseconds(250)))
                await drain;
            else
                ObserveFault(drain);
        }
        catch
        {
            // Pumps translate their failures into diagnostics.
        }
    }

    private static void ObserveFault(Task task)
    {
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void RefreshExitState()
    {
        if (knownExited || processDisposed)
            return;
        try
        {
            if (!process.HasExited)
                return;
            knownExited = true;
            knownExitCode = process.ExitCode;
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void DisposeProcess()
    {
        if (processDisposed)
            return;
        processDisposed = true;
        outputCancellation.Dispose();
        process.Dispose();
    }

    private sealed class ProcessOutputPump
    {
        private static readonly Encoding DefaultFileEncoding = new UTF8Encoding(false);
        private readonly string streamName;
        private readonly StreamReader reader;
        private readonly ProcessOutputOptions options;
        private readonly Encoding fileEncoding;
        private readonly ConcurrentQueue<ProcessExecutionDiagnostic> diagnostics;
        private readonly CancellationToken cancellationToken;
        private readonly object sync = new();
        private readonly StringBuilder captured = new();
        private readonly StringBuilder pendingLine = new();
        private bool frozen;
        private bool lineObserverEnabled;
        private bool chunkObserverEnabled;

        public ProcessOutputPump(
            string streamName,
            StreamReader reader,
            ProcessOutputOptions options,
            Encoding? processEncoding,
            ConcurrentQueue<ProcessExecutionDiagnostic> diagnostics,
            CancellationToken cancellationToken)
        {
            this.streamName = streamName;
            this.reader = reader;
            this.options = options;
            fileEncoding = options.FileEncoding ?? processEncoding ?? DefaultFileEncoding;
            this.diagnostics = diagnostics;
            this.cancellationToken = cancellationToken;
            lineObserverEnabled = options.LineObserver is not null;
            chunkObserverEnabled = options.ChunkObserver is not null;
            Completion = PumpAsync();
        }

        public Task Completion { get; }

        public string CapturedText
        {
            get
            {
                lock (sync)
                    return captured.ToString();
            }
        }

        public void Freeze()
        {
            lock (sync)
                frozen = true;
        }

        private async Task PumpAsync()
        {
            StreamWriter? writer = null;
            if (options.FilePath is not null)
            {
                try
                {
                    var directory = Path.GetDirectoryName(options.FilePath);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);
                    writer = new StreamWriter(options.FilePath, false, fileEncoding);
                }
                catch (Exception exception)
                {
                    Record(
                        ProcessExecutionDiagnosticKinds.OutputFileFailure,
                        "open-output-file",
                        $"Could not open {streamName} output file.",
                        exception);
                }
            }

            try
            {
                var buffer = new char[4096];
                while (true)
                {
                    int count;
                    try
                    {
                        count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception exception)
                    {
                        Record(
                            ProcessExecutionDiagnosticKinds.OutputReadFailure,
                            "read-output",
                            $"Could not read redirected {streamName}.",
                            exception);
                        break;
                    }

                    if (count == 0)
                        break;

                    var memory = buffer.AsMemory(0, count);
                    if (options.Capture)
                    {
                        lock (sync)
                        {
                            if (!frozen)
                                captured.Append(memory.Span);
                        }
                    }

                    if (writer is not null)
                    {
                        try
                        {
                            await writer.WriteAsync(memory, cancellationToken);
                            await writer.FlushAsync(cancellationToken);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception exception)
                        {
                            Record(
                                ProcessExecutionDiagnosticKinds.OutputFileFailure,
                                "write-output-file",
                                $"Could not persist redirected {streamName}.",
                                exception);
                            try { await writer.DisposeAsync(); } catch { }
                            writer = null;
                        }
                    }

                    if (chunkObserverEnabled && options.ChunkObserver is not null)
                    {
                        try
                        {
                            await options.ChunkObserver(memory, cancellationToken);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception exception)
                        {
                            chunkObserverEnabled = false;
                            Record(
                                ProcessExecutionDiagnosticKinds.OutputObserverFailure,
                                "observe-output-chunk",
                                $"{streamName} chunk observer failed.",
                                exception);
                        }
                    }

                    if (lineObserverEnabled)
                        ObserveLines(memory.Span);
                }

                FlushPendingLine();
            }
            finally
            {
                if (writer is not null)
                {
                    try
                    {
                        await writer.DisposeAsync();
                    }
                    catch (Exception exception)
                    {
                        Record(
                            ProcessExecutionDiagnosticKinds.OutputFileFailure,
                            "close-output-file",
                            $"Could not close {streamName} output file.",
                            exception);
                    }
                }
            }
        }

        private void ObserveLines(ReadOnlySpan<char> value)
        {
            if (!lineObserverEnabled || options.LineObserver is null)
                return;

            foreach (var character in value)
            {
                if (character != '\n')
                {
                    pendingLine.Append(character);
                    continue;
                }

                var line = pendingLine.ToString();
                pendingLine.Clear();
                if (line.EndsWith('\r'))
                    line = line[..^1];
                InvokeLineObserver(line);
                if (!lineObserverEnabled)
                    return;
            }
        }

        private void FlushPendingLine()
        {
            if (!lineObserverEnabled || pendingLine.Length == 0)
                return;
            var line = pendingLine.ToString();
            pendingLine.Clear();
            if (line.EndsWith('\r'))
                line = line[..^1];
            InvokeLineObserver(line);
        }

        private void InvokeLineObserver(string line)
        {
            try
            {
                options.LineObserver?.Invoke(line);
            }
            catch (Exception exception)
            {
                lineObserverEnabled = false;
                Record(
                    ProcessExecutionDiagnosticKinds.OutputObserverFailure,
                    "observe-output-line",
                    $"{streamName} line observer failed.",
                    exception);
            }
        }

        private void Record(
            string kind,
            string stage,
            string message,
            Exception exception) =>
            diagnostics.Enqueue(new(kind, stage, streamName, message, exception));
    }
}
