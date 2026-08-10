using System.Text.RegularExpressions;
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
                    else if (child.ParentThreadId is not null && !string.Equals(child.ParentThreadId, rollout.ThreadId, StringComparison.Ordinal)) diagnostics.Add(new("TRACE_PARENT_CONFLICT", "warning", "Session metadata takes precedence over the structured spawn event.", child.ThreadId, child.File));
                    if (analysis.SpawnPrompts.TryGetValue(childId, out var prompt)) spawnPrompts.TryAdd(childId, prompt);
                }
            }
        }

        var nodes = included.Values.Select(rollout => ToNode(rollout, GetAnalysis(rollout), rootThreadId, spawnParents.GetValueOrDefault(rollout.ThreadId), spawnPrompts.GetValueOrDefault(rollout.ThreadId), processInterrupted, diagnostics)).ToArray();
        return new(2, rootThreadId, nodes, diagnostics);

        CodexRolloutAnalysis GetAnalysis(CodexRollout rollout) => analyses.TryGetValue(rollout.ThreadId, out var analysis) ? analysis : analyses[rollout.ThreadId] = (reader ?? new CodexRolloutReader()).Analyze(rollout, diagnostics);
    }

    private static AgentTraceNode ToNode(CodexRollout rollout, CodexRolloutAnalysis analysis, string rootThreadId, string? spawnParent, string? spawnPrompt, bool interrupted, ICollection<AgentTraceDiagnostic> diagnostics)
    {
        var role = rollout.ThreadId == rootThreadId ? "factory-root" : NormalizeRole(rollout.MetadataRole) ?? Role(spawnPrompt) ?? Role(analysis.DispatchMessage) ?? "unknown";
        if (role == "unknown") diagnostics.Add(new("TRACE_ROLE_UNKNOWN", "info", "The Factory role could not be determined from rollout metadata or dispatch message.", rollout.ThreadId, rollout.File));
        var dispatch = spawnPrompt ?? analysis.DispatchMessage;
        var action = FactoryDispatchContract.ReadAction(dispatch);
        var workItem = WorkItem(dispatch);
        if (action == "INITIALIZE") workItem = null;
        var status = analysis.Status ?? (interrupted ? "interrupted" : "unknown");
        var violations = FactoryDispatchContract.Validate(role, dispatch);
        foreach (var violation in violations)
            diagnostics.Add(new(violation.Code, "warning", violation.Message, rollout.ThreadId, rollout.File));
        if (violations.Count > 0)
            status = "protocol-invalid";
        var duration = rollout.StartedAt is not null && analysis.CompletedAt is not null ? (long?)(analysis.CompletedAt.Value - rollout.StartedAt.Value).TotalMilliseconds : null;
        var tokens = analysis.TokenUsage;
        return new(rollout.ThreadId, rollout.ThreadId == rootThreadId ? null : rollout.ParentThreadId ?? spawnParent, role, workItem, action, status, rollout.StartedAt, analysis.CompletedAt, duration, analysis.TurnCount, analysis.ToolCallCount, tokens?.InputTokens, tokens?.CachedInputTokens, tokens?.OutputTokens, tokens?.ReasoningOutputTokens, tokens?.TotalTokens);
    }

    private static string? NormalizeRole(string? value) => value?.Trim().ToLowerInvariant() switch { "factory-root" or "task-decomposer" or "factory-step-coordinator" or "implementer" or "checkpoint-reviewer" or "final-reviewer" => value.Trim().ToLowerInvariant(), _ => null };
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
