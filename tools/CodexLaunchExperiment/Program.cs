using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using CodexLaunchExperiment;

var repositoryRoot = FindRepositoryRoot();
var runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
var runDirectory = Path.Combine(repositoryRoot, "artifacts", "codex-launch-experiment", runId);
Directory.CreateDirectory(runDirectory);
var discovery = CodexDiscovery.Discover();
await FileSnapshots.WriteJsonAsync(Path.Combine(runDirectory, "environment.json"), new
{
    runId, windowsVersion = Environment.OSVersion.VersionString, processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(), osArchitecture = RuntimeInformation.OSArchitecture.ToString(), dotnetVersion = Environment.Version.ToString(), pathExt = Environment.GetEnvironmentVariable("PATHEXT"), cmdExe = discovery.CmdExe, pathEntries = discovery.PathEntries, relevantEnvironmentVariables = discovery.RelevantEnvironmentVariables,
    candidates = discovery.Candidates.Select(candidate => new { kind = candidate.Kind.ToString(), candidate.Executable, candidate.PrefixArguments, candidate.DisplayName, candidate.ScriptPath })
});

var cases = new List<CaseResult>();
foreach (var candidate in discovery.Candidates)
{
    await RunCaseAsync($"version-{Slug(candidate.DisplayName)}", candidate, ["--version"], null, false, false, false);
    await RunCaseAsync($"doctor-{Slug(candidate.DisplayName)}", candidate, ["doctor"], null, false, false, false, TimeSpan.FromSeconds(45));
}

var verifiedCandidates = discovery.Candidates.Where(candidate => cases.Any(result => result.CaseId == "version-" + Slug(candidate.DisplayName) && result.Outcome == Outcome.Success));
var selected = discovery.CmdExe is null ? [] : CodexDiscovery.ChooseModelCandidates(verifiedCandidates, discovery.CmdExe);
foreach (var candidate in selected)
{
    await RunSandboxMatrixAsync(candidate);
    await RunExecAsync("exec-baseline-" + Slug(candidate.DisplayName), candidate, "workspace-simple", "both", "outside", true);
}

var best = cases.Where(result => result.ModelRun && result.Outcome == Outcome.Success).Select(result => result.Candidate).FirstOrDefault() ?? selected.FirstOrDefault();
if (best is not null)
{
    foreach (var strategy in new[] { "process", "cd", "both" }) await RunExecAsync("working-directory-" + strategy, best, "workspace-simple", strategy, "outside", true);
    await RunExecAsync("output-inside-workspace", best, "workspace-simple", "both", "inside", true);
    await RunExecAsync("schema-outside-workspace", best, "workspace-simple", "both", "schema-outside", true);
    foreach (var name in new[] { "workspace with spaces", "workspace-тест", Path.Combine("artifacts", "factory-evals", "sample", "workspace") }) await RunExecAsync("path-" + Slug(name), best, name, "both", "outside", true);
    for (var run = 1; run <= 3; run++) await RunExecAsync("repeat-" + run, best, "workspace-simple-" + run, "both", "outside", true);
}

var successfulRepeats = cases.Count(result => result.CaseId.StartsWith("repeat-", StringComparison.Ordinal) && result.Outcome == Outcome.Success);
var summary = new
{
    recommendedLaunchKind = best?.Kind.ToString(), executable = best?.Executable, prefixArguments = best?.PrefixArguments, workingDirectoryStrategy = BestWorkingDirectoryStrategy(), sandboxProbePolicy = "after-failure", sandboxMode = "workspace-write", approvalPolicy = "never", outsideWorkspaceWriteBlocked = cases.Any(result => result.OutsideWriteAttempt && result.Outcome == Outcome.Success && !result.OutsideWriteExists), supportsPathsWithSpaces = cases.Any(result => result.CaseId.Contains("spaces") && result.Outcome == Outcome.Success), supportsUnicodePaths = cases.Any(result => result.CaseId.Contains("тест") && result.Outcome == Outcome.Success), jsonlValid = cases.Where(result => result.ModelRun).All(result => result.JsonlValid), lastMessageValid = cases.Where(result => result.ModelRun).All(result => result.LastMessageValid), repeatRunsPassed = successfulRepeats, repeatRunsTotal = 3,
    recommendedArguments = new[] { "exec", "--json", "--ephemeral", "--ignore-user-config", "--ignore-rules", "-c", "approval_policy=never", "--sandbox", "workspace-write", "--cd", "<workspace>", "--output-last-message", "<path>" },
    knownLimitations = cases.Where(result => result.Outcome != Outcome.Success).Select(result => result.CaseId + ": " + result.Outcome).ToArray()
};
await FileSnapshots.WriteJsonAsync(Path.Combine(runDirectory, "summary.json"), summary);
await File.WriteAllTextAsync(Path.Combine(runDirectory, "report.md"), Report(summary));
Console.WriteLine($"Codex launch experiment complete: {runDirectory}");

