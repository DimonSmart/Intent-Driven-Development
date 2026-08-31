using System.Security.Cryptography;
using System.Text;
using Idd.Factory.Domain;
using YamlDotNet.RepresentationModel;

namespace Idd.Factory.Configuration;

public sealed record FactoryConfiguration(
    int SchemaVersion,
    FactoryLimits Limits,
    FinalReviewPolicy FinalReview,
    IReadOnlySet<string> AllowedCapabilities,
    string SourcePath,
    string Hash);

public sealed record FactoryLimits(
    int MaxAgentAttempts,
    int MaxReplans,
    int MaxCorrectiveCycles,
    int MaxWorkItems);

public sealed record FinalReviewPolicy(bool Required);

public sealed class FactoryConfigurationLoader
{
    public FactoryConfiguration Load(string workspace, string packagedConfigurationPath)
    {
        var overridePath = Path.Combine(workspace, ".idd", "factory.yaml");
        var sourcePath = File.Exists(overridePath) ? overridePath : packagedConfigurationPath;
        if (!File.Exists(sourcePath))
            throw new FactoryConfigurationException("FACTORY_CONFIGURATION_NOT_FOUND", $"Factory configuration not found: {sourcePath}");

        var yaml = File.ReadAllText(sourcePath).Replace("\r\n", "\n").Trim() + "\n";
        var configuration = RestrictedFactoryConfigurationYaml.Parse(yaml, Path.GetFullPath(sourcePath));
        FactoryConfigurationValidator.Validate(configuration);
        return configuration with
        {
            Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(yaml))).ToLowerInvariant()
        };
    }
}

public static class FactoryConfigurationValidator
{
    public static void Validate(FactoryConfiguration configuration)
    {
        if (configuration.SchemaVersion != 1)
            throw new FactoryConfigurationException("UNSUPPORTED_FACTORY_CONFIGURATION_SCHEMA", $"Unsupported Factory configuration schema {configuration.SchemaVersion}.");
        if (configuration.Limits.MaxAgentAttempts is < 1 or > 10 ||
            configuration.Limits.MaxReplans is < 0 or > 10 ||
            configuration.Limits.MaxCorrectiveCycles is < 0 or > 20 ||
            configuration.Limits.MaxWorkItems is < 1 or > 256)
            throw new FactoryConfigurationException("INVALID_FACTORY_LIMITS", "Factory limits exceed runtime safety ceilings.");
        if (!configuration.FinalReview.Required)
            throw new FactoryConfigurationException("INVALID_FACTORY_CONFIGURATION", "Final integrated semantic review remains mandatory in this Factory version.");

        var known = FactoryCapabilityCatalog.WorkItemCapabilities.ToHashSet(StringComparer.Ordinal);
        var unknown = configuration.AllowedCapabilities.Where(x => !known.Contains(x)).ToArray();
        if (unknown.Length > 0)
            throw new FactoryConfigurationException("UNKNOWN_CAPABILITY", $"Unknown configured capabilities: {string.Join(", ", unknown)}.");
        foreach (var required in new[] { "implementation", "semantic-review" })
            if (!configuration.AllowedCapabilities.Contains(required))
                throw new FactoryConfigurationException("INVALID_FACTORY_CONFIGURATION", $"Capability '{required}' is required by Factory product semantics.");
    }
}

internal static class RestrictedFactoryConfigurationYaml
{
    public static FactoryConfiguration Parse(string yaml, string sourcePath)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            if (stream.Documents.Count != 1) Invalid("Configuration must be one YAML mapping document.");
            var root = Mapping(AsMapping(stream.Documents[0].RootNode, "configuration"), "configuration");
            RejectUnknown(root, ["schemaVersion", "limits", "finalReview", "capabilities"], "configuration");
            var schemaVersion = Number(RequiredScalar(root, "schemaVersion", "configuration"), "schemaVersion");

            var limitsNode = Mapping(RequiredMapping(root, "limits", "configuration"), "limits");
            RejectUnknown(limitsNode, ["maxAgentAttempts", "maxReplans", "maxCorrectiveCycles", "maxWorkItems"], "limits");
            var limits = new FactoryLimits(
                Number(RequiredScalar(limitsNode, "maxAgentAttempts", "limits"), "maxAgentAttempts"),
                Number(RequiredScalar(limitsNode, "maxReplans", "limits"), "maxReplans"),
                Number(RequiredScalar(limitsNode, "maxCorrectiveCycles", "limits"), "maxCorrectiveCycles"),
                Number(RequiredScalar(limitsNode, "maxWorkItems", "limits"), "maxWorkItems"));

