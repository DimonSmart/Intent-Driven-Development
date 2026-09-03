using System.Collections;
using Markdig;
using Markdig.Syntax;

namespace Idd.Factory.Runtime;

internal sealed record PlannerBatchResult(IReadOnlyList<string> Tasks, string? Question) : IReadOnlyList<string>
{
    public int Count => Tasks.Count;
    public string this[int index] => Tasks[index];
    public IEnumerator<string> GetEnumerator() => Tasks.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class PlannerMarkdownParser
{
    private const string TaskHeading = "# Task";
    private const string QuestionHeading = "# Question";
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().Build();

    public PlannerBatchResult Parse(string markdown)
    {
        var normalized = Normalize(markdown);
        if (string.IsNullOrWhiteSpace(normalized)) return new([], null);

        var document = Markdown.Parse(normalized, Pipeline);
        var markers = document
            .OfType<HeadingBlock>()
            .Where(heading => heading.Level == 1 && !heading.IsSetext)
            .Select(heading => TryCreateMarker(normalized, heading))
            .Where(marker => marker is not null)
            .Select(marker => marker!)
            .OrderBy(marker => marker.Start)
            .ToArray();

        if (markers.Length == 0 || !string.IsNullOrWhiteSpace(normalized[..markers[0].Start]))
            throw Malformed();

        var tasks = new List<string>();
        string? question = null;
        for (var index = 0; index < markers.Length; index++)
        {
            var marker = markers[index];
            var bodyEnd = index + 1 < markers.Length ? markers[index + 1].Start : normalized.Length;
            var body = normalized[marker.EndExclusive..bodyEnd].Trim();
            if (body.Length == 0)
                throw new AgentProtocolException("MALFORMED_PLANNER_OUTPUT", "Planner sections must be non-empty.");

            if (marker.Kind == PlannerSectionKind.Task)
            {
                tasks.Add(body);
                continue;
            }

            if (question is not null)
                throw new AgentProtocolException("MALFORMED_PLANNER_OUTPUT", "Planner output may contain only one '# Question' section.");
            question = body;
        }

        if (question is not null && tasks.Count > 0)
            throw new AgentProtocolException("MALFORMED_PLANNER_OUTPUT", "Planner output cannot mix '# Task' sections with '# Question'. Execute safely contractable tasks first and ask only when no task can be contracted now.");
        return new(tasks, question);
    }

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
            QuestionHeading => PlannerSectionKind.Question,
            _ => PlannerSectionKind.None
        };
        return kind == PlannerSectionKind.None
            ? null
            : new(heading.Span.Start, lineEnd, kind);
    }

    private static string Normalize(string markdown) =>
        markdown.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n');

    private static AgentProtocolException Malformed() =>
        new("MALFORMED_PLANNER_OUTPUT", "Planner output must be empty, consist only of exact '# Task' sections, or contain exactly one exact '# Question' section.");

    private sealed record PlannerSectionMarker(int Start, int EndExclusive, PlannerSectionKind Kind);
    private enum PlannerSectionKind { None, Task, Question }
}
