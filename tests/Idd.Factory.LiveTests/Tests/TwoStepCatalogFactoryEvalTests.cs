using System.Text.Json;
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
        using var sleepPrevention = SystemSleepPrevention.Acquire();
        var cancellationToken = CancellationToken.None;
        var repositoryRoot = RepositoryRootFinder.Find();
        var processRunner = new ProcessRunner();
        var workspace = new FactoryEvalWorkspaceBuilder().Create(repositoryRoot);
        var assertions = new EvalAssertionCollector();
        var factoryResult = new FactoryResultReadResult(null, "Factory result was not read.");
        var metrics = new FactoryEvalMetrics();
        var agentTrace = new AgentTrace(2, null, [], []);
        var result = new FactoryEvalResult { RunDirectory = workspace.RunDirectory, Outcome = "INFRASTRUCTURE_FAILURE" };

        try
        {
            await LogProgressAsync(workspace, "Workspace created; resolving methodology and tool versions.", cancellationToken);
            var version = await MethodologyVersionResolver.ResolveAsync(repositoryRoot, cancellationToken);
            var options = FactoryEvalOptions.FromEnvironment(version.Value) with { PersistSessionRollouts = true };
            var codexCommand = CodexExecutableResolver.Resolve();
            var codexVersion = await RequireVersionAsync(processRunner, codexCommand.Executable, codexCommand.PrefixArguments.Concat(["--version"]).ToArray(), repositoryRoot, workspace, "codex-version", cancellationToken);
            var dotnetVersion = await RequireVersionAsync(processRunner, "dotnet", ["--version"], repositoryRoot, workspace, "dotnet-version", cancellationToken);
            await RequireVersionAsync(processRunner, "git", ["--version"], repositoryRoot, workspace, "git-version", cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(workspace.RunDirectory, "run-manifest.json"), JsonSerializer.Serialize(new FactoryEvalRunManifest(1, "two-step-catalog", options.Model, options.ReasoningEffort, version.Value, version.SourceRevision, version.SourceDirty, codexVersion, dotnetVersion, DateTimeOffset.UtcNow), new JsonSerializerOptions { WriteIndented = true }) + "\n", cancellationToken);
            await LogProgressAsync(workspace, $"Prerequisites ready; model={options.Model}, reasoning={options.ReasoningEffort}, Codex timeout={options.Timeout.TotalMinutes:0} minutes.", cancellationToken);
            assertions.Require(dotnetVersion.StartsWith("10.", StringComparison.Ordinal), "Infrastructure", "NET 10 SDK", $"Expected a .NET 10 SDK, but dotnet --version reported '{dotnetVersion}'.");

            var buildGenerator = await processRunner.RunAsync("dotnet", ["build", "tools/generate/Generate.csproj", "--nologo"], repositoryRoot, Path.Combine(workspace.VerificationDirectory, "generator-build.log"), Path.Combine(workspace.VerificationDirectory, "generator-build.stderr.log"), TimeSpan.FromMinutes(2), cancellationToken);
            assertions.Require(buildGenerator.ExitCode == 0, "Infrastructure", "Generator build", $"Could not build the current generator. See {buildGenerator.StderrPath}.");
            if (buildGenerator.ExitCode != 0) throw new InvalidOperationException("Generator build failed.");

            await new CurrentIddArtifactBuilder(processRunner).BuildAsync(repositoryRoot, workspace, version.Value, cancellationToken);
            await LogProgressAsync(workspace, "Generated current IDD artifacts.", cancellationToken);
            assertions.Require(Directory.Exists(Path.Combine(workspace.WorkspaceDirectory, ".agents", "skills", "idd-factory-run")), "Infrastructure", "Local Factory skills", "Generated Factory skills were not copied into the project-local .agents/skills directory.");
            assertions.Require(File.Exists(Path.Combine(workspace.WorkspaceDirectory, ".agents", "runtime", "idd-factory.dll")), "Infrastructure", "Local Factory runtime", "Packaged Factory runtime was not copied beside the local skills.");
            assertions.Require(File.Exists(Path.Combine(workspace.WorkspaceDirectory, ".agents", "skills", "idd-factory-run", "references", "methodology-version.json")), "Version", "Methodology reference", "The generated idd-factory-run methodology version reference is missing.");

            await InitializeGitAsync(processRunner, workspace, cancellationToken);
            await LogProgressAsync(workspace, "Initialized fixture repository; restoring and checking its baseline.", cancellationToken);
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
            await LogProgressAsync(workspace, "Fixture baseline is valid; starting Codex Factory execution.", cancellationToken);
            var codex = await environment.RunCodexAsync(workspace, options, cancellationToken);
            await LogProgressAsync(workspace, $"Codex finished after {codex.Duration.TotalMinutes:0.0} minutes; exit={codex.ExitCode}, timedOut={codex.TimedOut}, completionSignaled={codex.CompletionSignaled}.", cancellationToken);
            result.CodexProcessPassed = !codex.TimedOut && (codex.ExitCode == 0 || codex.CompletionSignaled);
            assertions.Require(!codex.TimedOut, "Infrastructure", "Codex timeout", $"Codex exceeded the {options.Timeout.TotalMinutes} minute timeout. Partial logs are in {workspace.RunDirectory}.");
            assertions.Require(codex.ExitCode == 0 || codex.CompletionSignaled, "Infrastructure", "Codex execution", $"Codex exited with code {codex.ExitCode} before producing its final response. See {workspace.StderrPath}.");
            agentTrace = TryBuildAgentTrace(workspace, codex.TimedOut);
            var factoryProtocol = FactoryOutcomeTraceAnalyzer.Analyze(workspace.EventsPath);
            var executionResponse = ExecutionResponseReader.TryRead(workspace.LastMessagePath, workspace.WorkspaceDirectory);
            FactoryProtocolAssertions.Assert(assertions, factoryProtocol, executionResponse);
            metrics = CodexJsonlAnalyzer.Analyze(workspace.EventsPath, codex.Duration);
            metrics.TotalSpawnedAgentCount = agentTrace.Agents.Count == 0 ? null : agentTrace.Agents.Count - 1;
            assertions.Require(metrics.MalformedLineCount == 0, "Infrastructure", "Codex JSONL", $"Codex JSONL contains {metrics.MalformedLineCount} malformed line(s). See {workspace.EventsPath}.");
            assertions.Require(metrics.ModelEffective is null || metrics.ModelEffective == options.Model, "Infrastructure", "Effective Codex model", $"Expected Codex to use requested model '{options.Model}' without fallback, but JSONL reports '{metrics.ModelEffective}'.");

            factoryResult = FactoryResultReader.TryReadSingle(workspace.WorkspaceDirectory);
            result.ExecutionResponsePassed = executionResponse.IsSuccess;
            result.FactoryOutcome = executionResponse.Response?.FactoryOutcome;
            metrics.FactoryOutcome = result.FactoryOutcome;
            result.FactoryResultExpected = executionResponse.Response?.FactoryOutcome == "COMPLETED";
            FactoryPostRunDiagnostics.Assert(assertions, executionResponse, factoryResult, options.MethodologyVersion);
            var (finalBuild, finalTests) = await FinalProductVerification.RunAsync(environment, workspace, cancellationToken);
            await LogProgressAsync(workspace, $"Final product verification finished; build={finalBuild.ExitCode}, tests={finalTests.ExitCode}.", cancellationToken);
            result.FinalBuildPassed = finalBuild.ExitCode == 0;
            result.FinalTestsPassed = finalTests.ExitCode == 0;
            var finalVerificationPassed = result.FinalBuildPassed && result.FinalTestsPassed;
            assertions.Require(finalVerificationPassed, "Product", "Final verification", $"Expected final product verification to pass, but build={finalBuild.ExitCode} and test={finalTests.ExitCode}. See {workspace.VerificationDirectory}.");
            assertions.Require(Directory.GetFiles(Path.Combine(workspace.WorkspaceDirectory, "src", "MiniCatalog"), "*.cs").Any(path => File.ReadAllText(path).Contains("ProductCode", StringComparison.Ordinal)), "Product", "ProductCode type", "Expected a ProductCode production type under src/MiniCatalog/.");
            assertions.Require(!File.ReadAllText(Path.Combine(workspace.WorkspaceDirectory, "src", "MiniCatalog", "MiniCatalog.csproj")).Contains("PackageReference", StringComparison.Ordinal), "Product", "External packages", "Expected no external package to be added to the product project.");
            assertions.Require(Directory.GetFiles(workspace.WorkspaceDirectory, "*.csproj", SearchOption.AllDirectories).Length == 2, "Product", "Unexpected projects", "Expected the fixture to retain exactly its two prepared projects.");
            _ = await GitOutputAsync(processRunner, workspace, ["diff", "--binary", "HEAD"], "git-diff.patch", cancellationToken);
            var changeSet = GitChangeSet.Parse(await GitOutputAsync(processRunner, workspace, ["status", "--porcelain=v1", "-z", "--untracked-files=all"], "git-status.porcelain", cancellationToken));
            AssertPreservation(assertions, workspace, changeSet, executionResponse.Response?.FactoryOutcome == "COMPLETED");
            AssertOrchestration(assertions, agentTrace);
            result.ProductPassed = !assertions.HasFailuresIn("Product");
            result.FactoryPassed = !assertions.HasFailuresIn("Factory contract") && !assertions.HasFailuresIn("Factory execution") && !assertions.HasFailuresIn("Factory protocol") && !assertions.HasFailuresIn("Factory") && !assertions.HasFailuresIn("Version") && !assertions.HasFailuresIn("Orchestration failure");
            result.Outcome = FactoryPostRunDiagnostics.Outcome(result.ProductPassed, result.FactoryPassed, !assertions.HasFailuresIn("Infrastructure"));
        }
        catch (Exception exception) when (exception is not Xunit.Sdk.XunitException)
        {
            assertions.Require(false, "Infrastructure", "Live eval execution", $"Live eval infrastructure failed: {exception.Message}");
            result.Outcome = "INFRASTRUCTURE_FAILURE";
        }
        finally
        {
            if (agentTrace.RootThreadId is null && agentTrace.Diagnostics.Count == 0)
                agentTrace = TryBuildAgentTrace(workspace, processInterrupted: false);
            await File.WriteAllTextAsync(workspace.AgentTracePath, JsonSerializer.Serialize(agentTrace, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) + "\n", cancellationToken);
            await assertions.WriteAsync(workspace, result, metrics, factoryResult, agentTrace);
        }
        assertions.ThrowIfFailed(workspace.RunDirectory);
    }

    private static Task LogProgressAsync(FactoryEvalWorkspace workspace, string message, CancellationToken cancellationToken)
        => File.AppendAllTextAsync(workspace.ProgressPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}", cancellationToken);

    private static AgentTrace TryBuildAgentTrace(FactoryEvalWorkspace workspace, bool processInterrupted)
    {
        var diagnostics = new List<AgentTraceDiagnostic>();
        var rootThreadId = CodexJsonlAnalyzer.TryReadRootThreadId(workspace.EventsPath);
        if (rootThreadId is null)
            return new(2, null, [], [new("ROOT_THREAD_ID_NOT_FOUND", "warning", "Root thread ID was not found in events.jsonl.", null, "events.jsonl")]);

        var runtimeTrace = FactoryRuntimeTraceReader.TryRead(workspace.WorkspaceDirectory, rootThreadId, processInterrupted);
        if (runtimeTrace is not null) return runtimeTrace;

        var sessions = new CodexHomeLocator().FindSessionsDirectory();
        if (sessions is null)
        {
            diagnostics.Add(new("CODEX_HOME_NOT_FOUND", "warning", "The standard Codex sessions directory was not found.", rootThreadId, null));
            return new(2, rootThreadId, [], diagnostics);
        }
        try { return new AgentTraceBuilder().Build(sessions, rootThreadId, processInterrupted); }
        catch (Exception exception) { return new(2, rootThreadId, [], [new("ROLLOUT_READ_FAILED", "warning", "Agent trace could not be built: " + exception.Message, rootThreadId, null)]); }
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

    private static void AssertPreservation(EvalAssertionCollector assertions, FactoryEvalWorkspace workspace, GitChangeSet changeSet, bool requireFactoryCleanup)
    {
        var unexpected = changeSet.Paths.Where(path => !path.StartsWith("src/MiniCatalog/", StringComparison.Ordinal)).ToArray();
        assertions.Require(unexpected.Length == 0, "Product", "Preservation boundaries", $"Expected only files under src/MiniCatalog/ to change, but found: {string.Join(", ", unexpected)}. Inspect verification/git-status.porcelain and git-diff.patch.");
        if (requireFactoryCleanup)
        {
            var current = Path.Combine(workspace.WorkspaceDirectory, ".idd", "factory", "current");
            assertions.Require(Directory.Exists(current) && !Directory.EnumerateFileSystemEntries(current).Any(), "Factory", "Factory cleanup", "Expected .idd/factory/current to exist and be empty after successful finalization.");
        }
    }

    internal static void AssertOrchestration(EvalAssertionCollector assertions, AgentTrace trace)
    {
        if (trace.RootThreadId is null || trace.Agents.Count == 0)
        {
            assertions.Require(false, "Orchestration failure", "Agent trace", "Expected a complete semantic agent trace, but no root trace was available.");
            return;
        }

        var byId = trace.Agents.ToDictionary(agent => agent.ThreadId, StringComparer.Ordinal);
        var roleCounts = trace.Agents.GroupBy(agent => agent.Role, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var expectedRoleCounts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["factory-root"] = 1,
            ["task-decomposer"] = 1,
            ["implementer"] = 2,
            ["checkpoint-reviewer"] = 1,
            ["final-reviewer"] = 1
        };
        assertions.Require(roleCounts.Count == expectedRoleCounts.Count && expectedRoleCounts.All(expected => roleCounts.GetValueOrDefault(expected.Key) == expected.Value), "Orchestration failure", "Semantic roles", $"Expected roles {FormatCounts(expectedRoleCounts)}; actual roles {FormatCounts(roleCounts)}.");

        var rootChildren = trace.Agents.Where(agent => agent.ParentThreadId == trace.RootThreadId).ToArray();
        assertions.Require(rootChildren.Length == 5 && rootChildren.Count(agent => agent.Role == "task-decomposer") == 1 && rootChildren.Count(agent => agent.Role == "implementer") == 2 && rootChildren.Count(agent => agent.Role == "checkpoint-reviewer") == 1 && rootChildren.Count(agent => agent.Role == "final-reviewer") == 1, "Orchestration failure", "Runtime topology", "Expected five direct semantic subprocess workers and no coordinator agents.");
        assertions.Require(!trace.Agents.Any(agent => agent.Role == "factory-step-coordinator"), "Orchestration failure", "Coordinator absence", "Expected factory-step-coordinator count to be zero.");
        assertions.Require(!trace.Agents.Any(agent => agent.Role == "factory-replanner"), "Orchestration failure", "Happy-path replan absence", "Expected factory-replanner count to be zero on the happy path.");
        assertions.Require(trace.Agents.Where(agent => agent.Role != "factory-root").All(agent => agent.Status == "completed"), "Orchestration failure", "Agent completion", "Expected every semantic subprocess worker to complete.");
    }

    private static string FormatCounts(IReadOnlyDictionary<string, int> counts) => string.Join(", ", counts.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => $"{item.Key}={item.Value}"));

}
