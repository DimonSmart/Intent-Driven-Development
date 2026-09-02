using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Idd.Factory.Domain;

internal enum FactoryRuntimeCommand { Run, Continue, Cancel }

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
        string? request,
        string? answer,
        CancellationToken cancellationToken)
    {
        ValidateWorkspace(workspace);
        if (command == FactoryRuntimeCommand.Run && (request is null || request.Length == 0))
            throw new ArgumentException("request is required.", nameof(request));
        if (command != FactoryRuntimeCommand.Run && request is not null)
            throw new ArgumentException("request is supported only for factory_run.", nameof(request));
        if (command != FactoryRuntimeCommand.Continue && answer is not null)
            throw new ArgumentException("answer is supported only for factory_continue.", nameof(answer));

        if (request is not null && InvalidUnicodeReason(request, "Factory request") is { } requestError)
            return new("INVALID_REQUEST_ENCODING", "unknown", requestError, "Resubmit the original request without corrupted Unicode replacement characters.", null);
        if (answer is not null && InvalidUnicodeReason(answer, "Factory clarification answer") is { } answerError)
            return new("INVALID_CLARIFICATION_ENCODING", "unknown", answerError, "Resubmit the clarification answer without corrupted Unicode replacement characters.", null);

        var runtimeAssembly = Path.Combine(AppContext.BaseDirectory, "idd-factory.dll");
        if (!File.Exists(runtimeAssembly))
            throw new FactoryTransportException("FACTORY_TRANSPORT_UNAVAILABLE", "The packaged Factory Runtime assembly is missing.");
        var pluginRoot = ResolvePluginRoot(AppContext.BaseDirectory);
        string? requestFile = null;
        string? answerFile = null;
        try
        {
            if (request is not null)
            {
                requestFile = Path.Combine(Path.GetTempPath(), $"idd-factory-request-{Guid.NewGuid():N}.md");
                await File.WriteAllTextAsync(requestFile, request, TextUtf8, cancellationToken);
            }
            if (answer is not null)
            {
                answerFile = Path.Combine(Path.GetTempPath(), $"idd-factory-answer-{Guid.NewGuid():N}.txt");
                await File.WriteAllTextAsync(answerFile, answer, TextUtf8, cancellationToken);
            }

            var invocation = BuildInvocation(command, workspace, requestFile, answerFile, runtimeAssembly, pluginRoot);
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
            if (requestFile is not null) TryDeleteTemporaryFile(requestFile);
            if (answerFile is not null) TryDeleteTemporaryFile(answerFile);
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
        string? requestFile,
        string? answerFile,
        string runtimeAssembly,
        string pluginRoot)
    {
        var arguments = new List<string>
        {
            runtimeAssembly,
            command.ToString().ToLowerInvariant(),
            "--workspace", workspace,
            "--plugin-root", pluginRoot
        };
        if (command == FactoryRuntimeCommand.Run)
        {
            if (requestFile is null) throw new ArgumentException("requestFile is required for Factory run.", nameof(requestFile));
            arguments.AddRange(["--request-file", requestFile]);
        }
        if (answerFile is not null)
            arguments.AddRange(["--answer-file", answerFile]);
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

internal sealed class SystemFactoryProcessInvoker(Action<int>? onProcessStarted = null) : IFactoryProcessInvoker
{
    private static readonly UTF8Encoding TransportUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public async Task<FactoryProcessResult> RunAsync(FactoryProcessInvocation invocation, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(invocation.Executable)
        {
            WorkingDirectory = invocation.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = TransportUtf8,
            CreateNoWindow = true
        };
        foreach (var argument in invocation.Arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("The packaged Factory Runtime process did not start.");
            onProcessStarted?.Invoke(process.Id);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            throw new FactoryTransportException("FACTORY_TRANSPORT_UNAVAILABLE", "The packaged Factory Runtime process did not start.", exception);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            if (invocation.StandardInput is not null)
            {
                await process.StandardInput.WriteAsync(invocation.StandardInput.AsMemory(), cancellationToken);
                process.StandardInput.Close();
            }
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdoutTask, stderrTask);
            ReleaseRuntimeLockAfterForcedTermination(invocation, process.Id);
            throw;
        }

        return new(process.ExitCode, await stdoutTask, await stderrTask);
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
