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
        var prompt = File.ReadAllText(Path.Combine(workspace.CaseDirectory, "task.md"));
        var environmentOverrides = BuildCodexEnvironment(Environment.GetEnvironmentVariable("PATH") ?? string.Empty, OperatingSystem.IsWindows());
        return processRunner.RunAsync(CodexCommand.Executable, CodexCommand.PrefixArguments.Concat(BuildRunCodexArguments(workspace, options)).ToArray(), workspace.WorkspaceDirectory, workspace.EventsPath, workspace.StderrPath, options.Timeout, cancellationToken, prompt, environmentOverrides);
    }

    internal static IReadOnlyList<string> BuildRunCodexArguments(FactoryEvalWorkspace workspace, FactoryEvalOptions options, string? launchProfileName = null)
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
            "-c", "agents.max_depth=2", "-c", "mcp_servers={}", "-c", "approval_policy=never", "-c", $"model_reasoning_effort={options.ReasoningEffort}"
        ]);
        if (profile.WindowsSandbox is not null) arguments.AddRange(["-c", $"windows.sandbox=\"{profile.WindowsSandbox}\""]);
        arguments.AddRange(["--model", options.Model, "--sandbox", "workspace-write", "--cd", workspace.WorkspaceDirectory, "--output-schema", Path.Combine(workspace.CaseDirectory, "final-response.schema.json"), "--output-last-message", workspace.LastMessagePath, "-"]);
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

        var sandboxCompatiblePath = string.Join(Path.PathSeparator,
            path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(directory => !directory.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)));
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["PATH"] = sandboxCompatiblePath };
    }

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
