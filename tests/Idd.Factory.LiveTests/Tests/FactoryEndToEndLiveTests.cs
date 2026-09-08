using Idd.Factory.LiveTests.Infrastructure;
using Xunit;
using Xunit.Sdk;

namespace Idd.Factory.LiveTests.Tests;

[CollectionDefinition("Live Factory Evals", DisableParallelization = true)]
public sealed class LiveFactoryEvalsCollection;

[Collection("Live Factory Evals")]
public sealed class FactoryEndToEndLiveTests
{
    [LiveFactoryEvalFact]
    [Trait("Category", "LiveFactoryEval")]
    public async Task TwoStepCatalog_CompletesThroughOneBlockingFactoryCall()
    {
        var repositoryRoot = LiveTestWorkspace.FindRepositoryRoot();
        var workspace = LiveTestWorkspace.CreateCase(repositoryRoot, "TwoStepCatalog", copyTemplate: true);
        var runner = new ProcessRunner();
        var gitInitialized = false;
        try
        {
            await workspace.LogAsync("Building and installing the current Factory plugin.");
            var installed = await new InstalledFactory(runner).BuildAndInstallAsync(repositoryRoot, workspace, CancellationToken.None);

            await InitializeGitAsync(runner, workspace);
            gitInitialized = true;
            await workspace.LogAsync("Checking the prepared product baseline.");
            await RequireSuccessAsync(runner, workspace, "dotnet", ["restore", "MiniCatalog.sln"], "baseline-restore", TimeSpan.FromMinutes(3));
            await RequireSuccessAsync(runner, workspace, "dotnet", ["build", "MiniCatalog.sln", "--no-restore"], "baseline-build", TimeSpan.FromMinutes(2));
            var baselineProduct = await RunAsync(runner, workspace, "dotnet", ["test", "tests/MiniCatalog.Tests/MiniCatalog.Tests.csproj", "--no-restore", "--filter", "FullyQualifiedName~ProductCodeTests"], "baseline-product-tests", TimeSpan.FromMinutes(2));
            var baselineCatalog = await RunAsync(runner, workspace, "dotnet", ["test", "tests/MiniCatalog.Tests/MiniCatalog.Tests.csproj", "--no-restore", "--filter", "FullyQualifiedName~CatalogIntegrationTests"], "baseline-catalog-tests", TimeSpan.FromMinutes(2));
            Assert.NotEqual(0, baselineProduct.ExitCode);
            Assert.NotEqual(0, baselineCatalog.ExitCode);

            await workspace.LogAsync("Starting real Codex -> installed plugin -> factory_run -> Factory Runtime execution.");
            var codex = await new CodexProcess(runner).RunAsync(workspace, "danger-full-access", factoryEnvironment: true, CancellationToken.None);
            Assert.False(codex.Process.TimedOut);
            Assert.True(codex.Process.ExitCode == 0 || codex.Process.CompletionSignaled, $"Codex exit={codex.Process.ExitCode}. See {workspace.StderrPath}.");

            var factory = FactoryResultReader.ReadSingle(workspace.WorkspaceDirectory);
            Assert.Equal("COMPLETED", factory.Outcome);
            Assert.Equal(installed.MethodologyVersion, factory.MethodologyVersion);
            Assert.True(factory.CompletedWorkCount >= 2, $"Expected at least two completed work items, observed {factory.CompletedWorkCount}.");
            Assert.Equal("passed", factory.VerificationStatus);
            Assert.False(string.IsNullOrWhiteSpace(factory.CommitMessagePath));
            Assert.True(File.Exists(Path.Combine(workspace.WorkspaceDirectory, factory.CommitMessagePath!.Replace('/', Path.DirectorySeparatorChar))));

            var trace = MinimalCodexTraceReader.Read(workspace.EventsPath);
            Assert.Equal(1, trace.FactoryRunCalls);
            Assert.Equal(0, trace.FactoryStatusCalls);
            Assert.Equal(0, trace.ModelTurnsDuringFactoryRun);
            Assert.True(trace.FactoryCallCompleted);

            await workspace.LogAsync("Running independent final product verification.");
            await RequireSuccessAsync(runner, workspace, "dotnet", ["build", "MiniCatalog.sln", "--no-restore"], "final-build", TimeSpan.FromMinutes(2));
            await RequireSuccessAsync(runner, workspace, "dotnet", ["test", "tests/MiniCatalog.Tests/MiniCatalog.Tests.csproj", "--no-restore"], "final-tests", TimeSpan.FromMinutes(2));
            Assert.Contains(Directory.GetFiles(Path.Combine(workspace.WorkspaceDirectory, "src", "MiniCatalog"), "*.cs"), path => File.ReadAllText(path).Contains("ProductCode", StringComparison.Ordinal));
            Assert.DoesNotContain("PackageReference", File.ReadAllText(Path.Combine(workspace.WorkspaceDirectory, "src", "MiniCatalog", "MiniCatalog.csproj")), StringComparison.Ordinal);
            Assert.Equal(2, Directory.GetFiles(workspace.WorkspaceDirectory, "*.csproj", SearchOption.AllDirectories).Length);
            AssertProtectedInputsUnchanged(workspace);
            await workspace.LogAsync("Live Factory evaluation passed.");
        }
        catch (XunitException exception)
        {
            throw new XunitException($"{exception.Message}{Environment.NewLine}Artifacts: {workspace.RunDirectory}");
        }
        catch (Exception exception)
        {
            throw new XunitException($"Live Factory evaluation failed: {exception.Message}{Environment.NewLine}Artifacts: {workspace.RunDirectory}");
        }
        finally
        {
            if (gitInitialized) await workspace.CaptureGitEvidenceAsync(runner);
        }
    }

    private static void AssertProtectedInputsUnchanged(LiveTestWorkspace workspace)
    {
        foreach (var relative in new[] { Path.Combine(".idd", "intent", "IDD-0001-mini-catalog.md"), Path.Combine(".idd", "verification.yaml") })
        {
            var expected = File.ReadAllText(Path.Combine(workspace.CaseDirectory, "Template", relative));
            var actual = File.ReadAllText(Path.Combine(workspace.WorkspaceDirectory, relative));
            Assert.Equal(expected, actual);
        }
    }

    private static async Task InitializeGitAsync(ProcessRunner runner, LiveTestWorkspace workspace)
    {
        foreach (var arguments in new[]
        {
            new[] { "init" }, new[] { "config", "user.name", "IDD Factory Live Test" }, new[] { "config", "user.email", "idd-factory-live@local" },
            new[] { "add", "." }, new[] { "commit", "-m", "Live test baseline" }
        })
            await RequireSuccessAsync(runner, workspace, "git", arguments, "git-" + arguments[0], TimeSpan.FromMinutes(1));
    }

    private static Task<ProcessResult> RunAsync(ProcessRunner runner, LiveTestWorkspace workspace, string executable, IReadOnlyList<string> arguments, string name, TimeSpan timeout) =>
        runner.RunAsync(executable, arguments, workspace.WorkspaceDirectory, Path.Combine(workspace.VerificationDirectory, name + ".log"), Path.Combine(workspace.VerificationDirectory, name + ".stderr.log"), timeout, CancellationToken.None);

    private static async Task<ProcessResult> RequireSuccessAsync(ProcessRunner runner, LiveTestWorkspace workspace, string executable, IReadOnlyList<string> arguments, string name, TimeSpan timeout)
    {
        var result = await RunAsync(runner, workspace, executable, arguments, name, timeout);
        Assert.Equal(0, result.ExitCode);
        return result;
    }
}
