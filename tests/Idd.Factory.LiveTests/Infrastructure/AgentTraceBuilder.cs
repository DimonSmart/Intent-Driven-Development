using System.Text.RegularExpressions;
using System.Text;
using Idd.Factory.LiveTests.Models;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed class AgentTraceBuilder(CodexRolloutReader? reader = null)
{
    private static readonly Regex RolePattern = new("(?im)^\\s*Role:[ \\t]*(?:\\r?\\n[ \\t]*)?(?<role>[^\\r\\n]+)", RegexOptions.Compiled);
    private static readonly Regex RoleReferencePattern = new("(?i)references[/\\\\]roles[/\\\\](?<role>[a-z0-9-]+)\\.md", RegexOptions.Compiled);
    private static readonly Regex WorkItemFieldPattern = new("(?im)^\\s*Work item:[ \\t]*(?:\\r?\\n[ \\t]*)?(?<item>[^\\r\\n]+)", RegexOptions.Compiled);
    private static readonly Regex WorkItemPattern = new("(?im)(?<path>\\.idd/factory/current/[^\\s`]+\\.active\\.md)", RegexOptions.Compiled);

    public AgentTrace Build(string sessionsDirectory, string? rootThreadId, bool processInterrupted = false)
    {
        var diagnostics = new List<AgentTraceDiagnostic>();
        if (rootThreadId is null)
            return new(2, null, [], [new("ROOT_THREAD_ID_NOT_FOUND", "warning", "Root thread ID was not found in events.jsonl.", null, "events.jsonl")]);

        var rollouts = (reader ?? new CodexRolloutReader()).Index(sessionsDirectory);
        var byId = new Dictionary<string, CodexRollout>(StringComparer.Ordinal);
        foreach (var rollout in rollouts)
            if (!byId.TryAdd(rollout.ThreadId, rollout)) diagnostics.Add(new("TRACE_DUPLICATE_THREAD", "warning", "More than one rollout describes this thread.", rollout.ThreadId, rollout.File));
        if (!byId.TryGetValue(rootThreadId, out var root))
        {
            diagnostics.Add(new("ROOT_ROLLOUT_NOT_FOUND", "warning", "The root thread rollout was not found in Codex session storage.", rootThreadId, null));
            return new(2, rootThreadId, [], diagnostics);
        }

        var included = new Dictionary<string, CodexRollout>(StringComparer.Ordinal) { [root.ThreadId] = root };
        var analyses = new Dictionary<string, CodexRolloutAnalysis>(StringComparer.Ordinal);
        var spawnParents = new Dictionary<string, string>(StringComparer.Ordinal);
        var spawnPrompts = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var rollout in byId.Values)
                if (rollout.ParentThreadId is not null && included.ContainsKey(rollout.ParentThreadId) && included.TryAdd(rollout.ThreadId, rollout)) changed = true;
            foreach (var rollout in included.Values.ToArray())
            {
                var analysis = GetAnalysis(rollout);
                foreach (var childId in analysis.SpawnedThreadIds)
                {
                    if (!byId.TryGetValue(childId, out var child)) { diagnostics.Add(new("CHILD_ROLLOUT_NOT_FOUND", "warning", "A structured spawn event referenced a rollout that was not found.", childId, rollout.File)); continue; }
                    if (child.ParentThreadId is null)
                    {
                        spawnParents.TryAdd(child.ThreadId, rollout.ThreadId);
                        if (included.TryAdd(child.ThreadId, child)) changed = true;
                    }
                    else if (!string.Equals(child.ParentThreadId, rollout.ThreadId, StringComparison.Ordinal)) diagnostics.Add(new("TRACE_PARENT_CONFLICT", "warning", "Session metadata takes precedence over the structured spawn event.", child.ThreadId, child.File));
                    if (analysis.SpawnPrompts.TryGetValue(childId, out var prompt)) spawnPrompts.TryAdd(childId, prompt);
                }
            }
        }

        var nodes = included.Values.Select(rollout => ToNode(rollout, GetAnalysis(rollout), rootThreadId, spawnParents.GetValueOrDefault(rollout.ThreadId), spawnPrompts.GetValueOrDefault(rollout.ThreadId), processInterrupted, diagnostics)).ToArray();
        ApplyProcessToolFailureFallback(nodes, root, rootThreadId, diagnostics);
        return new(2, rootThreadId, nodes, diagnostics);

        CodexRolloutAnalysis GetAnalysis(CodexRollout rollout) => analyses.TryGetValue(rollout.ThreadId, out var analysis) ? analysis : analyses[rollout.ThreadId] = (reader ?? new CodexRolloutReader()).Analyze(rollout, diagnostics);
    }

    private static AgentTraceNode ToNode(CodexRollout rollout, CodexRolloutAnalysis analysis, string rootThreadId, string? spawnParent, string? spawnPrompt, bool interrupted, ICollection<AgentTraceDiagnostic> diagnostics)
    {
        var role = rollout.ThreadId == rootThreadId ? "factory-root" : NormalizeRole(rollout.MetadataRole) ?? Role(spawnPrompt) ?? Role(analysis.DispatchMessage) ?? "unknown";
        if (role == "unknown") diagnostics.Add(new("TRACE_ROLE_UNKNOWN", "info", "The Factory role could not be determined from rollout metadata or fallback telemetry.", rollout.ThreadId, rollout.File));
        var dispatch = spawnPrompt ?? analysis.DispatchMessage;
        var workItem = WorkItem(dispatch);
        var status = analysis.Status ?? (interrupted ? "interrupted" : "unknown");
        var duration = rollout.StartedAt is not null && analysis.CompletedAt is not null ? (long?)(analysis.CompletedAt.Value - rollout.StartedAt.Value).TotalMilliseconds : null;
        var tokens = analysis.TokenUsage;
        var fresh = Fresh(tokens?.InputTokens, tokens?.CachedInputTokens);
        if (tokens?.InputTokens is not null && tokens.CachedInputTokens is not null && fresh is null)
            diagnostics.Add(new("TOKEN_COUNTER_INCONSISTENT", "warning", "Cached input tokens exceed total input tokens; fresh input is unavailable.", rollout.ThreadId, rollout.File));
        var toolCalls = analysis.ToolCalls;
        var codeMode = CodexCodeModeTelemetryReader.Read(rollout, diagnostics);
        var fileReads = analysis.FileReads.Concat(codeMode.FileReads).ToArray();
        var readsByPath = fileReads.GroupBy(read => read.Path, StringComparer.OrdinalIgnoreCase);
        var repeatedReads = readsByPath.Sum(group => Math.Max(0, group.Count() - 1));
        var waitMs = toolCalls.Where(call => call.Tool == "wait_agent").Sum(call => call.DurationMs ?? 0);
        var turnCount = analysis.TurnCount > 0 ? analysis.TurnCount : codeMode.ModelTurns;
        var dispatchText = dispatch ?? string.Empty;
        var dispatchReferences = analysis.DispatchReferences.Concat(CodexRolloutReader.ReadDispatchReferences(spawnPrompt, rollout.WorkingDirectory)).DistinctBy(reference => reference.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        return new(rollout.ThreadId, rollout.ThreadId == rootThreadId ? null : rollout.ParentThreadId ?? spawnParent, role, workItem, null, status, rollout.StartedAt, analysis.CompletedAt, duration, turnCount, analysis.ToolCallCount, tokens?.InputTokens, tokens?.CachedInputTokens, tokens?.OutputTokens, tokens?.ReasoningOutputTokens, tokens?.TotalTokens,
            fresh, Percentage(tokens?.CachedInputTokens, tokens?.InputTokens), toolCalls.Count(call => call.IsFailure), toolCalls.Count(call => call.IsRejected), toolCalls.Count(call => call.IsRetryOrFallback), fileReads.Length, readsByPath.Count(), repeatedReads, fileReads.Sum(read => read.ReturnedBytes), waitMs, dispatchText.Length, Encoding.UTF8.GetByteCount(dispatchText), analysis.TokenProgression, toolCalls, fileReads, dispatchReferences);
    }

    private static void ApplyProcessToolFailureFallback(AgentTraceNode[] nodes, CodexRollout root, string rootThreadId, ICollection<AgentTraceDiagnostic> diagnostics)
    {
        var stderrPath = FindEvalProcessStderr(root);
        var processFailures = CodexProcessToolFailureReader.Read(stderrPath, rootThreadId, diagnostics);
        if (processFailures is null || nodes.Length == 0) return;

        var observedFailed = nodes.Sum(node => node.FailedToolCallCount);
        var observedRejected = nodes.Sum(node => node.RejectedToolCallCount);
        var missingFailed = Math.Max(0, processFailures.FailedToolCalls - observedFailed);
        var missingRejected = Math.Max(0, processFailures.RejectedToolCalls - observedRejected);
        if (missingFailed == 0 && missingRejected == 0) return;

        var rootIndex = Array.FindIndex(nodes, node => node.ThreadId == rootThreadId);
        if (rootIndex < 0) return;
        nodes[rootIndex] = nodes[rootIndex] with
        {
            FailedToolCallCount = nodes[rootIndex].FailedToolCallCount + missingFailed,
            RejectedToolCallCount = nodes[rootIndex].RejectedToolCallCount + missingRejected
        };
        diagnostics.Add(new(
            "PROCESS_TOOL_FAILURE_FALLBACK",
            "info",
            $"Process stderr supplied {missingFailed} nested tool failure(s) and {missingRejected} rejection(s) not represented as structured rollout tool results.",
            rootThreadId,
            stderrPath));
    }

    private static string? FindEvalProcessStderr(CodexRollout root)
    {
        if (string.IsNullOrWhiteSpace(root.WorkingDirectory)) return null;
        try
        {
            var workspace = new DirectoryInfo(root.WorkingDirectory);
            var runDirectory = workspace.Parent;
            if (runDirectory is null || !File.Exists(Path.Combine(runDirectory.FullName, "run-manifest.json"))) return null;
            var stderr = Path.Combine(runDirectory.FullName, "stderr.log");
            return File.Exists(stderr) ? stderr : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static long? Fresh(long? input, long? cached) => input is not null && cached is not null && cached >= 0 && input >= cached ? input - cached : null;
    private static double? Percentage(long? part, long? total) => part is not null && total is > 0 && part >= 0 && part <= total ? 100d * part.Value / total.Value : null;

    private static string? NormalizeRole(string? value) => value?.Trim().ToLowerInvariant() switch { "factory-root" or "planner" or "executor" => value.Trim().ToLowerInvariant(), _ => null };
    private static string? Role(string? text)
    {
        text ??= string.Empty;
        return NormalizeRole(RolePattern.Match(text).Groups["role"].Value) ??
               NormalizeRole(RoleReferencePattern.Match(text).Groups["role"].Value);
    }
    private static string? WorkItem(string? text)
    {
        var field = WorkItemFieldPattern.Match(text ?? string.Empty).Groups["item"].Value.Trim();
        if (field.Length > 0) return field;
        var match = WorkItemPattern.Match(text ?? string.Empty);
        return match.Success ? Path.GetFileNameWithoutExtension(match.Groups["path"].Value)[..^".active".Length] : null;
    }
}
