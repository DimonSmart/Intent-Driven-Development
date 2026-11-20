using System.Text.RegularExpressions;

namespace Idd.Factory.Report;

/// <summary>
/// Read-only native Factory batch trace interpretation. These methods do not
/// decide whether the next Root is safe to start: the native Supervisor must
/// independently confirm trusted normal native terminals and durable checkpoints.
/// </summary>
public static partial class FactoryBatchTrace
{
    public sealed record Name(string Role, int BatchOrdinal, int? AttemptOrdinal);
    public sealed record Terminal(string Status, int CompletedWorkers, string Reason, string? QuestionFile);

    [GeneratedRegex(@"^idd_factory_root_b(?<batch>[0-9]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex RootNamePattern();

    [GeneratedRegex(@"^idd_factory_(?<role>planner|worker)_b(?<batch>[0-9]+)_(?<attempt>[0-9]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ChildNamePattern();

    [GeneratedRegex(@"^idd_factory_(?<role>planner|worker)_[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex LegacyNamePattern();

    public static Name? ParseName(string? nativeTaskName)
    {
        if (nativeTaskName is null) return null;
        var root = RootNamePattern().Match(nativeTaskName);
        if (root.Success && int.TryParse(root.Groups["batch"].Value, out var batch) && batch > 0)
            return new Name("root", batch, null);
        var child = ChildNamePattern().Match(nativeTaskName);
        if (child.Success &&
            int.TryParse(child.Groups["batch"].Value, out batch) && batch > 0 &&
            int.TryParse(child.Groups["attempt"].Value, out var attempt) && attempt > 0)
            return new Name(child.Groups["role"].Value, batch, attempt);
        var legacy = LegacyNamePattern().Match(nativeTaskName);
        return legacy.Success ? new Name(legacy.Groups["role"].Value, 0, null) : null;
    }

    // The Supervisor MUST carry the explicit effective budget in each Root
    // spawn message. Missing, malformed or conflicting values are not inferred
    // from user dialogue or previous Root sessions.
    // Capture EVERY explicit assignment, including malformed ones: a valid
    // numeric occurrence must never mask a contradictory negative, fractional
    // or non-numeric assignment elsewhere in the native child prompt.
    [GeneratedRegex(@"(?<![A-Za-z0-9_])""?max_workers_per_batch""?\s*[:=]\s*(?<value>[^\s,;}]+)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex BatchLimitPattern();

    public static int? ParseBatchLimit(string? nativeRootAssignment)
    {
        if (string.IsNullOrWhiteSpace(nativeRootAssignment)) return null;
        var matches = BatchLimitPattern().Matches(nativeRootAssignment);
        if (matches.Count == 0) return null;
        int? effective = null;
        foreach (Match match in matches)
        {
            var value = match.Groups["value"].Value;
            if (!Regex.IsMatch(value, @"\A[0-9]+\z", RegexOptions.CultureInvariant) ||
                !int.TryParse(value, out var parsed) || parsed <= 0 ||
                effective is not null && effective != parsed)
                return null;
            effective = parsed;
        }
        return effective;
    }

    /// <summary>
    /// Parses one complete final message, not arbitrary quoted intermediate
    /// content or a tool output. The caller must independently attest that the
    /// enclosing native child turn really ended.
    /// </summary>
    public static bool TryParseTerminal(string? finalMessage, int? limit, out Terminal? terminal)
    {
        terminal = null;
        if (string.IsNullOrWhiteSpace(finalMessage)) return false;

        var lines = finalMessage.ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        string[] Fields(string name) =>
            lines.Where(line => line.StartsWith(name + ":", StringComparison.Ordinal))
                .Select(line => line[(name.Length + 1)..].Trim())
                .ToArray();

        var statuses = Fields("Status");
        var counts = Fields("CompletedWorkers");
        var reasons = Fields("Reason");
        var questions = Fields("QuestionFile");
        if (statuses.Length != 1 || counts.Length != 1 ||
            reasons.Length != 1 || questions.Length > 1 ||
            reasons[0].Length == 0 ||
            !Regex.IsMatch(counts[0], @"\A[0-9]+\z", RegexOptions.CultureInvariant) ||
            !uint.TryParse(counts[0], out var count) ||
            count > int.MaxValue)
            return false;

        var status = statuses[0];
        if (status is not ("CONTINUE" or "COMPLETED" or "NEEDS_INPUT" or "BLOCKED"))
            return false;
        if (status == "CONTINUE" &&
            (reasons[0] != "BATCH_LIMIT" || count == 0 ||
             limit is not null && count < limit.Value))
            return false;
        if (status != "CONTINUE" && reasons[0] == "BATCH_LIMIT")
            return false;
        if (status != "NEEDS_INPUT" && questions.Length > 0)
            return false;
        if (questions.Length == 1 &&
            questions[0] is not (".idd/factory/preparation/question.md" or
                ".idd/factory/current/question.md"))
            return false;

        terminal = new Terminal(status, (int)count, reasons[0],
            questions.Length == 1 ? questions[0] : null);
        return true;
    }
}

public sealed class FactoryBatchReport
{
    public int? BatchOrdinal { get; init; }
    public string RootThreadId { get; init; } = "";
    public string? NativeTaskName { get; init; }
    public int? EffectiveWorkerLimit { get; init; }
    public string Status { get; init; } = "unavailable";
    public int? CompletedWorkers { get; init; }
    public string? Reason { get; init; }
    public string TerminalEvidence { get; init; } = "unavailable";
}
