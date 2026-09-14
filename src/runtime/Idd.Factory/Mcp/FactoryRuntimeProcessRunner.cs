using System.Text;
using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Processes;
using Idd.Factory.Runtime;

internal enum FactoryRuntimeCommand { Run, Restart, Continue, Retry, Cancel }

internal sealed class FactoryRuntimeProcessRunner(
    IFactoryProcessInvoker processInvoker,
    Action<string>? deleteTemporaryFile = null)
{
    private const int DiagnosticTailLimit = 2048;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly UTF8Encoding TextUtf8 = new(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);

    public async Task<FactoryMcpResult> RunAsync(
        FactoryRuntimeCommand command,
        string workspace,
        string? input,
        CancellationToken cancellationToken,
        int? additionalAttempts = null)
    {
        ValidateWorkspace(workspace);
        if ((command is FactoryRuntimeCommand.Run or FactoryRuntimeCommand.Restart) && (input is null || input.Length == 0))
            throw new ArgumentException("request is required.", nameof(input));
        if (command == FactoryRuntimeCommand.Cancel && input is not null)
            throw new ArgumentException("input is not supported for factory_cancel.", nameof(input));
        if (command == FactoryRuntimeCommand.Retry && (additionalAttempts is null || additionalAttempts < 1))
            throw new ArgumentException("additionalAttempts must be at least 1 for factory_retry.", nameof(additionalAttempts));
        var requestCommand = command is FactoryRuntimeCommand.Run or FactoryRuntimeCommand.Restart;
        var inputLabel = requestCommand ? "Factory request" : "Factory user answer";
        if (input is not null && InvalidUnicodeReason(input, inputLabel) is { } inputError)
            return new(
                requestCommand ? "INVALID_REQUEST_ENCODING" : "INVALID_USER_ANSWER_ENCODING",
                "unknown",
                inputError,
                requestCommand
                    ? "Resubmit the original request without corrupted Unicode replacement characters."
                    : "Resubmit the user answer without corrupted Unicode replacement characters.",
                null);

        var runtimeAssembly = Path.Combine(AppContext.BaseDirectory, "idd-factory.dll");
        if (!File.Exists(runtimeAssembly))
            throw new FactoryTransportException("FACTORY_TRANSPORT_UNAVAILABLE", "The packaged Factory Runtime assembly is missing.");
        var pluginRoot = ResolvePluginRoot(AppContext.BaseDirectory);
        string? inputFile = null;
        try
        {
            if (input is not null)
            {
                var prefix = requestCommand ? "request" : "answer";
                inputFile = Path.Combine(Path.GetTempPath(), $"idd-factory-{prefix}-{Guid.NewGuid():N}.md");
                await File.WriteAllTextAsync(inputFile, input, TextUtf8, cancellationToken);
            }
            var invocation = BuildInvocation(command, workspace, inputFile, runtimeAssembly, pluginRoot, additionalAttempts);
            FactoryProcessResult processResult;
            try
            {
                processResult = await processInvoker.RunAsync(invocation, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is not FactoryTransportException)
            {
                throw new FactoryTransportException("FACTORY_TRANSPORT_UNAVAILABLE", "The packaged Factory Runtime could not be started.", exception);
            }

            FactoryCliOutcome? outcome;
            try
            {
                outcome = JsonSerializer.Deserialize<FactoryCliOutcome>(processResult.StandardOutput, FactoryJson.Options);
            }
            catch (JsonException exception)
            {
                throw ProtocolError(processResult, "The packaged Factory Runtime returned invalid JSON.", exception);
            }
            if (outcome is null)
                throw ProtocolError(processResult, "The packaged Factory Runtime returned no structured outcome.");

            return new(outcome.FactoryOutcome, outcome.RunId, outcome.Reason, outcome.ResumeWhen, outcome.ResultDirectory, outcome.Payload);
        }
        finally
        {
            if (inputFile is not null) TryDeleteTemporaryFile(inputFile);
        }
    }

    private static string? InvalidUnicodeReason(string text, string label)
    {
        if (text.Contains('\uFFFD'))
            return $"{label} contains Unicode replacement character U+FFFD and may have been corrupted during transport.";
        try
        {
            _ = StrictUtf8.GetByteCount(text);
            return null;
        }
        catch (EncoderFallbackException)
        {
            return $"{label} contains invalid Unicode data that cannot be encoded as UTF-8 without replacement.";
        }
    }

    private void TryDeleteTemporaryFile(string path)
    {
        try
        {
            (deleteTemporaryFile ?? File.Delete)(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    internal static FactoryProcessInvocation BuildInvocation(
        FactoryRuntimeCommand command,
        string workspace,
        string? inputFile,
        string runtimeAssembly,
        string pluginRoot,
        int? additionalAttempts = null)
    {
        var arguments = new List<string>
        {
            runtimeAssembly,
            command.ToString().ToLowerInvariant(),
            "--workspace", workspace,
            "--plugin-root", pluginRoot
        };
        if (command is FactoryRuntimeCommand.Run or FactoryRuntimeCommand.Restart)
        {
            if (inputFile is null) throw new ArgumentException("inputFile is required for Factory run or restart.", nameof(inputFile));
            arguments.AddRange(["--request-file", inputFile]);
        }
        else if (command == FactoryRuntimeCommand.Continue && inputFile is not null)
        {
            arguments.AddRange(["--answer-file", inputFile]);
        }
        else if (command == FactoryRuntimeCommand.Retry)
        {
            if (additionalAttempts is null) throw new ArgumentException("additionalAttempts is required for retry.", nameof(additionalAttempts));
            arguments.AddRange(["--additional-attempts", additionalAttempts.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        }
        return new(ResolveDotnetHost(), arguments, workspace, null);
    }

    internal static string ResolvePluginRoot(string runtimeDirectory) =>
        Directory.GetParent(Path.TrimEndingDirectorySeparator(runtimeDirectory))?.FullName
        ?? throw new FactoryTransportException("FACTORY_TRANSPORT_UNAVAILABLE", "The installed plugin root could not be resolved from the packaged runtime directory.");

    internal static void ValidateWorkspace(string workspace)
    {
        if (string.IsNullOrWhiteSpace(workspace) || !Path.IsPathFullyQualified(workspace))
            throw new ArgumentException("workspace must be an absolute path.", nameof(workspace));
        if (!Directory.Exists(workspace))
            throw new DirectoryNotFoundException($"Factory workspace does not exist: {workspace}");
    }

    private static string ResolveDotnetHost()
    {
        var currentProcess = Environment.ProcessPath;
        return currentProcess is not null && Path.GetFileNameWithoutExtension(currentProcess).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? currentProcess
            : "dotnet";
    }

    private static FactoryTransportException ProtocolError(FactoryProcessResult result, string message, Exception? inner = null)
    {
        var stderr = result.StandardError.Length <= DiagnosticTailLimit
            ? result.StandardError
            : result.StandardError[^DiagnosticTailLimit..];
        var diagnostic = $"{message} Exit code: {result.ExitCode}. Stderr tail: {stderr}";
        return new("FACTORY_TRANSPORT_PROTOCOL_ERROR", diagnostic, inner);
    }
}

internal interface IFactoryProcessInvoker
{
    Task<FactoryProcessResult> RunAsync(FactoryProcessInvocation invocation, CancellationToken cancellationToken);
}

internal sealed class SystemFactoryProcessInvoker : IFactoryProcessInvoker
{
    private static readonly UTF8Encoding TransportUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly IProcessExecutor processExecutor;

    public SystemFactoryProcessInvoker() : this(ProcessExecutor.Shared) { }

    internal SystemFactoryProcessInvoker(IProcessExecutor processExecutor)
    {
        this.processExecutor = processExecutor;
    }

    public async Task<FactoryProcessResult> RunAsync(
        FactoryProcessInvocation invocation,
        CancellationToken cancellationToken)
    {
        var result = await processExecutor.RunAsync(
            new(
                invocation.Executable,
                invocation.Arguments,
                invocation.WorkingDirectory)
            {
                StandardInput = invocation.StandardInput,
                StandardInputEncoding = invocation.StandardInput is null ? null : TransportUtf8,
                StandardOutputEncoding = TransportUtf8,
                StandardErrorEncoding = TransportUtf8
            },
            cancellationToken);

        if (result.CompletionReason == ProcessCompletionReason.Cancelled)
        {
            if (result.ProcessId is { } processId && result.Termination.Requested)
                ReleaseRuntimeLockAfterForcedTermination(invocation, processId);
            throw new OperationCanceledException(cancellationToken);
        }

        if (result.CompletionReason is ProcessCompletionReason.StartFailed
            or ProcessCompletionReason.InfrastructureFailed)
        {
            throw new FactoryTransportException(
                "FACTORY_TRANSPORT_UNAVAILABLE",
                "The packaged Factory Runtime process did not start or complete its transport lifecycle.",
                result.Failure);
        }

        return new(
            result.ExitCode ?? -1,
            result.StandardOutput,
            result.StandardError);
    }

    internal static bool ReleaseRuntimeLockAfterForcedTermination(FactoryProcessInvocation invocation, int processId)
    {
        var lockPath = Path.Combine(invocation.WorkingDirectory, ".idd", "factory", "runtime.lock");
        return FactoryRuntimeLock.TryReleaseOwned(lockPath, processId);
    }
}

internal sealed record FactoryProcessInvocation(
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string? StandardInput);

internal sealed record FactoryProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal sealed class FactoryTransportException(string code, string message, Exception? innerException = null)
    : Exception($"{code}: {message}", innerException)
{
    public string Code { get; } = code;
}
