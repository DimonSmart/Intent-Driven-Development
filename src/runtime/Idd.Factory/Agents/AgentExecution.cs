using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Idd.Factory.Domain;

namespace Idd.Factory.Agents;

public interface IAgentBackend
{
    Task<AgentRunHandle> StartAsync(AgentInvocation invocation, CancellationToken cancellationToken);
    Task<AgentProcessResult> WaitAsync(AgentRunHandle handle, CancellationToken cancellationToken);
    Task CancelAsync(AgentRunHandle handle, CancellationToken cancellationToken);
}

public sealed class CodexCliBackend : IAgentBackend
{
    private readonly Lazy<CodexCommand> command;
    private readonly string pluginRoot;
    private readonly AgentExecutionConfiguration executionConfiguration;
    private readonly AgentCapabilityPolicy capabilityPolicy;
    private readonly Dictionary<string, RunningProcess> processes = new(StringComparer.Ordinal);

    public CodexCliBackend(
        string pluginRoot,
        string? executable = null,
        AgentExecutionConfiguration? executionConfiguration = null,
        AgentCapabilityPolicy? capabilityPolicy = null)
    {
        this.pluginRoot = Path.GetFullPath(pluginRoot);
        this.executionConfiguration = executionConfiguration ?? new();
        this.capabilityPolicy = capabilityPolicy ?? AgentCapabilityPolicy.ProductionDefault;
        command = new Lazy<CodexCommand>(() => executable is null ? CodexExecutableResolver.Resolve() : new(executable, []));
    }

    public async Task<AgentRunHandle> StartAsync(AgentInvocation invocation, CancellationToken cancellationToken)
    {
        CodexCommand resolvedCommand;
        try { resolvedCommand = command.Value; }
        catch (FileNotFoundException exception)
        { throw new AgentProtocolException("AGENT_BACKEND_UNAVAILABLE", $"Codex CLI could not be located: {exception.Message}"); }

        var attemptDirectory = Path.GetDirectoryName(invocation.RawResultPath)!;
        var privateHome = PreparePrivateHome(invocation.RunId, invocation.AttemptId, invocation.SkillName);
        var codexHome = privateHome.Path;
        string skillInstructions;
        try { skillInstructions = ReadSkillInstructions(pluginRoot, invocation); }
        catch { CleanupPrivateHome(codexHome); throw; }
        var stdoutPath = Path.Combine(attemptDirectory, "stdout.log");
        var stderrPath = Path.Combine(attemptDirectory, "stderr.log");
        var sqliteDirectory = Path.Combine(codexHome, "state");
        Directory.CreateDirectory(sqliteDirectory);
        var start = new ProcessStartInfo(resolvedCommand.Executable)
        {
            WorkingDirectory = invocation.Workspace,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.Environment["CODEX_HOME"] = codexHome;
        start.Environment["CODEX_SQLITE_HOME"] = sqliteDirectory;
        start.Environment["TEMP"] = Path.Combine(codexHome, "tmp");
        start.Environment["TMP"] = Path.Combine(codexHome, "tmp");
        var pathPreparation = CodexProcessEnvironment.PrepareSandboxCompatiblePath(
            start.Environment["PATH"] ?? string.Empty,
            OperatingSystem.IsWindows());
        if (OperatingSystem.IsWindows())
            start.Environment["PATH"] = pathPreparation.Path;
        Directory.CreateDirectory(start.Environment["TEMP"]!);
        foreach (var argument in BuildArguments(invocation, executionConfiguration, resolvedCommand.PrefixArguments, OperatingSystem.IsWindows()))
            start.ArgumentList.Add(argument);
        await File.WriteAllTextAsync(
            Path.Combine(attemptDirectory, "attempt-telemetry.json"),
            JsonSerializer.Serialize(
                BuildTelemetry(
                    invocation,
                    executionConfiguration,
                    capabilityPolicy,
                    privateHome.InheritedSkillCount,
                    ReadSkillSourceVersion(),
                    pluginRoot,
                    OperatingSystem.IsWindows() ? executionConfiguration.WindowsSandbox : null,
                    pathPreparation.WindowsAppsPathEntriesRemoved),
                FactoryJson.Options),
            cancellationToken);
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        try { if (!process.Start()) throw new AgentProtocolException("AGENT_BACKEND_UNAVAILABLE", "Codex CLI did not start."); }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or FileNotFoundException)
        { CleanupPrivateHome(codexHome); throw new AgentProtocolException("AGENT_BACKEND_UNAVAILABLE", $"Codex CLI could not start: {exception.Message}"); }
        var stdout = CaptureAsync(process.StandardOutput, stdoutPath, cancellationToken);
        var stderr = CaptureAsync(process.StandardError, stderrPath, cancellationToken);
        var prompt = BuildBootstrapPrompt(invocation, skillInstructions);
        await process.StandardInput.WriteAsync(prompt.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken); process.StandardInput.Close();
        processes.Add(invocation.AttemptId, new(process, stdout, stderr, invocation.RawResultPath));
        return new(invocation.AttemptId, process.Id, invocation.AttemptId);
    }

