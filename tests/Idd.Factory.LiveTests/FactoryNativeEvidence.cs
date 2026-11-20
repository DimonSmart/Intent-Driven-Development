using System.Text.Json;
using System.Text.RegularExpressions;
using Idd.Factory.Report;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

// Strict E2E assertions over events normalized by CodexRolloutReader.
// Report timing and free-form assistant text are never lifecycle evidence.
internal static class FactoryNativeEvidence
{
    private sealed record PlannedTask(string Assignment);
    private sealed record Child(CodexRollout Rollout, SpawnRecord Spawn, long SpawnOrdinal, long WaitOrdinal);

    internal static string ConfirmedFinalText(CodexRollout rollout)
    {
        Assert.DoesNotContain(rollout.Diagnostics, diagnostic =>
            diagnostic.Code is "malformed_rollout_line" or "conflicting_turn_ids");
        Assert.DoesNotContain(rollout.Events, item =>
            item.Type is "turn.failed" or "turn_failed" or "task_failed" ||
            item.Status is "failed" or "error" or "cancelled" or "canceled" or "interrupted" &&
            item.ToolName is null);

        var terminal = rollout.Events.LastOrDefault(item =>
            item.Type is "turn.completed" or "turn_completed" or "task_complete" &&
            (item.ThreadId is null || item.ThreadId == rollout.ThreadId));
        Assert.True(terminal is not null,
            $"No native successful terminal for thread {rollout.ThreadId}.");
        Assert.DoesNotContain(rollout.Events, item =>
            item.Ordinal > terminal!.Ordinal && item.Role == "assistant" &&
            !string.IsNullOrWhiteSpace(item.Text));

        var message = rollout.Events.LastOrDefault(item =>
            item.Ordinal < terminal!.Ordinal && item.Role == "assistant" &&
            !string.IsNullOrWhiteSpace(item.Text));
        Assert.True(message is not null,
            $"No final assistant response before native terminal for {rollout.ThreadId}.");
        return message!.Text!.ReplaceLineEndings("\n").Trim();
    }

