using System.Text.Json;
using Idd.Factory.LiveTests.Infrastructure;
using Idd.Factory.LiveTests.Models;

namespace Idd.Factory.LiveTests.Environments;

public sealed class LocalFactoryEvalEnvironment(ProcessRunner processRunner) : IFactoryEvalEnvironment
{
    public const string LaunchProfileEnvironmentVariable = "IDD_CODEX_LAUNCH_PROFILE";

    public static IReadOnlyList<string> LaunchProfileDiscoveryOrder { get; } =
    [
        "isolated-workspace-write",
        "configured-workspace-write",
        "windows-unelevated-workspace-write",
        "windows-elevated-workspace-write"
    ];

    public CodexCommand CodexCommand { get; } = CodexExecutableResolver.Resolve();
    public Task PrepareAsync(FactoryEvalWorkspace workspace, CancellationToken cancellationToken)
    {
        VerifyWorkspaceWriteAccess(workspace.WorkspaceDirectory);
        return Task.CompletedTask;
    }

    public Task<ProcessResult> RunCommandAsync(FactoryEvalWorkspace workspace, string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var name = Path.GetRandomFileName();
        return processRunner.RunAsync(executable, arguments, workspace.WorkspaceDirectory,
            Path.Combine(workspace.VerificationDirectory, name + ".stdout.log"),
            Path.Combine(workspace.VerificationDirectory, name + ".stderr.log"), TimeSpan.FromMinutes(2), cancellationToken);
    }

    public Task<ProcessResult> RunCodexAsync(FactoryEvalWorkspace workspace, FactoryEvalOptions options, CancellationToken cancellationToken)
    {
        var prompt = BuildRunCodexPrompt(workspace.CaseDirectory);
        var environmentOverrides = BuildCodexEnvironment(Environment.GetEnvironmentVariable("PATH") ?? string.Empty, OperatingSystem.IsWindows());
        return processRunner.RunAsync(CodexCommand.Executable, CodexCommand.PrefixArguments.Concat(BuildRunCodexArguments(workspace, options)).ToArray(), workspace.WorkspaceDirectory, workspace.EventsPath, workspace.StderrPath, options.Timeout, cancellationToken, prompt, environmentOverrides, workspace.LastMessagePath);
    }

    internal static string BuildRunCodexPrompt(string caseDirectory)
    {
        var task = File.ReadAllText(Path.Combine(caseDirectory, "task.md")).TrimEnd();
        var finalResponseSchema = File.ReadAllText(Path.Combine(caseDirectory, "final-response.schema.json")).Trim();
        return $"""
            {task}

            The JSON Schema below applies only to your final response. Intermediate progress messages must remain natural-language progress and must not use this response shape.

            Final response JSON Schema:
            {finalResponseSchema}
            """;
    }

    internal static IReadOnlyList<string> BuildRunCodexArguments(FactoryEvalWorkspace workspace, FactoryEvalOptions options, string? launchProfileName = null, string? userConfigPath = null)
    {
        var profile = ResolveLaunchProfile(launchProfileName ?? Environment.GetEnvironmentVariable(LaunchProfileEnvironmentVariable));
        var arguments = new List<string>
        {
            "exec", "--json"
        };
        if (!options.PersistSessionRollouts) arguments.Add("--ephemeral");
        if (profile.IgnoreUserConfig) arguments.Add("--ignore-user-config");
        arguments.AddRange([
            "--ignore-rules",
            "--enable", "multi_agent", "--disable", "multi_agent_v2",
            "--disable", "plugins", "--disable", "apps", "--disable", "browser_use", "--disable", "code_mode_host",
            "-c", "agents.max_depth=2", "-c", "agents.max_threads=10",
            "-c", "features.code_mode.direct_only_tool_namespaces=[\"multi_agent_v1\"]",
            "-c", "mcp_servers={}", "-c", "approval_policy=never", "-c", $"model_reasoning_effort={options.ReasoningEffort}"
        ]);
        if (!profile.IgnoreUserConfig)
            foreach (var serverName in FindConfiguredMcpServerNames(userConfigPath))
                arguments.AddRange(["-c", $"mcp_servers.{FormatTomlKey(serverName)}.enabled=false"]);
        if (profile.WindowsSandbox is not null) arguments.AddRange(["-c", $"windows.sandbox=\"{profile.WindowsSandbox}\""]);
        arguments.AddRange(["--model", options.Model, "--sandbox", "workspace-write", "--cd", workspace.WorkspaceDirectory, "--output-last-message", workspace.LastMessagePath, "-"]);
        return arguments;
    }

    internal static CodexLaunchProfile ResolveLaunchProfile(string? name)
    {
        var effectiveName = string.IsNullOrWhiteSpace(name) ? LaunchProfileDiscoveryOrder[0] : name;
        return effectiveName switch
        {
            "isolated-workspace-write" => new(effectiveName, IgnoreUserConfig: true, WindowsSandbox: null),
            "configured-workspace-write" => new(effectiveName, IgnoreUserConfig: false, WindowsSandbox: null),
            "windows-unelevated-workspace-write" => new(effectiveName, IgnoreUserConfig: false, WindowsSandbox: "unelevated"),
            "windows-elevated-workspace-write" => new(effectiveName, IgnoreUserConfig: false, WindowsSandbox: "elevated"),
            _ => throw new InvalidOperationException($"Unknown Codex launch profile '{effectiveName}'. Expected one of: {string.Join(", ", LaunchProfileDiscoveryOrder)}.")
        };
    }

    internal static IReadOnlyDictionary<string, string> BuildCodexEnvironment(string path, bool isWindows)
    {
        if (!isWindows) return new Dictionary<string, string>();

        const char windowsPathSeparator = ';';
        var sandboxCompatiblePath = string.Join(windowsPathSeparator,
            path.Split(windowsPathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(directory => !directory.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)));
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["PATH"] = sandboxCompatiblePath };
    }

    internal static IReadOnlyList<string> FindConfiguredMcpServerNames(string? configPath = null)
    {
        configPath ??= Path.Combine(
            Environment.GetEnvironmentVariable("CODEX_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"),
            "config.toml");
        if (!File.Exists(configPath)) return [];

        const string prefix = "[mcp_servers.";
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(configPath))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith(prefix, StringComparison.Ordinal) || !trimmed.EndsWith(']')) continue;
            var key = trimmed[prefix.Length..^1].Trim();
            var name = ParseTomlKey(key);
            if (name is not null) names.Add(name);
        }
        return names.Order(StringComparer.Ordinal).ToArray();
    }

    private static string? ParseTomlKey(string key)
    {
        if (key.Length == 0) return null;
        if (key[0] == '"' && key[^1] == '"')
        {
            try { return JsonSerializer.Deserialize<string>(key); }
            catch (JsonException) { return null; }
        }
        if (key[0] == '\'' && key[^1] == '\'') return key[1..^1];
        return key.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-') ? key : null;
    }

    private static string FormatTomlKey(string key)
        => key.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-')
            ? key
            : JsonSerializer.Serialize(key);

    private static void VerifyWorkspaceWriteAccess(string workspaceDirectory)
    {
        var temporaryPath = Path.Combine(workspaceDirectory, $".codex-workspace-write-check-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(temporaryPath, string.Empty);
            File.Delete(temporaryPath);
        }
        catch (Exception exception)
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
            throw new InvalidOperationException($"Workspace write check failed for '{workspaceDirectory}'. This indicates an ACL or workspace filesystem problem, not a Codex sandbox problem.", exception);
        }
    }
}

internal sealed record CodexLaunchProfile(string Name, bool IgnoreUserConfig, string? WindowsSandbox);