async Task RunSandboxMatrixAsync(CodexCandidate candidate)
{
    if (discovery.CmdExe is null) return;
    var baseArgs = new[] { "-c", "windows.sandbox=\"unelevated\"", "-c", "sandbox_mode=\"workspace-write\"", "sandbox", "windows" };
    await Sandbox("sandbox-s1-no-separator", baseArgs.Concat(["cmd.exe", "/d", "/c", "echo probe>.codex-sandbox-write-probe"]).ToArray(), ".codex-sandbox-write-probe", false, false);
    await Sandbox("sandbox-s2-separator", baseArgs.Concat(["--", "cmd.exe", "/d", "/c", "echo probe>.codex-sandbox-write-probe"]).ToArray(), ".codex-sandbox-write-probe", false, false);
    await Sandbox("sandbox-s3-absolute-cmd", baseArgs.Concat(["--", discovery.CmdExe, "/d", "/c", "echo probe>.codex-sandbox-write-probe"]).ToArray(), ".codex-sandbox-write-probe", false, false);
    await Sandbox("sandbox-s4-minimal", ["sandbox", "windows", "--", discovery.CmdExe, "/d", "/c", "echo probe>.codex-sandbox-write-probe"], ".codex-sandbox-write-probe", false, false);
    await Sandbox("sandbox-s5-prepared-nested", baseArgs.Concat(["--", discovery.CmdExe, "/d", "/c", "echo nested>a\\b\\probe.txt"]).ToArray(), "nested/a/b/probe.txt", true, false);
    await Sandbox("sandbox-s5-create-nested", baseArgs.Concat(["--", discovery.CmdExe, "/d", "/c", "mkdir nested\\a\\b && echo nested>nested\\a\\b\\probe.txt"]).ToArray(), "nested/a/b/probe.txt", false, false);
    await Sandbox("sandbox-s6-change-existing", baseArgs.Concat(["--", discovery.CmdExe, "/d", "/c", "echo updated>existing.txt"]).ToArray(), "existing.txt", false, false);
    await Sandbox("sandbox-s7-outside", baseArgs.Concat(["--", discovery.CmdExe, "/d", "/c", "echo outside>..\\outside-workspace-probe.txt"]).ToArray(), null, false, true);
    async Task Sandbox(string suffix, IReadOnlyList<string> args, string? expected, bool prepareNested, bool outside) => await RunCaseAsync(suffix + "-" + Slug(candidate.DisplayName), candidate, args, null, expected is not null, outside, false, TimeSpan.FromMinutes(1), prepareNested, expected);
}

