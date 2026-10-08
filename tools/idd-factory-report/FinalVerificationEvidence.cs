using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Idd.Factory.Report;

// Read-only interpretation of existing policy and trace evidence, not an execution engine.
internal sealed class FinalVerificationEvidence
{
    private sealed record Check(string Id, string? Command);
    internal sealed record Observation(CodexEvent Event, string Check, string Result);
    internal sealed record Evaluation(string Result, IReadOnlyList<Observation> Observations);

    private readonly bool configured;
    private readonly IReadOnlyList<Check> checks;
    private readonly string? workingDirectory;
    public string? Error { get; }
    public bool HasPolicy => configured;

    private FinalVerificationEvidence(bool configured, IReadOnlyList<Check> checks, string? error = null,
        string? workingDirectory = null)
    {
        this.configured = configured;
        this.checks = checks;
        Error = error;
        this.workingDirectory = workingDirectory;
    }

    public static FinalVerificationEvidence Load(string repository, string? workingDirectory = null)
    {
        var path = Path.Combine(repository, ".idd", "verification.yaml");
        workingDirectory ??= repository;
        if (!File.Exists(path)) return new(false, [], workingDirectory: workingDirectory);
        try
        {
            var yaml = new YamlStream();
            using var reader = File.OpenText(path);
            yaml.Load(reader);
            if (yaml.Documents.Count != 1) throw new FormatException("Expected one YAML document.");
            var root = Map(yaml.Documents[0].RootNode);
            Only(root, "version", "checks", "default", "direct", "subtask", "checkpoint", "final");
            if (Scalar(Required(root, "version")) != "1") throw new FormatException("Unsupported policy version.");
            var definitions = Map(Required(root, "checks"));
            if (definitions.Count == 0) throw new FormatException("No checks defined.");
            var all = new Dictionary<string, Check>(StringComparer.Ordinal);
            foreach (var (id, node) in definitions)
            {
                var check = Map(node);
                Only(check, "run", "instructions", "timeout", "confirmation");
                if (check.ContainsKey("run") == check.ContainsKey("instructions"))
                    throw new FormatException($"Check {id} must have exactly one of run or instructions.");
                var command = check.TryGetValue("run", out var run) ? Scalar(run) : null;
                if (command is null) _ = Scalar(check["instructions"]);
                if (check.TryGetValue("timeout", out var timeout) &&
                    !Regex.IsMatch(Scalar(timeout), @"^[1-9]\d*[smh]$"))
                    throw new FormatException($"Invalid timeout for {id}.");
                if (check.TryGetValue("confirmation", out var confirmation) &&
                    (command is null || Scalar(confirmation) != "required"))
                    throw new FormatException($"Invalid confirmation for {id}.");
                all.Add(id, new(id, command));
            }
            var defaults = Map(Required(root, "default"));
            Only(defaults, "use");
            var defaultIds = Uses(Required(defaults, "use"), all);
            foreach (var context in new[] { "direct", "subtask", "checkpoint", "final" })
            {
                if (!root.TryGetValue(context, out var node)) continue;
                ValidateContext(Map(node), all);
            }
            var selected = defaultIds;
            if (root.TryGetValue("final", out var finalNode))
            {
                var final = Map(finalNode);
                if (final.TryGetValue("use", out var use)) selected = Uses(use, all);
                else
                {
                    var rules = Sequence(final["rules"]).Select(Map).ToArray();
                    // No reliable integrated changed scope is available in every host trace.
                    if (rules.Any(rule => rule.ContainsKey("paths")))
                        throw new FormatException("Cannot resolve final path rules without authoritative integrated changed scope.");
                    selected = rules.Length == 0 ? defaultIds : Uses(rules[0]["use"], all);
                }
            }
            var selectedChecks = selected.Select(id => all[id]).ToArray();
            var commands = selectedChecks.Where(check => check.Command is not null)
                .Select(check => NormalizeCommand(check.Command)).ToArray();
            if (commands.Any(command => command is null) || commands.Distinct().Count() != commands.Length)
                throw new FormatException("Final commands are ambiguous or use unsupported shell syntax.");
            return new(true, selectedChecks, workingDirectory: workingDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            FormatException or YamlDotNet.Core.YamlException or ArgumentException)
        {
            return new(true, [], ex.Message);
        }
    }

    public Evaluation Evaluate(IEnumerable<CodexEvent> events, DateTimeOffset? since, DateTimeOffset? until = null)
    {
        if (since is null || Error is not null) return new("unavailable", []);
        var observations = Observe(events, since.Value, until);
        if (!configured)
        {
            // With no policy, only recognizable repository/platform verification commands count.
            var last = observations.GroupBy(item => item.Check, StringComparer.Ordinal).Select(group => group.Last()).ToArray();
            return new(last.Length == 0 ? "unavailable" : Combine(last.Select(item => item.Result)), observations);
        }
        var results = checks.Select(check => observations.LastOrDefault(item => item.Check == check.Id)?.Result ?? "unavailable");
        return new(Combine(results), observations);
    }

    private List<Observation> Observe(IEnumerable<CodexEvent> events, DateTimeOffset since, DateTimeOffset? until)
    {
        var result = new List<Observation>();
        foreach (var item in events.Where(item => item.Timestamp >= since && (until is null || item.Timestamp <= until))
                     .Where(item => item.ToolPhase is "completed" or "call")
                     .OrderBy(item => item.Timestamp).ThenBy(item => item.Ordinal))
        {
            if (item.ToolName is null || !(item.ToolName == "command_execution" ||
                    item.ToolName.Contains("exec_command", StringComparison.OrdinalIgnoreCase) ||
                    item.ToolName.Contains("local_shell", StringComparison.OrdinalIgnoreCase))) continue;
            if (item.CommandWorkingDirectory is not null && workingDirectory is not null &&
                NormalizeDirectory(item.CommandWorkingDirectory) != NormalizeDirectory(workingDirectory)) continue;
            var command = NormalizeCommand(item.ToolArguments);
            if (command is null) continue;
            var check = configured
                ? checks.SingleOrDefault(check => check.Command is not null && NormalizeCommand(check.Command) == command)
                : IsFallbackVerification(command) ? new Check(command, command) : null;
            if (check is null) continue;
            var status = item.ExitCode is not null && item.ExitCode != 0 || item.Status is "failed" or "error"
                ? "failed" : item.ExitCode == 0 ? "passed" : "unavailable";
            if (status == "passed" && IsDotnetTest(command) && !HasExecutedTests(item.ToolOutput))
                status = "unavailable";
            result.Add(new(item, check.Id, status));
        }
        return result;
    }

    private static string Combine(IEnumerable<string> results)
    {
        var values = results.ToArray();
        return values.Contains("failed") ? "failed" : values.Length > 0 && values.All(value => value == "passed")
            ? "passed" : "unavailable";
    }

    private static bool IsDotnetTest(string command) => command == "dotnet\0test" || command.StartsWith("dotnet\0test\0", StringComparison.Ordinal);
    private static bool IsFallbackVerification(string command) =>
        IsDotnetTest(command) || command == "dotnet\0build" || command.StartsWith("dotnet\0build\0", StringComparison.Ordinal) ||
        command is "npm\0test" or "cargo\0test" or "pytest" || command.StartsWith("pytest\0", StringComparison.Ordinal);

    private static bool HasExecutedTests(string? output) => output is not null &&
        Regex.IsMatch(output, @"Passed!\s*-.*\bFailed:\s*0\b.*\bPassed:\s*[1-9]\d*\b.*\bTotal:\s*[1-9]\d*\b");

    private static string NormalizeDirectory(string path) =>
        Path.TrimEndingDirectorySeparator(Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile
            ? uri.LocalPath : path);

    // Compare argv, never shell substrings: a quoted echo or a compound command isn't evidence.
    internal static string? NormalizeCommand(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        try
        {
            using var json = JsonDocument.Parse(input);
            var node = json.RootElement;
            if (node.ValueKind == JsonValueKind.Object)
            {
                if (!node.TryGetProperty("cmd", out var command) && !node.TryGetProperty("command", out command)) return null;
                node = command;
            }
            if (node.ValueKind == JsonValueKind.Array)
            {
                var values = node.EnumerateArray().ToArray();
                if (values.Any(arg => arg.ValueKind != JsonValueKind.String)) return null;
                var args = values.Select(arg => arg.GetString()!).ToArray();
                return NormalizeArgs(args);
            }
            if (node.ValueKind != JsonValueKind.String) return null;
            input = node.GetString();
        }
        catch (JsonException) { }
        var tokens = Tokenize(input!);
        return tokens is null ? null : NormalizeArgs(tokens);
    }

    private static string? NormalizeArgs(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0]) || args.Any(arg => arg.Contains('\0'))) return null;
        var executable = args[0].Replace('\\', '/').Split('/').Last();
        if (args.Count == 3 && executable is "sh" or "bash" or "zsh" && args[1] is "-c" or "-lc")
            return NormalizeCommand(args[2]);
        // Reject wrappers/options whose semantics have not been interpreted.
        if (executable is "sh" or "bash" or "zsh" or "cmd.exe" or "pwsh" or "powershell") return null;
        // Preserve explicit executable paths; only standard dotnet host paths are equivalent here.
        var normalized = args.ToArray();
        if (executable is "dotnet" or "dotnet.exe") normalized[0] = "dotnet";
        return string.Join('\0', normalized);
    }

    private static List<string>? Tokenize(string input)
    {
        var tokens = new List<string>();
        var token = new StringBuilder();
        char quote = '\0';
        var active = false;
        for (var i = 0; i < input.Length; i++)
        {
            var ch = input[i];
            if (ch is '\r' or '\n') return null;
            if (quote == '\'')
            {
                if (ch == '\'') quote = '\0'; else token.Append(ch);
                continue;
            }
            if (ch is '$' or '`') return null;
            if (ch == '\\' && quote == '"')
            {
                if (++i == input.Length) return null;
                var escaped = input[i];
                if (escaped is '$' or '`' or '"' or '\\') token.Append(escaped);
                else { token.Append('\\'); token.Append(escaped); }
                continue;
            }
            if (quote == '"')
            {
                if (ch == '"') quote = '\0'; else token.Append(ch);
                continue;
            }
            if (ch is '\'' or '"') { quote = ch; active = true; continue; }
            if (ch is ';' or '&' or '|' or '<' or '>' or '(' or ')' or '*' or '?' or '#') return null;
            if (char.IsWhiteSpace(ch))
            {
                if (active) { tokens.Add(token.ToString()); token.Clear(); active = false; }
            }
            else { token.Append(ch); active = true; }
        }
        if (quote != '\0') return null;
        if (active) tokens.Add(token.ToString());
        return tokens;
    }

    private static Dictionary<string, YamlNode> Map(YamlNode node)
    {
        if (node is not YamlMappingNode mapping) throw new FormatException("Expected YAML mapping.");
        return mapping.Children.ToDictionary(pair => Scalar(pair.Key), pair => pair.Value, StringComparer.Ordinal);
    }

    private static string Scalar(YamlNode node) => node is YamlScalarNode scalar && !string.IsNullOrWhiteSpace(scalar.Value)
        ? scalar.Value : throw new FormatException("Expected nonempty YAML scalar.");
    private static IEnumerable<YamlNode> Sequence(YamlNode node) => node is YamlSequenceNode sequence
        ? sequence.Children : throw new FormatException("Expected YAML sequence.");
    private static YamlNode Required(Dictionary<string, YamlNode> map, string key) => map.TryGetValue(key, out var node)
        ? node : throw new FormatException($"Missing {key}.");
    private static void Only(Dictionary<string, YamlNode> map, params string[] keys)
    {
        if (map.Keys.Any(key => !keys.Contains(key, StringComparer.Ordinal))) throw new FormatException("Unknown policy field.");
    }
    private static string[] Uses(YamlNode node, Dictionary<string, Check> checks)
    {
        var ids = Sequence(node).Select(Scalar).ToArray();
        if (ids.Length == 0 || ids.Distinct().Count() != ids.Length || ids.Any(id => !checks.ContainsKey(id)))
            throw new FormatException("Empty, repeated or unknown check references.");
        return ids;
    }
    private static void ValidateContext(Dictionary<string, YamlNode> context, Dictionary<string, Check> checks)
    {
        Only(context, "use", "rules");
        if (context.ContainsKey("use") == context.ContainsKey("rules"))
            throw new FormatException("A context must contain exactly one of use or rules.");
        if (context.TryGetValue("use", out var use)) { Uses(use, checks); return; }
        var rules = Sequence(context["rules"]).Select(Map).ToArray();
        for (var i = 0; i < rules.Length; i++)
        {
            var rule = rules[i];
            Only(rule, "use", "paths", "fallback");
            Uses(Required(rule, "use"), checks);
            if (rule.TryGetValue("paths", out var paths))
            {
                if (!Sequence(paths).Any()) throw new FormatException("Empty path rule.");
                foreach (var path in Sequence(paths)) _ = Scalar(path);
                if (rule.ContainsKey("fallback")) throw new FormatException("A path rule cannot be a fallback.");
            }
            else if (i != rules.Length - 1) throw new FormatException("Fallback must be the last rule.");
            if (rule.TryGetValue("fallback", out var fallback) && Scalar(fallback) != "true")
                throw new FormatException("Invalid fallback.");
        }
    }
}
