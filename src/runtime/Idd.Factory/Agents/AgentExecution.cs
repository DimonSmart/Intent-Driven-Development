using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Verification;

namespace Idd.Factory.Agents;

public interface IAgentBackend
{
    Task<AgentRunHandle> StartAsync(AgentInvocation invocation, CancellationToken cancellationToken);
    Task<AgentProcessResult> WaitAsync(AgentRunHandle handle, CancellationToken cancellationToken);
    Task CancelAsync(AgentRunHandle handle, CancellationToken cancellationToken);
}

public sealed class CodexCliBackend : IAgentBackend
{
    private readonly CodexCommand command;
    private readonly Dictionary<string, RunningProcess> processes = new(StringComparer.Ordinal);

    public CodexCliBackend(string? executable = null) => command = executable is null ? CodexExecutableResolver.Resolve() : new(executable, []);

    public async Task<AgentRunHandle> StartAsync(AgentInvocation invocation, CancellationToken cancellationToken)
    {
        var attemptDirectory = Path.GetDirectoryName(invocation.ResultPath)!;
        CleanupStalePrivateHomes(invocation.Workspace);
        var codexHome = PreparePrivateHome(attemptDirectory);
        var stdoutPath = Path.Combine(attemptDirectory, "stdout.log");
        var stderrPath = Path.Combine(attemptDirectory, "stderr.log");
        var sqliteDirectory = Path.Combine(codexHome, "state");
        Directory.CreateDirectory(sqliteDirectory);
        var start = new ProcessStartInfo(command.Executable)
        {
            WorkingDirectory = invocation.Workspace,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.Environment["CODEX_HOME"] = codexHome;
        start.Environment["CODEX_SQLITE_HOME"] = sqliteDirectory;
        start.Environment["TEMP"] = Path.Combine(codexHome, "tmp");
        start.Environment["TMP"] = Path.Combine(codexHome, "tmp");
        Directory.CreateDirectory(start.Environment["TEMP"]!);
        foreach (var prefix in command.PrefixArguments) start.ArgumentList.Add(prefix);
        start.ArgumentList.Add("exec"); start.ArgumentList.Add("--json"); start.ArgumentList.Add("--ephemeral"); start.ArgumentList.Add("--ignore-user-config");
        start.ArgumentList.Add("--sandbox"); start.ArgumentList.Add(invocation.Role == "implementer" ? "workspace-write" : "read-only");
        start.ArgumentList.Add("-c"); start.ArgumentList.Add("approval_policy=\"never\"");
        start.ArgumentList.Add("-c"); start.ArgumentList.Add("mcp_servers={}");
        if (OperatingSystem.IsWindows()) { start.ArgumentList.Add("-c"); start.ArgumentList.Add("windows.sandbox=\"unelevated\""); }
        start.ArgumentList.Add("--skip-git-repo-check"); start.ArgumentList.Add("-C"); start.ArgumentList.Add(invocation.Workspace);
        start.ArgumentList.Add("--output-last-message"); start.ArgumentList.Add(invocation.ResultPath); start.ArgumentList.Add("-");
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        try { if (!process.Start()) throw new AgentProtocolException("AGENT_BACKEND_UNAVAILABLE", "Codex CLI did not start."); }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or FileNotFoundException)
        { CleanupPrivateHome(codexHome); throw new AgentProtocolException("AGENT_BACKEND_UNAVAILABLE", $"Codex CLI could not start: {exception.Message}"); }
        var stdout = CaptureAsync(process.StandardOutput, stdoutPath, cancellationToken);
        var stderr = CaptureAsync(process.StandardError, stderrPath, cancellationToken);
        await process.StandardInput.WriteAsync(invocation.Prompt.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken); process.StandardInput.Close();
        processes.Add(invocation.AttemptId, new(process, stdout, stderr));
        return new(invocation.AttemptId, process.Id, invocation.AttemptId);
    }

    public async Task<AgentProcessResult> WaitAsync(AgentRunHandle handle, CancellationToken cancellationToken)
    {
        if (!processes.Remove(handle.BackendHandle, out var running))
            return new(-1, "", "The backend handle is not active in this runtime process.", false);
        try
        {
            await running.Process.WaitForExitAsync(cancellationToken);
            var stdout = await running.Stdout; var stderr = await running.Stderr;
            return new AgentProcessResult(running.Process.ExitCode, stdout, stderr, false);
        }
        catch (OperationCanceledException) { await CancelProcessAsync(running.Process); await Task.WhenAll(running.Stdout, running.Stderr); throw; }
        finally { CleanupPrivateHome(running.Process.StartInfo.Environment["CODEX_HOME"]!); running.Process.Dispose(); }
    }

