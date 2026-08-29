using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Idd.Factory.Domain;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Idd.Factory.Verification;

public sealed record VerificationEvidence(
    int SchemaVersion, string EvidenceId, string CheckId, string CheckDefinitionHash,
    DateTimeOffset StartedAt, DateTimeOffset FinishedAt,
    int ExitCode, string Status, string Output);

public enum VerificationStatus { Passed, Failed, RequiresUserAction, InfrastructureFailure }

public sealed record VerificationResult(VerificationStatus Status, IReadOnlyList<VerificationEvidence> Evidence)
{
    public bool Passed => Status == VerificationStatus.Passed;
}

public sealed class VerificationEngine(string workspace, string currentDirectory)
{
    public void ValidateCheckIds(IEnumerable<string> checkIds)
    {
        var ids = checkIds.Distinct(StringComparer.Ordinal).ToArray();
        var policy = LoadPolicy();
        if (policy is null)
        {
            if (ids.Length > 0) throw new VerificationException("UNKNOWN_VERIFICATION_CHECK", "Explicit verification IDs require .idd/verification.yaml.");
            return;
        }
        var unknown = ids.Where(id => !policy.Checks.ContainsKey(id)).ToArray();
        if (unknown.Length > 0) throw new VerificationException("UNKNOWN_VERIFICATION_CHECK", $"Unknown check IDs: {string.Join(", ", unknown)}.");
    }

    public async Task<VerificationResult> RunContextAsync(string context, CancellationToken cancellationToken)
    {
        var policy = await LoadPolicyAsync(cancellationToken);
        return policy is null
            ? await RunRepositoryFallbackAsync(cancellationToken)
            : await RunPolicyChecksAsync(policy, policy.ResolveContext(context), cancellationToken);
    }

    public async Task<VerificationResult> RunSubtaskAsync(IEnumerable<string> explicitCheckIds, CancellationToken cancellationToken)
    {
        var policy = await LoadPolicyAsync(cancellationToken);
        return policy is null
            ? await RunRepositoryFallbackAsync(cancellationToken)
            : await RunPolicyChecksAsync(policy, explicitCheckIds, cancellationToken);
    }

    public async Task<VerificationResult> RunAsync(IEnumerable<string> checkIds, CancellationToken cancellationToken)
    {
        var ids = checkIds.Distinct(StringComparer.Ordinal).ToArray();
        var policy = await LoadPolicyAsync(cancellationToken);
        if (policy is null)
        {
            if (ids.Length > 0) throw new VerificationException("UNKNOWN_VERIFICATION_CHECK", "Explicit verification IDs require .idd/verification.yaml.");
            return new(VerificationStatus.Passed, []);
        }
        return await RunPolicyChecksAsync(policy, ids, cancellationToken);
    }

    private async Task<VerificationResult> RunPolicyChecksAsync(VerificationPolicy policy, IEnumerable<string> checkIds, CancellationToken cancellationToken)
    {
        var selected = new List<(string Id, VerificationCheck Check)>();
        foreach (var id in checkIds.Distinct(StringComparer.Ordinal))
        {
            if (!policy.Checks.TryGetValue(id, out var check)) throw new VerificationException("UNKNOWN_VERIFICATION_CHECK", $"Unknown check ID {id}.");
            selected.Add((id, check));
        }
        return await RunChecksAsync(selected, cancellationToken);
    }

    private VerificationPolicy? LoadPolicy()
    {
        var path = Path.Combine(workspace, ".idd", "verification.yaml");
        return File.Exists(path) ? VerificationPolicyParser.Parse(File.ReadAllText(path)) : null;
    }