    public async Task<AgentProcessResult> WaitAsync(AgentRunHandle handle, CancellationToken cancellationToken)
    {
        if (!processes.Remove(handle.BackendHandle, out var running))
            return new(-1, "", "The backend handle is not active in this runtime process.", false, false, AgentTerminationKind.TransportFailure);
        try
        {
            using var resultWatcherCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var processExit = running.Process.WaitForExitAsync(cancellationToken);
            var resultReady = WaitForCompleteResultAsync(running.ResultPath, resultWatcherCancellation.Token);
            var first = await Task.WhenAny(processExit, resultReady);
            var completedResultWasObserved = first == resultReady && await resultReady;
            if (!completedResultWasObserved && first == processExit)
            {
                await processExit;
                completedResultWasObserved = IsCompleteResult(running.ResultPath);
            }
            resultWatcherCancellation.Cancel();

            var killRequired = false;
            if (completedResultWasObserved && !running.Process.HasExited)
            {
                using var gracefulExit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                gracefulExit.CancelAfter(TimeSpan.FromSeconds(5));
                try { await running.Process.WaitForExitAsync(gracefulExit.Token); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                { killRequired = true; await CancelProcessAsync(running.Process); }
            }
            else
            {
                await processExit;
            }
            var stdout = await running.Stdout; var stderr = await running.Stderr;
            int? exitCode = running.Process.HasExited ? running.Process.ExitCode : null;
            var termination = killRequired
                ? AgentTerminationKind.ForcedAfterResult
                : exitCode == 0 ? AgentTerminationKind.CleanExit : AgentTerminationKind.TransportFailure;
            return new AgentProcessResult(exitCode, stdout, stderr, completedResultWasObserved, killRequired, termination);
        }
        catch (OperationCanceledException)
        {
            await CancelProcessAsync(running.Process);
            string stdout = ""; string stderr = "";
            try { stdout = await running.Stdout; stderr = await running.Stderr; } catch (OperationCanceledException) { }
            return new(running.Process.HasExited ? running.Process.ExitCode : null, stdout, stderr, IsCompleteResult(running.ResultPath), true, AgentTerminationKind.Cancelled);
        }
        finally { TryCleanupPrivateHome(running.Process.StartInfo.Environment["CODEX_HOME"]!); running.Process.Dispose(); }
    }

    public async Task CancelAsync(AgentRunHandle handle, CancellationToken cancellationToken)
    {
        if (!processes.Remove(handle.BackendHandle, out var running)) return;
        try { await CancelProcessAsync(running.Process); await Task.WhenAll(running.Stdout, running.Stderr); }
        finally { TryCleanupPrivateHome(running.Process.StartInfo.Environment["CODEX_HOME"]!); running.Process.Dispose(); }
    }

    private PrivateHome PreparePrivateHome(string runId, string attemptId, string selectedSkill)
    {
        var home = Path.Combine(Path.GetTempPath(), "idd-factory", "codex-private", runId, attemptId);
        CleanupPrivateHome(home);
        Directory.CreateDirectory(home);
        var configuredHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        var sourceHome = string.IsNullOrWhiteSpace(configuredHome)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex")
            : configuredHome;
        var sourceAuth = Path.Combine(sourceHome, "auth.json");
        if (File.Exists(sourceAuth)) File.Copy(sourceAuth, Path.Combine(home, "auth.json"), overwrite: true);
        var inheritedSkillCount = 0;
        var sourceSkills = Path.Combine(sourceHome, "skills");
        if (capabilityPolicy.InheritUserSkills && Directory.Exists(sourceSkills))
        {
            foreach (var sourceSkill in Directory.EnumerateDirectories(sourceSkills))
            {
                if (!ShouldInheritSkill(Path.GetFileName(sourceSkill), selectedSkill)) continue;
                CopyDirectory(sourceSkill, Path.Combine(home, "skills", Path.GetFileName(sourceSkill)));
                inheritedSkillCount++;
            }
        }
        return new(home, inheritedSkillCount);
    }

    internal static string ReadSkillInstructions(string pluginRoot, AgentInvocation invocation)
    {
        ValidateSkillIdentity(pluginRoot, invocation);
        var path = Path.Combine(Path.GetFullPath(pluginRoot), "skills", invocation.SkillName, "SKILL.md");
        var instructions = File.ReadAllText(path).Trim();
        if (instructions.Length == 0)
            throw new AgentProtocolException("FACTORY_SKILL_UNAVAILABLE", $"Factory skill {invocation.SkillName} is empty in the configured plugin.");
        return instructions;
    }

    internal static void ValidateSkillIdentity(string pluginRoot, AgentInvocation invocation)
    {
        var skillName = invocation.SkillName;
        var source = Path.GetFullPath(Path.Combine(pluginRoot, "skills", skillName));
        var skillsRoot = Path.GetFullPath(Path.Combine(pluginRoot, "skills")) + Path.DirectorySeparatorChar;
        if (!source.StartsWith(skillsRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(source, "SKILL.md")))
            throw new AgentProtocolException("FACTORY_SKILL_UNAVAILABLE", $"Factory skill {skillName} is not available in the configured plugin.");
        var projectSkill = Path.Combine(invocation.Workspace, ".agents", "skills", skillName, "SKILL.md");
        if (File.Exists(projectSkill))
            throw new AgentProtocolException("FACTORY_SKILL_COLLISION", $"Project-local skill {skillName} conflicts with the runtime-selected Factory skill.");
    }

    internal static bool ShouldInheritSkill(string candidateSkill, string selectedFactorySkill) =>
        !string.Equals(candidateSkill, selectedFactorySkill, StringComparison.OrdinalIgnoreCase);

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var directory in Directory.EnumerateDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    internal static string Sandbox(AgentExecutionProfile profile) => profile switch
    {
        AgentExecutionProfile.ReadOnly => "read-only",
        AgentExecutionProfile.WorkspaceWrite => "workspace-write",
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null)
    };

