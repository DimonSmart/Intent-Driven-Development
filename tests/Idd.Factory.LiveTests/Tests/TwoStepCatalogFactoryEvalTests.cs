using System.Text.Json;
using System.Text.Json.Nodes;
using Idd.Factory.LiveTests.Environments;
using Idd.Factory.LiveTests.Infrastructure;
using Idd.Factory.LiveTests.Models;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

[CollectionDefinition("Live Factory Evals", DisableParallelization = true)]
public sealed class LiveFactoryEvalsCollection;

[Collection("Live Factory Evals")]
public sealed class TwoStepCatalogFactoryEvalTests
{
    [LiveFactoryEvalFact]
    [Trait("Category", "LiveFactoryEval")]
    public async Task TwoStepCatalog_CompletesTwoSubtasksAndReviewCheckpoint()
    {
        var cancellationToken = CancellationToken.None;
        var repositoryRoot = RepositoryRootFinder.Find();
        var processRunner = new ProcessRunner();
        var workspace = new FactoryEvalWorkspaceBuilder().Create(repositoryRoot);
        var assertions = new EvalAssertionCollector();
        FactoryResult? factoryResult = null;
        var metrics = new FactoryEvalMetrics();
        var result = new FactoryEvalResult { RunDirectory = workspace.RunDirectory, Outcome = "INFRASTRUCTURE_FAILURE" };

        try
        {
            var version = await MethodologyVersionResolver.ResolveAsync(repositoryRoot, cancellationToken);
            var options = FactoryEvalOptions.FromEnvironment(version.Value);
            var codexCommand = CodexExecutableResolver.Resolve();
            var codexVersion = await RequireVersionAsync(processRunner, codexCommand.Executable, codexCommand.PrefixArguments.Concat(["--version"]).ToArray(), repositoryRoot, workspace, "codex-version", cancellationToken);
            var dotnetVersion = await RequireVersionAsync(processRunner, "dotnet", ["--version"], repositoryRoot, workspace, "dotnet-version", cancellationToken);
            await RequireVersionAsync(processRunner, "git", ["--version"], repositoryRoot, workspace, "git-version", cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(workspace.RunDirectory, "run-manifest.json"), JsonSerializer.Serialize(new FactoryEvalRunManifest(1, "two-step-catalog", options.Model, options.ReasoningEffort, version.Value, version.SourceRevision, version.SourceDirty, codexVersion, dotnetVersion, DateTimeOffset.UtcNow), new JsonSerializerOptions { WriteIndented = true }) + "\n", cancellationToken);
            assertions.Require(dotnetVersion.StartsWith("10.", StringComparison.Ordinal), "Infrastructure", "NET 10 SDK", $"Expected a .NET 10 SDK, but dotnet --version reported '{dotnetVersion}'.");

            var buildGenerator = await processRunner.RunAsync("dotnet", ["build", "tools/generate/Generate.csproj", "--nologo"], repositoryRoot, Path.Combine(workspace.VerificationDirectory, "generator-build.log"), Path.Combine(workspace.VerificationDirectory, "generator-build.stderr.log"), TimeSpan.FromMinutes(2), cancellationToken);
            assertions.Require(buildGenerator.ExitCode == 0, "Infrastructure", "Generator build", $"Could not build the current generator. See {buildGenerator.StderrPath}.");
            if (buildGenerator.ExitCode != 0) throw new InvalidOperationException("Generator build failed.");

            await new CurrentIddArtifactBuilder(processRunner).BuildAsync(repositoryRoot, workspace, version.Value, cancellationToken);
            assertions.Require(Directory.Exists(Path.Combine(workspace.WorkspaceDirectory, ".agents", "skills", "idd-factory-run")), "Infrastructure", "Local Factory skills", "Generated Factory skills were not copied into the project-local .agents/skills directory.");
            assertions.Require(File.Exists(Path.Combine(workspace.WorkspaceDirectory, ".agents", "skills", "idd-factory-run", "references", "methodology-version.json")), "Version", "Methodology reference", "The generated idd-factory-run methodology version reference is missing.");

            await InitializeGitAsync(processRunner, workspace, cancellationToken);
            var restore = await RunWorkspaceAsync(processRunner, workspace, "dotnet", ["restore", "MiniCatalog.sln"], "restore", TimeSpan.FromMinutes(3), cancellationToken);
            assertions.Require(restore.ExitCode == 0, "Infrastructure", "Fixture restore", $"Fixture restore failed. See {restore.StderrPath}.");
            if (restore.ExitCode != 0) throw new InvalidOperationException("Fixture restore failed.");
            var baselineBuild = await RunWorkspaceAsync(processRunner, workspace, "dotnet", ["build", "MiniCatalog.sln", "--no-restore"], "baseline-build", TimeSpan.FromMinutes(2), cancellationToken);
            var baselineProduct = await RunWorkspaceAsync(processRunner, workspace, "dotnet", ["test", "tests/MiniCatalog.Tests/MiniCatalog.Tests.csproj", "--no-restore", "--filter", "FullyQualifiedName~ProductCodeTests"], "baseline-product-code-tests", TimeSpan.FromMinutes(2), cancellationToken);
            var baselineCatalog = await RunWorkspaceAsync(processRunner, workspace, "dotnet", ["test", "tests/MiniCatalog.Tests/MiniCatalog.Tests.csproj", "--no-restore", "--filter", "FullyQualifiedName~CatalogIntegrationTests"], "baseline-catalog-tests", TimeSpan.FromMinutes(2), cancellationToken);
            assertions.Require(baselineBuild.ExitCode == 0 && baselineProduct.ExitCode != 0 && baselineCatalog.ExitCode != 0, "Infrastructure", "Fixture baseline", $"Expected a compiling fixture with failing ProductCode and Catalog tests; build={baselineBuild.ExitCode}, product={baselineProduct.ExitCode}, catalog={baselineCatalog.ExitCode}. See {workspace.VerificationDirectory}.");
            if (baselineBuild.ExitCode != 0 || baselineProduct.ExitCode == 0 || baselineCatalog.ExitCode == 0) throw new InvalidOperationException("Fixture baseline is invalid.");

            var environment = new LocalFactoryEvalEnvironment(processRunner);
            await environment.PrepareAsync(workspace, cancellationToken);
            var codex = await environment.RunCodexAsync(workspace, options, cancellationToken);
            assertions.Require(!codex.TimedOut, "Infrastructure", "Codex timeout", $"Codex exceeded the {options.Timeout.TotalMinutes} minute timeout. Partial logs are in {workspace.RunDirectory}.");
            assertions.Require(codex.ExitCode == 0, "Infrastructure", "Codex execution", $"Codex exited with code {codex.ExitCode}. See {workspace.StderrPath}.");
            metrics = CodexJsonlAnalyzer.Analyze(workspace.EventsPath, codex.Duration);
            assertions.Require(metrics.MalformedLineCount == 0, "Infrastructure", "Codex JSONL", $"Codex JSONL contains {metrics.MalformedLineCount} malformed line(s). See {workspace.EventsPath}.");
            assertions.Require(metrics.ModelEffective is null || metrics.ModelEffective == options.Model, "Infrastructure", "Effective Codex model", $"Expected Codex to use requested model '{options.Model}' without fallback, but JSONL reports '{metrics.ModelEffective}'.");

            factoryResult = FactoryResultReader.ReadSingle(workspace.WorkspaceDirectory);
            AssertFactoryResult(assertions, factoryResult, options.MethodologyVersion);
            CompareLastMessage(assertions, workspace.LastMessagePath, factoryResult);
            var finalBuild = await RunWorkspaceAsync(processRunner, workspace, "dotnet", ["build", "MiniCatalog.sln", "--no-restore"], "final-build", TimeSpan.FromMinutes(2), cancellationToken);
            var finalTests = await RunWorkspaceAsync(processRunner, workspace, "dotnet", ["test", "tests/MiniCatalog.Tests/MiniCatalog.Tests.csproj", "--no-restore"], "final-tests", TimeSpan.FromMinutes(2), cancellationToken);
            result.ProductPassed = finalBuild.ExitCode == 0 && finalTests.ExitCode == 0;
            assertions.Require(result.ProductPassed, "Product", "Final verification", $"Expected final product verification to pass, but build={finalBuild.ExitCode} and test={finalTests.ExitCode}. See {workspace.VerificationDirectory}.");
            assertions.Require(Directory.GetFiles(Path.Combine(workspace.WorkspaceDirectory, "src", "MiniCatalog"), "*.cs").Any(path => File.ReadAllText(path).Contains("ProductCode", StringComparison.Ordinal)), "Product", "ProductCode type", "Expected a ProductCode production type under src/MiniCatalog/.");
            assertions.Require(!File.ReadAllText(Path.Combine(workspace.WorkspaceDirectory, "src", "MiniCatalog", "MiniCatalog.csproj")).Contains("PackageReference", StringComparison.Ordinal), "Product", "External packages", "Expected no external package to be added to the product project.");
            assertions.Require(Directory.GetFiles(workspace.WorkspaceDirectory, "*.csproj", SearchOption.AllDirectories).Length == 2, "Product", "Unexpected projects", "Expected the fixture to retain exactly its two prepared projects.");
            AssertPreservation(assertions, workspace, await GitOutputAsync(processRunner, workspace, ["diff", "--binary", "HEAD"], "git-diff.patch", cancellationToken));
            AssertOrchestration(assertions, metrics);
            result.FactoryPassed = !assertions.HasFailures;
            result.Outcome = result.ProductPassed && result.FactoryPassed ? "PASSED" : result.ProductPassed ? "FACTORY_CONTRACT_FAILURE" : result.FactoryPassed ? "PRODUCT_FAILURE" : "PRODUCT_AND_FACTORY_FAILURE";
        }
        catch (Exception exception) when (exception is not Xunit.Sdk.XunitException)
        {
            assertions.Require(false, "Infrastructure", "Live eval execution", $"Live eval infrastructure failed: {exception.Message}");
            result.Outcome = "INFRASTRUCTURE_FAILURE";
        }
        finally
        {
            await assertions.WriteAsync(workspace, result, metrics, factoryResult);
        }
        assertions.ThrowIfFailed(workspace.RunDirectory);
    }

