using System.Text.Json;
using Idd.Factory.Domain;

namespace Idd.Factory.Agents;

internal static class CodexCommandProtocol
{
    public static string Sandbox(AgentExecutionProfile profile) => profile switch
    {
        AgentExecutionProfile.ReadOnly => "read-only",
        AgentExecutionProfile.WorkspaceWrite => "workspace-write",
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null)
    };

    public static IReadOnlyList<string> BuildArguments(
        AgentInvocation invocation,
        AgentExecutionConfiguration configuration,
        IReadOnlyList<string>? prefixArguments = null,
        bool? isWindows = null)
    {
        if (!string.IsNullOrWhiteSpace(configuration.WindowsSandbox)
            && configuration.WindowsSandbox is not ("elevated" or "unelevated"))
        {
            throw new ArgumentException(
                "WindowsSandbox must be 'elevated' or 'unelevated'.",
                nameof(configuration));
        }

        var arguments = new List<string>();
        if (prefixArguments is not null) arguments.AddRange(prefixArguments);
        arguments.AddRange(["exec", "--json", "--ephemeral", "--ignore-user-config"]);
        if (!string.IsNullOrWhiteSpace(configuration.Model))
            arguments.AddRange(["--model", configuration.Model]);
        if (!string.IsNullOrWhiteSpace(configuration.ReasoningEffort))
            arguments.AddRange(["-c", $"model_reasoning_effort={configuration.ReasoningEffort}"]);
        arguments.AddRange([
            "--sandbox", Sandbox(invocation.ExecutionProfile),
            "-c", "approval_policy=\"never\"",
            "-c", "mcp_servers={}"
        ]);
        if ((isWindows ?? OperatingSystem.IsWindows())
            && !string.IsNullOrWhiteSpace(configuration.WindowsSandbox))
        {
            arguments.AddRange(["-c", $"windows.sandbox=\"{configuration.WindowsSandbox}\""]);
        }
        arguments.AddRange([
            "--skip-git-repo-check",
            "-C", invocation.Workspace,
            "--output-last-message", invocation.SemanticOutputPath,
            "-"
        ]);
        return arguments;
    }

    public static string BuildBootstrapPrompt(
        AgentInvocation invocation,
        string skillInstructions)
    {
        if (string.IsNullOrWhiteSpace(skillInstructions))
            throw new ArgumentException(
                "Factory-selected skill instructions cannot be empty.",
                nameof(skillInstructions));
        return $"Factory-selected role instructions ({invocation.SkillName}):\n\n{skillInstructions.Trim()}\n\nAssigned Factory work:\n\n{invocation.Input}\n\n"
            + "Return only the human-readable Markdown requested by the selected skill. The backend captures the final response through the invocation-specific result channel; do not create or edit result artifacts yourself. "
            + "Do not return while a shell command is still running; wait for it to finish or terminate it first. "
            + "Run dotnet build and test commands with --disable-build-servers -m:1 to avoid leaving compiler or MSBuild workers behind. "
            + "Do not plan a subsequent Factory step, select another worker, or return runtime bookkeeping. "
            + "Do not mutate .idd/factory/current or .idd/intent. stdout is diagnostic only.";
    }

    public static IReadOnlyList<IncompleteCommandExecution> FindIncompleteCommandExecutions(string stdout)
    {
        var active = new Dictionary<string, IncompleteCommandExecution>(StringComparer.Ordinal);
        using var reader = new StringReader(stdout);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("type", out var eventType)
                    || !root.TryGetProperty("item", out var item)
                    || !item.TryGetProperty("id", out var idValue)
                    || !item.TryGetProperty("type", out var itemType)
                    || itemType.GetString() is not ("command_execution" or "local_shell_call"))
                    continue;

                var id = idValue.GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (eventType.GetString() == "item.started")
                {
                    var command = item.TryGetProperty("command", out var commandValue)
                        && commandValue.ValueKind == JsonValueKind.String
                        ? commandValue.GetString()
                        : null;
                    if (string.IsNullOrWhiteSpace(command)) command = null;
                    active[id] = new(id, command);
                }
                else if (eventType.GetString() == "item.completed")
                {
                    active.Remove(id);
                }
            }
            catch (JsonException)
            {
            }
        }
        return active.Values.ToArray();
    }

    public static string BuildIncompleteCommandDiagnostic(
        IReadOnlyList<IncompleteCommandExecution> commands)
    {
        const int maximumReportedCommands = 3;
        var descriptions = commands
            .Take(maximumReportedCommands)
            .Select(command =>
                $"[{BoundDiagnosticValue(command.Id, 128)}] {BoundDiagnosticValue(command.Command ?? "<command unavailable>", 512)}");
        var omitted = commands.Count > maximumReportedCommands
            ? $"; {commands.Count - maximumReportedCommands} more omitted"
            : string.Empty;
        return $"Detected {commands.Count} incomplete shell command(s) after the agent exited. "
            + $"Unmatched item.started event(s): {string.Join("; ", descriptions)}{omitted}. "
            + "Inspect stdout.log for the complete event stream. Temporary directory cleanup was skipped.";
    }

    public static string BuildCommandTimeoutDiagnostic(
        IncompleteCommandExecution command,
        TimeSpan timeout) =>
        $"Shell command [{BoundDiagnosticValue(command.Id, 128)}] exceeded the semantic-command timeout "
        + $"of {timeout:c}: {BoundDiagnosticValue(command.Command ?? "<command unavailable>", 512)}. "
        + "The worker process tree was terminated, partial results are not trusted, and the temporary directory was preserved.";

    public static string BuildCommandOverlapDiagnostic(CommandExecutionOverlap overlap) =>
        $"Shell command [{BoundDiagnosticValue(overlap.Started.Id, 128)}] started before "
        + $"[{BoundDiagnosticValue(overlap.Active.Id, 128)}] completed. Active command: "
        + $"{BoundDiagnosticValue(overlap.Active.Command ?? "<command unavailable>", 384)}. Later command: "
        + $"{BoundDiagnosticValue(overlap.Started.Command ?? "<command unavailable>", 384)}. "
        + "The worker process tree was terminated and partial results are not trusted.";

    public static AgentAttemptTelemetry BuildTelemetry(
        AgentInvocation invocation,
        AgentExecutionConfiguration? configuration = null,
        AgentCapabilityPolicy? capabilityPolicy = null,
        int inheritedUserSkillCount = 0,
        string skillSourceVersion = "unknown",
        string skillSource = "unknown",
        string? windowsSandbox = null,
        int windowsAppsPathEntriesRemoved = 0)
    {
        configuration ??= new();
        capabilityPolicy ??= AgentCapabilityPolicy.ProductionDefault;
        return new(
            invocation.Role,
            invocation.SkillName,
            "codex-cli",
            invocation.ExecutionProfile,
            "inline-skill",
            invocation.Input.Length,
            configuration.RequestedModel,
            configuration.RequestedReasoningEffort,
            "unknown",
            "unknown",
            skillSource,
            skillSourceVersion,
            capabilityPolicy.InheritUserSkills ? "inherit" : "isolated",
            CodexHomePreparation.CountProjectSkills(invocation.Workspace),
            inheritedUserSkillCount,
            capabilityPolicy.Profile,
            windowsSandbox,
            windowsAppsPathEntriesRemoved,
            (long)configuration.EffectiveCommandTimeout.TotalMilliseconds);
    }

    private static string BoundDiagnosticValue(string value, int maximumLength)
    {
        var normalized = string.Join(
            " ",
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength] + "...";
    }
}