    private async Task<VerificationPolicy?> LoadPolicyAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(workspace, ".idd", "verification.yaml");
        return File.Exists(path) ? VerificationPolicyParser.Parse(await File.ReadAllTextAsync(path, cancellationToken)) : null;
    }

    private async Task<VerificationResult> RunRepositoryFallbackAsync(CancellationToken cancellationToken)
    {
        var fallback = RepositoryFallback();
        return fallback is null ? new(VerificationStatus.Passed, []) : await RunChecksAsync([new("repository-fallback", fallback)], cancellationToken);
    }

    private async Task<VerificationResult> RunChecksAsync(IEnumerable<(string Id, VerificationCheck Check)> checks, CancellationToken cancellationToken)
    {
        var evidence = new List<VerificationEvidence>();
        foreach (var (id, check) in checks) evidence.Add(await RunCheckAsync(id, check, cancellationToken));
        var status = evidence.Any(x => x.Status == "infrastructure-failure") ? VerificationStatus.InfrastructureFailure
            : evidence.Any(x => x.Status == "requires-user-action") ? VerificationStatus.RequiresUserAction
            : evidence.Any(x => x.Status == "failed") ? VerificationStatus.Failed
            : VerificationStatus.Passed;
        return new(status, evidence);
    }

    private async Task<VerificationEvidence> RunCheckAsync(string id, VerificationCheck check, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        if (check.Instructions is not null)
            return await PersistAsync(id, check.Instructions, started, -1, "requires-user-action", check.Instructions, cancellationToken);
        var shell = OperatingSystem.IsWindows() ? "powershell" : "/bin/sh";
        var info = new ProcessStartInfo(shell) { WorkingDirectory = workspace, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        if (OperatingSystem.IsWindows()) { info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-Command"); info.ArgumentList.Add(check.Run!); }
        else { info.ArgumentList.Add("-c"); info.ArgumentList.Add(check.Run!); }
        Process? startedProcess;
        try { startedProcess = Process.Start(info); }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        { return await PersistAsync(id, check.Run!, started, -1, "infrastructure-failure", exception.Message, cancellationToken); }
        if (startedProcess is null) return await PersistAsync(id, check.Run!, started, -1, "infrastructure-failure", $"Could not start check {id}.", cancellationToken);
        using var process = startedProcess;
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(check.Timeout);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited) process.Kill(true);
            return await PersistAsync(id, check.Run!, started, -1, "infrastructure-failure", $"Check {id} timed out.", CancellationToken.None);
        }
        var output = (await stdoutTask) + (await stderrTask);
        return await PersistAsync(id, check.Run!, started, process.ExitCode, process.ExitCode == 0 ? "passed" : "failed", output, cancellationToken);
    }

    private async Task<VerificationEvidence> PersistAsync(string id, string definition, DateTimeOffset started, int exitCode, string status, string output, CancellationToken cancellationToken)
    {
        var definitionHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(definition))).ToLowerInvariant();
        var result = new VerificationEvidence(2, $"V{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..36], id, definitionHash, started, DateTimeOffset.UtcNow, exitCode, status, output);
        var directory = Path.Combine(currentDirectory, "verification"); Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, result.EvidenceId + ".json"), JsonSerializer.Serialize(result, FactoryJson.Options), cancellationToken);
        return result;
    }

    private VerificationCheck? RepositoryFallback()
    {
        if (File.Exists(Path.Combine(workspace, "scripts", "Check.ps1")))
        {
            var run = OperatingSystem.IsWindows()
                ? "& './scripts/Check.ps1'"
                : "pwsh -NoProfile -File './scripts/Check.ps1'";
            return new(run, null, TimeSpan.FromMinutes(30));
        }
        var solution = Directory.GetFiles(workspace, "*.sln").OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault();
        if (solution is not null) return new($"dotnet test '{Path.GetFileName(solution)}'", null, TimeSpan.FromMinutes(30));
        return null;
    }
}

