using Idd.Factory.LiveTests.Infrastructure;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class CodexRootRuntimeTelemetryReaderTests
{
    [Fact]
    public void TryRead_UsesOnlyMatchingRootRolloutModel()
    {
        var root = CreateHome();
        try
        {
            var sessions = Path.Combine(root, "sessions");
            WriteRollout(sessions, "root.jsonl",
                "{\"type\":\"session_meta\",\"payload\":{\"id\":\"root-thread\"}}",
                "{\"type\":\"turn_context\",\"payload\":{\"model\":\"gpt-root\",\"effort\":\"low\"}}");
            WriteRollout(sessions, "child.jsonl",
                "{\"type\":\"session_meta\",\"payload\":{\"id\":\"child-thread\",\"parent_thread_id\":\"root-thread\"}}",
                "{\"type\":\"turn_context\",\"payload\":{\"model\":\"gpt-child\",\"effort\":\"high\"}}");

            var result = CodexRootRuntimeTelemetryReader.TryRead(sessions, "root-thread");

            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal("gpt-root", result.Model);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void TryRead_RejectsRootModelChange()
    {
        var root = CreateHome();
        try
        {
            var sessions = Path.Combine(root, "sessions");
            WriteRollout(sessions, "root.jsonl",
                "{\"type\":\"session_meta\",\"payload\":{\"id\":\"root-thread\"}}",
                "{\"type\":\"turn_context\",\"payload\":{\"model\":\"gpt-a\"}}",
                "{\"type\":\"turn_context\",\"payload\":{\"model\":\"gpt-b\"}}");

            var result = CodexRootRuntimeTelemetryReader.TryRead(sessions, "root-thread");

            Assert.False(result.IsSuccess);
            Assert.Null(result.Model);
            Assert.Contains("multiple models", result.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Analyze_UsesRootRolloutAsEffectiveModelSource()
    {
        var root = CreateHome();
        var events = Path.Combine(root, "events.jsonl");
        try
        {
            var sessions = Path.Combine(root, "sessions");
            WriteRollout(sessions, "root.jsonl",
                "{\"type\":\"session_meta\",\"payload\":{\"id\":\"root-thread\"}}",
                "{\"type\":\"turn_context\",\"payload\":{\"model\":\"gpt-5.6-luna\"}}");
            File.WriteAllLines(events,
            [
                "{\"type\":\"thread.started\",\"thread_id\":\"root-thread\"}",
                "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}"
            ]);

            var metrics = CodexJsonlAnalyzer.Analyze(
                events,
                TimeSpan.Zero,
                new CodexHomeLocator(() => root, () => "ignored"));

            Assert.Equal("root-thread", metrics.RootThreadId);
            Assert.Equal("gpt-5.6-luna", metrics.ModelEffective);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Analyze_MarksModelUnavailableWhenRootCannotBeProven()
    {
        var root = CreateHome();
        var events = Path.Combine(root, "events.jsonl");
        try
        {
            File.WriteAllText(events, "{\"type\":\"thread.started\",\"thread_id\":\"missing-root\"}\n");

            var metrics = CodexJsonlAnalyzer.Analyze(
                events,
                TimeSpan.Zero,
                new CodexHomeLocator(() => root, () => "ignored"));

            Assert.Null(metrics.ModelEffective);
        }
        finally { Directory.Delete(root, true); }
    }

    private static string CreateHome()
    {
        var root = Path.Combine(Path.GetTempPath(), $"codex-root-telemetry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "sessions"));
        return root;
    }

    private static void WriteRollout(string sessionsDirectory, string fileName, params string[] lines) =>
        File.WriteAllLines(Path.Combine(sessionsDirectory, fileName), lines);
}
