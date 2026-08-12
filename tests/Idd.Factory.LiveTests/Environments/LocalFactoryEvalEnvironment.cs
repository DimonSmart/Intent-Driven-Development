using System.Text.Json;
using Idd.Factory.LiveTests.Infrastructure;
using Idd.Factory.LiveTests.Models;

namespace Idd.Factory.LiveTests.Environments;

public sealed class LocalFactoryEvalEnvironment(ProcessRunner processRunner) : IFactoryEvalEnvironment
{
    public const string LaunchProfileEnvironmentVariable = "IDD_CODEX_LAUNCH_PROFILE";

    public static IReadOnlyList<string> LaunchProfileDiscoveryOrder { get; } =
    [
        "unrestricted-runtime-launch",
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

    public async Task<ProcessResult> RunCodexAsync(FactoryEvalWorkspace workspace, FactoryEvalOptions options, CancellationToken cancellationToken)
    {
        var prompt = BuildRunCodexPrompt(workspace.CaseDirectory);
        var factoryCodexExecutable = PrepareSandboxFactoryCodexExecutable(workspace);
        var environmentOverrides = BuildCodexEnvironment(
            Environment.GetEnvironmentVariable("PATH") ?? string.Empty,
            OperatingSystem.IsWindows(),
            factoryCodexExecutable);
        try
        {
            return await processRunner.RunAsync(CodexCommand.Executable, CodexCommand.PrefixArguments.Concat(BuildRunCodexArguments(workspace, options)).ToArray(), workspace.WorkspaceDirectory, workspace.EventsPath, workspace.StderrPath, options.Timeout, cancellationToken, prompt, environmentOverrides, workspace.LastMessagePath);
        }
        finally
        {
            if (factoryCodexExecutable is not null && File.Exists(factoryCodexExecutable)) File.Delete(factoryCodexExecutable);
        }
    }

    private string? PrepareSandboxFactoryCodexExecutable(FactoryEvalWorkspace workspace)
    {
        if (CodexCommand.PrefixArguments.Count != 0) return null;
        var target = Path.Combine(workspace.WorkspaceDirectory, ".agents", "runtime", "idd-factory-codex.exe");
        if (!File.Exists(target)) File.Copy(CodexCommand.Executable, target);
        return target;
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
        arguments.AddRange(["--model", options.Model, "--sandbox", profile.SandboxMode, "--cd", workspace.WorkspaceDirectory, "--output-last-message", workspace.LastMessagePath, "-"]);
        return arguments;
    }

    internal static CodexLaunchProfile ResolveLaunchProfile(string? name)
    {
        var effectiveName = string.IsNullOrWhiteSpace(name) ? LaunchProfileDiscoveryOrder[0] : name;
        return effectiveName switch
        {
            "unrestricted-runtime-launch" => new(effectiveName, IgnoreUserConfig: false, SandboxMode: "danger-full-access", WindowsSandbox: null),
            "isolated-workspace-write" => new(effectiveName, IgnoreUserConfig: true, SandboxMode: "workspace-write", WindowsSandbox: null),
            "configured-workspace-write" => new(effectiveName, IgnoreUserConfig: false, SandboxMode: "workspace-write", WindowsSandbox: null),
            "windows-unelevated-workspace-write" => new(effectiveName, IgnoreUserConfig: false, SandboxMode: "workspace-write", WindowsSandbox: "unelevated"),
            "windows-elevated-workspace-write" => new(effectiveName, IgnoreUserConfig: false, SandboxMode: "workspace-write", WindowsSandbox: "elevated"),
            _ => throw new InvalidOperationException($"Unknown Codex launch profile '{effectiveName}'. Expected one of: {string.Join(", ", LaunchProfileDiscoveryOrder)}.")
        };
    }

    internal static IReadOnlyDictionary<string, string> BuildCodexEnvironment(string path, bool isWindows, string? factoryCodexExecutable = null)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(factoryCodexExecutable))
            environment["IDD_FACTORY_CODEX_EXECUTABLE"] = factoryCodexExecutable;
        if (!isWindows) return environment;

        const char windowsPathSeparator = ';';
        var sandboxCompatiblePath = string.Join(windowsPathSeparator,
            path.Split(windowsPathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(directory => !directory.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)));
        environment["PATH"] = sandboxCompatiblePath;
        return environment;
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

internal sealed record CodexLaunchProfile(string Name, bool IgnoreUserConfig, string SandboxMode, string? WindowsSandbox);
