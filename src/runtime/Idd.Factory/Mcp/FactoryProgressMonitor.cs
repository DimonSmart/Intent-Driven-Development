using System.Text;
using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Runtime;

internal sealed class FactoryProgressMonitor(FactoryStatusReader statusReader)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    private const int MaximumMessageLength = 160;
    private const int MaximumSubjectLength = 96;
    private readonly FactoryStatusReader statusReader = statusReader;

    public int CaptureExistingEventCount(string workspace)
    {
        try
        {
            var path = EventPath(workspace);
            return File.Exists(path) ? File.ReadLines(path).Count() : 0;
        }
        catch (Exception exception) when (IsDiagnosticFailure(exception))
        {
            return 0;
        }
    }

    public async Task RunAsync(
        string workspace,
        int initialEventCount,
        Action<string> report,
        CancellationToken cancellationToken)
    {
        var nextEventIndex = initialEventCount;
        string? previousMessage = null;
        var lastReportAt = DateTimeOffset.UtcNow;

        while (true)
        {
            await Task.Delay(PollInterval, cancellationToken);

            try
            {
                var batch = await ReadNewEventLinesAsync(workspace, nextEventIndex, cancellationToken);
                nextEventIndex = batch.NextIndex;
                foreach (var line in batch.Lines)
                {
                    var message = await ProjectEventAsync(workspace, line, cancellationToken);
                    if (message is null || StringComparer.Ordinal.Equals(message, previousMessage)) continue;
                    SafeReport(report, message);
                    previousMessage = message;
                    lastReportAt = DateTimeOffset.UtcNow;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsDiagnosticFailure(exception))
            {
                // Progress is best-effort. State-based heartbeat remains available below.
            }

            var now = DateTimeOffset.UtcNow;
            if (now - lastReportAt < HeartbeatInterval) continue;

            try
            {
                var status = await statusReader.ReadAsync(workspace, cancellationToken);
                if (StringComparer.Ordinal.Equals(status.Status, "ACTIVE"))
                {
                    var heartbeat = FormatHeartbeat(status, now);
                    SafeReport(report, heartbeat);
                    previousMessage = heartbeat;
                    lastReportAt = now;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsDiagnosticFailure(exception))
            {
                lastReportAt = now;
            }
        }
    }

    internal static async Task<(IReadOnlyList<string> Lines, int NextIndex)> ReadNewEventLinesAsync(
        string workspace,
        int nextIndex,
        CancellationToken cancellationToken)
    {
        var path = EventPath(workspace);
        if (!File.Exists(path)) return ([], nextIndex);
        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        if (lines.Length <= nextIndex) return ([], nextIndex);
        return (lines.Skip(nextIndex).ToArray(), lines.Length);
    }

    internal static async Task<string?> ProjectEventAsync(
        string workspace,
        string line,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!TryText(root, out var type, "type") || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                return null;

            string? message = type switch
            {
                "scheduler-decision" => await FormatSchedulerDecisionAsync(workspace, data, cancellationToken),
                "agent-dispatching" => await FormatAgentDispatchAsync(workspace, data, cancellationToken),
                "agent-completed" => await FormatAgentCompletedAsync(workspace, data, cancellationToken),
                "verification-decision" => FormatVerificationDecision(data),
                _ => null
            };
            return message is null ? null : Bound(message, MaximumMessageLength);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    internal static string NormalizePreview(string value, int maximumLength = MaximumSubjectLength)
    {
        var builder = new StringBuilder(Math.Min(value.Length, maximumLength));
        var whitespacePending = false;
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                whitespacePending = builder.Length > 0;
                continue;
            }
            if (char.IsControl(character)) continue;
            if (whitespacePending)
            {
                builder.Append(' ');
                whitespacePending = false;
            }
            builder.Append(character);
            if (builder.Length > maximumLength) break;
        }

        var normalized = builder.ToString().Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..Math.Max(0, maximumLength - 1)] + "…";
    }

    internal static string FormatHeartbeat(FactoryStatusResult status, DateTimeOffset now)
    {
        var activity = status.CurrentWorkItemId is { Length: > 0 } workItem ? workItem : "Factory";
        if (status.CurrentAttemptId is { Length: > 0 } attempt) activity += $" {attempt}";
        if (status.CurrentPhase is { Length: > 0 } phase) activity += $" {phase.ToLowerInvariant()}";
        if (status.RuntimeStartedAt is { } startedAt && now >= startedAt) activity += $"; active {FormatElapsed(now - startedAt)}";
        return Bound(activity, MaximumMessageLength);
    }

    private static async Task<string?> FormatSchedulerDecisionAsync(string workspace, JsonElement data, CancellationToken cancellationToken)
    {
        var kind = ReadEnum<FactoryCommandKind>(data, "Kind", "kind");
        var workItemId = Text(data, "WorkItemId", "workItemId");
        if (kind == FactoryCommandKind.Plan)
            return await IsReplanningAsync(workspace, cancellationToken) ? "Replanning" : "Planning";

        return kind switch
        {
            FactoryCommandKind.SelectNextWork => "Selecting next work",
            FactoryCommandKind.RunVerification => workItemId is null ? "Verification" : $"Verifying {workItemId}",
            FactoryCommandKind.RunFinalVerification => "Final verification",
            FactoryCommandKind.RunFinalReview => "Final review",
            FactoryCommandKind.Finalize => "Finalizing",
            FactoryCommandKind.StopBlocked => "Factory blocked",
            _ => null
        };
    }

    private static async Task<bool> IsReplanningAsync(string workspace, CancellationToken cancellationToken)
    {
        try
        {
            var path = Path.Combine(workspace, ".idd", "factory", "current", "state.json");
            if (!File.Exists(path)) return false;
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
            var root = document.RootElement;
            return root.TryGetProperty("pendingReplanTrigger", out var trigger)
                && trigger.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task<string?> FormatAgentDispatchAsync(string workspace, JsonElement data, CancellationToken cancellationToken)
    {
        var attemptId = Text(data, "attemptId", "AttemptId");
        var capability = Text(data, "capability", "Capability");
        var workItemId = Text(data, "workItemId", "WorkItemId");
        if (attemptId is null || capability is null) return null;

        var invocation = await ReadInvocationAsync(workspace, attemptId, cancellationToken);
        workItemId ??= invocation.WorkItemId;
        var subject = Subject(invocation.Input, capability);

        if (StringComparer.Ordinal.Equals(capability, "planning"))
            return subject is null ? $"Planning {attemptId}" : $"Planning {attemptId}: \"{subject}\"";
        if (StringComparer.Ordinal.Equals(capability, "final-review"))
            return $"Final review {attemptId}";

        var prefix = workItemId is null ? capability : $"{workItemId} {capability}";
        return subject is null ? $"{prefix} {attemptId}" : $"{prefix} {attemptId}: \"{subject}\"";
    }

    private static async Task<string?> FormatAgentCompletedAsync(string workspace, JsonElement data, CancellationToken cancellationToken)
    {
        var attemptId = Text(data, "attemptId", "AttemptId");
        var capability = Text(data, "capability", "Capability");
        var outcome = Text(data, "Outcome", "outcome");
        if (attemptId is null || capability is null || outcome is null) return null;

        var invocation = await ReadInvocationAsync(workspace, attemptId, cancellationToken);
        if (StringComparer.Ordinal.Equals(capability, "planning"))
            return outcome == "ready" ? "Planning completed" : $"Planning: {Humanize(outcome)}";
        if (StringComparer.Ordinal.Equals(capability, "final-review"))
            return outcome == "approved" ? "Final review approved" : $"Final review: {Humanize(outcome)}";

        var prefix = invocation.WorkItemId is null ? capability : $"{invocation.WorkItemId} {capability}";
        return outcome switch
        {
            "completed" or "approved" => $"{prefix} completed",
            "additional-work-required" => $"{prefix}: additional work required",
            "global-replan-required" => $"{prefix}: replanning required",
            _ => $"{prefix}: {Humanize(outcome)}"
        };
    }

    private static string? FormatVerificationDecision(JsonElement data)
    {
        var context = Text(data, "context", "Context");
        var workItemId = Text(data, "workItemId", "WorkItemId");
        var decision = ReadEnum<VerificationDecision>(data, "decision", "Decision");
        if (decision is null) return null;

        if (StringComparer.Ordinal.Equals(context, "final"))
            return decision == VerificationDecision.UnexpectedFailure ? "Final verification failed" : "Final verification passed";

        var prefix = workItemId is null ? "Verification" : $"{workItemId} verification";
        return decision switch
        {
            VerificationDecision.Ok => $"{prefix} passed",
            VerificationDecision.ExpectedFailure => $"{prefix} completed with expected failure",
            VerificationDecision.UnexpectedFailure => $"{prefix} failed; retrying",
            _ => null
        };
    }

    private static async Task<(string? Input, string? WorkItemId)> ReadInvocationAsync(
        string workspace,
        string attemptId,
        CancellationToken cancellationToken)
    {
        try
        {
            var path = Path.Combine(workspace, ".idd", "factory", "current", "attempts", attemptId, "invocation.json");
            if (!File.Exists(path)) return (null, null);
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
            var root = document.RootElement;
            return (Text(root, "input", "Input"), Text(root, "workItemId", "WorkItemId"));
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return (null, null);
        }
    }

    private static string? Subject(string? input, string capability)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var candidate = capability switch
        {
            "planning" => Section(input, "Original request:"),
            "final-review" => Section(input, "Original Factory request:"),
            _ => Section(input, "Work item contract:")
        };
        var preview = NormalizePreview(candidate ?? input);
        return preview.Length == 0 ? null : preview;
    }

    private static string? Section(string input, string label)
    {
        var marker = label + "\n";
        var start = input.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += marker.Length;
        var end = input.IndexOf("\n\n", start, StringComparison.Ordinal);
        return end < 0 ? input[start..] : input[start..end];
    }

    private static TEnum? ReadEnum<TEnum>(JsonElement data, params string[] names) where TEnum : struct, Enum
    {
        foreach (var name in names)
        {
            if (!data.TryGetProperty(name, out var node)) continue;
            if (node.ValueKind == JsonValueKind.String && Enum.TryParse<TEnum>(node.GetString(), true, out var parsed)) return parsed;
            if (node.ValueKind == JsonValueKind.Number && node.TryGetInt32(out var number) && Enum.IsDefined(typeof(TEnum), number))
                return (TEnum)Enum.ToObject(typeof(TEnum), number);
        }
        return null;
    }

    private static string? Text(JsonElement data, params string[] names)
    {
        foreach (var name in names)
            if (TryText(data, out var value, name))
                return value;
        return null;
    }

    private static bool TryText(JsonElement data, out string? value, string name)
    {
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var node) && node.ValueKind == JsonValueKind.String)
        {
            value = node.GetString();
            return !string.IsNullOrWhiteSpace(value);
        }
        value = null;
        return false;
    }

    private static string Humanize(string outcome) => outcome.Replace('-', ' ');

    private static string Bound(string message, int maximumLength) =>
        message.Length <= maximumLength ? message : message[..Math.Max(0, maximumLength - 1)] + "…";

    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{elapsed.Minutes}:{elapsed.Seconds:00}";

    private static string EventPath(string workspace) =>
        Path.Combine(workspace, ".idd", "factory", "current", "events.jsonl");

    private static void SafeReport(Action<string> report, string message)
    {
        try { report(message); }
        catch { }
    }

    private static bool IsDiagnosticFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException;
}
