using System.Text.Json;
using Idd.Factory.Domain;

namespace Idd.Factory.Verification;

internal sealed class VerificationEvidenceStore(
    string currentDirectory,
    VerificationRuntimeHooks hooks)
{
    public static VerificationEvidence Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("schemaVersion", out var schema)
            && schema.GetInt32() == 2)
        {
            var root = document.RootElement;
            return new VerificationEvidence(
                2,
                root.GetProperty("evidenceId").GetString()!,
                root.GetProperty("checkId").GetString()!,
                root.GetProperty("checkDefinitionHash").GetString()!,
                root.GetProperty("startedAt").GetDateTimeOffset(),
                root.GetProperty("finishedAt").GetDateTimeOffset(),
                root.GetProperty("exitCode").GetInt32(),
                root.GetProperty("status").GetString()!,
                root.TryGetProperty("output", out var output) ? output.GetString() ?? "" : "");
        }

        var evidence = JsonSerializer.Deserialize<VerificationEvidence>(json, FactoryJson.Options)
            ?? throw new JsonException("Verification evidence was empty.");
        var rootV3 = document.RootElement;
        VerificationFailure? failure = null;
        if (rootV3.TryGetProperty("failureKind", out var kind)
            && kind.ValueKind != JsonValueKind.Null)
        {
            rootV3.TryGetProperty("exception", out var exception);
            failure = new(
                kind.GetString()!,
                rootV3.GetProperty("failureStage").GetString()!,
                rootV3.GetProperty("summary").GetString()!,
                exception.ValueKind == JsonValueKind.Object && exception.TryGetProperty("type", out var type)
                    ? type.GetString()
                    : null,
                exception.ValueKind == JsonValueKind.Object && exception.TryGetProperty("message", out var message)
                    ? message.GetString()
                    : null,
                exception.ValueKind == JsonValueKind.Object && exception.TryGetProperty("stackTrace", out var stack)
                    ? stack.GetString()
                    : null);
        }

        var secondary = rootV3.TryGetProperty("diagnosticIssues", out var issues)
                        || rootV3.TryGetProperty("secondaryIssues", out issues)
            ? JsonSerializer.Deserialize<VerificationDiagnosticIssue[]>(issues.GetRawText(), FactoryJson.Options) ?? []
            : [];
        return evidence with { PrimaryFailure = failure, DiagnosticIssues = secondary };
    }

    public async Task<VerificationEvidence> PersistManualAsync(
        string id,
        string definition,
        DateTimeOffset started,
        int exitCode,
        string status,
        string output,
        CancellationToken cancellationToken)
    {
        var finished = DateTimeOffset.UtcNow;
        var evidence = new VerificationEvidence
        {
            SchemaVersion = 3,
            EvidenceId = NewEvidenceId(),
            CheckId = id,
            CheckDefinitionHash = VerificationPolicyService.DefinitionHash(
                new VerificationCheck(definition, null, TimeSpan.Zero)),
            StartedAt = started,
            FinishedAt = finished,
            DurationMilliseconds = Math.Max(0, (long)(finished - started).TotalMilliseconds),
            ExitCode = exitCode,
            Status = status,
            Output = output,
            Stdout = new(null, 0, "", false),
            Stderr = new(null, 0, "", false),
            Command = new("manual", ".")
        };
        // Preserve the old evidence hash contract: manual evidence hashes the supplied
        // definition directly rather than the synthetic VerificationCheck shape.
        evidence = evidence with { CheckDefinitionHash = HashDefinition(definition) };
        return await PersistAsync(evidence, cancellationToken);
    }

    public async Task<VerificationEvidence> PersistAsync(
        VerificationEvidence evidence,
        CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.Combine(currentDirectory, "verification");
            Directory.CreateDirectory(directory);
            await hooks.WriteEvidence(
                Path.Combine(directory, evidence.EvidenceId + ".json"),
                JsonSerializer.Serialize(evidence, FactoryJson.Options),
                cancellationToken);
            return evidence;
        }
        catch (Exception exception)
        {
            var issues = evidence.DiagnosticIssues
                .Concat([VerificationDiagnostics.Issue("evidence-persistence", "persist-evidence", exception)])
                .ToArray();
            return evidence with
            {
                Status = "infrastructure-failure",
                EvidencePersisted = false,
                DiagnosticIssues = issues,
                PrimaryFailure = evidence.PrimaryFailure
                    ?? VerificationDiagnostics.Failure(
                        "evidence-persistence-failure",
                        "persist-evidence",
                        "Verification evidence JSON could not be persisted.",
                        exception)
            };
        }
    }

    internal static string NewEvidenceId() =>
        $"V{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..36];

    private static string HashDefinition(string value) =>
        Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}

internal static class VerificationDiagnostics
{
    public static VerificationDiagnosticIssue Issue(string kind, string stage, Exception exception) =>
        new(kind, stage, exception.Message, exception.GetType().FullName, exception.StackTrace);

    public static VerificationFailure Failure(
        string kind,
        string stage,
        string message,
        Exception exception) =>
        new(kind, stage, message, exception.GetType().FullName, exception.Message, exception.StackTrace);
}