    private static async Task<string> RequireVersionAsync(ProcessRunner runner, string executable, IReadOnlyList<string> arguments, string workingDirectory, FactoryEvalWorkspace workspace, string name, CancellationToken token)
    {
        var result = await runner.RunAsync(executable, arguments, workingDirectory, Path.Combine(workspace.VerificationDirectory, name + ".log"), Path.Combine(workspace.VerificationDirectory, name + ".stderr.log"), TimeSpan.FromMinutes(1), token);
        if (result.ExitCode != 0) throw new InvalidOperationException($"Required executable '{executable}' is unavailable or failed. See {result.StderrPath}.");
        return (await File.ReadAllTextAsync(result.StdoutPath, token)).Trim();
    }

    private static async Task InitializeGitAsync(ProcessRunner runner, FactoryEvalWorkspace workspace, CancellationToken token)
    {
        foreach (var command in new[] { new[] { "init" }, new[] { "config", "user.name", "IDD Factory Eval" }, new[] { "config", "user.email", "idd-factory-eval@local" }, new[] { "add", "." }, new[] { "commit", "-m", "Initial eval fixture" } })
        {
            var result = await RunWorkspaceAsync(runner, workspace, "git", command, "git-" + command[0], TimeSpan.FromMinutes(1), token);
            if (result.ExitCode != 0) throw new InvalidOperationException($"git {command[0]} failed. See {result.StderrPath}.");
        }
        var status = await GitOutputAsync(runner, workspace, ["status", "--porcelain"], "git-initial-status.log", token);
        if (!string.IsNullOrWhiteSpace(status)) throw new InvalidOperationException("Initial eval fixture has uncommitted files after git commit.");
    }