    internal static IReadOnlyList<string> BuildArguments(
        AgentInvocation invocation,
        AgentExecutionConfiguration configuration,
        IReadOnlyList<string>? prefixArguments = null,
        bool? isWindows = null)
    {
        if (!string.IsNullOrWhiteSpace(configuration.WindowsSandbox) && configuration.WindowsSandbox is not ("elevated" or "unelevated"))
            throw new ArgumentException("WindowsSandbox must be 'elevated' or 'unelevated'.", nameof(configuration));
        var arguments = new List<string>();
        if (prefixArguments is not null) arguments.AddRange(prefixArguments);
        arguments.AddRange(["exec", "--json", "--ephemeral", "--ignore-user-config"]);
        if (!string.IsNullOrWhiteSpace(configuration.Model)) arguments.AddRange(["--model", configuration.Model]);
        if (!string.IsNullOrWhiteSpace(configuration.ReasoningEffort)) arguments.AddRange(["-c", $"model_reasoning_effort={configuration.ReasoningEffort}"]);
        arguments.AddRange(["--sandbox", Sandbox(invocation.ExecutionProfile), "-c", "approval_policy=\"never\"", "-c", "mcp_servers={}"]);
        if ((isWindows ?? OperatingSystem.IsWindows()) && !string.IsNullOrWhiteSpace(configuration.WindowsSandbox))
            arguments.AddRange(["-c", $"windows.sandbox=\"{configuration.WindowsSandbox}\""]);
        arguments.AddRange(["--skip-git-repo-check", "-C", invocation.Workspace, "--output-last-message", invocation.RawResultPath, "-"]);
        return arguments;
    }

