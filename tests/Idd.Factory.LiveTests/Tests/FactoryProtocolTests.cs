using Idd.Factory.LiveTests.Infrastructure;
using Idd.Factory.LiveTests.Environments;
using Idd.Factory.LiveTests.Models;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class FactoryProtocolTests
{
    [Fact]
    public void FactoryResultReader_ReadsValidResult()
    {
        using var fixture = new FactoryFixture();
        fixture.WriteResult(ValidFactoryResult);

        var result = FactoryResultReader.TryReadSingle(fixture.Workspace);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData("missing-results")]
    [InlineData("multiple-directories")]
    [InlineData("missing-result-file")]
    [InlineData("invalid-json")]
    [InlineData("invalid-shape")]
    public void FactoryResultReader_ReturnsDiagnosticsForExpectedFailures(string scenario)
    {
        using var fixture = new FactoryFixture();
        switch (scenario)
        {
            case "multiple-directories": fixture.WriteResult(ValidFactoryResult, "first"); fixture.WriteResult(ValidFactoryResult, "second"); break;
            case "missing-result-file": Directory.CreateDirectory(Path.Combine(fixture.Workspace, ".idd", "factory", "results", "run")); break;
            case "invalid-json": fixture.WriteResult("{"); break;
            case "invalid-shape": fixture.WriteResult("[]"); break;
        }

        var result = FactoryResultReader.TryReadSingle(fixture.Workspace);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"factoryOutcome\":\"COMPLETED\",\"factoryResultPath\":\".idd/factory/results/run/factory-result.json\",\"reason\":null}", true)]
    [InlineData("{\"schemaVersion\":1,\"factoryOutcome\":\"COMPLETED\",\"factoryResultPath\":null,\"reason\":null}", false)]
    [InlineData("{\"schemaVersion\":1,\"factoryOutcome\":\"BLOCKED\",\"factoryResultPath\":null,\"reason\":\"Repository evidence is unavailable.\"}", true)]
    [InlineData("{\"schemaVersion\":1,\"factoryOutcome\":\"BLOCKED\",\"factoryResultPath\":\"result.json\",\"reason\":\"Stopped.\"}", false)]
    [InlineData("{\"schemaVersion\":1,\"factoryOutcome\":\"BLOCKED\",\"factoryResultPath\":null,\"reason\":null}", false)]
    public void ExecutionResponseReader_ValidatesSemantics(string json, bool expectedSuccess)
    {
        using var fixture = new FactoryFixture();
        fixture.WriteResult(ValidFactoryResult);
        File.WriteAllText(fixture.LastMessagePath, json);

        var result = ExecutionResponseReader.TryRead(fixture.LastMessagePath, fixture.Workspace);

        Assert.Equal(expectedSuccess, result.IsSuccess);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("invalid")]
    public void ExecutionResponseReader_ReturnsDiagnosticForMissingOrInvalidJson(string scenario)
    {
        using var fixture = new FactoryFixture();
        if (scenario == "invalid") File.WriteAllText(fixture.LastMessagePath, "{");

        var result = ExecutionResponseReader.TryRead(fixture.LastMessagePath, fixture.Workspace);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("COMPLETED", true, false, false)]
    [InlineData("COMPLETED", false, true, false)]
    [InlineData("BLOCKED", false, false, true)]
    [InlineData("BLOCKED", true, true, true)]
    public void PostRunDiagnostics_ClassifiesExecutionAndResult(string outcome, bool createResult, bool expectsContractFailure, bool expectsExecutionFailure)
    {
        using var fixture = new FactoryFixture();
        if (createResult) fixture.WriteResult(ValidFactoryResult);
        File.WriteAllText(fixture.LastMessagePath, $"{{\"schemaVersion\":1,\"factoryOutcome\":\"{outcome}\",\"factoryResultPath\":{(outcome == "COMPLETED" ? "\".idd/factory/results/run/factory-result.json\"" : "null")},\"reason\":{(outcome == "COMPLETED" ? "null" : "\"Stopped.\"")}}}");
        var assertions = new EvalAssertionCollector();

        FactoryPostRunDiagnostics.Assert(assertions, ExecutionResponseReader.TryRead(fixture.LastMessagePath, fixture.Workspace), FactoryResultReader.TryReadSingle(fixture.Workspace), "1.0");

        Assert.Equal(expectsContractFailure, assertions.HasFailuresIn("Factory contract"));
        Assert.Equal(expectsExecutionFailure, assertions.HasFailuresIn("Factory execution"));
    }

    [Fact]
    public async Task FinalProductVerification_RunsBuildAndTestsAfterMissingFactoryResult()
    {
        using var fixture = new FactoryFixture();
        var environment = new RecordingEnvironment();
        var workspace = new FactoryEvalWorkspace(fixture.Workspace, fixture.Workspace, fixture.Workspace, fixture.Workspace, fixture.Workspace);
        Assert.False(FactoryResultReader.TryReadSingle(fixture.Workspace).IsSuccess);

        await FinalProductVerification.RunAsync(environment, workspace, CancellationToken.None);

        Assert.Equal(["build", "test"], environment.Commands);
    }

    [Theory]
    [InlineData("src/canonical/skills/idd-factory-coordinate-step.md")]
    [InlineData("src/canonical/skills/idd-factory-decompose-task.md")]
    public void CanonicalSkills_BoundReadOnlyRecoveryWithoutPolicyEscalation(string relativePath)
    {
        var content = File.ReadAllText(Path.Combine(RepositoryRootFinder.Find(), relativePath));

        Assert.Contains("at most two", content, StringComparison.Ordinal);
        Assert.Contains("narrower", content, StringComparison.Ordinal);
        Assert.Contains("repeat", content, StringComparison.Ordinal);
        Assert.Contains("elevate permissions", content, StringComparison.Ordinal);
        Assert.Contains("approval or sandbox policy", content, StringComparison.Ordinal);
        Assert.Contains("only after", content, StringComparison.Ordinal);
    }

    [Fact]
    public void CodexRun_UsesWorkspaceWriteSandboxAndReadsPromptFromStdin()
    {
        using var fixture = new FactoryFixture();
        var caseDirectory = Path.Combine(RepositoryRootFinder.Find(), "tests", "Idd.Factory.LiveTests", "Cases", "TwoStepCatalog");
        var workspace = new FactoryEvalWorkspace(fixture.Workspace, fixture.Workspace, fixture.Workspace, fixture.Workspace, caseDirectory);
        var options = new FactoryEvalOptions("model", "medium", TimeSpan.FromMinutes(1), "1.0");

        var arguments = LocalFactoryEvalEnvironment.BuildRunCodexArguments(workspace, options, "isolated-workspace-write");

        Assert.Equal(["--sandbox", "workspace-write"], arguments.SkipWhile(argument => argument != "--sandbox").Take(2));
        Assert.Contains("--ignore-user-config", arguments);
        Assert.True(HasOption(arguments, "--enable", "multi_agent"));
        Assert.True(HasOption(arguments, "--disable", "multi_agent_v2"));
        Assert.True(HasOption(arguments, "-c", "agents.max_threads=10"));
        Assert.True(HasOption(arguments, "--disable", "plugins"));
        Assert.True(HasOption(arguments, "--disable", "apps"));
        Assert.Contains("mcp_servers={}", arguments);
        Assert.DoesNotContain(arguments, argument => argument.StartsWith("windows.sandbox=", StringComparison.Ordinal));
        Assert.Equal("-", arguments[^1]);
    }

    [Theory]
    [InlineData("isolated-workspace-write", true, null)]
    [InlineData("configured-workspace-write", false, null)]
    [InlineData("windows-unelevated-workspace-write", false, "windows.sandbox=\"unelevated\"")]
    [InlineData("windows-elevated-workspace-write", false, "windows.sandbox=\"elevated\"")]
    public void CodexRun_BuildsNamedLaunchProfile(string profileName, bool ignoresUserConfig, string? windowsSandbox)
    {
        using var fixture = new FactoryFixture();
        var caseDirectory = Path.Combine(RepositoryRootFinder.Find(), "tests", "Idd.Factory.LiveTests", "Cases", "TwoStepCatalog");
        var workspace = new FactoryEvalWorkspace(fixture.Workspace, fixture.Workspace, fixture.Workspace, fixture.Workspace, caseDirectory);
        var options = new FactoryEvalOptions("model", "medium", TimeSpan.FromMinutes(1), "1.0");

        var arguments = LocalFactoryEvalEnvironment.BuildRunCodexArguments(workspace, options, profileName);

        Assert.Equal(ignoresUserConfig, arguments.Contains("--ignore-user-config"));
        Assert.True(HasOption(arguments, "-c", "approval_policy=never"));
        Assert.True(HasOption(arguments, "--enable", "multi_agent"));
        Assert.True(HasOption(arguments, "--disable", "multi_agent_v2"));
        Assert.Equal(["--sandbox", "workspace-write"], arguments.SkipWhile(argument => argument != "--sandbox").Take(2));
        if (windowsSandbox is null)
            Assert.DoesNotContain(arguments, argument => argument.StartsWith("windows.sandbox=", StringComparison.Ordinal));
        else
            Assert.True(HasOption(arguments, "-c", windowsSandbox));
        Assert.DoesNotContain("danger-full-access", arguments);
    }

    [Fact]
    public void CodexRun_RejectsUnknownLaunchProfile()
    {
        using var fixture = new FactoryFixture();
        var workspace = new FactoryEvalWorkspace(fixture.Workspace, fixture.Workspace, fixture.Workspace, fixture.Workspace, fixture.Workspace);
        var options = new FactoryEvalOptions("model", "medium", TimeSpan.FromMinutes(1), "1.0");

        var exception = Assert.Throws<InvalidOperationException>(() => LocalFactoryEvalEnvironment.BuildRunCodexArguments(workspace, options, "surprising-profile"));

        Assert.Contains("Unknown Codex launch profile", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LaunchProfileCommandLine_RedactsNamedSecrets()
    {
        var commandLine = CodexLaunchProfileReport.FormatCommandLine("codex", ["exec", "OPENAI_API_KEY=secret-value", "--sandbox", "workspace-write"]);

        Assert.DoesNotContain("secret-value", commandLine, StringComparison.Ordinal);
        Assert.Contains("OPENAI_API_KEY=<redacted>", commandLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaunchProfileReport_AggregatesAttemptsAndSelectsFirstPassingProfile()
    {
        var repositoryRoot = Path.Combine(Path.GetTempPath(), "idd-launch-profile-report-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var discoveryDirectory = Path.Combine(repositoryRoot, "artifacts", "factory-evals", "codex-launch-profiles", "discovery");
            var isolatedDirectory = Path.Combine(discoveryDirectory, "isolated-workspace-write", "attempt-1");
            var configuredDirectory = Path.Combine(discoveryDirectory, "configured-workspace-write", "attempt-2");
            Directory.CreateDirectory(isolatedDirectory);
            Directory.CreateDirectory(configuredDirectory);
            await CodexLaunchProfileReport.WriteAsync(repositoryRoot, "discovery", CreateAttempt("isolated-workspace-write", isolatedDirectory, passed: false), CancellationToken.None);
            await CodexLaunchProfileReport.WriteAsync(repositoryRoot, "discovery", CreateAttempt("configured-workspace-write", configuredDirectory, passed: true), CancellationToken.None);

            var report = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "artifacts", "factory-evals", "codex-launch-profile-report.md"));

            Assert.Contains("| `isolated-workspace-write` | FAIL |", report, StringComparison.Ordinal);
            Assert.Contains("| `configured-workspace-write` | PASS |", report, StringComparison.Ordinal);
            Assert.Contains("Selected profile: `configured-workspace-write`", report, StringComparison.Ordinal);
            Assert.Contains("- Failure reason: none", report, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(repositoryRoot)) Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    private static CodexLaunchProfileAttempt CreateAttempt(string profileName, string attemptDirectory, bool passed)
        => new(profileName, attemptDirectory, "codex exec", "codex 1.0", 0, false, Path.Combine(attemptDirectory, "stderr.log"), Path.Combine(attemptDirectory, "events.jsonl"), passed, passed ? "WORKSPACE_WRITE_OK" : null, true, passed ? "WORKSPACE_UPDATE_OK" : "WORKSPACE_UPDATE_PENDING", passed ? null : "created file is missing", passed);

    [Theory]
    [InlineData("{\"result\":\"WORKSPACE_WRITE_OK\"}", true)]
    [InlineData("{\"result\":\"WRONG\"}", false)]
    [InlineData("{\"result\":\"WORKSPACE_WRITE_OK\",\"extra\":true}", false)]
    [InlineData("not json", false)]
    public void WorkspaceWriteProbeResponse_RequiresTheExpectedStructuredResult(string content, bool expectedPassed)
    {
        using var fixture = new FactoryFixture();
        File.WriteAllText(fixture.LastMessagePath, content);

        var result = CodexLaunchProfileReport.TryReadProbeResponse(fixture.LastMessagePath, "WORKSPACE_WRITE_OK");

        Assert.Equal(expectedPassed, result.Passed);
    }

    private static bool HasOption(IReadOnlyList<string> arguments, string option, string value) => arguments.Select((argument, index) => (argument, index)).Any(pair => pair.argument == option && pair.index + 1 < arguments.Count && arguments[pair.index + 1] == value);

    [Fact]
    public void CodexEnvironment_RemovesWindowsAppsShellsFromPath()
    {
        var path = string.Join(Path.PathSeparator, @"C:\Tools", @"C:\Program Files\WindowsApps\PowerShell", @"C:\Windows\System32\WindowsPowerShell\v1.0");

        var environment = LocalFactoryEvalEnvironment.BuildCodexEnvironment(path, isWindows: true);

        Assert.Equal(string.Join(Path.PathSeparator, @"C:\Tools", @"C:\Windows\System32\WindowsPowerShell\v1.0"), environment["PATH"]);
    }

    private const string ValidFactoryResult = "{\"methodologyVersion\":\"1.0\",\"factoryOutcome\":\"COMPLETED\",\"subtaskCount\":2,\"completedSubtaskCount\":2,\"reviewCheckpointCount\":1,\"completedReviewCheckpointCount\":1,\"correctiveSubtaskCount\":0,\"blockedItemCount\":0,\"incompleteItemCount\":0,\"finalReviewVerdict\":\"approved\",\"verificationStatus\":\"passed\",\"commitMessagePath\":\"notes/commit-message.md\"}";

    private sealed class FactoryFixture : IDisposable
    {
        public string Workspace { get; } = Path.Combine(Path.GetTempPath(), "idd-factory-tests", Guid.NewGuid().ToString("N"));
        public string LastMessagePath => Path.Combine(Workspace, "last-message.json");
        public FactoryFixture() { Directory.CreateDirectory(Workspace); Directory.CreateDirectory(Path.Combine(Workspace, "notes")); File.WriteAllText(Path.Combine(Workspace, "notes", "commit-message.md"), "message"); }
        public void WriteResult(string content, string directory = "run") { var path = Path.Combine(Workspace, ".idd", "factory", "results", directory); Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "factory-result.json"), content); }
        public void Dispose() { if (Directory.Exists(Workspace)) Directory.Delete(Workspace, true); }
    }

    private sealed class RecordingEnvironment : IFactoryEvalEnvironment
    {
        public List<string> Commands { get; } = [];
        public Task PrepareAsync(FactoryEvalWorkspace workspace, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ProcessResult> RunCodexAsync(FactoryEvalWorkspace workspace, FactoryEvalOptions options, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProcessResult> RunCommandAsync(FactoryEvalWorkspace workspace, string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Commands.Add(arguments[0]);
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new ProcessResult(0, now, now, false, string.Empty, string.Empty));
        }
    }
}
