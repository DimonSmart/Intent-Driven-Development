using Idd.Factory.LiveTests.Infrastructure;
using Idd.Factory.LiveTests.Models;

namespace Idd.Factory.LiveTests.Environments;

public sealed class LocalFactoryEvalEnvironment(ProcessRunner processRunner) : IFactoryEvalEnvironment
{
    public CodexCommand CodexCommand { get; } = CodexExecutableResolver.Resolve();
    public Task PrepareAsync(FactoryEvalWorkspace workspace, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<ProcessResult> RunCommandAsync(FactoryEvalWorkspace workspace, string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var name = Path.GetRandomFileName();
        return processRunner.RunAsync(executable, arguments, workspace.WorkspaceDirectory,
            Path.Combine(workspace.VerificationDirectory, name + ".stdout.log"),
            Path.Combine(workspace.VerificationDirectory, name + ".stderr.log"), TimeSpan.FromMinutes(2), cancellationToken);
    }

    public Task<ProcessResult> RunCodexAsync(FactoryEvalWorkspace workspace, FactoryEvalOptions options, CancellationToken cancellationToken) =>
        processRunner.RunAsync(CodexCommand.Executable, CodexCommand.PrefixArguments.Concat(["exec", "--json", "--ephemeral", "--ignore-user-config", "--ignore-rules", "-c", "approval_policy=never", "-c", $"model_reasoning_effort={options.ReasoningEffort}", "--model", options.Model, "--sandbox", "workspace-write", "--cd", workspace.WorkspaceDirectory, "--output-schema", Path.Combine(workspace.CaseDirectory, "final-response.schema.json"), "--output-last-message", workspace.LastMessagePath, File.ReadAllText(Path.Combine(workspace.CaseDirectory, "task.md"))]).ToArray(), workspace.WorkspaceDirectory, workspace.EventsPath, workspace.StderrPath, options.Timeout, cancellationToken);
}
