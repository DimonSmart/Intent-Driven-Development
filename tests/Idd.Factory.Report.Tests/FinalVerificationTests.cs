using System.Text.Json;
using Xunit;

namespace Idd.Factory.Report.Tests;

public sealed partial class FactoryReportTests
{
    private const string VerificationPolicy = """
        version: 1
        checks:
          build:
            run: dotnet build MiniCatalog.sln --no-restore
          tests:
            run: dotnet test MiniCatalog.sln --no-restore
        default:
          use: [tests]
        final:
          use: [build, tests]
        """;
    private const string TestSummary = "Passed!  - Failed: 0, Passed: 9, Skipped: 0, Total: 9";

    [Theory]
    [InlineData("success", "passed", "ok")]
    [InlineData("helper-failed", "passed", "ok")]
    [InlineData("recovered-without-planner", "passed", "warning")]
    [InlineData("recovered-with-planner", "passed", "ok")]
    [InlineData("missing", "unavailable", "warning")]
    [InlineData("before-done", "unavailable", "warning")]
    [InlineData("worker-only", "unavailable", "warning")]
    [InlineData("last-failed", "failed", "warning")]
    [InlineData("quiet-tests", "unavailable", "warning")]
    [InlineData("empty-tests", "unavailable", "warning")]
    [InlineData("echo", "unavailable", "warning")]
    [InlineData("compound", "unavailable", "warning")]
    [InlineData("wrong-directory", "unavailable", "warning")]
    [InlineData("missing-recheck", "unavailable", "warning")]
    [InlineData("later-question", "unavailable", "warning")]
    public void Report_FinalVerificationRequiresEveryCheckAndSeparatesRecoveryProtocol(
        string scenario, string expectedVerification, string expectedProtocol)
    {
        var repo = Path.Combine(_root, scenario);
        var sessions = Path.Combine(_root, scenario + "-codex", "sessions");
        Directory.CreateDirectory(Path.Combine(repo, ".idd"));
        File.WriteAllText(Path.Combine(repo, ".idd", "verification.yaml"), VerificationPolicy);
        const string root = "11111111-1111-1111-1111-111111111111";
        const string planner = "22222222-2222-2222-2222-222222222222";
        const string recovery = "33333333-3333-3333-3333-333333333333";
        var lines = new List<object>
        {
            Session(root, repo), User("2026-09-23T10:00:00Z", "Use idd-factory-run"),
            NativeSpawn("2026-09-23T10:00:01Z", "p1", "Use idd-factory-decompose-task", planner)
        };
        WriteChild(sessions, "planner.jsonl", planner, root, repo,
            Assistant("2026-09-23T10:01:00Z", "# Done"));
        if (scenario == "helper-failed")
            lines.Add(NativeCommandCompleted("2026-09-23T10:01:01Z", "helper", "cat missing.txt", 1));
        if (scenario.StartsWith("recovered-", StringComparison.Ordinal) || scenario == "missing-recheck")
        {
            lines.Add(NativeCommandCompleted("2026-09-23T10:01:02Z", "failed-build", "dotnet build MiniCatalog.sln --no-restore", 1));
            if (scenario != "recovered-without-planner")
            {
                lines.Add(NativeCommandCompleted("2026-09-23T10:01:03Z", "diagnostic", "write verification-failure.md", 0));
                lines.Add(NativeSpawn("2026-09-23T10:01:05Z", "p2", "Use idd-factory-decompose-task", recovery));
                WriteChild(sessions, "recovery.jsonl", recovery, root, repo,
                    Assistant("2026-09-23T10:02:00Z", "# Done"));
            }
        }
        var build = VerificationCommand("2026-09-23T10:03:00Z", "build", "dotnet build MiniCatalog.sln --no-restore", 0, "Build succeeded.");
        var test = VerificationCommand("2026-09-23T10:03:05Z", "test",
            "/bin/zsh -lc 'dotnet test MiniCatalog.sln --no-restore'", scenario == "last-failed" ? 1 : 0,
            scenario == "quiet-tests" ? "" : scenario == "empty-tests" ? "Passed! - Failed: 0, Passed: 0, Total: 0" : TestSummary);
        if (scenario == "before-done")
        {
            lines.Add(VerificationCommand("2026-09-23T10:00:10Z", "early-build", "dotnet build MiniCatalog.sln --no-restore", 0, "Build succeeded."));
            lines.Add(VerificationCommand("2026-09-23T10:00:20Z", "early-test", "dotnet test MiniCatalog.sln --no-restore", 0, TestSummary));
        }
        else if (scenario == "worker-only")
        {
            const string worker = "44444444-4444-4444-4444-444444444444";
            lines.Add(NativeSpawn("2026-09-23T10:00:10Z", "w", "Use idd-factory-execute-subtask", worker));
            WriteChild(sessions, "worker.jsonl", worker, root, repo, build, test,
                Assistant("2026-09-23T10:00:59Z", "completed"));
        }
        else
        {
            if (scenario != "missing-recheck") lines.Add(build);
            if (scenario == "echo") lines.Add(VerificationCommand("2026-09-23T10:03:05Z", "fake", "echo 'dotnet test MiniCatalog.sln --no-restore'", 0, TestSummary));
            else if (scenario == "compound") lines.Add(VerificationCommand("2026-09-23T10:03:05Z", "compound", "dotnet test MiniCatalog.sln --no-restore; true", 0, TestSummary));
            else if (scenario == "wrong-directory") lines.Add(VerificationCommand("2026-09-23T10:03:05Z", "elsewhere", "dotnet test MiniCatalog.sln --no-restore", 0, TestSummary, "/other-repo"));
            else if (scenario != "missing") lines.Add(test);
        }
        if (scenario == "later-question")
        {
            lines.Add(NativeSpawn("2026-09-23T10:03:10Z", "question", "Use idd-factory-decompose-task", recovery));
            WriteChild(sessions, "question.jsonl", recovery, root, repo,
                Assistant("2026-09-23T10:03:20Z", "# Question\nWhich contract should apply?"));
        }
        lines.Add(Assistant("2026-09-23T10:04:00Z", "{\"status\":\"COMPLETED\",\"reason\":\"Done\"}"));
        Write(Path.Combine(sessions, "root.jsonl"), lines);
        var report = Assert.Single(new Idd.Factory.Report.FactoryReportEngine().FindRuns(repo, Path.GetDirectoryName(sessions)!));
        Assert.Equal(expectedVerification, report.Completion.ProjectVerification);
        Assert.Equal(expectedProtocol, report.Completion.ProtocolValidation);
        if (scenario == "recovered-without-planner")
        {
            Assert.Contains(report.Diagnostics, diagnostic => diagnostic.Code == "verification_failure_without_fresh_planner");
            Assert.Equal(1, report.Metrics.Tools.FailedCommands);
            Assert.Contains(report.Timeline, item => item.Kind == "failed-command");
        }
    }