    private static bool IsAssignedTo(CodexEvent item, string childId)
    {
        if (item.ReceiverThreadIds.Count > 0)
            return item.ReceiverThreadIds.Count == 1 && item.ReceiverThreadIds[0] == childId;
        if (string.IsNullOrWhiteSpace(item.ToolArguments))
            return false;
        try
        {
            using var document = JsonDocument.Parse(item.ToolArguments);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;
            foreach (var name in new[] { "agent_ids", "ids" })
            {
                if (root.TryGetProperty(name, out var ids) && ids.ValueKind == JsonValueKind.Array)
                    return ids.GetArrayLength() == 1 &&
                        ids[0].ValueKind == JsonValueKind.String &&
                        ids[0].GetString() == childId;
            }
            return root.TryGetProperty("agent_id", out var id) &&
                id.ValueKind == JsonValueKind.String && id.GetString() == childId;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ConfirmsChild(string? output, string childId)
    {
        if (string.IsNullOrWhiteSpace(output))
            return false;
        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;
            // The status must describe the child, not the wait tool call.
            if (root.TryGetProperty("agent_id", out var id) &&
                id.ValueKind == JsonValueKind.String && id.GetString() == childId &&
                root.TryGetProperty("status", out var status) &&
                status.ValueKind == JsonValueKind.String &&
                status.GetString() == "completed")
                return true;
            if (root.TryGetProperty("statuses", out var statuses) &&
                statuses.ValueKind == JsonValueKind.Object &&
                statuses.TryGetProperty(childId, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                value.GetString() == "completed")
                return true;
        }
        catch (JsonException)
        {
        }
        return false;
    }

    private static Child ConfirmChild(CodexRollout parent, CodexRollout child)
    {
        Assert.Equal(parent.ThreadId, child.ParentThreadId);
        ConfirmedFinalText(child);
        var spawn = Assert.Single(parent.SpawnRecords.Values, record =>
            record.Children.Contains(child.ThreadId, StringComparer.Ordinal));
        Assert.False(spawn.CorrelationConflict);
        Assert.Equal(child.ThreadId, Assert.Single(spawn.Children));
        Assert.NotEqual("rejected", spawn.SpawnOutcome);
        Assert.Equal(parent.ThreadId, spawn.SenderThreadId);

        var calls = parent.Events.Where(item => item.ToolName == "spawn_agent" &&
            item.ToolId == spawn.CallId && item.ToolPhase is "call" or "started" or "completed")
            .ToArray();
        Assert.NotEmpty(calls);
        var spawnOrdinal = calls.Min(item => item.Ordinal);

        var waits = parent.Events.Where(item =>
            item.ToolName == "wait_agent" && item.ToolId is not null &&
            IsAssignedTo(item, child.ThreadId)).ToArray();
        Assert.NotEmpty(waits);
        var successful = waits.Select(call => parent.Events
            .Where(result => result.ToolId == call.ToolId &&
                result.Ordinal >= call.Ordinal &&
                result.ToolPhase is "completed" or "output" &&
                ConfirmsChild(result.ToolOutput, child.ThreadId))
            .Select(result => (long?)result.Ordinal).FirstOrDefault())
            .Where(ordinal => ordinal is not null)
            .Select(ordinal => ordinal!.Value)
            .ToArray();
        Assert.True(successful.Length > 0,
            $"No correlated successful child terminal-result from wait_agent for {child.ThreadId}. " +
            "A completed wait tool call or assistant message is not enough.");
        var waitOrdinal = successful.Min();
        Assert.True(spawnOrdinal < waitOrdinal, "Wait result precedes the child's spawn.");
        return new Child(child, spawn, spawnOrdinal, waitOrdinal);
    }

    private static Child[] OrderedChildren(CodexRollout parent, IReadOnlyList<CodexRollout> children)
    {
        var ordered = children.Select(child => ConfirmChild(parent, child))
            .OrderBy(item => item.SpawnOrdinal).ToArray();
        Assert.Equal(ordered.Length, ordered.Select(item => item.Spawn.CallId)
            .Distinct(StringComparer.Ordinal).Count());
        for (var i = 1; i < ordered.Length; i++)
            Assert.True(ordered[i - 1].WaitOrdinal < ordered[i].SpawnOrdinal,
                $"Native parent {parent.ThreadId} spawned {ordered[i].Rollout.ThreadId} " +
                $"before confirmed wait for {ordered[i - 1].Rollout.ThreadId}.");
        return ordered;
    }

    internal static void VerifyRoots(CodexRollout supervisor, IReadOnlyList<CodexRollout> roots)
    {
        Assert.NotEmpty(roots);
        var ordered = OrderedChildren(supervisor, roots);
        Assert.Equal(roots.Count, supervisor.SpawnRecords.Values.Count(spawn =>
            FactoryBatchTrace.ParseName(spawn.RequestedTaskName)?.Role == "root"));
        foreach (var root in ordered)
        {
            Assert.Equal("root", FactoryBatchTrace.ParseName(root.Spawn.RequestedTaskName)?.Role);
            Assert.Equal(3, FactoryBatchTrace.ParseBatchLimit(root.Spawn.Task));
            Assert.True(FactoryBatchTrace.TryParseTerminal(
                ConfirmedFinalText(root.Rollout), 3, out var terminal),
                $"Invalid confirmed Root terminal in {root.Rollout.ThreadId}.");
            var last = ReferenceEquals(root, ordered[^1]);
            Assert.Equal(last ? "COMPLETED" : "CONTINUE", terminal!.Status);
            Assert.Equal(last ? "DONE" : "BATCH_LIMIT", terminal.Reason);
        }
    }

    internal static int VerifyPlans(CodexRollout root, IReadOnlyList<CodexRollout> children)
    {
        var ordered = OrderedChildren(root, children);
        Assert.Equal(children.Count, root.SpawnRecords.Values.Count(spawn =>
            FactoryBatchTrace.ParseName(spawn.RequestedTaskName)?.Role is "planner" or "worker"));
        PlannedTask[] pending = [];
        var position = 0;
        var workers = 0;
        var done = false;
        foreach (var child in ordered)
        {
            var role = FactoryBatchTrace.ParseName(child.Spawn.RequestedTaskName)?.Role;
            if (role == "planner")
            {
                Assert.Equal(pending.Length, position);
                Assert.False(done, "Planner ran after confirmed # Done.");
                var final = ConfirmedFinalText(child.Rollout);
                if (final == "# Done")
                {
                    done = true;
                    pending = [];
                    position = 0;
                    continue;
                }
                pending = ParsePlan(final);
                position = 0;
            }
            else
            {
                Assert.Equal("worker", role);
                Assert.False(done, "Worker ran after confirmed # Done.");
                Assert.True(position < pending.Length,
                    "Worker launched without a pending Task from a completed Planner plan.");
                var assignment = ExtractAssignment(child.Spawn.Task);
                Assert.Equal(pending[position].Assignment, assignment);
                position++;
                workers++;
            }
        }
        Assert.Equal(pending.Length, position);
        return workers;
    }

    private static PlannedTask[] ParsePlan(string text)
    {
        var headings = Regex.Matches(text, @"(?m)^# Task[ \t]*$");
        Assert.InRange(headings.Count, 1, 4);
        Assert.Equal(0, headings[0].Index);
        var plan = new List<PlannedTask>();
        for (var i = 0; i < headings.Count; i++)
        {
            var start = headings[i].Index;
            var end = i + 1 < headings.Count ? headings[i + 1].Index : text.Length;
            var assignment = text[start..end].Trim();
            var profiles = Regex.Matches(assignment, @"(?m)^# ExecutionProfile[ \t]*\n(?<profile>[^\n]+)$");
            Assert.Single(profiles.Cast<Match>());
            Assert.Matches(@"\A(?:standard|strong|economy)\z",
                profiles[0].Groups["profile"].Value.Trim());
            Assert.False(string.IsNullOrWhiteSpace(
                assignment["# Task".Length..profiles[0].Index]));
            plan.Add(new PlannedTask(assignment));
        }
        return plan.ToArray();
    }

    private static string ExtractAssignment(string? nativePrompt)
    {
        Assert.False(string.IsNullOrWhiteSpace(nativePrompt),
            "Worker native assignment is opaque or absent; exact Task matching is unavailable.");
        const string start = "--- Factory worker assignment ---";
        const string end = "--- End Factory worker assignment ---";
        var normalized = nativePrompt!.ReplaceLineEndings("\n");
        var begin = normalized.IndexOf(start, StringComparison.Ordinal);
        Assert.True(begin >= 0, "No explicit native Worker assignment delimiter.");
        begin += start.Length;
        var finish = normalized.IndexOf(end, begin, StringComparison.Ordinal);
        Assert.True(finish > begin, "No end of native Worker assignment.");
        Assert.Equal(-1, normalized.IndexOf(start, begin, StringComparison.Ordinal));
        return normalized[begin..finish].Trim();
    }
}
