using Idd.Factory.LiveTests.Infrastructure;
using Idd.Factory.LiveTests.Models;

namespace Idd.Factory.LiveTests.Environments;

public sealed class LocalFactoryEvalEnvironment(ProcessRunner processRunner) : IFactoryEvalEnvironment
{
    public CodexCommand CodexCommand { get; } = CodexExecutableResolver.Resolve();
    public async Task PrepareAsync(FactoryEvalWorkspace workspace, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return;

        var probePath = Path.Combine(workspace.WorkspaceDirectory, ".codex-write-probe");
        var result = await processRunner.RunAsync(CodexCommand.Executable, CodexCommand.PrefixArguments.Concat(["-c", "windows.sandbox=\"unelevated\"", "sandbox", "windows", "cmd.exe", "/d", "/c", "echo probe>.codex-write-probe"]).ToArray(), workspace.WorkspaceDirectory, Path.Combine(workspace.VerificationDirectory, "codex-sandbox-probe.log"), Path.Combine(workspace.VerificationDirectory, "codex-sandbox-probe.stderr.log"), TimeSpan.FromMinutes(1), cancellationToken);
        try
        {
            if (result.ExitCode != 0 || !File.Exists(probePath)) throw new InvalidOperationException("Codex Windows sandbox could not write to the prepared workspace. The Factory was not started. Configured sandbox: workspace-write. Windows sandbox backend: unelevated. See verification/codex-sandbox-probe.stderr.log.");
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
        var arguments = new List<string> { "exec", "--json", "--ephemeral", "--ignore-user-config", "--ignore-rules", "-c", "approval_policy=never", "-c", $"model_reasoning_effort={options.ReasoningEffort}" };
        if (OperatingSystem.IsWindows()) arguments.AddRange(["-c", "windows.sandbox=\"unelevated\""]);
        arguments.AddRange(["--model", options.Model, "--sandbox", "workspace-write", "--cd", workspace.WorkspaceDirectory, "--output-schema", Path.Combine(workspace.CaseDirectory, "final-response.schema.json"), "--output-last-message", workspace.LastMessagePath, File.ReadAllText(Path.Combine(workspace.CaseDirectory, "task.md"))]);
        return processRunner.RunAsync(CodexCommand.Executable, CodexCommand.PrefixArguments.Concat(arguments).ToArray(), workspace.WorkspaceDirectory, workspace.EventsPath, workspace.StderrPath, options.Timeout, cancellationToken);
    }
}