    public async Task CancelAsync(AgentRunHandle handle, CancellationToken cancellationToken)
    {
        if (!processes.Remove(handle.BackendHandle, out var running)) return;
        try { await CancelProcessAsync(running.Process); await Task.WhenAll(running.Stdout, running.Stderr); }
        finally { CleanupPrivateHome(running.Process.StartInfo.Environment["CODEX_HOME"]!); running.Process.Dispose(); }
    }

    private static string PreparePrivateHome(string attemptDirectory)
    {
        var home = Path.Combine(attemptDirectory, "codex-private");
        Directory.CreateDirectory(home);
        var configuredHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        var sourceHome = string.IsNullOrWhiteSpace(configuredHome)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex")
            : configuredHome;
        var sourceAuth = Path.Combine(sourceHome, "auth.json");
        if (File.Exists(sourceAuth)) File.Copy(sourceAuth, Path.Combine(home, "auth.json"), overwrite: true);
        return home;
    }

    private static void CleanupStalePrivateHomes(string workspace)
    {
        var attempts = Path.Combine(workspace, ".idd", "factory", "current", "attempts");
        if (!Directory.Exists(attempts)) return;
        foreach (var home in Directory.EnumerateDirectories(attempts, "codex-private", SearchOption.AllDirectories).ToArray())
            Directory.Delete(home, recursive: true);
    }

    private static void CleanupPrivateHome(string home)
    {
        if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
    }

    private static async Task<string> CaptureAsync(StreamReader reader, string path, CancellationToken cancellationToken)
    { var text = await reader.ReadToEndAsync(cancellationToken); await File.WriteAllTextAsync(path, text, cancellationToken); return text; }
    private static async Task CancelProcessAsync(Process process)
    { if (process.HasExited) return; try { process.CloseMainWindow(); await Task.Delay(1500); } catch { } if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); }
    private sealed record RunningProcess(Process Process, Task<string> Stdout, Task<string> Stderr);
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

public sealed class AgentExecutor(IAgentBackend backend, AgentResultValidator validator)
{
    public async Task<AgentResultEnvelope> ExecuteAsync(AgentInvocation invocation, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(invocation.ResultPath)!);
        var invocationPath = Path.Combine(Path.GetDirectoryName(invocation.ResultPath)!, "invocation.json");
        await File.WriteAllTextAsync(invocationPath, JsonSerializer.Serialize(invocation, FactoryJson.Options), cancellationToken);
        var protectedArtifacts = ProtectedArtifactSnapshot.Capture(invocation);
        var handle = await backend.StartAsync(invocation, cancellationToken);
        var process = await backend.WaitAsync(handle, cancellationToken);
        if (process.ExitCode != 0) throw new AgentProtocolException("AGENT_TRANSPORT_FAILURE", $"Agent exited with {process.ExitCode}: {process.Stderr}");
        protectedArtifacts.ValidateUnchanged();
        if (!File.Exists(invocation.ResultPath)) throw new AgentProtocolException("MISSING_AGENT_RESULT", "Agent did not produce result.json.");
        AgentResultEnvelope? result;
        try { result = JsonSerializer.Deserialize<AgentResultEnvelope>(await File.ReadAllTextAsync(invocation.ResultPath, cancellationToken), FactoryJson.Options); }
        catch (JsonException exception) { throw new AgentProtocolException("MALFORMED_AGENT_RESULT", exception.Message); }
        var validated = validator.Validate(invocation, result);
        return validated.Metrics is null && TryReadUsage(process.Stdout) is { } usage ? validated with { Metrics = usage } : validated;
    }

    private static JsonElement? TryReadUsage(string stdout)
    {
        JsonElement? usage = null;
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.TryGetProperty("usage", out var direct)) usage = direct.Clone();
                else if (document.RootElement.TryGetProperty("payload", out var payload) && payload.TryGetProperty("usage", out var nested)) usage = nested.Clone();
            }
            catch (JsonException) { }
        }
        return usage;
    }
}

internal sealed class ProtectedArtifactSnapshot
{
    private readonly IReadOnlyDictionary<string, string> hashes;
    private readonly string? readOnlyWorkspaceHash;
    private readonly string workspace;
    private ProtectedArtifactSnapshot(IReadOnlyDictionary<string, string> hashes, string workspace, string? readOnlyWorkspaceHash) { this.hashes = hashes; this.workspace = workspace; this.readOnlyWorkspaceHash = readOnlyWorkspaceHash; }