    private static Task<ProcessResult> RunWorkspaceAsync(ProcessRunner runner, FactoryEvalWorkspace workspace, string executable, IReadOnlyList<string> arguments, string name, TimeSpan timeout, CancellationToken token) => runner.RunAsync(executable, arguments, workspace.WorkspaceDirectory, Path.Combine(workspace.VerificationDirectory, name + ".log"), Path.Combine(workspace.VerificationDirectory, name + ".stderr.log"), timeout, token);
    private static async Task<string> GitOutputAsync(ProcessRunner runner, FactoryEvalWorkspace workspace, IReadOnlyList<string> arguments, string name, CancellationToken token) { var result = await RunWorkspaceAsync(runner, workspace, "git", arguments, name, TimeSpan.FromMinutes(1), token); return await File.ReadAllTextAsync(result.StdoutPath, token); }

    private static void AssertFactoryResult(EvalAssertionCollector assertions, FactoryResult result, string version)
    {
        assertions.Require(result.String("methodologyVersion") == version, "Version", "Factory methodology version", $"Expected factory-result.json methodologyVersion '{version}', but it reports '{result.String("methodologyVersion") ?? "missing"}'.");
        var commitPath = result.String("commitMessagePath");
        var workspace = new DirectoryInfo(Path.GetDirectoryName(result.Path)!).Parent!.Parent!.Parent!.Parent!.FullName;
        assertions.Require(!string.IsNullOrWhiteSpace(commitPath) && File.Exists(Path.Combine(workspace, commitPath.Replace('/', Path.DirectorySeparatorChar))), "Factory", "Commit message path", $"Expected factory-result.json commitMessagePath to point to an existing file, but it reports '{commitPath ?? "missing"}'.");
        foreach (var (name, expected) in new (string, object)[] { ("factoryOutcome", "COMPLETED"), ("subtaskCount", 2), ("completedSubtaskCount", 2), ("reviewCheckpointCount", 1), ("completedReviewCheckpointCount", 1), ("correctiveSubtaskCount", 0), ("blockedItemCount", 0), ("incompleteItemCount", 0), ("finalReviewVerdict", "approved"), ("verificationStatus", "passed") })
        {
            object? actual = expected is int ? result.Int(name) : result.String(name);
            assertions.Require(Equals(actual, expected), "Factory", name, $"Expected Factory {name} to be '{expected}', but factory-result.json reports '{actual ?? "missing"}'.");
        }
    }