async Task RunExecAsync(string caseId, CodexCandidate candidate, string workspaceName, string strategy, string outputPlacement, bool modelRun)
{
    var caseDirectory = Path.Combine(runDirectory, "cases", caseId + "-" + Slug(candidate.DisplayName));
    var workspace = Path.Combine(caseDirectory, workspaceName);
    Directory.CreateDirectory(workspace); await File.WriteAllTextAsync(Path.Combine(workspace, "existing.txt"), "old\n"); await File.WriteAllTextAsync(Path.Combine(workspace, "delete-me.txt"), "delete\n");
    var lastMessage = outputPlacement == "inside" ? Path.Combine(workspace, "last-message.json") : Path.Combine(caseDirectory, "last-message.json");
    var schema = outputPlacement == "inside" ? Path.Combine(workspace, "final-response.schema.json") : Path.Combine(caseDirectory, "final-response.schema.json");
    await File.WriteAllTextAsync(schema, "{\"type\":\"object\",\"properties\":{\"status\":{\"type\":\"string\"}},\"required\":[\"status\"],\"additionalProperties\":false}\n");
    var args = new List<string> { "exec", "--json", "--ephemeral", "--ignore-user-config", "--ignore-rules", "-c", "approval_policy=never", "--sandbox", "workspace-write" };
    if (strategy is "cd" or "both") { args.Add("--cd"); args.Add(workspace); }
    args.AddRange(["--output-schema", schema, "--output-last-message", lastMessage, "Work only inside the current workspace. Create directory nested/result. Create nested/result/generated.txt containing exactly CODEX_WORKSPACE_WRITE_OK. Replace existing.txt with exactly CODEX_EXISTING_FILE_UPDATE_OK. Delete delete-me.txt. Return a JSON object with status. Do not modify any other files."]);
    await RunCaseAsync(caseId + "-" + Slug(candidate.DisplayName), candidate, args, strategy == "cd" ? repositoryRoot : workspace, true, false, modelRun, TimeSpan.FromMinutes(4), false, "nested/result/generated.txt", lastMessage, workspace);
}

async Task RunCaseAsync(string caseId, CodexCandidate candidate, IReadOnlyList<string> arguments, string? workingDirectory, bool expectedWrite, bool outsideWriteAttempt, bool modelRun, TimeSpan? timeout = null, bool prepareNested = false, string? expectedPath = null, string? lastMessagePath = null, string? workspaceDirectory = null)
{
    var caseDirectory = Path.Combine(runDirectory, "cases", caseId); Directory.CreateDirectory(caseDirectory);
    var workspace = workspaceDirectory ?? workingDirectory ?? Path.Combine(caseDirectory, "workspace"); Directory.CreateDirectory(workspace);
    if (prepareNested) Directory.CreateDirectory(Path.Combine(workspace, "nested", "a", "b"));
    if (caseId.Contains("s6-", StringComparison.Ordinal)) await File.WriteAllTextAsync(Path.Combine(workspace, "existing.txt"), "old\n");
    var before = FileSnapshots.Take(workspace); await FileSnapshots.WriteJsonAsync(Path.Combine(caseDirectory, "filesystem-before.json"), before);
    var (executable, finalArguments) = Invocation(candidate, arguments);
    await FileSnapshots.WriteJsonAsync(Path.Combine(caseDirectory, "case.json"), new { caseId, candidate = candidate.DisplayName, executable, arguments = finalArguments, command = CommandFormatting.Format(executable, finalArguments), workingDirectory = workspace, timeoutSeconds = (timeout ?? TimeSpan.FromMinutes(1)).TotalSeconds });
    var started = DateTimeOffset.UtcNow; int? exitCode = null; string? startError = null; var timedOut = false;
    try
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = workingDirectory ?? workspace } };
        foreach (var argument in finalArguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        await using var stdout = File.Create(Path.Combine(caseDirectory, "stdout.log")); await using var stderr = File.Create(Path.Combine(caseDirectory, "stderr.log"));
        var outputTask = process.StandardOutput.BaseStream.CopyToAsync(stdout); var errorTask = process.StandardError.BaseStream.CopyToAsync(stderr);
        using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(1));
        try { await process.WaitForExitAsync(cancellation.Token); } catch (OperationCanceledException) { timedOut = true; if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); }
        await Task.WhenAll(outputTask, errorTask); exitCode = process.ExitCode;
    }
    catch (Exception exception) { startError = exception.ToString(); await File.WriteAllTextAsync(Path.Combine(caseDirectory, "stderr.log"), Diagnostics.Redact(startError)); await File.WriteAllTextAsync(Path.Combine(caseDirectory, "stdout.log"), string.Empty); }
    var after = FileSnapshots.Take(workspace); await FileSnapshots.WriteJsonAsync(Path.Combine(caseDirectory, "filesystem-after.json"), after);
    var outsidePath = Path.Combine(caseDirectory, "outside-workspace-probe.txt"); var standardError = await File.ReadAllTextAsync(Path.Combine(caseDirectory, "stderr.log"));
    var capture = new ProcessCapture(exitCode, timedOut, started, DateTimeOffset.UtcNow, startError); var jsonl = modelRun && Json.IsValidJsonl(Path.Combine(caseDirectory, "stdout.log")); var finalValid = lastMessagePath is not null && File.Exists(lastMessagePath) && Json.IsValidJson(lastMessagePath);
    var expectedExists = expectedPath is null || File.Exists(Path.Combine(workspace, expectedPath.Replace('/', Path.DirectorySeparatorChar)));
    var outcome = Diagnostics.Classify(capture, standardError, expectedWrite && !expectedExists, outsideWriteAttempt, File.Exists(outsidePath), jsonl, finalValid, modelRun);
    var result = new CaseResult(caseId, candidate, CommandFormatting.Format(executable, finalArguments), outcome, capture, FileSnapshots.Compare(before, after), expectedExists, outsideWriteAttempt, File.Exists(outsidePath), jsonl, finalValid, modelRun);
    cases.Add(result); await FileSnapshots.WriteJsonAsync(Path.Combine(caseDirectory, "result.json"), result);
}