    public static ProtectedArtifactSnapshot Capture(AgentInvocation invocation)
    {
        var attemptDirectory = Path.GetDirectoryName(invocation.ResultPath)!;
        var current = Directory.GetParent(Directory.GetParent(attemptDirectory)!.FullName)!.FullName;
        var roots = new[] { Path.Combine(current, "state.json"), Path.Combine(current, "request.md"), Path.Combine(current, "run-context.md"), Path.Combine(current, "work-items"), Path.Combine(current, "clarifications"), Path.Combine(invocation.Workspace, ".idd", "intent"), Path.Combine(invocation.Workspace, ".idd", "verification.yaml") };
        var workspaceHash = invocation.Role == "implementer" ? null : new WorkspaceFingerprinter().Compute(invocation.Workspace);
        return new(Enumerate(roots).ToDictionary(path => path, Hash, StringComparer.OrdinalIgnoreCase), invocation.Workspace, workspaceHash);
    }

    public void ValidateUnchanged()
    {
        if (readOnlyWorkspaceHash is not null && new WorkspaceFingerprinter().Compute(workspace) != readOnlyWorkspaceHash)
            throw new AgentProtocolException("READ_ONLY_WORKER_CHANGED_WORKSPACE", "A read-only semantic worker changed the product workspace.");
        var roots = hashes.Keys.Select(path => File.Exists(path) ? path : Directory.Exists(path) ? path : path).ToArray();
        foreach (var (path, hash) in hashes)
            if (!File.Exists(path) || Hash(path) != hash) throw new AgentProtocolException(path.Contains($"{Path.DirectorySeparatorChar}intent{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) || path.EndsWith("verification.yaml", StringComparison.OrdinalIgnoreCase) ? "WORKER_CHANGED_PRODUCT_INTENT" : "WORKER_CHANGED_RUNNER_STATE", $"Worker changed protected artifact {path}.");
        // Detect new protected files, especially work-item or intent additions.
        var parents = hashes.Keys.Select(Path.GetDirectoryName).Where(x => x is not null).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var parent in parents)
            if (Directory.Exists(parent!) && Directory.EnumerateFiles(parent!, "*", SearchOption.TopDirectoryOnly).Any(path => !hashes.ContainsKey(path) && (parent!.EndsWith("work-items", StringComparison.OrdinalIgnoreCase) || parent.EndsWith("intent", StringComparison.OrdinalIgnoreCase))))
                throw new AgentProtocolException(parent!.EndsWith("intent", StringComparison.OrdinalIgnoreCase) ? "WORKER_CHANGED_PRODUCT_INTENT" : "WORKER_CHANGED_RUNNER_STATE", $"Worker added a protected artifact under {parent}.");
    }

    private static IEnumerable<string> Enumerate(IEnumerable<string> roots) => roots.SelectMany(root => File.Exists(root) ? [root] : Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories) : []);
    private static string Hash(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
}

public sealed class AgentResultValidator
{
    private static readonly IReadOnlyDictionary<string, HashSet<string>> Outcomes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
    {
        ["task-decomposer"] = ["ready", "intent-required", "needs-clarification", "focused-handoff", "blocked"],
        ["implementer"] = ["completed", "blocked", "needs-replan", "intent-required"],
        ["checkpoint-reviewer"] = ["approved", "needs-fix", "needs-replan", "blocked", "intent-required"],
        ["final-reviewer"] = ["approved", "needs-fix", "needs-replan", "blocked", "intent-required"],
        ["factory-replanner"] = ["replan-proposed", "intent-required", "needs-clarification", "blocked"]
    };

    public AgentResultEnvelope Validate(AgentInvocation invocation, AgentResultEnvelope? result)
    {
        if (result is null) throw new AgentProtocolException("MALFORMED_AGENT_RESULT", "Result is null.");
        if (result.ProtocolVersion != AgentInvocation.CurrentProtocolVersion) throw new AgentProtocolException("UNSUPPORTED_AGENT_PROTOCOL", $"Unsupported protocol {result.ProtocolVersion}.");
        if (result.RunId != invocation.RunId || result.AttemptId != invocation.AttemptId || result.Role != invocation.Role)
            throw new AgentProtocolException("AGENT_RESULT_IDENTITY_MISMATCH", "Result identity does not match invocation.");
        if (!Outcomes.TryGetValue(result.Role, out var outcomes) || !outcomes.Contains(result.Outcome))
            throw new AgentProtocolException("UNSUPPORTED_AGENT_OUTCOME", $"Outcome {result.Outcome} is invalid for {result.Role}.");
        return result;
    }
}

public sealed class AgentProtocolException(string code, string message) : Exception(message) { public string Code { get; } = code; }