    [Theory]
    [InlineData("version: 2\nchecks: {}\ndefault: {use: [tests]}")]
    [InlineData("# heading\n```yaml\nversion: 1\n```")]
    [InlineData("version: 1\nchecks: {tests: {run: dotnet test}}\ndefault: {use: [missing]}")]
    [InlineData("version: 1\nchecks: {tests: {run: dotnet test}}\ndefault: {use: [tests]}\nunknown: {}")]
    [InlineData("version: 1\nchecks: {tests: {run: dotnet test}}\ndefault: {use: [tests]}\nfinal: {rules: [{paths: [src/**], use: [tests]}]}")]
    public void Report_DoesNotFallbackWhenPolicyCannotBeResolved(string policy)
    {
        var report = ReportWithPolicyAndCommands(policy, true,
            VerificationCommand("2026-09-23T10:03:00Z", "test", "dotnet test", 0, TestSummary));
        Assert.Equal("unavailable", report.Completion.ProjectVerification);
        Assert.Contains(report.Diagnostics, diagnostic => diagnostic.Code == "verification_policy_unavailable");
    }

    [Fact]
    public void Report_ManualFinalCheckCannotBeApprovedByAnAutomatedCommand()
    {
        var report = ReportWithPolicyAndCommands("version: 1\nchecks: {manual: {instructions: Inspect UI}}\ndefault: {use: [manual]}", true,
            NativeCommandCompleted("2026-09-23T10:03:00Z", "build", "dotnet build", 0));
        Assert.Equal("unavailable", report.Completion.ProjectVerification);
    }