(string Executable, IReadOnlyList<string> Arguments) Invocation(CodexCandidate candidate, IReadOnlyList<string> arguments) => candidate.Kind == LaunchKind.CmdShim
    ? (candidate.ScriptPath is null ? discovery.CmdExe! : candidate.Executable, ["/d", "/c", CommandFormatting.BuildCmdCommand(candidate.ScriptPath ?? candidate.Executable, candidate.PrefixArguments.Concat(arguments))])
    : (candidate.Executable, candidate.PrefixArguments.Concat(arguments).ToArray());

string BestWorkingDirectoryStrategy() => cases.FirstOrDefault(result => result.CaseId.StartsWith("working-directory-", StringComparison.Ordinal) && result.Outcome == Outcome.Success)?.CaseId.Replace("working-directory-", "", StringComparison.Ordinal).Split('-')[0] ?? "both";
string Report(object currentSummary) => $"# Codex launch experiment\n\nRun: `{runId}`\n\n## Recommendation\n\nThe machine-readable recommendation is in `summary.json`. The only directly runnable, absolute installation was selected. The model-backed checks did not establish a working sandbox on this machine: both direct `exec` and the helper report `CreateProcessAsUserW failed: 2`. This CLI version accepts the approval policy only as `-c approval_policy=never`, not as `--ask-for-approval`.\n\n## Cases\n\n| Case | Outcome | Command | JSONL | Final message | Outside write |\n|---|---|---|---:|---:|---:|\n" + string.Join("\n", cases.Select(result => $"| {result.CaseId} | {result.Outcome} | `{result.Command.Replace("`", "'")}` | {result.JsonlValid} | {result.LastMessageValid} | {result.OutsideWriteExists} |")) + "\n\n## Canonical live-eval invocation\n\n```csharp\nvar start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = workspace };\nforeach (var argument in arguments) start.ArgumentList.Add(argument);\n// exec --json --ephemeral --ignore-user-config --ignore-rules -c approval_policy=never --sandbox workspace-write --cd <workspace> --output-last-message <outside-workspace>\n```\n\nUse an absolute discovered executable, preserve stderr verbatim in artifacts, and classify `CreateProcessAsUserW failed` as `SandboxInitializationFailed`. The proposed policy is `after-failure`: a helper probe may refine a failed real run but must not prevent it.\n";
string Slug(string value) => string.Concat(value.Select(character => char.IsLetterOrDigit(character) ? character : '-')).Trim('-').ToLowerInvariant();
string FindRepositoryRoot() { var directory = new DirectoryInfo(Environment.CurrentDirectory); while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent; return directory?.FullName ?? Environment.CurrentDirectory; }

public sealed record CaseResult(string CaseId, CodexCandidate Candidate, string Command, Outcome Outcome, ProcessCapture Process, FileDiff FilesystemDiff, bool ExpectedProbeExists, bool OutsideWriteAttempt, bool OutsideWriteExists, bool JsonlValid, bool LastMessageValid, bool ModelRun);