    internal static string BuildBootstrapPrompt(AgentInvocation invocation, string skillInstructions)
    {
        if (string.IsNullOrWhiteSpace(skillInstructions))
            throw new ArgumentException("Factory-selected skill instructions cannot be empty.", nameof(skillInstructions));
        return $"Factory-selected role instructions ({invocation.SkillName}):\n\n{skillInstructions.Trim()}\n\nAssigned Factory work:\n\n{invocation.Input}\n\n" +
            $"Return only one semantic JSON object as your final response. The backend captures it through the invocation-specific result channel; do not create or edit result artifacts yourself. " +
            "Return outcome and only the outcome-specific fields defined by the selected skill. Do not return protocol or schema versions, run ID, attempt ID, role, capability, work-item ID, skill, execution profile, result path, or other runtime bookkeeping. " +
            "Do not mutate .idd/factory/current or .idd/intent. stdout is diagnostic only.";
    }

    internal static AgentAttemptTelemetry BuildTelemetry(
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
        return new(invocation.Role, invocation.SkillName, "codex-cli", invocation.ExecutionProfile, "inline-skill", invocation.Input.Length,
            configuration.RequestedModel, configuration.RequestedReasoningEffort, "unknown", "unknown", skillSource, skillSourceVersion,
            capabilityPolicy.InheritUserSkills ? "inherit" : "isolated", CountProjectSkills(invocation.Workspace), inheritedUserSkillCount, capabilityPolicy.Profile,
            windowsSandbox, windowsAppsPathEntriesRemoved);
    }

    private string ReadSkillSourceVersion()
    {
        var path = Path.Combine(pluginRoot, "skills", "idd-factory-run", "references", "methodology-version.json");
        if (!File.Exists(path)) return "unknown";
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("methodologyVersion", out var value) ? value.GetString() ?? "unknown" : "unknown";
        }
        catch (JsonException) { return "unknown"; }
    }

    private static int CountProjectSkills(string workspace)
    {
        var root = Path.Combine(workspace, ".agents", "skills");
        return Directory.Exists(root) ? Directory.EnumerateDirectories(root).Count(path => File.Exists(Path.Combine(path, "SKILL.md"))) : 0;
    }

    private static void CleanupPrivateHome(string home)
    {
        if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
    }

    private static void TryCleanupPrivateHome(string home)
    {
        try { CleanupPrivateHome(home); }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException) { }
    }

    private static async Task<string> CaptureAsync(StreamReader reader, string path, CancellationToken cancellationToken)
    { var text = await reader.ReadToEndAsync(cancellationToken); await File.WriteAllTextAsync(path, text, cancellationToken); return text; }
    private static async Task<bool> WaitForCompleteResultAsync(string path, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (File.Exists(path))
            {
                try
                {
                    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
                    if (document.RootElement.ValueKind == JsonValueKind.Object) return true;
                }
                catch (JsonException) { }
                catch (IOException) { }
            }
            await Task.Delay(100, cancellationToken);
        }
        return false;
    }
    private static bool IsCompleteResult(string path)
    {
        if (!File.Exists(path)) return false;
        try { using var document = JsonDocument.Parse(File.ReadAllText(path)); return document.RootElement.ValueKind == JsonValueKind.Object; }
        catch (Exception exception) when (exception is JsonException or IOException) { return false; }
    }
    private static async Task CancelProcessAsync(Process process)
    { if (process.HasExited) return; try { process.CloseMainWindow(); await Task.Delay(1500); } catch { } if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); }
    private sealed record RunningProcess(Process Process, Task<string> Stdout, Task<string> Stderr, string ResultPath);
    private sealed record PrivateHome(string Path, int InheritedSkillCount);
}

public sealed record CodexCommand(string Executable, IReadOnlyList<string> PrefixArguments);

public static class CodexExecutableResolver
{
    public const string ExecutableEnvironmentVariable = "IDD_FACTORY_CODEX_EXECUTABLE";

