using Idd.Factory.LiveTests.Infrastructure;
using Idd.Factory.LiveTests.Models;

namespace Idd.Factory.LiveTests.Environments;

public sealed class LocalFactoryEvalEnvironment(ProcessRunner processRunner) : IFactoryEvalEnvironment
{
    public CodexCommand CodexCommand { get; } = CodexExecutableResolver.Resolve();
    public async Task PrepareAsync(FactoryEvalWorkspace workspace, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return;

        VerifyWorkspaceWriteAccess(workspace.WorkspaceDirectory);

        const string probeFileName = ".codex-sandbox-write-probe";
        var probePath = Path.Combine(workspace.WorkspaceDirectory, probeFileName);
        var stderrPath = Path.Combine(workspace.VerificationDirectory, "codex-sandbox-probe.stderr.log");
        try
        {
            var result = await processRunner.RunAsync(CodexCommand.Executable, CodexCommand.PrefixArguments.Concat(BuildWindowsSandboxProbeArguments(probeFileName)).ToArray(), workspace.WorkspaceDirectory, Path.Combine(workspace.VerificationDirectory, "codex-sandbox-probe.log"), stderrPath, TimeSpan.FromMinutes(1), cancellationToken);
            if (result.ExitCode != 0 || result.TimedOut || !File.Exists(probePath))
                throw new InvalidOperationException($"Codex Windows sandbox could not write to the prepared workspace. Workspace: '{workspace.WorkspaceDirectory}'. Exit code: {result.ExitCode}. Timed out: {result.TimedOut}. Windows sandbox backend: unelevated. Sandbox mode: workspace-write. Stderr: {ReadStderr(stderrPath)}");
        }
        finally
        {
            if (File.Exists(probePath)) File.Delete(probePath);
        }
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
        return processRunner.RunAsync(CodexCommand.Executable, CodexCommand.PrefixArguments.Concat(BuildRunCodexArguments(workspace, options, OperatingSystem.IsWindows())).ToArray(), workspace.WorkspaceDirectory, workspace.EventsPath, workspace.StderrPath, options.Timeout, cancellationToken);
    }

    internal static IReadOnlyList<string> BuildWindowsSandboxProbeArguments(string probeFileName) =>
        ["-c", "windows.sandbox=\"unelevated\"", "-c", "sandbox_mode=\"workspace-write\"", "sandbox", "windows", "cmd.exe", "/d", "/c", $"echo probe>{probeFileName}"];

    internal static IReadOnlyList<string> BuildRunCodexArguments(FactoryEvalWorkspace workspace, FactoryEvalOptions options, bool isWindows)
    {
        var arguments = new List<string> { "exec", "--json", "--ephemeral", "--ignore-user-config", "--ignore-rules", "-c", "approval_policy=never", "-c", $"model_reasoning_effort={options.ReasoningEffort}" };
        if (isWindows) arguments.AddRange(["-c", "windows.sandbox=\"unelevated\""]);
        arguments.AddRange(["--model", options.Model, "--sandbox", "workspace-write", "--cd", workspace.WorkspaceDirectory, "--output-schema", Path.Combine(workspace.CaseDirectory, "final-response.schema.json"), "--output-last-message", workspace.LastMessagePath, File.ReadAllText(Path.Combine(workspace.CaseDirectory, "task.md"))]);
        return arguments;
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

    private static string ReadStderr(string stderrPath) => File.Exists(stderrPath) ? File.ReadAllText(stderrPath) : "<stderr log not found>";
}
