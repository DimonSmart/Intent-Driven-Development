using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Idd.Factory.Domain;

namespace Idd.Factory.Agents;

internal static class AgentFailureCodes
{
    public const string CapacityUnavailable = "AGENT_CAPACITY_UNAVAILABLE";
    public const string RateLimited = "AGENT_RATE_LIMITED";
    public const string AuthenticationRequired = "AGENT_AUTHENTICATION_REQUIRED";
    public const string TransportFailure = "AGENT_TRANSPORT_FAILURE";
    public const string CommandTimeout = "AGENT_COMMAND_TIMEOUT";
    public const string CommandIncomplete = "AGENT_COMMAND_INCOMPLETE";

    public static bool IsExternalBackendBlocker(string code) =>
        code is CapacityUnavailable or RateLimited or AuthenticationRequired;
}

internal static class AgentFailureDiagnosticSources
{
    public const string StructuredAgentEvent = "structured-agent-event";
    public const string Stderr = "stderr";
    public const string Stdout = "stdout";
    public const string ProcessExit = "process-exit";

    public static bool IsKnown(string source) =>
        source is StructuredAgentEvent or Stderr or Stdout or ProcessExit;
}

public sealed record AgentFailureDiagnostic(
    int SchemaVersion,
    string FailureCode,
    string HumanReadableMessage,
    int? ExitCode,
    AgentTerminationKind TerminationKind,
    string Source)
{
    public const int CurrentSchemaVersion = 1;
}

internal sealed record BackendFailureSignal(
    string Message,
    string Source,
    string? Code = null,
    string? Type = null);

internal static class AgentBackendFailureClassifier
{
    private const int MaximumDiagnosticLength = 4096;
    private const int MaximumMatchingLength = 16 * 1024;

    private static readonly Regex SecretAssignment = new(
        @"(?i)\b(api[_ -]?key|access[_ -]?token|refresh[_ -]?token|authorization|credential(?:s)?)\b\s*[:=]\s*[^\s,;]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex BearerSecret = new(
        @"(?i)\bbearer\s+[A-Za-z0-9._~+/=-]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex OpenAiStyleSecret = new(
        @"\bsk-[A-Za-z0-9_-]{12,}\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Http429 = new(
        @"(?i)\b(?:HTTP(?:/\d(?:\.\d)?)?\s+)?429\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Http401 = new(
        @"(?i)\b(?:HTTP(?:/\d(?:\.\d)?)?\s+)?401\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static AgentFailureDiagnostic Classify(AgentProcessResult process)
    {
        var signal = ExtractPrimarySignal(process);
        var failureCode = process.TerminationKind switch
        {
            AgentTerminationKind.CommandTimeout => AgentFailureCodes.CommandTimeout,
            AgentTerminationKind.IncompleteCommand => AgentFailureCodes.CommandIncomplete,
            _ => ClassifyExternal(signal) ?? AgentFailureCodes.TransportFailure
        };
        var message = signal is null
            ? $"Agent exited with {process.ExitCode?.ToString() ?? "unknown"} ({process.TerminationKind})."
            : BoundAndSanitize(signal.Message, signal.Source == AgentFailureDiagnosticSources.StructuredAgentEvent);

        return new(
            AgentFailureDiagnostic.CurrentSchemaVersion,
            failureCode,
            message,
            process.ExitCode,
            process.TerminationKind,
            signal?.Source ?? AgentFailureDiagnosticSources.ProcessExit);
    }

    private static string? ClassifyExternal(BackendFailureSignal? signal)
    {
        if (signal is null)
            return null;

        if (signal.Source == AgentFailureDiagnosticSources.StructuredAgentEvent)
        {
            var structuredCode = ClassifyStructuredIdentifier(signal.Code)
                                 ?? ClassifyStructuredIdentifier(signal.Type);
            if (structuredCode is not null)
                return structuredCode;
        }

        var text = BoundForMatching(signal.Message);
        if (ContainsAny(
                text,
                "you've hit your usage limit",
                "you have hit your usage limit",
                "usage limit exhausted",
                "usage quota exhausted",
                "quota exceeded",
                "quota exhausted",
                "insufficient quota",
                "account usage limit",
                "model usage limit"))
        {
            return AgentFailureCodes.CapacityUnavailable;
        }

        if (ContainsAny(text, "rate limit exceeded", "rate limit reached", "too many requests")
            || signal.Source != AgentFailureDiagnosticSources.Stdout && Http429.IsMatch(text))
        {
            return AgentFailureCodes.RateLimited;
        }

        if (ContainsAny(
                text,
                "authentication required",
                "login required",
                "invalid authentication",
                "expired authentication",
                "invalid credential",
                "expired credential",
                "invalid api key",
                "expired api key",
                "401 unauthorized")
            || signal.Source != AgentFailureDiagnosticSources.Stdout && Http401.IsMatch(text))
        {
            return AgentFailureCodes.AuthenticationRequired;
        }

        return null;
    }

    private static string? ClassifyStructuredIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim().ToLowerInvariant() switch
        {
            "usage_limit" or "usage_limit_exceeded" or "quota_exceeded" or "quota_exhausted" or "insufficient_quota"
                => AgentFailureCodes.CapacityUnavailable,
            "rate_limit" or "rate_limit_error" or "rate_limit_exceeded" or "too_many_requests"
                => AgentFailureCodes.RateLimited,
            "authentication_required" or "authentication_error" or "unauthorized" or "invalid_authentication" or "invalid_api_key" or "expired_credential" or "expired_credentials"
                => AgentFailureCodes.AuthenticationRequired,
            _ => null
        };
    }

