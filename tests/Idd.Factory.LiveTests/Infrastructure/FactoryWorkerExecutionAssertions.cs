using System.Text.Json;
using Idd.Factory.LiveTests.Models;

namespace Idd.Factory.LiveTests.Infrastructure;

public static class FactoryWorkerExecutionAssertions
{
    public static void Assert(EvalAssertionCollector assertions, FactoryEvalWorkspace workspace, FactoryEvalOptions options, string installedPluginPath)
    {
        var attemptsRoot = FindAttemptsRoot(workspace.WorkspaceDirectory);
        if (attemptsRoot is null)
        {
            assertions.Require(false, "Worker execution", "Attempt telemetry", "No Factory attempt directory was found.");
            return;
        }

        var attempts = Directory.EnumerateDirectories(attemptsRoot).Order(StringComparer.Ordinal).ToArray();
        assertions.Require(attempts.Length > 0, "Worker execution", "Attempt telemetry", "No semantic worker attempts were recorded.");
        foreach (var attempt in attempts)
        {
            var id = Path.GetFileName(attempt);
            var telemetry = Read(Path.Combine(attempt, "attempt-telemetry.json"));
            var process = Read(Path.Combine(attempt, "process-telemetry.json"));
            if (telemetry is null || process is null)
            {
                assertions.Require(false, "Worker execution", $"{id} telemetry completeness", "Attempt or process telemetry is missing or malformed.");
                continue;
            }

            var requestedModel = Text(telemetry.Value, "requestedModel");
            var requestedReasoning = Text(telemetry.Value, "requestedReasoningEffort");
            assertions.Require(requestedModel == options.Model, "Worker execution", $"{id} requested model", $"Expected '{options.Model}', got '{requestedModel ?? "missing"}'.");
            assertions.Require(requestedReasoning == options.ReasoningEffort, "Worker execution", $"{id} requested reasoning", $"Expected '{options.ReasoningEffort}', got '{requestedReasoning ?? "missing"}'.");
            assertions.Require(Text(telemetry.Value, "skillSource") is { } source && Path.GetFullPath(source) == Path.GetFullPath(installedPluginPath), "Worker execution", $"{id} installed skill source", "Worker did not use the runtime-selected installed Factory plugin root.");
            assertions.Require(Text(telemetry.Value, "userSkillInheritancePolicy") == "isolated" && Number(telemetry.Value, "inheritedUserSkillCount") == 0, "Worker execution", $"{id} controlled capabilities", "Release eval worker inherited user-global skills.");

            AssertEffective(assertions, options, id, "model", Text(telemetry.Value, "effectiveModel"), options.Model);
            AssertEffective(assertions, options, id, "reasoning", Text(telemetry.Value, "effectiveReasoningEffort"), options.ReasoningEffort);
            assertions.Require(Text(process.Value, "terminationKind") == "CleanExit", "Worker execution", $"{id} clean process exit", $"Expected CleanExit, got {Text(process.Value, "terminationKind") ?? "missing"}.");
            assertions.Require(Boolean(process.Value, "completeResultObserved") == true && Boolean(process.Value, "killRequired") == false, "Worker execution", $"{id} result and termination", "Expected a complete result followed by natural process completion.");
        }
    }

    private static void AssertEffective(EvalAssertionCollector assertions, FactoryEvalOptions options, string attemptId, string kind, string? actual, string expected)
    {
        if (actual is null or "unknown")
        {
            assertions.Inconclusive("Worker execution", $"{attemptId} effective {kind}", $"Backend does not expose effective {kind}; requested configuration was recorded but cannot be independently confirmed.");
            return;
        }
        assertions.Require(actual == expected, "Worker execution", $"{attemptId} effective {kind}", $"Expected '{expected}', got '{actual}'.");
    }

    private static string? FindAttemptsRoot(string workspace)
    {
        var factory = Path.Combine(workspace, ".idd", "factory");
        var current = Path.Combine(factory, "current", "attempts");
        if (Directory.Exists(current) && Directory.EnumerateDirectories(current).Any()) return current;
        var results = Path.Combine(factory, "results");
        if (!Directory.Exists(results)) return null;
        return Directory.EnumerateDirectories(results, "attempts", SearchOption.AllDirectories).SingleOrDefault();
    }

    private static JsonElement? Read(string path)
    {
        if (!File.Exists(path)) return null;
        try { using var document = JsonDocument.Parse(File.ReadAllText(path)); return document.RootElement.Clone(); }
        catch (JsonException) { return null; }
    }
    private static string? Text(JsonElement value, string name) => value.TryGetProperty(name, out var node) && node.ValueKind == JsonValueKind.String ? node.GetString() : null;
    private static int? Number(JsonElement value, string name) => value.TryGetProperty(name, out var node) && node.TryGetInt32(out var number) ? number : null;
    private static bool? Boolean(JsonElement value, string name) => value.TryGetProperty(name, out var node) && node.ValueKind is JsonValueKind.True or JsonValueKind.False ? node.GetBoolean() : null;
}
