using System.Text;
using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Verification;

namespace Idd.Factory.Runtime;

internal sealed class FactoryContextReader(FactoryRuntimeContext context)
{
    public async Task<string> BuildPlanningCompletedContextAsync(
        FactoryState state,
        CancellationToken cancellationToken)
    {
        if (state.Completed.Count == 0)
            return "none";

        var planningState = context.CloneState(state);
        foreach (var completed in planningState.Completed)
        {
            if (completed.VerificationEvidenceRefs.Count == 0)
                continue;

            var visiblePaths = completed.VerificationEvidenceRefs
                .Select(GetWorkspaceVisibleEvidencePath)
                .ToArray();
            completed.VerificationEvidenceRefs.Clear();
            completed.VerificationEvidenceRefs.AddRange(visiblePaths);
        }

        return await BuildCompletedContextAsync(planningState, cancellationToken);
    }

    public async Task<string> BuildCompletedContextAsync(
        FactoryState state,
        CancellationToken cancellationToken)
    {
        if (state.Completed.Count == 0)
            return "none";

        var lines = new List<string>();
        foreach (var completed in state.Completed)
        {
            lines.Add($"## {completed.Id}");
            lines.Add("Task contract:");
            lines.Add(ReadContract(completed.ContractPath));
            lines.Add("Semantic result:");
            lines.Add(completed.ResultRef is null
                ? "none"
                : await ReadSemanticResultAsync(completed.ResultRef, cancellationToken));
            lines.Add("Actual changed paths: " + (
                completed.ChangedPaths.Count == 0
                    ? "none"
                    : string.Join(", ", completed.ChangedPaths)));
            lines.Add("Verification evidence: " + (
                completed.VerificationEvidenceRefs.Count == 0
                    ? "none"
                    : string.Join(", ", completed.VerificationEvidenceRefs)));
        }

        return string.Join("\n", lines);
    }

    public async Task<string> BuildPlanningVerificationEvidenceContextAsync(
        IEnumerable<string> references,
        CancellationToken cancellationToken)
    {
        var summaries = new List<string>();
        foreach (var reference in references)
        {
            var visiblePath = GetWorkspaceVisibleEvidencePath(reference);
            VerificationEvidence evidence;
            try
            {
                evidence = VerificationEngine.Read(
                    await File.ReadAllTextAsync(
                        Path.Combine(context.CurrentDirectory, reference),
                        cancellationToken));
            }
            catch (JsonException exception)
            {
                throw new FactoryStateException(
                    "CORRUPT_FACTORY_STATE",
                    $"Invalid authoritative verification evidence '{reference}': {exception.Message}");
            }

            summaries.Add($"- Check: {evidence.CheckId}");
            summaries.Add($"  Status: {evidence.Status}");
            summaries.Add($"  Exit code: {evidence.ExitCode?.ToString() ?? "unavailable"}");
            summaries.Add($"  Evidence: {visiblePath}");
            summaries.Add("  Bounded diagnostic output:");
            foreach (var line in BoundedVerificationOutput(evidence)
                         .Replace("\r\n", "\n")
                         .Split('\n'))
            {
                summaries.Add($"  {line}");
            }
        }

        return summaries.Count == 0 ? "none" : string.Join("\n", summaries);
    }

