using System.Text.Json;

namespace Idd.Factory.LiveTests.Tests;

internal sealed record WorkerExecutionSettings(string Model, string ReasoningEffort);

internal sealed record LiveEvalSettings(
    string Model,
    string ModelSource,
    string ReasoningEffort,
    string ReasoningEffortSource,
    IReadOnlyDictionary<string, WorkerExecutionSettings> ExecutionProfiles)
{
    internal static readonly string[] Profiles = ["economy", "standard", "strong"];

    internal static LiveEvalSettings Resolve(
        string repositoryRoot,
        string? environmentModel,
        string? environmentReasoningEffort,
        string? environmentModelSource = null,
        string? environmentReasoningEffortSource = null,
        Func<string, string?>? profileEnvironment = null)
    {
        var configPath = Path.Combine(repositoryRoot, "tests", "Idd.Factory.LiveTests", "live-eval.json");
        var config = ReadProjectConfig(configPath);

        var modelFromEnvironment = !string.IsNullOrWhiteSpace(environmentModel);
        var reasoningFromEnvironment = !string.IsNullOrWhiteSpace(environmentReasoningEffort);
        var model = modelFromEnvironment ? environmentModel! : config.Model;
        var profiles = Profiles.ToDictionary(profile => profile, profile =>
        {
            var prefix = $"IDD_FACTORY_EVAL_{profile.ToUpperInvariant()}";
            var configured = config.ExecutionProfiles[profile];
            return new WorkerExecutionSettings(
                Override(profileEnvironment?.Invoke(prefix + "_MODEL"), configured.Model),
                Override(profileEnvironment?.Invoke(prefix + "_REASONING_EFFORT"), configured.ReasoningEffort));
        }, StringComparer.Ordinal);
        ValidateProfiles(profiles);

        return new LiveEvalSettings(
            model,
            modelFromEnvironment ? Source(environmentModelSource, "environment") : "repository config",
            reasoningFromEnvironment ? environmentReasoningEffort! : config.ReasoningEffort,
            reasoningFromEnvironment ? Source(environmentReasoningEffortSource, "environment") : "repository config",
            profiles);
    }

    internal static void ValidateProfiles(IReadOnlyDictionary<string, WorkerExecutionSettings> profiles)
    {
        if (profiles.Count != Profiles.Length || Profiles.Any(profile =>
                !profiles.TryGetValue(profile, out var settings) ||
                string.IsNullOrWhiteSpace(settings.Model) || string.IsNullOrWhiteSpace(settings.ReasoningEffort)))
            throw new InvalidOperationException("Live routing evaluation requires complete economy, standard, and strong model/reasoning mappings.");
        if (profiles.Values.Select(settings => settings.Model).Distinct(StringComparer.Ordinal).Count() != Profiles.Length)
            throw new InvalidOperationException("Live routing evaluation requires three distinct models; varying reasoning alone cannot verify model routing.");
    }

    private static string Override(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

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
            if (!root.TryGetProperty("executionProfiles", out var profiles) || profiles.ValueKind != JsonValueKind.Object ||
                profiles.EnumerateObject().Count() != Profiles.Length)
                throw new InvalidOperationException($"Live-eval configuration requires all three executionProfiles: {configPath}");
            var mappings = Profiles.ToDictionary(profile => profile, profile =>
            {
                if (!profiles.TryGetProperty(profile, out var mapping) || mapping.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException($"Live-eval configuration requires the '{profile}' mapping: {configPath}");
                var profileModel = RequiredString(mapping, "model", configPath);
                return new ProfileConfig(profileModel, RequiredString(mapping, "reasoningEffort", configPath));
            }, StringComparer.Ordinal);
            return new ProjectConfig(model, reasoningEffort, mappings);
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

    private sealed record ProfileConfig(string Model, string ReasoningEffort);
    private sealed record ProjectConfig(string Model, string ReasoningEffort,
        IReadOnlyDictionary<string, ProfileConfig> ExecutionProfiles);
}