            var reviewNode = Mapping(RequiredMapping(root, "finalReview", "configuration"), "finalReview");
            RejectUnknown(reviewNode, ["required"], "finalReview");
            var review = new FinalReviewPolicy(Boolean(RequiredScalar(reviewNode, "required", "finalReview"), "finalReview.required"));

            var capabilitiesNode = Mapping(RequiredMapping(root, "capabilities", "configuration"), "capabilities");
            RejectUnknown(capabilitiesNode, ["allow"], "capabilities");
            var allow = Scalars(RequiredSequence(capabilitiesNode, "allow", "capabilities"), "capabilities.allow")
                .ToHashSet(StringComparer.Ordinal);
            if (allow.Count == 0) Invalid("capabilities.allow must not be empty.");

            return new FactoryConfiguration(schemaVersion, limits, review, allow, sourcePath, "");
        }
        catch (FactoryConfigurationException) { throw; }
        catch (Exception exception) when (exception is YamlDotNet.Core.YamlException or FormatException or OverflowException or ArgumentException)
        {
            throw new FactoryConfigurationException("INVALID_FACTORY_CONFIGURATION_YAML", exception.Message);
        }
    }

    private static Dictionary<string, YamlNode> Mapping(YamlMappingNode node, string location)
    {
        var result = new Dictionary<string, YamlNode>(StringComparer.Ordinal);
        foreach (var pair in node.Children)
        {
            var key = Scalar(pair.Key, location);
            if (!result.TryAdd(key, pair.Value)) Invalid($"Duplicate key {key} in {location}.");
        }
        return result;
    }

    private static void RejectUnknown(IReadOnlyDictionary<string, YamlNode> values, IEnumerable<string> allowed, string location)
    {
        var set = allowed.ToHashSet(StringComparer.Ordinal);
        var unknown = values.Keys.Where(x => !set.Contains(x)).ToArray();
        if (unknown.Length > 0) Invalid($"Unknown fields in {location}: {string.Join(", ", unknown)}.");
    }

    private static YamlMappingNode RequiredMapping(IReadOnlyDictionary<string, YamlNode> values, string key, string location) =>
        values.TryGetValue(key, out var value) ? AsMapping(value, $"{location}.{key}") : throw new FactoryConfigurationException("INVALID_FACTORY_CONFIGURATION_YAML", $"Missing {location}.{key}.");

    private static YamlSequenceNode RequiredSequence(IReadOnlyDictionary<string, YamlNode> values, string key, string location) =>
        values.TryGetValue(key, out var value) ? AsSequence(value, $"{location}.{key}") : throw new FactoryConfigurationException("INVALID_FACTORY_CONFIGURATION_YAML", $"Missing {location}.{key}.");

    private static string RequiredScalar(IReadOnlyDictionary<string, YamlNode> values, string key, string location) =>
        values.TryGetValue(key, out var value) ? Scalar(value, $"{location}.{key}") : throw new FactoryConfigurationException("INVALID_FACTORY_CONFIGURATION_YAML", $"Missing {location}.{key}.");

    private static YamlMappingNode AsMapping(YamlNode node, string location) =>
        node as YamlMappingNode ?? throw new FactoryConfigurationException("INVALID_FACTORY_CONFIGURATION_YAML", $"{location} must be a mapping.");

    private static YamlSequenceNode AsSequence(YamlNode node, string location) =>
        node as YamlSequenceNode ?? throw new FactoryConfigurationException("INVALID_FACTORY_CONFIGURATION_YAML", $"{location} must be a sequence.");

    private static string Scalar(YamlNode node, string location) =>
        node is YamlScalarNode { Value: not null } scalar ? scalar.Value : throw new FactoryConfigurationException("INVALID_FACTORY_CONFIGURATION_YAML", $"{location} must be a scalar.");

    private static IReadOnlyList<string> Scalars(YamlSequenceNode node, string location) =>
        node.Children.Select((value, index) => Scalar(value, $"{location}[{index}]")).ToArray();

    private static int Number(string value, string name) =>
        int.TryParse(value, out var result) ? result : throw new FactoryConfigurationException("INVALID_FACTORY_CONFIGURATION_YAML", $"{name} must be an integer.");

    private static bool Boolean(string value, string name) => value switch
    {
        "true" => true,
        "false" => false,
        _ => throw new FactoryConfigurationException("INVALID_FACTORY_CONFIGURATION_YAML", $"{name} must be true or false.")
    };

    private static void Invalid(string message) => throw new FactoryConfigurationException("INVALID_FACTORY_CONFIGURATION_YAML", message);
}

public sealed class FactoryConfigurationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