    private static BackendFailureSignal? ExtractPrimarySignal(AgentProcessResult process)
    {
        var structured = TryExtractStructuredFailure(process.Stdout);
        if (structured is not null)
            return structured;

        if (!string.IsNullOrWhiteSpace(process.Stderr))
        {
            return new(
                BoundTail(process.Stderr, MaximumMatchingLength),
                AgentFailureDiagnosticSources.Stderr);
        }

        if (!string.IsNullOrWhiteSpace(process.Stdout))
        {
            return new(
                BoundTail(process.Stdout, MaximumMatchingLength),
                AgentFailureDiagnosticSources.Stdout);
        }

        return null;
    }

    private static BackendFailureSignal? TryExtractStructuredFailure(string stdout)
    {
        BackendFailureSignal? errorSignal = null;
        BackendFailureSignal? turnFailedSignal = null;
        using var reader = new StringReader(stdout);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("type", out var eventType)
                    || eventType.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                switch (eventType.GetString())
                {
                    case "turn.failed" when root.TryGetProperty("error", out var failedError)
                                            && failedError.ValueKind == JsonValueKind.Object:
                        turnFailedSignal = ReadStructuredError(failedError);
                        break;
                    case "error":
                        errorSignal = root.TryGetProperty("error", out var nestedError)
                                      && nestedError.ValueKind == JsonValueKind.Object
                            ? ReadStructuredError(nestedError)
                            : ReadStructuredError(root);
                        break;
                }
            }
            catch (JsonException)
            {
            }
        }

        return turnFailedSignal ?? errorSignal;
    }

    private static BackendFailureSignal? ReadStructuredError(JsonElement error)
    {
        if (!error.TryGetProperty("message", out var messageElement)
            || messageElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(messageElement.GetString()))
        {
            return null;
        }

        return new(
            messageElement.GetString()!,
            AgentFailureDiagnosticSources.StructuredAgentEvent,
            ReadOptionalString(error, "code"),
            ReadOptionalString(error, "type"));
    }

    private static string? ReadOptionalString(JsonElement value, string propertyName) =>
        value.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool ContainsAny(string value, params string[] patterns) =>
        patterns.Any(pattern => value.Contains(pattern, StringComparison.OrdinalIgnoreCase));

    private static string BoundForMatching(string value) =>
        BoundTail(value, MaximumMatchingLength);

    private static string BoundAndSanitize(string value, bool preferHead)
    {
        var trimmed = value.Trim();
        var bounded = trimmed.Length <= MaximumDiagnosticLength
            ? trimmed
            : preferHead
                ? trimmed[..MaximumDiagnosticLength] + " [truncated]"
                : "[truncated] " + trimmed[^MaximumDiagnosticLength..];
        bounded = SecretAssignment.Replace(bounded, "$1=[redacted]");
        bounded = BearerSecret.Replace(bounded, "Bearer [redacted]");
        bounded = OpenAiStyleSecret.Replace(bounded, "[redacted-api-key]");
        return string.IsNullOrWhiteSpace(bounded) ? "no diagnostic output" : bounded;
    }

    private static string BoundTail(string value, int maximumLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maximumLength
            ? trimmed
            : trimmed[^maximumLength..];
    }
}