    private static void CompareLastMessage(EvalAssertionCollector assertions, string path, FactoryResult result)
    {
        var same = File.Exists(path) && JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(path)), JsonNode.Parse(result.Json.GetRawText()));
        assertions.Require(same, "Factory", "Final response contract", $"Expected last-message.json to contain the same JSON values as factory-result.json. See {path}.");
    }

    private static void AssertPreservation(EvalAssertionCollector assertions, FactoryEvalWorkspace workspace, string diff)
    {
        var changed = diff.Split('\n').Where(line => line.StartsWith("+++ b/", StringComparison.Ordinal)).Select(line => line[6..]).Where(path => path != "/dev/null").ToArray();
        assertions.Require(changed.All(path => path.StartsWith("src/MiniCatalog/", StringComparison.Ordinal)), "Product", "Preservation boundaries", "Expected only files under src/MiniCatalog/ to change; inspect verification/git-diff.patch.");
        var current = Path.Combine(workspace.WorkspaceDirectory, ".idd", "factory", "current");
        assertions.Require(Directory.Exists(current) && !Directory.EnumerateFileSystemEntries(current).Any(), "Factory", "Factory cleanup", "Expected .idd/factory/current to exist and be empty after successful finalization.");
    }

    private static void AssertOrchestration(EvalAssertionCollector assertions, FactoryEvalMetrics metrics)
    {
        assertions.Inconclusive("Orchestration", "Worker classification", "Current Codex JSONL format did not provide enough role and write-provenance data to classify workers safely.");
        assertions.Require(metrics.WaitAgentCallCount == 0, "Orchestration", "Wait-only calls", $"Expected no wait_agent calls, but Codex JSONL reports {metrics.WaitAgentCallCount}.");
    }
}