    public string GetWorkspaceVisibleEvidencePath(string reference)
    {
        var runDirectory = Path.GetFullPath(context.CurrentDirectory);
        if (string.IsNullOrWhiteSpace(reference) || Path.IsPathRooted(reference))
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                $"Invalid authoritative verification evidence reference '{reference}'.");
        }

        string resolvedPath;
        try
        {
            resolvedPath = Path.GetFullPath(Path.Combine(runDirectory, reference));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                $"Invalid authoritative verification evidence reference '{reference}': {exception.Message}");
        }

        var runRelative = Path.GetRelativePath(runDirectory, resolvedPath);
        if (Path.IsPathRooted(runRelative)
            || runRelative.Equals("..", StringComparison.Ordinal)
            || runRelative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || runRelative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                $"Authoritative verification evidence reference '{reference}' resolves outside Factory run directory '{runDirectory}'.");
        }

        if (!File.Exists(resolvedPath))
        {
            throw new FactoryStateException(
                "CORRUPT_FACTORY_STATE",
                $"Authoritative verification evidence reference '{reference}' is missing at '{resolvedPath}'.");
        }

        return RelativePath(resolvedPath);
    }

    public async Task<string> BuildPriorCommandFailureContextAsync(
        PlannedWorkItem item,
        CancellationToken cancellationToken)
    {
        if (item.PriorAttemptDiagnosticRefs.Count == 0)
            return "none";

        var sections = new List<string>
        {
            "These attempts did not complete. Their test or command results are partial and must not be trusted. Diagnose and remove the hang before relying on a rerun."
        };
        foreach (var reference in item.PriorAttemptDiagnosticRefs.TakeLast(3))
        {
            var path = Path.Combine(
                context.CurrentDirectory,
                reference.Replace('/', Path.DirectorySeparatorChar));
            var diagnostic = File.Exists(path)
                ? await File.ReadAllTextAsync(path, cancellationToken)
                : "missing diagnostic artifact";
            sections.Add($"- {reference}:\n{BoundDiagnostic(diagnostic, 4096)}");
        }

        return string.Join("\n", sections);
    }

    public async Task<string> BuildPriorResultContextAsync(
        PlannedWorkItem item,
        CancellationToken cancellationToken)
    {
        if (item.PriorResultRefs.Count == 0)
            return "none";

        var lines = new List<string>();
        foreach (var reference in item.PriorResultRefs.TakeLast(3))
        {
            lines.Add($"- {reference}:\n{await ReadSemanticResultAsync(reference, cancellationToken)}");
        }

        return string.Join("\n", lines);
    }

    public async Task<string> BuildVerificationObservationsAsync(
        PlannedWorkItem item,
        CancellationToken cancellationToken)
    {
        var failures = await ReadFailedVerificationEvidenceAsync(item, cancellationToken);
        if (failures.Count == 0)
            return "none";

        var currentReferences = item.LastVerificationEvidenceRefs.Count == 0
            ? failures.Select(x => x.Reference).ToHashSet(StringComparer.Ordinal)
            : item.LastVerificationEvidenceRefs.ToHashSet(StringComparer.Ordinal);
        var currentFailures = failures
            .Where(x => currentReferences.Contains(x.Reference))
            .ToList();
        var historicalFailures = failures
            .Where(x => !currentReferences.Contains(x.Reference))
            .ToList();
        var observations = new List<string> { "Current authoritative verification failures:" };

        if (currentFailures.Count == 0)
        {
            observations.Add("none");
        }
        else
        {
            foreach (var failure in currentFailures)
            {
                AppendVerificationMetadata(observations, failure.Reference, failure.Evidence);
                observations.Add("");
                observations.Add("  Relevant output:");
                foreach (var line in BoundedVerificationOutput(failure.Evidence)
                             .Replace("\r\n", "\n")
                             .Split('\n'))
                {
                    observations.Add($"  {line}");
                }
            }
        }

        observations.Add("");
        observations.Add("Historical verification failures:");
        if (historicalFailures.Count == 0)
        {
            observations.Add("none");
        }
        else
        {
            foreach (var failure in historicalFailures)
            {
                AppendVerificationMetadata(observations, failure.Reference, failure.Evidence);
                observations.Add("");
            }
        }

        return string.Join("\n", observations).TrimEnd();
    }

    public async Task<string> BuildRetryBudgetExhaustedMessageAsync(
        PlannedWorkItem item,
        CancellationToken cancellationToken)
    {
        var failures = await ReadFailedVerificationEvidenceAsync(item, cancellationToken);
        if (failures.Count == 0)
        {
            var diagnostic = await BuildPriorCommandFailureContextAsync(item, cancellationToken);
            return diagnostic == "none"
                ? $"{item.Id} exhausted its semantic attempt budget."
                : $"Work item {item.Id} exhausted its semantic attempt budget after repeated shell-command failures.\n\n{diagnostic}";
        }

        var (reference, evidence) = failures[^1];
        return $"Work item {item.Id} could not pass authoritative verification after {item.AttemptCount} semantic attempts.\n\n" +
               $"Failed check:\n{evidence.CheckId}\n\n" +
               $"Exit code:\n{(evidence.ExitCode?.ToString() ?? "unavailable")}\n\n" +
               $"Latest bounded verification tails:\n{BoundedVerificationOutput(evidence)}\n\n" +
               $"Evidence:\n{reference}";
    }

    public string ReadContract(string path) =>
        File.Exists(Path.Combine(context.CurrentDirectory, path))
            ? File.ReadAllText(Path.Combine(context.CurrentDirectory, path))
            : "[missing contract]";

    public async Task<string> ReadSemanticResultAsync(
        string relative,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(context.CurrentDirectory, relative);
        if (!File.Exists(path))
            return "missing result artifact";

        var result = (await File.ReadAllTextAsync(path, cancellationToken))
            .Replace("\r\n", "\n")
            .Trim();
        return result.Length <= 8000
            ? result
            : result[..8000] + "\n[result truncated; see semantic artifact]";
    }

    public static string BoundedVerificationOutput(VerificationEvidence evidence)
    {
        const int maximumBytes = 12 * 1024;
        var sections = new List<string>();
        if (evidence.SchemaVersion >= 3)
        {
            if (!string.IsNullOrEmpty(evidence.Stderr.Tail))
                sections.Add($"stderr tail:\n{evidence.Stderr.Tail}");
            if (!string.IsNullOrEmpty(evidence.Stdout.Tail))
                sections.Add($"stdout tail:\n{evidence.Stdout.Tail}");
        }
        else if (!string.IsNullOrEmpty(evidence.Output))
        {
            sections.Add($"combined output (schema v2):\n{evidence.Output}");
        }

        var output = sections.Count == 0 ? "none" : string.Join("\n", sections);
        if (Encoding.UTF8.GetByteCount(output) <= maximumBytes)
            return output;

        var minimum = 0;
        var maximum = Math.Min(output.Length, maximumBytes);
        while (minimum < maximum)
        {
            var candidate = minimum + (maximum - minimum + 1) / 2;
            if (Encoding.UTF8.GetByteCount(output.AsSpan(0, candidate)) <= maximumBytes)
                minimum = candidate;
            else
                maximum = candidate - 1;
        }

        return output[..minimum] + "\n[verification output truncated; see evidence artifact]";
    }

    private async Task<List<(string Reference, VerificationEvidence Evidence)>> ReadFailedVerificationEvidenceAsync(
        PlannedWorkItem item,
        CancellationToken cancellationToken)
    {
        var failures = new List<(string Reference, VerificationEvidence Evidence)>();
        foreach (var reference in item.VerificationEvidenceRefs)
        {
            var path = Path.Combine(context.CurrentDirectory, reference);
            if (!File.Exists(path))
                continue;

            try
            {
                var evidence = VerificationEngine.Read(
                    await File.ReadAllTextAsync(path, cancellationToken));
                if (evidence.Status == "failed")
                    failures.Add((reference, evidence));
            }
            catch (JsonException)
            {
                // Preserve the previous behavior: malformed historical evidence is ignored here.
            }
        }

        return failures;
    }

    private string RelativePath(string path) =>
        Path.GetRelativePath(context.Workspace, path).Replace('\\', '/');

    private static string BoundDiagnostic(string value, int maximumLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maximumLength
            ? trimmed
            : trimmed[..maximumLength] + "\n[diagnostic truncated; see local artifact]";
    }

    private static void AppendVerificationMetadata(
        List<string> observations,
        string reference,
        VerificationEvidence evidence)
    {
        observations.Add($"- Check: {evidence.CheckId}");
        observations.Add($"  Status: {evidence.Status}");
        observations.Add($"  Exit code: {evidence.ExitCode?.ToString() ?? "unavailable"}");
        observations.Add($"  Evidence: {reference}");
    }
}
