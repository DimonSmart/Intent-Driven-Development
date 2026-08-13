using Idd.Factory.LiveTests.Environments;
using Idd.Factory.LiveTests.Infrastructure;
using Idd.Factory.LiveTests.Models;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

[Collection("Live Factory Evals")]
public sealed class FactoryMcpHostCompatibilityLiveTests
{
    [LiveFactoryEvalFact]
    [Trait("Category", "LiveFactoryEval")]
    public async Task BundledFactoryToolIsDirectlyVisibleWithoutCodeModeOrToolSearch()
    {
        var (workspace, options, processRunner) = await PrepareAsync("direct-visibility", CancellationToken.None);
        var prompt = "Explicitly use $idd-factory-run now. Call the bundled Factory cancellation operation for the current workspace, then report its structured outcome. Do not use tool search or a shell launcher.";

        var result = await RunCodexAsync(processRunner, workspace, options, prompt, CancellationToken.None);
        var metrics = CodexJsonlAnalyzer.Analyze(workspace.EventsPath, result.Duration, new CodexHomeLocator(() => workspace.CodexHomeDirectory, () => workspace.CodexHomeDirectory));

        Assert.False(result.TimedOut);
        Assert.True(result.ExitCode == 0 || result.CompletionSignaled);
        var lastMessage = await File.ReadAllTextAsync(workspace.LastMessagePath);
        Assert.DoesNotContain("user cancelled MCP tool call", lastMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, metrics.FactoryMcpCallCount);
        Assert.Equal(0, metrics.ToolSearchCallCount);
        Assert.Equal(0, metrics.CommandExecutionCallCount);
        Assert.Equal(0, metrics.WriteStdinCallCount);
    }

    [LiveFactoryEvalFact]
    [Trait("Category", "LiveFactoryEval")]
    public async Task OrdinaryCodingRequestDoesNotInvokeFactory()
    {
        var (workspace, options, processRunner) = await PrepareAsync("manual-only", CancellationToken.None);
        var prompt = "This is an ordinary coding request, not an IDD Factory request. Create ordinary.txt in the workspace containing exactly OK. Do not invoke $idd-factory-run.";

        var result = await RunCodexAsync(processRunner, workspace, options, prompt, CancellationToken.None);
        var metrics = CodexJsonlAnalyzer.Analyze(workspace.EventsPath, result.Duration, new CodexHomeLocator(() => workspace.CodexHomeDirectory, () => workspace.CodexHomeDirectory));

        Assert.False(result.TimedOut);
        Assert.True(result.ExitCode == 0 || result.CompletionSignaled);
        Assert.Equal(0, metrics.FactoryRunCallCount);
        Assert.Equal(0, metrics.FactoryMcpCallCount);
    }

    [Fact]
    public void CompatibilityProfileDisablesCodeModeWithoutDirectOnlyNamespaceOverrides()
    {
        var workspace = CreateWorkspace("profile");
        var options = new FactoryEvalOptions("test-model", "low", TimeSpan.FromMinutes(1), "0.0.0-test");
        var arguments = LocalFactoryEvalEnvironment.BuildRunCodexArguments(workspace, options, userConfigPath: Path.Combine(workspace.CodexHomeDirectory, "config.toml"));
        Assert.Contains("code_mode_host", arguments);
        Assert.DoesNotContain(arguments, argument => argument.Contains("direct_only_tool_namespaces", StringComparison.Ordinal));
    }

    private static async Task<(FactoryEvalWorkspace Workspace, FactoryEvalOptions Options, ProcessRunner Runner)> PrepareAsync(string name, CancellationToken cancellationToken)
    {
        var repositoryRoot = RepositoryRootFinder.Find();
        var workspace = CreateWorkspace(name);
        var processRunner = new ProcessRunner();
        var version = await MethodologyVersionResolver.ResolveAsync(repositoryRoot, cancellationToken);
        var options = FactoryEvalOptions.FromEnvironment(version.Value) with { Timeout = TimeSpan.FromMinutes(5) };
        var generatorBuild = await processRunner.RunAsync("dotnet", ["build", "tools/generate/Generate.csproj", "--nologo"], repositoryRoot,
            Path.Combine(workspace.VerificationDirectory, "generator-build.log"), Path.Combine(workspace.VerificationDirectory, "generator-build.stderr.log"), TimeSpan.FromMinutes(2), cancellationToken);
        if (generatorBuild.ExitCode != 0) throw new InvalidOperationException("Generator build failed for MCP compatibility eval.");
        await new CurrentIddArtifactBuilder(processRunner).BuildAsync(repositoryRoot, workspace, version.Value, cancellationToken);
        return (workspace, options, processRunner);
    }

    private static FactoryEvalWorkspace CreateWorkspace(string name)
    {
        var repositoryRoot = RepositoryRootFinder.Find();
        var runDirectory = Path.Combine(repositoryRoot, "artifacts", "factory-evals", $"mcp-{name}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..44]);
        var workspace = new FactoryEvalWorkspace(runDirectory, Path.Combine(runDirectory, "workspace"), Path.Combine(runDirectory, "generated-marketplace"), Path.Combine(runDirectory, "verification"), runDirectory);
        Directory.CreateDirectory(workspace.WorkspaceDirectory);
        Directory.CreateDirectory(workspace.VerificationDirectory);
        return workspace;
    }

    private static Task<ProcessResult> RunCodexAsync(ProcessRunner runner, FactoryEvalWorkspace workspace, FactoryEvalOptions options, string prompt, CancellationToken cancellationToken)
    {
        var command = CodexExecutableResolver.Resolve();
        var arguments = command.PrefixArguments.Concat(LocalFactoryEvalEnvironment.BuildRunCodexArguments(
            workspace,
            options,
            userConfigPath: Path.Combine(workspace.CodexHomeDirectory, "config.toml"))).ToArray();
        var environment = LocalFactoryEvalEnvironment.BuildCodexEnvironment(
            Environment.GetEnvironmentVariable("PATH") ?? string.Empty,
            OperatingSystem.IsWindows(),
            codexHome: workspace.CodexHomeDirectory,
            options: options);
        return runner.RunAsync(command.Executable, arguments, workspace.WorkspaceDirectory, workspace.EventsPath, workspace.StderrPath, options.Timeout, cancellationToken, prompt, environment, workspace.LastMessagePath);
    }
}
