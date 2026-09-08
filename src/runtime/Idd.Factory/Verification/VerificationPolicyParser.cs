using System.Diagnostics.CodeAnalysis;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Idd.Factory.Verification;

internal static class VerificationPolicyParser
{
    private static readonly HashSet<string> ContextNames = ["direct", "subtask", "checkpoint", "final"];

    public static VerificationPolicy Parse(string yaml)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            if (stream.Documents.Count != 1) Invalid("Policy must be one YAML mapping document.");
            var root = AsMapping(stream.Documents[0].RootNode, "policy");
            var rootValues = Mapping(root, "policy");
            var allowedRoot = new HashSet<string>(ContextNames, StringComparer.Ordinal) { "version", "checks", "default" };
            RejectUnknown(rootValues, allowedRoot, "policy");
            if (RequiredScalar(rootValues, "version", "policy") != "1") Invalid("Only verification policy version 1 is supported.");

            var checksNode = RequiredMapping(rootValues, "checks", "policy");
            var checks = new Dictionary<string, VerificationCheck>(StringComparer.Ordinal);
            foreach (var (id, node) in Mapping(checksNode, "checks"))
            {
                if (string.IsNullOrWhiteSpace(id)) Invalid("Check IDs must not be empty.");
                var definition = Mapping(AsMapping(node, $"check {id}"), $"check {id}");
                RejectUnknown(definition, ["run", "instructions", "timeout", "confirmation"], $"check {id}");
                var run = OptionalScalar(definition, "run", $"check {id}");
                var instructions = OptionalScalar(definition, "instructions", $"check {id}");
                if ((run is null) == (instructions is null)) Invalid($"Check {id} must have exactly one of run or instructions.");
                if (run is not null && string.IsNullOrWhiteSpace(run) || instructions is not null && string.IsNullOrWhiteSpace(instructions)) Invalid($"Check {id} has an empty definition.");
                var timeout = definition.TryGetValue("timeout", out var timeoutNode)
                    ? ParseTimeout(Scalar(timeoutNode, $"check {id}.timeout"))
                    : TimeSpan.FromMinutes(10);
                var confirmationRequired = false;
                if (instructions is not null && definition.ContainsKey("timeout")) Invalid($"Check {id} cannot set timeout without run.");
                if (definition.TryGetValue("confirmation", out var confirmationNode))
                {
                    if (run is null || Scalar(confirmationNode, $"check {id}.confirmation") != "required")
                        Invalid($"Check {id} confirmation must be 'required' on a run check.");
                    confirmationRequired = true;
                }
                checks.Add(id, new(run, instructions, timeout, confirmationRequired));
            }