    public static CodexCommand Resolve()
    {
        var configured = Environment.GetEnvironmentVariable(ExecutableEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!File.Exists(configured))
                throw new FileNotFoundException($"The executable configured by {ExecutableEnvironmentVariable} does not exist.", configured);
            return new(Path.GetFullPath(configured), []);
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (OperatingSystem.IsWindows())
        {
            var candidates = new[]
            {
                Environment.GetEnvironmentVariable("APPDATA"),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE") ?? string.Empty, "AppData", "Roaming"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Roaming")
            };
            foreach (var applicationData in candidates.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var npm = Path.Combine(applicationData!, "npm");
                if (Directory.Exists(npm) && !path.Split(Path.PathSeparator).Contains(npm, StringComparer.OrdinalIgnoreCase))
                    path = string.IsNullOrEmpty(path) ? npm : path + Path.PathSeparator + npm;
            }
        }
        return ResolveFromPath(path, OperatingSystem.IsWindows());
    }

    public static CodexCommand ResolveFromPath(string path, bool isWindows)
    {
        if (!isWindows) return new("codex", []);
        var directories = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var directory in directories)
        {
            var direct = Path.Combine(directory, "codex.exe"); if (File.Exists(direct)) return new(direct, []);
            var packages = Path.Combine(directory, "node_modules", "@openai", "codex", "node_modules");
            if (!Directory.Exists(packages)) continue;
            var native = Directory.EnumerateFiles(packages, "codex.exe", SearchOption.AllDirectories).FirstOrDefault(candidate => candidate.Contains("@openai" + Path.DirectorySeparatorChar + "codex-win32-", StringComparison.OrdinalIgnoreCase));
            if (native is not null) return new(native, []);
        }
        var nodes = directories.Select(directory => Path.Combine(directory, "node.exe")).Where(File.Exists).ToArray();
        foreach (var directory in directories)
        {
            var script = Path.Combine(directory, "node_modules", "@openai", "codex", "bin", "codex.js");
            if (!File.Exists(script)) continue; var node = File.Exists(Path.Combine(directory, "node.exe")) ? Path.Combine(directory, "node.exe") : nodes.FirstOrDefault();
            if (node is not null) return new(node, [script]);
        }
        throw new FileNotFoundException("Could not locate a native Codex executable or npm Codex CLI with node.exe on PATH.");
    }
}

internal sealed class ProtectedArtifactGuard
{
    private readonly IReadOnlyDictionary<string, string> hashes;
    private readonly IReadOnlyList<string> roots;

    private ProtectedArtifactGuard(IReadOnlyDictionary<string, string> hashes, IReadOnlyList<string> roots)
    {
        this.hashes = hashes;
        this.roots = roots;
    }

    public static ProtectedArtifactGuard Capture(AgentInvocation invocation)
    {
        var attemptDirectory = Path.GetDirectoryName(invocation.RawResultPath)!;
        var current = Directory.GetParent(Directory.GetParent(attemptDirectory)!.FullName)!.FullName;
        var roots = new[] { Path.Combine(current, "state.json"), Path.Combine(current, "request.md"), Path.Combine(current, "run-context.md"), Path.Combine(current, "work-items"), Path.Combine(current, "clarifications"), Path.Combine(invocation.Workspace, ".idd", "intent"), Path.Combine(invocation.Workspace, ".idd", "verification.yaml") };
        return new(Enumerate(roots).ToDictionary(path => path, Hash, StringComparer.OrdinalIgnoreCase), roots);
    }

    public void ValidateUnchanged()
    {
        var current = Enumerate(roots).ToDictionary(path => path, Hash, StringComparer.OrdinalIgnoreCase);
        foreach (var path in hashes.Keys.Union(current.Keys, StringComparer.OrdinalIgnoreCase))
        {
            if (hashes.TryGetValue(path, out var before) && current.TryGetValue(path, out var after) && before == after) continue;
            throw new AgentProtocolException(IsProductArtifact(path) ? "WORKER_CHANGED_PRODUCT_INTENT" : "WORKER_CHANGED_RUNNER_STATE", $"Worker changed protected artifact {path}.");
        }
    }

    private static IEnumerable<string> Enumerate(IEnumerable<string> roots) => roots.SelectMany(root => File.Exists(root) ? [root] : Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories) : []);
    private static string Hash(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
    private static bool IsProductArtifact(string path) => path.Contains($"{Path.DirectorySeparatorChar}intent{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) || path.EndsWith("verification.yaml", StringComparison.OrdinalIgnoreCase);
}

public sealed class AgentProtocolException(string code, string message) : Exception(message) { public string Code { get; } = code; }