internal static class AgentFailureDiagnosticStore
{
    public const string FileName = "failure-diagnostic.json";
    private const int MaximumRecoveryLogBytes = 64 * 1024;

    public static string ReferenceFor(string attemptId) =>
        $"attempts/{attemptId}/{FileName}";

    public static async Task<AgentFailureDiagnostic> PersistAsync(
        string attemptId,
        string attemptDirectory,
        AgentProcessResult process,
        CancellationToken cancellationToken)
    {
        var diagnostic = AgentBackendFailureClassifier.Classify(process);
        await WriteAsync(attemptDirectory, diagnostic, cancellationToken);
        return diagnostic;
    }

    public static async Task<AgentFailureDiagnostic?> ReadOrRecoverAsync(
        string attemptId,
        string attemptDirectory,
        CancellationToken cancellationToken)
    {
        var diagnosticPath = Path.Combine(attemptDirectory, FileName);
        if (File.Exists(diagnosticPath))
        {
            try
            {
                var existing = JsonSerializer.Deserialize<AgentFailureDiagnostic>(
                    await File.ReadAllTextAsync(diagnosticPath, cancellationToken),
                    FactoryJson.Options);
                if (existing is null
                    || existing.SchemaVersion != AgentFailureDiagnostic.CurrentSchemaVersion
                    || string.IsNullOrWhiteSpace(existing.FailureCode)
                    || string.IsNullOrWhiteSpace(existing.HumanReadableMessage)
                    || !AgentFailureDiagnosticSources.IsKnown(existing.Source))
                {
                    throw new JsonException("Invalid normalized failure diagnostic.");
                }

                return existing;
            }
            catch (JsonException exception)
            {
                throw new AgentProtocolException(
                    "ATTEMPT_RECOVERY_UNSAFE",
                    $"Attempt '{attemptId}' has invalid {FileName}: {exception.Message}");
            }
        }

        var telemetryPath = Path.Combine(attemptDirectory, "process-telemetry.json");
        if (!File.Exists(telemetryPath))
            return null;

        AgentProcessResult? process;
        try
        {
            process = JsonSerializer.Deserialize<AgentProcessResult>(
                await File.ReadAllTextAsync(telemetryPath, cancellationToken),
                FactoryJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }

        if (process is null
            || process.CompleteResultObserved
            || process.TerminationKind == AgentTerminationKind.Cancelled
            || process.TerminationKind is not (
                AgentTerminationKind.CommandTimeout
                or AgentTerminationKind.IncompleteCommand
                or AgentTerminationKind.TransportFailure))
        {
            return null;
        }

        process = process with
        {
            Stdout = await ReadBoundedTailAsync(
                Path.Combine(attemptDirectory, process.StdoutLogPath),
                cancellationToken),
            Stderr = await ReadBoundedTailAsync(
                Path.Combine(attemptDirectory, process.StderrLogPath),
                cancellationToken)
        };
        var recovered = AgentBackendFailureClassifier.Classify(process);
        await WriteAsync(attemptDirectory, recovered, cancellationToken);
        return recovered;
    }

    private static async Task<string> ReadBoundedTailAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return string.Empty;

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            useAsync: true);
        if (stream.Length > MaximumRecoveryLogBytes)
            stream.Seek(-MaximumRecoveryLogBytes, SeekOrigin.End);
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false),
            detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static async Task WriteAsync(
        string attemptDirectory,
        AgentFailureDiagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(attemptDirectory);
        var path = Path.Combine(attemptDirectory, FileName);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(
            temporary,
            JsonSerializer.Serialize(diagnostic, FactoryJson.Options),
            cancellationToken);
        File.Move(temporary, path, true);
    }
}