            var contexts = new Dictionary<string, VerificationContext>(StringComparer.Ordinal)
            {
                ["default"] = ParseContext(RequiredMapping(rootValues, "default", "policy"), "default", checks, allowRules: false)
            };
            foreach (var context in ContextNames)
                if (rootValues.TryGetValue(context, out var node))
                    contexts[context] = ParseContext(AsMapping(node, context), context, checks, allowRules: true);
            return new(checks, contexts);
        }
        catch (VerificationException) { throw; }
        catch (YamlException exception)
        {
            throw new VerificationException("INVALID_VERIFICATION_POLICY", $"Malformed verification policy YAML: {exception.Message}");
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            throw new VerificationException("INVALID_VERIFICATION_POLICY", exception.Message);
        }
    }

    internal static bool IsKnownContext(string context) => ContextNames.Contains(context);

    private static VerificationContext ParseContext(
        YamlMappingNode node,
        string name,
        IReadOnlyDictionary<string, VerificationCheck> checks,
        bool allowRules)
    {
        var values = Mapping(node, name);
        RejectUnknown(values, allowRules ? ["use", "rules"] : ["use"], name);
        if (values.ContainsKey("use") == values.ContainsKey("rules"))
            Invalid($"Context {name} must have exactly one of use or rules.");
        if (values.TryGetValue("use", out var use))
            return new(ParseIds(use, $"{name}.use", checks), [], true);

        var rules = AsSequence(values["rules"], $"{name}.rules");
        var parsedRules = new List<VerificationRule>();
        var fallbackSeen = false;
        foreach (var (ruleNode, index) in rules.Children.Select((value, index) => (value, index)))
        {
            var ruleName = $"{name}.rules[{index}]";
            var rule = Mapping(AsMapping(ruleNode, ruleName), ruleName);
            RejectUnknown(rule, ["paths", "fallback", "use"], ruleName);
            if (!rule.TryGetValue("use", out var ruleUse)) Invalid($"{ruleName} must define use.");
            ParseIds(ruleUse, $"{ruleName}.use", checks);
            var hasPaths = rule.TryGetValue("paths", out var paths);
            var fallback = rule.TryGetValue("fallback", out var fallbackNode);
            if (fallback && Scalar(fallbackNode!, $"{ruleName}.fallback") != "true") Invalid($"{ruleName}.fallback must be true.");
            if (fallback == hasPaths) Invalid($"{ruleName} must define exactly one of paths or fallback: true.");
            if (hasPaths)
            {
                if (fallbackSeen) Invalid($"{ruleName} appears after a pathless fallback rule.");
                var pathValues = Scalars(AsSequence(paths!, $"{ruleName}.paths"), $"{ruleName}.paths");
                if (pathValues.Count == 0 || pathValues.Any(string.IsNullOrWhiteSpace))
                    Invalid($"{ruleName}.paths must contain non-empty paths.");
                parsedRules.Add(new(pathValues, ParseIds(ruleUse, $"{ruleName}.use", checks), false));
            }
            else
            {
                if (fallbackSeen) Invalid($"{ruleName} is a second pathless fallback rule.");
                fallbackSeen = true;
                parsedRules.Add(new([], ParseIds(ruleUse, $"{ruleName}.use", checks), true));
            }
        }
        if (parsedRules.Count == 0) Invalid($"{name}.rules must not be empty.");
        return new([], parsedRules, false);
    }

    private static IReadOnlyList<string> ParseIds(
        YamlNode node,
        string location,
        IReadOnlyDictionary<string, VerificationCheck> checks)
    {
        var ids = Scalars(AsSequence(node, location), location);
        if (ids.Any(string.IsNullOrWhiteSpace)) Invalid($"{location} contains an empty check ID.");
        var unknown = ids.Where(id => !checks.ContainsKey(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (unknown.Length > 0) Invalid($"{location} references unknown checks: {string.Join(", ", unknown)}.");
        return ids;
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

    private static void RejectUnknown(
        IReadOnlyDictionary<string, YamlNode> values,
        IEnumerable<string> allowed,
        string location)
    {
        var set = allowed.ToHashSet(StringComparer.Ordinal);
        var unknown = values.Keys.Where(key => !set.Contains(key)).ToArray();
        if (unknown.Length > 0) Invalid($"Unknown fields in {location}: {string.Join(", ", unknown)}.");
    }

    private static YamlMappingNode RequiredMapping(IReadOnlyDictionary<string, YamlNode> values, string key, string location) =>
        values.TryGetValue(key, out var node)
            ? AsMapping(node, $"{location}.{key}")
            : throw new VerificationException("INVALID_VERIFICATION_POLICY", $"Missing {location}.{key}.");

    private static string RequiredScalar(IReadOnlyDictionary<string, YamlNode> values, string key, string location) =>
        values.TryGetValue(key, out var node)
            ? Scalar(node, $"{location}.{key}")
            : throw new VerificationException("INVALID_VERIFICATION_POLICY", $"Missing {location}.{key}.");

    private static string? OptionalScalar(IReadOnlyDictionary<string, YamlNode> values, string key, string location) =>
        values.TryGetValue(key, out var node) ? Scalar(node, $"{location}.{key}") : null;

    private static YamlMappingNode AsMapping(YamlNode node, string location) =>
        node as YamlMappingNode
        ?? throw new VerificationException("INVALID_VERIFICATION_POLICY", $"{location} must be a mapping.");

    private static YamlSequenceNode AsSequence(YamlNode node, string location) =>
        node as YamlSequenceNode
        ?? throw new VerificationException("INVALID_VERIFICATION_POLICY", $"{location} must be a sequence.");

    private static string Scalar(YamlNode node, string location) =>
        node is YamlScalarNode { Value: not null } scalar
            ? scalar.Value
            : throw new VerificationException("INVALID_VERIFICATION_POLICY", $"{location} must be a scalar.");

    private static IReadOnlyList<string> Scalars(YamlSequenceNode node, string location) =>
        node.Children.Select((value, index) => Scalar(value, $"{location}[{index}]")).ToArray();

    private static TimeSpan ParseTimeout(string value)
    {
        if (value.Length < 2) Invalid($"Invalid timeout {value}.");
        if (!int.TryParse(value[..^1], out var amount) || amount < 0) Invalid($"Invalid timeout {value}.");
        return value[^1] switch
        {
            's' => TimeSpan.FromSeconds(amount),
            'm' => TimeSpan.FromMinutes(amount),
            'h' => TimeSpan.FromHours(amount),
            _ => throw new VerificationException("INVALID_VERIFICATION_POLICY", $"Invalid timeout {value}.")
        };
    }

    [DoesNotReturn]
    private static void Invalid(string message) =>
        throw new VerificationException("INVALID_VERIFICATION_POLICY", message);
}

internal sealed record VerificationRule(IReadOnlyList<string> Paths, IReadOnlyList<string> Use, bool Fallback);
internal sealed record VerificationContext(IReadOnlyList<string> Use, IReadOnlyList<VerificationRule> Rules, bool HasUse);

internal sealed record VerificationPolicy(
    IReadOnlyDictionary<string, VerificationCheck> Checks,
    IReadOnlyDictionary<string, VerificationContext> Contexts)
{
    public IReadOnlyList<string> ResolveContext(string context, IEnumerable<string> changedPaths)
    {
        if (context != "default" && !VerificationPolicyParser.IsKnownContext(context))
            throw new VerificationException("INVALID_VERIFICATION_POLICY", $"Unknown verification context {context}.");
        var selected = Contexts.TryGetValue(context, out var value) ? value : Contexts["default"];
        if (selected.HasUse) return selected.Use;
        var paths = changedPaths
            .Select(path => path.Replace('\\', '/').TrimStart('/'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var rule in selected.Rules)
        {
            if (rule.Fallback || paths.Length > 0 && paths.All(path => rule.Paths.Any(pattern => Glob(pattern, path))))
                return rule.Use;
        }
        return Contexts["default"].Use;
    }

    private static bool Glob(string pattern, string path)
    {
        var expression = "^" + System.Text.RegularExpressions.Regex.Escape(pattern.Replace('\\', '/'))
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*")
            .Replace("\\?", "[^/]") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(
            path,
            expression,
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }
}
