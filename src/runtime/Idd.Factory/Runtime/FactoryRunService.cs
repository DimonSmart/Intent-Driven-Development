using System.Text;
using System.Text.Json;
using Idd.Factory.Domain;

namespace Idd.Factory.Runtime;

internal sealed class FactoryRunService(
    FactoryRuntimeContext context,
    FactoryStateMachine stateMachine)
{
    private static readonly UTF8Encoding HumanReadableUtf8 =
        new(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);
    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public async Task<FactoryCliOutcome> RunRequestAsync(
        string request,
        string methodologyVersion,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        if (await context.LoadAsync(cancellationToken) is not null)
        {
            return new(
                "RUN_EXISTS",
                "unknown",
                "Use factory_continue for a resumable existing run, factory_restart to replace it, or factory_cancel if no replacement run is wanted.");
        }

        Directory.CreateDirectory(context.CurrentDirectory);
        Directory.CreateDirectory(Path.Combine(context.CurrentDirectory, "work-items"));
        Directory.CreateDirectory(Path.Combine(context.CurrentDirectory, "attempts"));
        Directory.CreateDirectory(Path.Combine(context.CurrentDirectory, "plan-revisions"));
        await File.WriteAllTextAsync(
            Path.Combine(context.CurrentDirectory, "request.md"),
            request,
            HumanReadableUtf8,
            cancellationToken);

        var state = new FactoryState
        {
            MethodologyVersion = methodologyVersion,
            RuntimeVersion = RuntimeVersion(),
            RunId = Guid.NewGuid().ToString("N"),
            FactoryConfigurationHash = context.Configuration.Hash,
            RequestPath = "request.md"
        };
        await context.CreateAsync(state, cancellationToken);
        await context.Events.WriteAsync(
            state.RunId,
            "run-created",
            new { configurationHash = context.Configuration.Hash },
            cancellationToken);
        return await stateMachine.RunAsync(state, cancellationToken);
    }

    public async Task<FactoryCliOutcome> RestartRequestAsync(
        string request,
        string methodologyVersion,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        if (!File.Exists(Path.Combine(context.CurrentDirectory, "state.json")))
        {
            throw new FactoryStateException(
                "MISSING_FACTORY_STATE",
                "No Factory run exists to restart. Use run to start a new workflow.");
        }

        await CancelAsync(cancellationToken);
        return await RunRequestAsync(request, methodologyVersion, cancellationToken);
    }

    public async Task<FactoryCliOutcome> ContinueAsync(
        CancellationToken cancellationToken,
        VerificationConfirmation confirmation,
        bool? verificationPassed,
        string? userAnswer)
    {
        var state = await LoadCurrentStateAsync(cancellationToken);
        if (state.FactoryConfigurationHash != context.Configuration.Hash)
        {
            return new(
                "FACTORY_CONFIGURATION_CHANGED",
                state.RunId,
                "Restore the pinned configuration, use factory_restart to replace the run, or use factory_cancel if no replacement run is wanted.");
        }

        if (userAnswer is not null)
        {
            ValidateUtf8Text(
                userAnswer,
                "INVALID_USER_ANSWER_ENCODING",
                "Factory user answer");
        }

        return await stateMachine.ContinueAsync(
            state,
            new(userAnswer, confirmation, verificationPassed),
            cancellationToken);
    }

    public async Task<FactoryCliOutcome> RetryExhaustedAsync(
        int additionalAttempts,
        CancellationToken cancellationToken)
    {
        var state = await LoadCurrentStateAsync(cancellationToken);
        if (state.FactoryConfigurationHash != context.Configuration.Hash)
        {
            return new(
                "FACTORY_CONFIGURATION_CHANGED",
                state.RunId,
                "Restore the pinned configuration, use factory_restart to replace the run, or use factory_cancel if no replacement run is wanted.");
        }

        return await stateMachine.RetryAsync(
            state,
            additionalAttempts,
            cancellationToken);
    }

    public async Task<FactoryCliOutcome> CancelAsync(
        CancellationToken cancellationToken)
    {
        FactoryState state;
        try
        {
            state = await LoadCurrentStateAsync(cancellationToken);
        }
        catch (FactoryStateException exception) when (exception.Code == "LEGACY_FACTORY_STATE")
        {
            var legacy = await ReadCancellationMetadataAsync(cancellationToken);
            return await ArchiveCancelledRunAsync(
                legacy.RunId,
                legacy.SchemaVersion,
                "The user cancelled an active run written by an older Factory schema.",
                cancellationToken);
        }

        await stateMachine.CancelAsync(state, cancellationToken);
        return await ArchiveCancelledRunAsync(
            state.RunId,
            state.SchemaVersion,
            "The user cancelled the run.",
            cancellationToken);
    }

    private async Task<FactoryState> LoadCurrentStateAsync(
        CancellationToken cancellationToken) =>
        await context.LoadAsync(cancellationToken)
        ?? throw new FactoryStateException(
            "MISSING_FACTORY_STATE",
            "No Factory run exists.");

    private async Task<CancellationMetadata> ReadCancellationMetadataAsync(
        CancellationToken cancellationToken)
    {
        var statePath = Path.Combine(context.CurrentDirectory, "state.json");
        try
        {
            await using var stream = new FileStream(
                statePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("schemaVersion", out var schemaNode)
                || !schemaNode.TryGetInt32(out var schemaVersion))
            {
                throw new FactoryStateException(
                    "CORRUPT_FACTORY_STATE",
                    "state.json has no valid schemaVersion.");
            }

            var runId = document.RootElement.TryGetProperty("runId", out var runIdNode)
                        && runIdNode.ValueKind == JsonValueKind.String
                ? runIdNode.GetString()
                : null;
            return new(
                schemaVersion,
                string.IsNullOrWhiteSpace(runId) ? "unknown" : runId);
        }
        catch (FactoryStateException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                $"Cannot read state.json for cancellation: {exception.Message}");
        }
    }

    private async Task<FactoryCliOutcome> ArchiveCancelledRunAsync(
        string runId,
        int sourceSchemaVersion,
        string reason,
        CancellationToken cancellationToken)
    {
        var cancelledRoot = Path.Combine(
            context.Workspace,
            ".idd",
            "factory",
            "cancelled");
        Directory.CreateDirectory(cancelledRoot);
        var safeRunId = new string(
            runId.Where(char.IsLetterOrDigit).Take(12).ToArray());
        if (safeRunId.Length == 0)
            safeRunId = "unknown";

        var baseName = $"{context.Clock.UtcNow:yyyy-MM-dd_HH-mm-ssZ}_{safeRunId}";
        var destination = Path.Combine(cancelledRoot, baseName);
        for (var suffix = 2; Directory.Exists(destination); suffix++)
            destination = Path.Combine(cancelledRoot, $"{baseName}-{suffix}");

        var record = new
        {
            schemaVersion = 1,
            factoryOutcome = "CANCELLED",
            runId,
            sourceStateSchemaVersion = sourceSchemaVersion,
            cancelledByRuntimeSchemaVersion = FactoryState.CurrentSchemaVersion,
            cancelledAt = context.Clock.UtcNow,
            reason
        };
        var recordPath = Path.Combine(context.CurrentDirectory, "cancellation.json");
        var temporaryPath = recordPath + ".tmp";
        await File.WriteAllTextAsync(
            temporaryPath,
            JsonSerializer.Serialize(record, FactoryJson.Options),
            cancellationToken);
        File.Move(temporaryPath, recordPath, true);

        cancellationToken.ThrowIfCancellationRequested();
        Directory.Move(context.CurrentDirectory, destination);
        var payload = JsonSerializer.SerializeToElement(
            new
            {
                archiveDirectory = destination,
                sourceStateSchemaVersion = sourceSchemaVersion
            },
            FactoryJson.Options);
        return new(
            "CANCELLED",
            runId,
            $"Product changes were preserved. Factory diagnostics were archived at '{destination}'. A new Factory run can now be started.",
            ResultDirectory: null,
            Payload: payload);
    }

    private static void ValidateRequest(string request)
    {
        if (string.IsNullOrWhiteSpace(request))
            throw new ArgumentException("Factory request cannot be empty.", nameof(request));
        ValidateUtf8Text(request, "INVALID_REQUEST_ENCODING", "Factory request");
        ValidateMaterializedRequest(request);
    }

    private static void ValidateUtf8Text(string text, string code, string label)
    {
        if (text.Contains('\uFFFD'))
        {
            throw new FactoryStateException(
                code,
                $"{label} contains Unicode replacement character U+FFFD and may have been corrupted before Factory received it.");
        }

        try
        {
            _ = StrictUtf8.GetByteCount(text);
        }
        catch (EncoderFallbackException)
        {
            throw new FactoryStateException(
                code,
                $"{label} contains invalid Unicode data that cannot be represented as UTF-8 without replacement.");
        }
    }

    private static void ValidateMaterializedRequest(string request)
    {
        var hasSuppliedFileEnvelope =
            request.Contains("# Files pasted by the user:", StringComparison.OrdinalIgnoreCase)
            || request.Contains("# Files mentioned by the user:", StringComparison.OrdinalIgnoreCase);
        if (!hasSuppliedFileEnvelope)
            return;

        var normalized = request.Replace('\\', '/');
        if (!normalized.Contains("/.codex/attachments/", StringComparison.OrdinalIgnoreCase)
            || !normalized.Contains("pasted-text.txt", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new FactoryStateException(
            "UNMATERIALIZED_REQUEST_INPUT",
            "Factory request contains a host-local pasted-text reference instead of the supplied text. Materialize the exact user-supplied content into a self-contained request before starting Factory.");
    }

    private static string RuntimeVersion() =>
        typeof(FactoryRuntime).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    private sealed record CancellationMetadata(int SchemaVersion, string RunId);
}
