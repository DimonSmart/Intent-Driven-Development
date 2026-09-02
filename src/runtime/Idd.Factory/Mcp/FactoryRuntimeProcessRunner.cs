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
        string? answerFile = null;
        try
        {
            if (answer is not null)
            {
                answerFile = Path.Combine(Path.GetTempPath(), $"idd-factory-answer-{Guid.NewGuid():N}.txt");
                await File.WriteAllTextAsync(answerFile, answer, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true), cancellationToken);
            }

            var invocation = BuildInvocation(command, workspace, request, answerFile, runtimeAssembly, pluginRoot);
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
            if (answerFile is not null)
                TryDeleteTemporaryFile(answerFile);
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
        string? request,
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
            arguments.AddRange(["--request-stdin", "true"]);
        if (answerFile is not null)
            arguments.AddRange(["--answer-file", answerFile]);
        return new(ResolveDotnetHost(), arguments, workspace, request);
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
    private static readonly UTF8Encoding TextUtf8 = new(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);

    public async Task<FactoryProcessResult> RunAsync(FactoryProcessInvocation invocation, CancellationToken cancellationToken)
    {
        string? temporaryRequestFile = null;
        var arguments = invocation.Arguments.ToList();
        var standardInput = invocation.StandardInput;
        var requestStdinIndex = FindRequestStdin(arguments);
        if (standardInput is not null && requestStdinIndex >= 0)
        {
            temporaryRequestFile = Path.Combine(Path.GetTempPath(), $"idd-factory-request-{Guid.NewGuid():N}.md");
            await File.WriteAllTextAsync(temporaryRequestFile, standardInput, TextUtf8, cancellationToken);
            ReplaceRequestStdinWithFile(arguments, requestStdinIndex, temporaryRequestFile);
            standardInput = null;
        }

        try
        {
            var startInfo = new ProcessStartInfo(invocation.Executable)
            {
                WorkingDirectory = invocation.WorkingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = TransportUtf8,
                StandardOutputEncoding = TransportUtf8,
                StandardErrorEncoding = TransportUtf8,
                CreateNoWindow = true
            };
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

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
                if (standardInput is not null)
                {
                    await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken);
                    process.StandardInput.Close();
                }
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                await Task.WhenAll(stdoutTask, stderrTask);
                throw;
            }

            return new(process.ExitCode, await stdoutTask, await stderrTask);
        }
        finally
        {
            if (temporaryRequestFile is not null)
            {
                try { File.Delete(temporaryRequestFile); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    internal static IReadOnlyList<string> UseRequestFileTransport(IReadOnlyList<string> sourceArguments, string requestFile)
    {
        var arguments = sourceArguments.ToList();
        var index = FindRequestStdin(arguments);
        if (index >= 0) ReplaceRequestStdinWithFile(arguments, index, requestFile);
        return arguments;
    }

    private static int FindRequestStdin(IReadOnlyList<string> arguments)
    {
        for (var i = 0; i + 1 < arguments.Count; i++)
            if (arguments[i] == "--request-stdin" && arguments[i + 1] == "true") return i;
        return -1;
    }

    private static void ReplaceRequestStdinWithFile(List<string> arguments, int index, string requestFile)
    {
        arguments.RemoveRange(index, 2);
        arguments.InsertRange(index, ["--request-file", requestFile]);
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
