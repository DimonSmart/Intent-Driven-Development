using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.LiveTests.Infrastructure;
using Idd.Factory.Verification;
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
        var workspace = LiveTestWorkspace.CreateTwoStepCatalog(repositoryRoot);
        Console.WriteLine($"Factory live workspace: {workspace.WorkspaceDirectory}");
        Console.Out.Flush();
        var runner = new ProcessRunner();
        var gitInitialized = false;
        try
        {
            VerificationPolicyParser.Parse(File.ReadAllText(Path.Combine(workspace.WorkspaceDirectory, ".idd", "verification.yaml")));

            await workspace.LogAsync("Building and installing the current Factory plugin.");
            var installed = await new InstalledFactory(runner).BuildAndInstallAsync(repositoryRoot, workspace, CancellationToken.None);

            await InitializeGitAsync(runner, workspace);
            gitInitialized = true;
            await ExecutorDiscoveryEvaluation.PrepareAsync(runner, workspace);
            await workspace.LogAsync("Checking the prepared product baseline.");
            await RequireSuccessAsync(runner, workspace, "dotnet", ["restore", "MiniCatalog.sln"], "baseline-restore", TimeSpan.FromMinutes(3));
            await RequireSuccessAsync(runner, workspace, "dotnet", ["build", "MiniCatalog.sln", "--no-restore"], "baseline-build", TimeSpan.FromMinutes(2));
            var baselineProduct = await RunAsync(runner, workspace, "dotnet", ["test", "tests/MiniCatalog.Tests/MiniCatalog.Tests.csproj", "--no-restore", "--filter", "FullyQualifiedName~ProductCodeTests"], "baseline-product-tests", TimeSpan.FromMinutes(2));
            var baselineCatalog = await RunAsync(runner, workspace, "dotnet", ["test", "tests/MiniCatalog.Tests/MiniCatalog.Tests.csproj", "--no-restore", "--filter", "FullyQualifiedName~CatalogIntegrationTests"], "baseline-catalog-tests", TimeSpan.FromMinutes(2));
            Assert.NotEqual(0, baselineProduct.ExitCode);
            Assert.NotEqual(0, baselineCatalog.ExitCode);

            await workspace.LogAsync("Starting real Codex -> installed plugin -> factory_run -> Factory Runtime execution.");
            var codex = await new CodexProcess(runner).RunAsync(workspace, CancellationToken.None);
            Assert.False(codex.Process.TimedOut);
            Assert.True(codex.Process.ExitCode == 0 || codex.Process.CompletionSignaled, $"Codex exit={codex.Process.ExitCode}. See {workspace.StderrPath}.");

            var trace = MinimalCodexTraceReader.Read(workspace.EventsPath);
            var resultsDirectory = Path.Combine(workspace.WorkspaceDirectory, ".idd", "factory", "results");
            if (!Directory.Exists(resultsDirectory))
            {
                throw new XunitException($"""
                    Factory results directory is missing.
                    Observed factory_run calls: {trace.FactoryRunCalls}; factory_status calls: {trace.FactoryStatusCalls}.
                    Codex last message:
                    {ReadDiagnosticFile(workspace.LastMessagePath)}
                    Codex stderr:
                    {ReadDiagnosticFile(workspace.StderrPath)}
                    """);
            }

            var factory = FactoryResultReader.ReadSingle(workspace.WorkspaceDirectory);
            Assert.Equal("COMPLETED", factory.Outcome);
            Assert.Equal(installed.MethodologyVersion, factory.MethodologyVersion);
            Assert.True(factory.CompletedWorkCount >= 2, $"Expected at least two completed work items, observed {factory.CompletedWorkCount}.");
            AssertSequentialWorkHandoff(factory.Path);
            Assert.Equal("passed", factory.VerificationStatus);
            ExecutorDiscoveryEvaluation.AssertEfficientDiscovery(factory.Path, workspace);
            Assert.False(string.IsNullOrWhiteSpace(factory.CommitMessagePath));
            Assert.True(File.Exists(Path.Combine(workspace.WorkspaceDirectory, factory.CommitMessagePath!.Replace('/', Path.DirectorySeparatorChar))));

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

    private static string ReadDiagnosticFile(string path)
    {
        if (!File.Exists(path)) return "<missing>";
        var text = File.ReadAllText(path).Trim();
        const int maxLength = 4000;
        return text.Length <= maxLength ? text : "..." + text[^maxLength..];
    }

    private static void AssertSequentialWorkHandoff(string factoryResultPath)
    {
        var resultDirectory = Path.GetDirectoryName(factoryResultPath)
            ?? throw new XunitException("Factory result directory could not be resolved.");
        var completedWorkPath = Path.Combine(resultDirectory, "completed-work.json");
        Assert.True(File.Exists(completedWorkPath), "completed-work.json is missing from the Factory result.");

        using var completedDocument = JsonDocument.Parse(File.ReadAllText(completedWorkPath));
        var completed = completedDocument.RootElement.GetProperty("completed").EnumerateArray()
            .Select(item => new
            {
                Id = item.GetProperty("id").GetString() ?? string.Empty,
                ResultRef = item.GetProperty("resultRef").GetString() ?? string.Empty
            })
            .ToArray();
        Assert.True(completed.Length >= 2, $"Expected at least two persisted completed work items, observed {completed.Length}.");
        Assert.False(string.IsNullOrWhiteSpace(completed[0].Id));
        Assert.False(string.IsNullOrWhiteSpace(completed[0].ResultRef));
        Assert.False(string.IsNullOrWhiteSpace(completed[1].Id));
        Assert.NotEqual(completed[0].Id, completed[1].Id);

        var attemptsDirectory = Path.Combine(resultDirectory, "attempts");
        var secondInvocation = Directory.GetFiles(attemptsDirectory, "invocation.json", SearchOption.AllDirectories)
            .Select(path => JsonSerializer.Deserialize<AgentInvocation>(File.ReadAllText(path), FactoryJson.Options)
                            ?? throw new XunitException($"Invalid invocation artifact: {path}"))
            .Where(invocation => invocation.Capability == "implementation" && invocation.WorkItemId == completed[1].Id)
            .OrderBy(invocation => invocation.StartedAt)
            .FirstOrDefault()
            ?? throw new XunitException($"No executor invocation found for second completed work item {completed[1].Id}.");

        var firstSemanticResultPath = Path.Combine(
            resultDirectory,
            completed[0].ResultRef.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(firstSemanticResultPath), $"Semantic result for first work item is missing: {completed[0].ResultRef}.");
        var firstSemanticResult = File.ReadAllText(firstSemanticResultPath).Trim();
        Assert.False(string.IsNullOrWhiteSpace(firstSemanticResult), "First work item semantic result is empty.");

        Assert.True(
            secondInvocation.Input.Contains($"## {completed[0].Id}", StringComparison.Ordinal),
            $"Second executor input does not contain completed-work metadata for {completed[0].Id}.");
        Assert.True(
            secondInvocation.Input.Contains(firstSemanticResult, StringComparison.Ordinal),
            $"Second executor input does not contain the semantic result of {completed[0].Id}.");
    }

    private static void AssertProtectedInputsUnchanged(LiveTestWorkspace workspace)
    {
        foreach (var relative in new[] { Path.Combine(".idd", "intent", "IDD-0001.spec-mini-catalog.md"), Path.Combine(".idd", "verification.yaml") })
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
