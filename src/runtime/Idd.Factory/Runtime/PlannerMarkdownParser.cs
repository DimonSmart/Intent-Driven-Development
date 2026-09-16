using System.Collections;
using Idd.Factory.Domain;
using Markdig;
using Markdig.Syntax;

namespace Idd.Factory.Runtime;

internal sealed record PlannerTaskDefinition(
    string Contract,
    IReadOnlyList<string> TaskRelatedIntentIds,
    IReadOnlyList<string> RelevantCompletedWorkIds);

internal sealed record PlannerBatchResult(
    IReadOnlyList<PlannerTaskDefinition> Tasks,
    string? Question) : IReadOnlyList<string>
{
    public int Count => Tasks.Count;
    public string this[int index] => Tasks[index].Contract;
    public IEnumerator<string> GetEnumerator() => Tasks.Select(task => task.Contract).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class PlannerMarkdownParser
{
    private const string TaskHeading = "# Task";
    private const string TaskRelatedIntentHeading = "# TaskRelatedIntent";
    private const string RelevantCompletedWorkHeading = "# RelevantCompletedWork";
    private const string QuestionHeading = "# Question";
    private const string DoneHeading = "# Done";
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().Build();

    public PlannerBatchResult Parse(string markdown)
    {
        var normalized = Normalize(markdown);
        if (string.IsNullOrWhiteSpace(normalized))
            throw Malformed();

        var document = Markdown.Parse(normalized, Pipeline);
        var markers = document
            .OfType<HeadingBlock>()
            .Where(heading => heading.Level == 1 && heading.Column == 0 && !heading.IsSetext)
            .Select(heading => TryCreateMarker(normalized, heading))
            .Where(marker => marker is not null)
            .Select(marker => marker!)
            .OrderBy(marker => marker.Start)
            .ToArray();

        if (markers.Length == 0 || !string.IsNullOrWhiteSpace(normalized[..markers[0].Start]))
            throw Malformed();

        var tasks = new List<PlannerTaskDefinition>();
        string? question = null;
        var done = false;
        for (var index = 0; index < markers.Length; index++)
        {
            var marker = markers[index];
            var bodyEnd = index + 1 < markers.Length ? markers[index + 1].Start : normalized.Length;
            var rawBody = normalized[marker.EndExclusive..bodyEnd];
            var body = rawBody.Trim();

            if (marker.Kind == PlannerSectionKind.Done)
            {
                if (body.Length != 0)
                    throw new AgentProtocolException("MALFORMED_PLANNER_OUTPUT", "Planner '# Done' must not have a body.");
                if (done)
                    throw new AgentProtocolException("MALFORMED_PLANNER_OUTPUT", "Planner output may contain only one '# Done' marker.");
                done = true;
                continue;
            }

            if (marker.Kind == PlannerSectionKind.TaskRelatedIntent)
            {
                if (index == 0 || markers[index - 1].Kind != PlannerSectionKind.Task || tasks.Count == 0)
                {
                    throw new AgentProtocolException(
                        "MALFORMED_PLANNER_OUTPUT",
                        "Planner '# TaskRelatedIntent' must occur at most once immediately after the '# Task' it describes.");
                }
                if (index + 1 < markers.Length
                    && markers[index + 1].Kind is not (PlannerSectionKind.RelevantCompletedWork or PlannerSectionKind.Task))
                {
                    throw new AgentProtocolException(
                        "MALFORMED_PLANNER_OUTPUT",
                        "Planner '# TaskRelatedIntent' must be followed only by '# RelevantCompletedWork', another '# Task', or the end of planner output.");
                }

                tasks[^1] = tasks[^1] with { TaskRelatedIntentIds = ParseTaskRelatedIntent(rawBody) };
                continue;
            }

            if (marker.Kind == PlannerSectionKind.RelevantCompletedWork)
            {
                if (index == 0
                    || markers[index - 1].Kind is not (PlannerSectionKind.Task or PlannerSectionKind.TaskRelatedIntent)
                    || tasks.Count == 0)
                {
                    throw new AgentProtocolException(
                        "MALFORMED_PLANNER_OUTPUT",
                        "Planner '# RelevantCompletedWork' must occur at most once after the '# Task' it describes and after '# TaskRelatedIntent' when both metadata sections are present.");
                }
                if (index + 1 < markers.Length && markers[index + 1].Kind != PlannerSectionKind.Task)
                {
                    throw new AgentProtocolException(
                        "MALFORMED_PLANNER_OUTPUT",
                        "Planner '# RelevantCompletedWork' must be followed only by another '# Task' or the end of planner output.");
                }

                tasks[^1] = tasks[^1] with { RelevantCompletedWorkIds = ParseRelevantCompletedWork(rawBody) };
                continue;
            }

            if (body.Length == 0)
                throw new AgentProtocolException("MALFORMED_PLANNER_OUTPUT", "Planner task and question sections must be non-empty.");

            if (marker.Kind == PlannerSectionKind.Task)
            {
                tasks.Add(new(body, [], []));
                continue;
            }

            if (question is not null)
                throw new AgentProtocolException("MALFORMED_PLANNER_OUTPUT", "Planner output may contain only one '# Question' section.");
            question = body;
        }

        if (done && (tasks.Count > 0 || question is not null))
            throw new AgentProtocolException("MALFORMED_PLANNER_OUTPUT", "Planner output cannot mix '# Done' with '# Task' or '# Question'.");
        if (done)
            return new([], null);
        if (question is not null && tasks.Count > 0)
            throw new AgentProtocolException("MALFORMED_PLANNER_OUTPUT", "Planner output cannot mix '# Task' sections with '# Question'. Execute safely contractable tasks first and ask only when no task can be contracted now.");
        return new(tasks, question);
    }

    private static IReadOnlyList<string> ParseTaskRelatedIntent(string body)
    {
        var ids = ParseNonEmptyLines(body);
        if (ids.Length == 0)
        {
            throw new AgentProtocolException(
                "MALFORMED_PLANNER_OUTPUT",
                "Planner '# TaskRelatedIntent' must contain at least one durable intent ID.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (!DurableIntentId.IsCanonical(id))
            {
                throw new AgentProtocolException(
                    "MALFORMED_PLANNER_OUTPUT",
                    $"Planner TaskRelatedIntent entry '{id}' must be exactly one canonical IDD-NNNN identifier per line, without surrounding whitespace.");
            }
            if (!seen.Add(id))
            {
                throw new AgentProtocolException(
                    "MALFORMED_PLANNER_OUTPUT",
                    $"Planner TaskRelatedIntent contains duplicate durable intent ID '{id}'.");
            }
        }

        return ids;
    }

    private static IReadOnlyList<string> ParseRelevantCompletedWork(string body)
    {
        var ids = ParseNonEmptyLines(body);
        if (ids.Length == 0)
        {
            throw new AgentProtocolException(
                "MALFORMED_PLANNER_OUTPUT",
                "Planner '# RelevantCompletedWork' must contain at least one completed work-item ID.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (!WorkItemId.IsCanonical(id))
            {
                throw new AgentProtocolException(
                    "MALFORMED_PLANNER_OUTPUT",
                    $"Planner RelevantCompletedWork entry '{id}' must be exactly one canonical WNNNNNN identifier per line, without surrounding whitespace.");
            }
            if (!seen.Add(id))
            {
                throw new AgentProtocolException(
                    "MALFORMED_PLANNER_OUTPUT",
                    $"Planner RelevantCompletedWork contains duplicate work-item ID '{id}'.");
            }
        }

        return ids;
    }

    private static string[] ParseNonEmptyLines(string body) =>
        body.Split('\n')
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

    private static PlannerSectionMarker? TryCreateMarker(string markdown, HeadingBlock heading)
    {
        if (heading.Span.Start < 0 || heading.Span.Start >= markdown.Length)
            throw Malformed();

        var lineEnd = markdown.IndexOf('\n', heading.Span.Start);
        if (lineEnd < 0) lineEnd = markdown.Length;
        var sourceLine = markdown[heading.Span.Start..lineEnd];
        var kind = sourceLine switch
        {
            TaskHeading => PlannerSectionKind.Task,
            TaskRelatedIntentHeading => PlannerSectionKind.TaskRelatedIntent,
            RelevantCompletedWorkHeading => PlannerSectionKind.RelevantCompletedWork,
            QuestionHeading => PlannerSectionKind.Question,
            DoneHeading => PlannerSectionKind.Done,
            _ => PlannerSectionKind.None
        };
        return kind == PlannerSectionKind.None
            ? null
            : new(heading.Span.Start, lineEnd, kind);
    }

    private static string Normalize(string markdown) =>
        markdown.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n');

    private static AgentProtocolException Malformed() =>
        new(
            "MALFORMED_PLANNER_OUTPUT",
            "Planner output must contain one or more exact '# Task' sections, exactly one non-empty '# Question' section, or exactly '# Done'. Optional '# TaskRelatedIntent' and '# RelevantCompletedWork' metadata may follow only their task in that order. Blank planner output is not a completion signal.");

    private sealed record PlannerSectionMarker(int Start, int EndExclusive, PlannerSectionKind Kind);
    private enum PlannerSectionKind { None, Task, TaskRelatedIntent, RelevantCompletedWork, Question, Done }
}