public sealed record VerificationCheck(string? Run, string? Instructions, TimeSpan Timeout);

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
                var timeout = definition.TryGetValue("timeout", out var timeoutNode) ? ParseTimeout(Scalar(timeoutNode, $"check {id}.timeout")) : TimeSpan.FromMinutes(10);
                if (instructions is not null && definition.ContainsKey("timeout")) Invalid($"Check {id} cannot set timeout without run.");
                if (definition.TryGetValue("confirmation", out var confirmationNode))
                {
                    if (run is null || Scalar(confirmationNode, $"check {id}.confirmation") != "required") Invalid($"Check {id} confirmation must be 'required' on a run check.");
                }
                checks.Add(id, new(run, instructions, timeout));
            }

            var contexts = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["default"] = ParseContext(RequiredMapping(rootValues, "default", "policy"), "default", checks, allowRules: false)
            };
            foreach (var context in ContextNames)
                if (rootValues.TryGetValue(context, out var node))
                    contexts[context] = ParseContext(AsMapping(node, context), context, checks, allowRules: true);
            return new(checks, contexts);
        }
        catch (VerificationException) { throw; }
        catch (YamlException exception) { throw new VerificationException("INVALID_VERIFICATION_POLICY", $"Malformed verification policy YAML: {exception.Message}"); }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        { throw new VerificationException("INVALID_VERIFICATION_POLICY", exception.Message); }
    }

    internal static bool IsKnownContext(string context) => ContextNames.Contains(context);

    private static IReadOnlyList<string> ParseContext(YamlMappingNode node, string name, IReadOnlyDictionary<string, VerificationCheck> checks, bool allowRules)
    {
        var values = Mapping(node, name); RejectUnknown(values, allowRules ? ["use", "rules"] : ["use"], name);
        if (values.ContainsKey("use") == values.ContainsKey("rules")) Invalid($"Context {name} must have exactly one of use or rules.");
        if (values.TryGetValue("use", out var use)) return ParseIds(use, $"{name}.use", checks);
        var rules = AsSequence(values["rules"], $"{name}.rules");
        var fallbackSeen = false;
        foreach (var (ruleNode, index) in rules.Children.Select((value, index) => (value, index)))
        {
            var ruleName = $"{name}.rules[{index}]"; var rule = Mapping(AsMapping(ruleNode, ruleName), ruleName);
            RejectUnknown(rule, ["paths", "use"], ruleName);
            if (!rule.TryGetValue("use", out var ruleUse)) Invalid($"{ruleName} must define use.");
            ParseIds(ruleUse, $"{ruleName}.use", checks);
            var hasPaths = rule.TryGetValue("paths", out var paths);
            if (hasPaths)
            {
                if (fallbackSeen) Invalid($"{ruleName} appears after a pathless fallback rule.");
                var pathValues = Scalars(AsSequence(paths!, $"{ruleName}.paths"), $"{ruleName}.paths");
                if (pathValues.Count == 0 || pathValues.Any(string.IsNullOrWhiteSpace)) Invalid($"{ruleName}.paths must contain non-empty paths.");
            }
            else
            {
                if (fallbackSeen) Invalid($"{ruleName} is a second pathless fallback rule.");
                fallbackSeen = true;
            }
        }
        return [];
    }

    private static IReadOnlyList<string> ParseIds(YamlNode node, string location, IReadOnlyDictionary<string, VerificationCheck> checks)
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
    private static void RejectUnknown(IReadOnlyDictionary<string, YamlNode> values, IEnumerable<string> allowed, string location)
    {
        var set = allowed.ToHashSet(StringComparer.Ordinal); var unknown = values.Keys.Where(key => !set.Contains(key)).ToArray();
        if (unknown.Length > 0) Invalid($"Unknown fields in {location}: {string.Join(", ", unknown)}.");
    }
    private static YamlMappingNode RequiredMapping(IReadOnlyDictionary<string, YamlNode> values, string key, string location)
        => values.TryGetValue(key, out var node) ? AsMapping(node, $"{location}.{key}") : throw new VerificationException("INVALID_VERIFICATION_POLICY", $"Missing {location}.{key}.");
    private static string RequiredScalar(IReadOnlyDictionary<string, YamlNode> values, string key, string location)
        => values.TryGetValue(key, out var node) ? Scalar(node, $"{location}.{key}") : throw new VerificationException("INVALID_VERIFICATION_POLICY", $"Missing {location}.{key}.");
    private static string? OptionalScalar(IReadOnlyDictionary<string, YamlNode> values, string key, string location)
        => values.TryGetValue(key, out var node) ? Scalar(node, $"{location}.{key}") : null;
    private static YamlMappingNode AsMapping(YamlNode node, string location) => node as YamlMappingNode ?? throw new VerificationException("INVALID_VERIFICATION_POLICY", $"{location} must be a mapping.");
    private static YamlSequenceNode AsSequence(YamlNode node, string location) => node as YamlSequenceNode ?? throw new VerificationException("INVALID_VERIFICATION_POLICY", $"{location} must be a sequence.");
    private static string Scalar(YamlNode node, string location) => node is YamlScalarNode { Value: not null } scalar ? scalar.Value : throw new VerificationException("INVALID_VERIFICATION_POLICY", $"{location} must be a scalar.");
    private static IReadOnlyList<string> Scalars(YamlSequenceNode node, string location) => node.Children.Select((value, index) => Scalar(value, $"{location}[{index}]" )).ToArray();
    private static TimeSpan ParseTimeout(string value)
    {
        if (value.Length < 2) Invalid($"Invalid timeout {value}.");
        if (!int.TryParse(value[..^1], out var amount) || amount < 0) Invalid($"Invalid timeout {value}.");
        return value[^1] switch { 's' => TimeSpan.FromSeconds(amount), 'm' => TimeSpan.FromMinutes(amount), 'h' => TimeSpan.FromHours(amount), _ => throw new VerificationException("INVALID_VERIFICATION_POLICY", $"Invalid timeout {value}.") };
    }
    [DoesNotReturn] private static void Invalid(string message) => throw new VerificationException("INVALID_VERIFICATION_POLICY", message);
}

internal sealed record VerificationPolicy(
    IReadOnlyDictionary<string, VerificationCheck> Checks,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Contexts)
{
    public IReadOnlyList<string> ResolveContext(string context)
    {
        if (context != "default" && !VerificationPolicyParser.IsKnownContext(context))
            throw new VerificationException("INVALID_VERIFICATION_POLICY", $"Unknown verification context {context}.");
        return Contexts.TryGetValue(context, out var ids) && ids.Count > 0 ? ids : Contexts["default"];
    }
}

public sealed class VerificationException(string code, string message) : Exception(message) { public string Code { get; } = code; }