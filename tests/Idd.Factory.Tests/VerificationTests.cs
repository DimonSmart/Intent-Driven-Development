using Idd.Factory.Verification;

namespace Idd.Factory.Tests;

public sealed class VerificationTests
{
    [Fact] public async Task UnknownCheckIsRejected()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  known:\n    run: exit 0\ndefault:\n  use:\n    - known\n");
        var engine = new VerificationEngine(temp.Path, System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"));
        Assert.Equal("UNKNOWN_VERIFICATION_CHECK", (await Assert.ThrowsAsync<VerificationException>(() => engine.RunAsync(["missing"], default))).Code);
    }

    [Fact] public async Task SuccessfulCheckCreatesEvidence()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  pass:\n    run: exit 0\n    timeout: 10s\ndefault:\n  use:\n    - pass\n");
        var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"); var evidence = await new VerificationEngine(temp.Path, current).RunAsync(["pass"], default);
        Assert.Equal(VerificationStatus.Passed, evidence.Status); Assert.Single(evidence.Evidence); Assert.Equal("passed", evidence.Evidence[0].Status); Assert.Equal(3, evidence.Evidence[0].SchemaVersion); Assert.True(File.Exists(System.IO.Path.Combine(current, "verification", evidence.Evidence[0].EvidenceId + ".json")));
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(current, "verification", evidence.Evidence[0].EvidenceId + ".json")));
        Assert.False(document.RootElement.TryGetProperty("workspaceFingerprint", out _));
        Assert.False(document.RootElement.TryGetProperty("output", out _));
        var stdout = document.RootElement.GetProperty("stdout");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, stdout.GetProperty("path").ValueKind);
        Assert.Equal(0, stdout.GetProperty("byteLength").GetInt64());
        Assert.Equal("", stdout.GetProperty("tail").GetString());
        Assert.False(stdout.GetProperty("truncated").GetBoolean());
    }

    [Fact] public async Task FinalContextRunsItsAssignedChecks()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  default-check:\n    run: exit 0\n  final-check:\n    run: exit 0\ndefault:\n  use:\n    - default-check\nfinal:\n  use:\n    - final-check\n");
        var evidence = await new VerificationEngine(temp.Path, System.IO.Path.Combine(temp.Path, ".idd", "factory", "current")).RunContextAsync("final", default);
        Assert.Equal(VerificationStatus.Passed, evidence.Status); Assert.Single(evidence.Evidence); Assert.Equal("final-check", evidence.Evidence[0].CheckId);
    }

    [Fact] public async Task PathRulesSelectFirstMatchingRule()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  backend:\n    run: exit 0\n  default-check:\n    run: exit 1\ndefault:\n  use:\n    - default-check\nsubtask:\n  rules:\n    - paths:\n        - src/backend/**\n      use:\n        - backend\n    - fallback: true\n      use:\n        - default-check\n");
        var result = await new VerificationEngine(temp.Path, Path.Combine(temp.Path, ".idd", "factory", "current")).RunContextAsync("subtask", ["src\\backend\\A.cs"], default);
        Assert.Equal(VerificationStatus.Passed, result.Status); Assert.Equal("backend", Assert.Single(result.Evidence).CheckId);
    }

    [Fact] public async Task ConfirmedCheckNeverRunsBeforeExplicitConfirmation()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  expensive:\n    run: exit 0\n    confirmation: required\ndefault:\n  use:\n    - expensive\n");
        var engine = new VerificationEngine(temp.Path, Path.Combine(temp.Path, ".idd", "factory", "current"));
        var pending = await engine.RunAsync(["expensive"], default);
        Assert.Equal(VerificationStatus.ConfirmationRequired, pending.Status); Assert.Empty(pending.Evidence);
        var completed = await engine.RunCheckAsync("expensive", confirmed: true, manualPassed: null, default);
        Assert.Equal(VerificationStatus.Passed, completed.Status);
    }

    [Fact] public async Task ExplicitConfirmationDeclineRecordsNotVerifiedEvidenceWithoutRunningTheCheck()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  expensive:\n    run: throw 'must not run'\n    confirmation: required\ndefault:\n  use:\n    - expensive\n");
        var engine = new VerificationEngine(temp.Path, Path.Combine(temp.Path, ".idd", "factory", "current"));
        var pending = await engine.RunCheckAsync("expensive", confirmed: false, manualPassed: null, default);

        var declined = await engine.DeclineCheckAsync("expensive", (await engine.GetCheckDefinitionHashAsync("expensive", default)), (await engine.ResolveContextAsync("subtask", [], default)).PolicyHash, default);

        Assert.Equal(VerificationStatus.Declined, declined.Status); Assert.Equal("not-verified", Assert.Single(declined.Evidence).Status); Assert.Equal(pending.PendingCheckId, declined.PendingCheckId);
    }

    [Fact] public async Task FailedCheckReturnsStructuredResultAndPersistsEvidence()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  fail:\n    run: exit 7\ndefault:\n  use:\n    - fail\n");
        var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current");
        var result = await new VerificationEngine(temp.Path, current).RunAsync(["fail"], default);
        Assert.Equal(VerificationStatus.Failed, result.Status); Assert.Equal(7, Assert.Single(result.Evidence).ExitCode);
        Assert.True(File.Exists(System.IO.Path.Combine(current, "verification", result.Evidence[0].EvidenceId + ".json")));
    }

    [Fact] public async Task ManualCheckRequiresExplicitResultWithoutRunning()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  manual:\n    instructions: Confirm behavior\ndefault:\n  use:\n    - manual\n");
        var result = await new VerificationEngine(temp.Path, System.IO.Path.Combine(temp.Path, ".idd", "factory", "current")).RunAsync(["manual"], default);
        Assert.Equal(VerificationStatus.ResultRequired, result.Status); Assert.Empty(result.Evidence); Assert.Equal("manual", result.PendingCheckId);
    }

    [Fact] public async Task RunnerTimeoutIsInfrastructureFailure()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  timeout:\n    run: dotnet --info\n    timeout: 0s\ndefault:\n  use:\n    - timeout\n");
        var result = await new VerificationEngine(temp.Path, System.IO.Path.Combine(temp.Path, ".idd", "factory", "current")).RunAsync(["timeout"], default);
        Assert.Equal(VerificationStatus.InfrastructureFailure, result.Status); Assert.Equal("infrastructure-failure", Assert.Single(result.Evidence).Status);
    }

    [Fact] public async Task TimeoutPreservesIncrementallyCapturedOutputAndMetadata()
    {
        using var temp = new TestWorkspace();
        var command = OperatingSystem.IsWindows() ? "[Console]::Out.Write('partial'); Start-Sleep -Seconds 5" : "printf partial; sleep 5";
        temp.Write(".idd/verification.yaml", $"version: 1\nchecks:\n  timeout:\n    run: >-\n      {command}\n    timeout: 1s\ndefault:\n  use:\n    - timeout\n");
        var current = Path.Combine(temp.Path, ".idd", "factory", "current");

        var result = await new VerificationEngine(temp.Path, current).RunAsync(["timeout"], default);

        var evidence = Assert.Single(result.Evidence);
        Assert.True(evidence.TimedOut); Assert.Null(evidence.ExitCode); Assert.Contains("partial", evidence.StdoutTail);
        Assert.Equal("timeout", evidence.PrimaryFailure?.Kind); Assert.NotNull(evidence.Termination);
        Assert.StartsWith(".idd/factory/current/verification/", evidence.StdoutPath);
        Assert.True(File.Exists(Path.Combine(temp.Path, evidence.StdoutPath!.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact] public async Task LargeOutputIsPersistedWhileInlineTailIsUtf8Bounded()
    {
        using var temp = new TestWorkspace();
        var command = OperatingSystem.IsWindows() ? "[Console]::Out.Write(('🙂' * 20000))" : "i=0; while [ $i -lt 20000 ]; do printf '🙂'; i=$((i+1)); done";
        temp.Write(".idd/verification.yaml", $"version: 1\nchecks:\n  large:\n    run: >-\n      {command}\n    timeout: 20s\ndefault:\n  use:\n    - large\n");
        var current = Path.Combine(temp.Path, ".idd", "factory", "current");

        var evidence = Assert.Single((await new VerificationEngine(temp.Path, current).RunAsync(["large"], default)).Evidence);

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(evidence.StdoutTail) <= 4 * 1024);
        Assert.DoesNotContain('\uFFFD', evidence.StdoutTail);
        var logLength = new FileInfo(Path.Combine(temp.Path, evidence.StdoutPath!.Replace('/', Path.DirectorySeparatorChar))).Length;
        Assert.Equal(logLength, evidence.Stdout.ByteLength); Assert.True(evidence.Stdout.Truncated);
        Assert.True(logLength > 16 * 1024);
    }

    [Fact] public async Task PersistedV3StreamsHaveExactShapeAndFortyLineTail()
    {
        using var temp = new TestWorkspace();
        var command = OperatingSystem.IsWindows() ? "1..60 | ForEach-Object { Write-Output \"line-$_\" }" : "i=1; while [ $i -le 60 ]; do echo line-$i; i=$((i+1)); done";
        temp.Write(".idd/verification.yaml", $"version: 1\nchecks:\n  lines:\n    run: >-\n      {command}\ndefault:\n  use:\n    - lines\n");
        var current = Path.Combine(temp.Path, ".idd", "factory", "current");

        var evidence = Assert.Single((await new VerificationEngine(temp.Path, current).RunAsync(["lines"], default)).Evidence);
        using var document = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(current, "verification", evidence.EvidenceId + ".json")));

        var root = document.RootElement;
        Assert.Equal(3, root.GetProperty("schemaVersion").GetInt32()); Assert.False(root.TryGetProperty("output", out _));
        Assert.False(root.TryGetProperty("stdoutPath", out _)); Assert.False(root.TryGetProperty("stdoutTail", out _));
        Assert.False(root.TryGetProperty("primaryFailure", out _)); Assert.False(root.TryGetProperty("secondaryIssues", out _));
        Assert.False(root.TryGetProperty("evidencePersisted", out _));
        Assert.True(root.TryGetProperty("durationMs", out _)); Assert.True(root.TryGetProperty("timeoutMs", out _));
        Assert.False(root.TryGetProperty("durationMilliseconds", out _)); Assert.False(root.TryGetProperty("timeoutMilliseconds", out _));
        Assert.Equal(new[] { "display", "workingDirectory" }, root.GetProperty("command").EnumerateObject().Select(x => x.Name).Order().ToArray());
        Assert.Equal(".", root.GetProperty("command").GetProperty("workingDirectory").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, root.GetProperty("failureKind").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, root.GetProperty("failureStage").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, root.GetProperty("summary").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, root.GetProperty("exception").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, root.GetProperty("termination").ValueKind);
        Assert.Equal(0, root.GetProperty("diagnosticIssues").GetArrayLength());
        var stdout = root.GetProperty("stdout");
        Assert.True(stdout.GetProperty("truncated").GetBoolean()); Assert.True(stdout.GetProperty("tail").GetString()!.Split('\n').Length <= 40);
        Assert.Equal(new FileInfo(Path.Combine(temp.Path, stdout.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar))).Length,
            stdout.GetProperty("byteLength").GetInt64());
        var stderr = root.GetProperty("stderr");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, stderr.GetProperty("path").ValueKind); Assert.Equal(0, stderr.GetProperty("byteLength").GetInt64());
        Assert.Equal("", stderr.GetProperty("tail").GetString()); Assert.False(stderr.GetProperty("truncated").GetBoolean());
    }

    [Fact] public async Task OutputWrittenImmediatelyBeforeTimeoutIsDrained()
    {
        using var temp = new TestWorkspace();
        var command = OperatingSystem.IsWindows() ? "1..100 | ForEach-Object { Write-Output 'late-output'; Start-Sleep -Milliseconds 100 }" : "i=0; while [ $i -lt 100 ]; do printf 'late-output\\n'; sleep .1; i=$((i+1)); done";
        temp.Write(".idd/verification.yaml", $"version: 1\nchecks:\n  check:\n    run: >-\n      {command}\n    timeout: 2s\ndefault:\n  use:\n    - check\n");

        var evidence = Assert.Single((await new VerificationEngine(temp.Path, Path.Combine(temp.Path, ".idd", "factory", "current")).RunAsync(["check"], default)).Evidence);

        Assert.True(evidence.TimedOut); Assert.Contains("late-output", evidence.StdoutTail);
    }

    [Fact] public async Task ProcessStartFailureAndEvidencePersistenceFailureAreReturnedInline()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  check:\n    run: exit 0\ndefault:\n  use:\n    - check\n");
        var hooks = new VerificationRuntimeHooks
        {
            StartProcess = _ => throw new System.ComponentModel.Win32Exception("simulated start failure"),
            WriteEvidence = (_, _, _) => Task.FromException(new IOException("simulated evidence failure"))
        };

        var evidence = Assert.Single((await new VerificationEngine(temp.Path, Path.Combine(temp.Path, ".idd", "factory", "current"), hooks).RunAsync(["check"], default)).Evidence);

        Assert.False(evidence.EvidencePersisted); Assert.Equal("process-start-failure", evidence.PrimaryFailure?.Kind);
        Assert.Equal("start", evidence.PrimaryFailure?.Stage); Assert.Equal(typeof(System.ComponentModel.Win32Exception).FullName, evidence.PrimaryFailure?.ExceptionType);
        Assert.Equal("simulated start failure", evidence.PrimaryFailure?.ExceptionMessage); Assert.NotEmpty(evidence.PrimaryFailure?.StackTrace ?? "");
        Assert.Contains(evidence.DiagnosticIssues, issue => issue.Kind == "evidence-persistence");
        Assert.DoesNotContain("exit 0", System.Text.Json.JsonSerializer.Serialize(evidence.Command));
    }

    [Fact] public async Task PersistedProcessStartFailureUsesFlatV3FailureContract()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  check:\n    run: exit 0\ndefault:\n  use:\n    - check\n");
        var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        var hooks = new VerificationRuntimeHooks { StartProcess = _ => throw new System.ComponentModel.Win32Exception("start exploded") };

        var evidence = Assert.Single((await new VerificationEngine(temp.Path, current, hooks).RunAsync(["check"], default)).Evidence);
        using var document = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(current, "verification", evidence.EvidenceId + ".json")));
        var root = document.RootElement;

        Assert.Equal("process-start-failure", root.GetProperty("failureKind").GetString());
        Assert.Equal("start", root.GetProperty("failureStage").GetString());
        Assert.Equal("Could not start check check.", root.GetProperty("summary").GetString());
        Assert.Equal(typeof(System.ComponentModel.Win32Exception).FullName, root.GetProperty("exception").GetProperty("type").GetString());
        Assert.Equal("start exploded", root.GetProperty("exception").GetProperty("message").GetString());
        Assert.NotEmpty(root.GetProperty("exception").GetProperty("stackTrace").GetString()!);
        Assert.False(root.TryGetProperty("primaryFailure", out _));
        var reread = VerificationEngine.Read(root.GetRawText());
        Assert.Equal("process-start-failure", reread.PrimaryFailure?.Kind);
    }

    [Fact] public async Task IndividualLogFailureDoesNotPreventOtherStreamCapture()
    {
        using var temp = new TestWorkspace();
        var command = OperatingSystem.IsWindows() ? "Write-Output out; [Console]::Error.Write('err')" : "printf out; printf err >&2";
        temp.Write(".idd/verification.yaml", $"version: 1\nchecks:\n  check:\n    run: >-\n      {command}\ndefault:\n  use:\n    - check\n");
        var hooks = new VerificationRuntimeHooks { CreateLog = path => path.EndsWith(".stdout.log", StringComparison.Ordinal) ? throw new IOException("no stdout log") : new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, true) };

        var evidence = Assert.Single((await new VerificationEngine(temp.Path, Path.Combine(temp.Path, ".idd", "factory", "current"), hooks).RunAsync(["check"], default)).Evidence);

        Assert.Equal("output-capture-failure", evidence.PrimaryFailure?.Kind); Assert.Equal("capture-output", evidence.PrimaryFailure?.Stage); Assert.Contains("out", evidence.StdoutTail); Assert.Contains("err", evidence.StderrTail);
        Assert.Null(evidence.StdoutPath); Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(evidence.StdoutTail), evidence.Stdout.ByteLength);
        Assert.Contains(evidence.DiagnosticIssues, issue => issue.Stage == "open-stdout-log");
        Assert.Equal(typeof(IOException).FullName, evidence.PrimaryFailure?.ExceptionType); Assert.NotEmpty(evidence.PrimaryFailure?.StackTrace ?? "");
    }

    [Fact] public async Task EvidencePersistenceFailureHasExactPrimaryDiagnosticAndNoReferenceFile()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  check:\n    run: exit 0\ndefault:\n  use:\n    - check\n");
        var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        var hooks = new VerificationRuntimeHooks { WriteEvidence = (_, _, _) => Task.FromException(new IOException("cannot persist evidence")) };

        var evidence = Assert.Single((await new VerificationEngine(temp.Path, current, hooks).RunAsync(["check"], default)).Evidence);

        Assert.False(evidence.EvidencePersisted); Assert.Equal("evidence-persistence-failure", evidence.PrimaryFailure?.Kind);
        Assert.Equal("persist-evidence", evidence.PrimaryFailure?.Stage); Assert.Equal("cannot persist evidence", evidence.PrimaryFailure?.ExceptionMessage);
        Assert.NotEmpty(evidence.PrimaryFailure?.StackTrace ?? ""); Assert.False(File.Exists(Path.Combine(current, "verification", evidence.EvidenceId + ".json")));
    }

    [Fact] public async Task TerminationFailureIsSecondaryToTimeout()
    {
        using var temp = new TestWorkspace();
        var command = OperatingSystem.IsWindows() ? "Start-Sleep -Seconds 5" : "sleep 5";
        temp.Write(".idd/verification.yaml", $"version: 1\nchecks:\n  check:\n    run: >-\n      {command}\n    timeout: 0s\ndefault:\n  use:\n    - check\n");
        var hooks = new VerificationRuntimeHooks { TerminateProcess = (process, _) => { if (!process.HasExited) process.Kill(true); throw new IOException("simulated termination failure"); } };

        var evidence = Assert.Single((await new VerificationEngine(temp.Path, Path.Combine(temp.Path, ".idd", "factory", "current"), hooks).RunAsync(["check"], default)).Evidence);

        Assert.Equal("timeout", evidence.PrimaryFailure?.Kind); Assert.False(evidence.Termination?.Succeeded);
        Assert.Contains(evidence.DiagnosticIssues, issue => issue.Kind == "termination-failure");
    }

    [Fact] public async Task BoundedDrainFailureIsClassifiedWithoutDiscardingCapturedOutput()
    {
        using var temp = new TestWorkspace();
        var command = OperatingSystem.IsWindows() ? "Write-Output captured" : "printf captured";
        temp.Write(".idd/verification.yaml", $"version: 1\nchecks:\n  check:\n    run: >-\n      {command}\ndefault:\n  use:\n    - check\n");
        var hooks = new VerificationRuntimeHooks { WaitForDrain = async (task, _) => { await task; return false; } };

        var evidence = Assert.Single((await new VerificationEngine(temp.Path, Path.Combine(temp.Path, ".idd", "factory", "current"), hooks).RunAsync(["check"], default)).Evidence);

        Assert.Equal("output-capture-failure", evidence.PrimaryFailure?.Kind); Assert.Contains("captured", evidence.StdoutTail);
        Assert.Contains(evidence.DiagnosticIssues, issue => issue.Kind == "bounded-drain");
    }

    [Fact] public async Task BoundedCleanupCancelsOutputPumpsBeforeEvidenceSnapshot()
    {
        using var temp = new TestWorkspace();
        var command = OperatingSystem.IsWindows() ? "Write-Output captured" : "printf captured";
        temp.Write(".idd/verification.yaml", $"version: 1\nchecks:\n  check:\n    run: >-\n      {command}\ndefault:\n  use:\n    - check\n");
        var hooks = new VerificationRuntimeHooks
        {
            CreateLog = _ => new CancellationBoundStream(),
            WaitForDrain = (_, _) => Task.FromResult(false)
        };

        var evidence = Assert.Single((await new VerificationEngine(temp.Path, Path.Combine(temp.Path, ".idd", "factory", "current"), hooks).RunAsync(["check"], default)).Evidence);
        var snapshot = evidence.StdoutTail;
        await Task.Delay(100);

        Assert.Contains("captured", snapshot);
        Assert.Equal(snapshot, evidence.StdoutTail);
        Assert.Contains(evidence.DiagnosticIssues, issue => issue.Kind == "bounded-drain");
    }

    [Fact] public async Task InfrastructureFailureStopsRemainingChecksAndRetainsCurrentEvidence()
    {
        using var temp = new TestWorkspace();
        temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  first:\n    run: exit 0\n  second:\n    run: exit 0\ndefault:\n  use:\n    - first\n    - second\n");
        var starts = 0;
        var hooks = new VerificationRuntimeHooks { StartProcess = _ => { starts++; throw new IOException("cannot start"); } };

        var result = await new VerificationEngine(temp.Path, Path.Combine(temp.Path, ".idd", "factory", "current"), hooks).RunAsync(["first", "second"], default);

        Assert.Equal(VerificationStatus.InfrastructureFailure, result.Status);
        Assert.Equal("first", Assert.Single(result.Evidence).CheckId);
        Assert.Equal(1, starts);
    }

    [Fact] public void LegacySchemaV2EvidenceRemainsReadable()
    {
        const string json = "{\"schemaVersion\":2,\"evidenceId\":\"V1\",\"checkId\":\"old\",\"checkDefinitionHash\":\"h\",\"startedAt\":\"2025-01-01T00:00:00Z\",\"finishedAt\":\"2025-01-01T00:00:01Z\",\"exitCode\":7,\"status\":\"failed\",\"output\":\"legacy\"}";
        var evidence = VerificationEngine.Read(json);
        Assert.Equal(2, evidence!.SchemaVersion); Assert.Equal(7, evidence.ExitCode); Assert.Equal("legacy", evidence.Output);
    }

    private sealed class CancellationBoundStream : MemoryStream
    {
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    [Fact] public async Task CancellationStopsTheRunningCheckProcess()
    {
        using var temp = new TestWorkspace();
        string command = OperatingSystem.IsWindows()
            ? "$PID | Set-Content -NoNewline verification.pid; Start-Sleep -Seconds 30"
            : "echo $$ > verification.pid; sleep 30";
        temp.Write(".idd/verification.yaml", $"version: 1\nchecks:\n  wait:\n    run: >-\n      {command}\n    timeout: 1m\ndefault:\n  use:\n    - wait\n");
        var engine = new VerificationEngine(temp.Path, System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"));
        using var cancellation = new CancellationTokenSource();
        var run = engine.RunAsync(["wait"], cancellation.Token);
        string pidPath = System.IO.Path.Combine(temp.Path, "verification.pid");

        for (var attempt = 0; attempt < 100 && !File.Exists(pidPath); attempt++)
            await Task.Delay(25);
        Assert.True(File.Exists(pidPath));
        int pid = int.Parse(await File.ReadAllTextAsync(pidPath));

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.False(IsProcessRunning(pid));
    }

    [Theory]
    [InlineData("version: 2\nchecks: {}\ndefault:\n  use: []\n")]
    [InlineData("checks: {}\ndefault:\n  use: []\n")]
    [InlineData("version: 1\nchecks: {}\ndefault: {}\n")]
    [InlineData("version: 1\nchecks:\n  broken: command\ndefault:\n  use: []\n")]
    [InlineData("```yaml\nversion: 1\nchecks: {}\ndefault:\n  use: []\n```\n")]
    [InlineData("version: 1\nchecks: [\n")]
    public async Task ExistingMalformedPolicyDoesNotFallback(string policy)
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/verification.yaml", policy);
        temp.Write("scripts/Check.ps1", "Set-Content -LiteralPath fallback-ran.txt -Value yes\n");
        var engine = new VerificationEngine(temp.Path, System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"));

        var exception = await Assert.ThrowsAsync<VerificationException>(() => engine.RunContextAsync("final", default));

        Assert.Equal("INVALID_VERIFICATION_POLICY", exception.Code);
        Assert.False(File.Exists(System.IO.Path.Combine(temp.Path, "fallback-ran.txt")));
    }

    [Fact] public async Task ExistingPolicyIsValidatedEvenWithNoExplicitIds()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/verification.yaml", "version: 1\nchecks: {}\n");
        var engine = new VerificationEngine(temp.Path, System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"));

        var exception = await Assert.ThrowsAsync<VerificationException>(() => engine.RunSubtaskAsync([], default));

        Assert.Equal("INVALID_VERIFICATION_POLICY", exception.Code);
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
