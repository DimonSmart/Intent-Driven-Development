using System.Text.Json;

namespace Idd.Factory.LiveTests.Tests;

internal sealed record LiveEvalSettings(
    string Model,
    string ModelSource,
    string ReasoningEffort,
    string ReasoningEffortSource)
{
    internal static LiveEvalSettings Resolve(
        string repositoryRoot,
        string? environmentModel,
        string? environmentReasoningEffort,
        string? environmentModelSource = null,
        string? environmentReasoningEffortSource = null)
    {
        var configPath = Path.Combine(repositoryRoot, "tests", "Idd.Factory.LiveTests", "live-eval.json");
        var config = ReadProjectConfig(configPath);

        var modelFromEnvironment = !string.IsNullOrWhiteSpace(environmentModel);
        var reasoningFromEnvironment = !string.IsNullOrWhiteSpace(environmentReasoningEffort);

        return new LiveEvalSettings(
            modelFromEnvironment ? environmentModel! : config.Model,
            modelFromEnvironment ? Source(environmentModelSource, "environment") : "repository config",
            reasoningFromEnvironment ? environmentReasoningEffort! : config.ReasoningEffort,
            reasoningFromEnvironment ? Source(environmentReasoningEffortSource, "environment") : "repository config");
    }

    private static ProjectConfig ReadProjectConfig(string configPath)
    {
        if (!File.Exists(configPath))
            throw new InvalidOperationException($"Live-eval configuration is missing: {configPath}");

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            var root = document.RootElement;
            var model = RequiredString(root, "model", configPath);
            var reasoningEffort = RequiredString(root, "reasoningEffort", configPath);
            return new ProjectConfig(model, reasoningEffort);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Live-eval configuration is not valid JSON: {configPath}", ex);
        }
    }

    private static string RequiredString(JsonElement root, string propertyName, string configPath)
    {
        if (!root.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidOperationException(
                $"Live-eval configuration requires a non-empty '{propertyName}' string: {configPath}");
        }

        return property.GetString()!;
    }

    private static string Source(string? configuredSource, string fallback) =>
        string.IsNullOrWhiteSpace(configuredSource) ? fallback : configuredSource;

    private sealed record ProjectConfig(string Model, string ReasoningEffort);
}
