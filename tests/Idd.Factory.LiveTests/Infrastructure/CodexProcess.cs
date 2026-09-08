namespace Idd.Factory.LiveTests.Infrastructure;

internal sealed record CodexCommand(string Executable, IReadOnlyList<string> PrefixArguments);
public sealed record CodexRunResult(ProcessResult Process, string Model, string ReasoningEffort);

public sealed class CodexProcess(ProcessRunner processRunner)
{
    public async Task<CodexRunResult> RunAsync(LiveTestWorkspace workspace, string sandboxMode, bool factoryEnvironment, CancellationToken cancellationToken)
    {
        InstalledFactory.PrepareIsolatedCodexHome(workspace.CodexHomeDirectory);
        var model = Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_MODEL") ?? "gpt-5.6-luna";
        var reasoning = Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_REASONING_EFFORT") ?? "low";
        var timeoutText = Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_TIMEOUT_MINUTES");
        var timeout = int.TryParse(timeoutText, out var minutes) && minutes > 0 ? TimeSpan.FromMinutes(minutes) : TimeSpan.FromMinutes(20);
        var prompt = BuildPrompt(workspace.CaseDirectory);
        var command = ResolveCommand();
        var factoryCodexExecutable = factoryEnvironment ? PrepareSandboxFactoryCodexExecutable(command, workspace) : null;
        var environment = BuildEnvironment(workspace, model, reasoning, factoryEnvironment, factoryCodexExecutable);
        var arguments = BuildArguments(workspace, model, reasoning, sandboxMode);
        try
        {
            var result = await processRunner.RunAsync(command.Executable, command.PrefixArguments.Concat(arguments).ToArray(), workspace.WorkspaceDirectory,
                workspace.EventsPath, workspace.StderrPath, timeout, cancellationToken, prompt, environment, workspace.LastMessagePath);
            return new(result, model, reasoning);
        }
        finally
        {
            DeleteSandboxFactoryCodexFiles(factoryCodexExecutable);
        }
    }

    internal static CodexCommand ResolveCommand()
    {
        if (!OperatingSystem.IsWindows()) return new("codex", []);
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var directories = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var directory in directories)
        {
            var packageDirectory = Path.Combine(directory, "node_modules", "@openai", "codex", "node_modules");
            if (!Directory.Exists(packageDirectory)) continue;
            var native = Directory.EnumerateFiles(packageDirectory, "codex.exe", SearchOption.AllDirectories)
                .FirstOrDefault(candidate => candidate.Contains("@openai" + Path.DirectorySeparatorChar + "codex-win32-", StringComparison.OrdinalIgnoreCase));
            if (native is not null) return new(native, []);
        }
        foreach (var directory in directories)
        {
            var script = Path.Combine(directory, "node_modules", "@openai", "codex", "bin", "codex.js");
            var node = Path.Combine(directory, "node.exe");
            if (File.Exists(script) && File.Exists(node)) return new(node, [script]);
        }
        throw new FileNotFoundException("Could not locate the npm Codex CLI on PATH.");
    }

    private static IReadOnlyList<string> BuildArguments(LiveTestWorkspace workspace, string model, string reasoning, string sandboxMode) =>
    [
        "exec", "--json", "--ephemeral", "--ignore-rules",
        "--enable", "multi_agent", "--disable", "multi_agent_v2",
        "--disable", "apps", "--disable", "browser_use", "--disable", "code_mode_host",
        "-c", "agents.max_depth=2", "-c", "agents.max_threads=10",
        "-c", "mcp_servers={}", "-c", "approval_policy=never", "-c", $"model_reasoning_effort={reasoning}",
        "--model", model, "--sandbox", sandboxMode, "--cd", workspace.WorkspaceDirectory,
        "--output-last-message", workspace.LastMessagePath, "-"
    ];

    private static string BuildPrompt(string caseDirectory)
    {
        var task = File.ReadAllText(Path.Combine(caseDirectory, "task.md")).TrimEnd();
        var schemaPath = Path.Combine(caseDirectory, "final-response.schema.json");
        if (!File.Exists(schemaPath)) return task;
        var schema = File.ReadAllText(schemaPath).Trim();
        return $"""
            {task}

            The JSON Schema below applies only to your final response.
            Final response JSON Schema:
            {schema}
            """;
    }

    private static IReadOnlyDictionary<string, string> BuildEnvironment(LiveTestWorkspace workspace, string model, string reasoning, bool factoryEnvironment, string? factoryCodexExecutable)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["CODEX_HOME"] = workspace.CodexHomeDirectory };
        if (!factoryEnvironment) return environment;
        environment["IDD_FACTORY_MODEL"] = model;
        environment["IDD_FACTORY_REASONING_EFFORT"] = reasoning;
        environment["IDD_FACTORY_INHERIT_USER_SKILLS"] = "false";
        environment["IDD_FACTORY_CAPABILITY_PROFILE"] = "release-eval-controlled";
        if (!string.IsNullOrWhiteSpace(factoryCodexExecutable)) environment["IDD_FACTORY_CODEX_EXECUTABLE"] = factoryCodexExecutable;
        if (OperatingSystem.IsWindows())
            environment["PATH"] = Idd.Factory.Agents.CodexProcessEnvironment.PrepareSandboxCompatiblePath(Environment.GetEnvironmentVariable("PATH") ?? string.Empty, isWindows: true).Path;
        return environment;
    }

    private static string? PrepareSandboxFactoryCodexExecutable(CodexCommand command, LiveTestWorkspace workspace)
    {
        if (!OperatingSystem.IsWindows() || command.PrefixArguments.Count != 0) return null;
        var target = Path.Combine(workspace.WorkspaceDirectory, ".agents", "runtime", "idd-factory-codex.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(command.Executable, target, overwrite: true);
        var sourceCodeModeHost = Path.Combine(Path.GetDirectoryName(command.Executable)!, "codex-code-mode-host.exe");
        if (File.Exists(sourceCodeModeHost)) File.Copy(sourceCodeModeHost, Path.Combine(Path.GetDirectoryName(target)!, "codex-code-mode-host.exe"), overwrite: true);
        return target;
    }

    private static void DeleteSandboxFactoryCodexFiles(string? factoryCodexExecutable)
    {
        if (factoryCodexExecutable is null) return;
        if (File.Exists(factoryCodexExecutable)) File.Delete(factoryCodexExecutable);
        var codeModeHost = Path.Combine(Path.GetDirectoryName(factoryCodexExecutable)!, "codex-code-mode-host.exe");
        if (File.Exists(codeModeHost)) File.Delete(codeModeHost);
    }
}