    [Theory]
    [InlineData("git status")]
    [InlineData("echo done")]
    [InlineData("dotnet restore")]
    public void Report_DoesNotInferCompletionFromUnrelatedSuccessfulCommand(string command)
    {
        var report = ReportWithPolicyAndCommands(null, false,
            NativeCommandCompleted("2026-09-23T10:03:00Z", "helper", command, 0));
        Assert.NotEqual("completed", report.Run.Result);
        Assert.Equal("unavailable", report.Completion.ProjectVerification);
    }

    [Fact]
    public void Report_NormalizesNativeShellArgvAndYamlQuotedCommands()
    {
        var command = JsonSerializer.Serialize(new[] { "/bin/zsh", "-lc", "dotnet test --filter 'FullyQualifiedName~CatalogIntegrationTests'" });
        var report = ReportWithPolicyAndCommands("version: 1\nchecks:\n  tests:\n    run: >-\n      dotnet test --filter \"FullyQualifiedName~CatalogIntegrationTests\"\ndefault: {use: [tests]}", false,
            VerificationCommand("2026-09-23T10:03:00Z", "test", command, 0, TestSummary));
        Assert.Equal("passed", report.Completion.ProjectVerification);
        Assert.Equal("completed", report.Run.Result);
    }

    [Theory]
    [InlineData("[\"dotnet\", null]")]
    [InlineData("[\"dotnet\", 42]")]
    [InlineData("[null, \"test\"]")]
    [InlineData("[]")]
    public void Report_ToleratesMalformedCommandArgvWithoutInventingVerification(string command)
    {
        var report = ReportWithPolicyAndCommands(null, false,
            VerificationCommand("2026-09-23T10:03:00Z", "bad-command", command, 0, TestSummary));
        Assert.Equal("unavailable", report.Completion.ProjectVerification);
        Assert.NotEqual("completed", report.Run.Result);
    }

    private Idd.Factory.Report.FactoryRunReport ReportWithPolicyAndCommands(string? policy, bool declare, params object[] commands)
    {
        var repo = Path.Combine(_root, "repo");
        var codex = Path.Combine(_root, "codex");
        var sessions = Path.Combine(codex, "sessions");
        Directory.CreateDirectory(Path.Combine(repo, ".idd"));
        if (policy is not null) File.WriteAllText(Path.Combine(repo, ".idd", "verification.yaml"), policy);
        const string root = "11111111-1111-1111-1111-111111111111";
        const string planner = "22222222-2222-2222-2222-222222222222";
        var lines = new List<object>
        {
            Session(root, repo), User("2026-09-23T10:00:00Z", "Use idd-factory-run"),
            NativeSpawn("2026-09-23T10:00:01Z", "planner", "Use idd-factory-decompose-task", planner)
        };
        lines.AddRange(commands);
        if (declare) lines.Add(Assistant("2026-09-23T10:04:00Z", "{\"status\":\"COMPLETED\"}"));
        Write(Path.Combine(sessions, "root.jsonl"), lines);
        WriteChild(sessions, "planner.jsonl", planner, root, repo,
            Assistant("2026-09-23T10:01:00Z", "# Done"));
        return Assert.Single(new Idd.Factory.Report.FactoryReportEngine().FindRuns(repo, codex));
    }

    private static object VerificationCommand(string timestamp, string id, string command, int exit, string output, string? cwd = null) => new
    {
        timestamp, type = "item.completed",
        item = new { id, type = "command_execution", command, cwd, exit_code = exit,
            status = exit == 0 ? "completed" : "failed", aggregated_output = output }
    };
}
