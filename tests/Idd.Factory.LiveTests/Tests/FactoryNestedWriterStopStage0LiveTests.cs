using System.Text.Json;
using System.Text.RegularExpressions;
using Idd.Factory.Report;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

/// <summary>
/// Opt-in host capability test. Exercises a native grandchild writing only to
/// a disposable probe file. This is NOT Factory implementation or a simulation
/// of a product Worker; the existing full-Skill Stage 0 probe is separate.
/// </summary>
public sealed partial class NativeFactoryEndToEndLiveTests
{
    [LiveFactoryMarketplaceFact]
    [Trait("Category", "LiveFactoryMarketplace")]
    public async Task Factory_Stage0NativeDescendantFirstCancellationStopsGrandchildWriter()
    {
        var repository = FindRepositoryRoot();
        var runId = "nested-writer-stop-" + Guid.NewGuid().ToString("N");
        var temp = Path.Combine(Path.GetTempPath(), "idd-factory-nested-stop", runId);
        var workspace = Path.Combine(temp, "workspace");
        var home = Path.Combine(temp, "codex-home");
        var artifacts = Path.Combine(repository, "artifacts", "factory-evals", runId);
        var heartbeat = Path.Combine(workspace, "native-worker-heartbeat.txt");
        var guard = Path.Combine(workspace, "native-writer-once.guard");
        var naturalCompletion = Path.Combine(workspace, "native-writer-natural-completion.txt");
        var heartbeatReady = Path.Combine(workspace, "native-heartbeat-ready.txt");
        var environment = new Dictionary<string, string?> { ["CODEX_HOME"] = home };
        var settings = LiveEvalSettings.Resolve(repository,
            Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_MODEL"),
            Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_REASONING_EFFORT"),
            profileEnvironment: Environment.GetEnvironmentVariable);
        var codex = Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_CODEX") ?? "codex";
        var timeout = int.TryParse(Environment.GetEnvironmentVariable(
            "IDD_FACTORY_EVAL_TIMEOUT_MINUTES"), out var minutes) ? minutes : 8;
        var confirmedQuiescence = false;

        Directory.CreateDirectory(artifacts);
        Directory.CreateDirectory(workspace);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(workspace, "README.md"),
                "Disposable native cancellation probe; no product files.\n");
            // Atomic CreateNew prevents a second invocation from generating
            // misleading extra heartbeats (the old prompt produced 80 lines).
            await File.WriteAllTextAsync(Path.Combine(workspace, "native-writer-probe.ps1"), """
                $ErrorActionPreference = 'Stop'
                $guard = Join-Path (Get-Location) 'native-writer-once.guard'
                $handle = [IO.File]::Open($guard, [IO.FileMode]::CreateNew,
                    [IO.FileAccess]::Write, [IO.FileShare]::None)
                $handle.Dispose()
                $heartbeat = Join-Path (Get-Location) 'native-worker-heartbeat.txt'
                for ($i = 1; $i -le 40; $i++) {
                    Add-Content -LiteralPath $heartbeat -Value $i
                    Start-Sleep -Seconds 1
                }
                Set-Content -LiteralPath (Join-Path (Get-Location) 'native-writer-natural-completion.txt') -Value 'completed'
                """);
            // Do not ask an LLM to invent a timed PowerShell loop: on the
            // intended Codex host its inline command failed with a parser
            // error, making the cancellation result unprovable.
            await File.WriteAllTextAsync(Path.Combine(workspace, "native-wait-for-heartbeat.ps1"), """
                $ErrorActionPreference = 'Stop'
                $heartbeat = Join-Path $PSScriptRoot 'native-worker-heartbeat.txt'
                $marker = Join-Path $PSScriptRoot 'native-heartbeat-ready.txt'
                $deadline = [DateTime]::UtcNow.AddSeconds(30)
                while ([DateTime]::UtcNow -lt $deadline) {
                    if (Test-Path -LiteralPath $heartbeat) {
                        $count = @(Get-Content -LiteralPath $heartbeat).Count
                        if ($count -ge 3) {
                            Set-Content -LiteralPath $marker -Value $count
                            Write-Output "HEARTBEAT_READY count=$count"
                            exit 0
                        }
                    }
                    Start-Sleep -Milliseconds 250
                }
                Write-Error 'HEARTBEAT_NOT_READY: Writer did not emit three lines within 30 seconds'
                exit 1
                """);
            await InitializeGitRepositoryAsync(workspace, artifacts);
            PrepareCodexHome(home, workspace);
            await RunAsync(codex, ["--version"], repository, environment, null,
                artifacts, "06-codex-version");

            // A parent-only interrupt failed to stop its Writer on the
            // intended Windows host. Verify direct descendant-first stop.
            const string prompt = """
                Native Codex Stage-0 descendant-first cancellation safety probe.
                Work only inside this disposable project and do not modify
                other folders. Do not invoke IDD Factory or any installed Skill.
                Use the native MultiAgentV2 generic child tools.

                1. Spawn ONE fresh generic ROOT agent task_name
                   idd_factory_root_b0001 with fork_turns "none".
                   Root spawns ONE fresh generic WRITER grandchild task_name
                   idd_factory_worker_b0001_0001 with fork_turns "none".
                2. The Writer must execute exactly ONCE the existing tracked
                   native-writer-probe.ps1 via ONE local PowerShell command:
                   pwsh -NoProfile -File ./native-writer-probe.ps1
                   Do not inline/reimplement its loop or run it twice.
                   The script has an atomic one-shot guard and writes
                   40 heartbeat lines over roughly 40 seconds; it writes
                   native-writer-natural-completion.txt only on full completion.
                   Root waits natively for Writer, without interrupting it.
                3. Supervisor must NOT wait for Root's terminal result first.
                   After spawning Root, run the EXISTING script using EXACTLY
                   this native shell command (do not construct or inline a
                   PowerShell loop or add quotes around the script body):
                   pwsh -NoProfile -File ./native-wait-for-heartbeat.ps1
                   This prewritten script waits up to 30 seconds for three
                   heartbeat lines, writes native-heartbeat-ready.txt ONLY on
                   success, prints HEARTBEAT_READY and exits 0. If the command
                   fails, times out or does not print HEARTBEAT_READY, report
                   BLOCKED and do not claim cancellation was established.
                   Immediately after success, while Writer is still writing,
                   call native list_agents, identify the full native Writer
                   path, and call native interrupt_agent on WRITER directly,
                   not Root. Immediately interrupt ROOT next.
                   Never kill a process by shell or run extra Writer commands.
                4. Confirm through native agent events that both Writer and
                   Root are stopped. Confirm that the in-flight PowerShell
                   process is no longer writing, not merely that a native
                   agent's status says 'interrupted'. Do not wait for natural
                   completion to claim cancellation. Return
                   NATIVE_DESCENDANT_STOP_CONFIRMED only if both agent and
                   Writer-command quiescence are established; otherwise
                   return BLOCKED_NATIVE_DESCENDANT_STOP_UNCONFIRMED.
                """;

            var run = await RunAsync(codex, [
                "exec", "--json", "--ignore-rules",
                "--enable", "multi_agent", "--enable", "multi_agent_v2",
                "-c", "agents.max_depth=2", "-c", "agents.max_threads=4",
                "-c", "approval_policy=never",
                "-c", $"model_reasoning_effort={settings.ReasoningEffort}",
                "--model", settings.Model, "--sandbox", "danger-full-access",
                "--cd", workspace, "-"
            ], workspace, environment, prompt, artifacts, "07-nested-writer-stop",
                timeout: TimeSpan.FromMinutes(timeout), requireSuccess: false);
            Assert.Equal(0, run.ExitCode);

            var sessions = Path.Combine(home, "sessions");
            Assert.True(Directory.Exists(sessions), "Native session metadata missing.");
            var reader = new CodexRolloutReader();
            var traces = Directory.EnumerateFiles(sessions, "*.jsonl",
                    SearchOption.AllDirectories)
                .Select(path => reader.Read(path)).ToArray();
            var supervisor = Assert.Single(traces, item =>
                !string.IsNullOrWhiteSpace(item.ThreadId) &&
                string.IsNullOrWhiteSpace(item.ParentThreadId));
            var root = Assert.Single(traces, item =>
                item.ParentThreadId == supervisor.ThreadId &&
                item.AgentPath?.Split('/').Last() == "idd_factory_root_b0001");
            var writer = Assert.Single(traces, item =>
                item.ParentThreadId == root.ThreadId &&
                item.AgentPath?.Split('/').Last() == "idd_factory_worker_b0001_0001");
            Assert.Equal(3, traces.Length);
            AssertMultiAgentV2(supervisor);
            Assert.Single(supervisor.Events, item => item.ToolName == "spawn_agent");
            Assert.Single(root.Events, item => item.ToolName == "spawn_agent");
            // The native shell can read/check the probe script without running
            // it. Reject actual invocation by Supervisor/Root, not textual
            // references in e.g. Get-Content or Test-Path commands.
            Assert.Contains(writer.Events, InvokesWriterProbe);
            var unexpectedParentExecutions = supervisor.Events
                .Concat(root.Events).Where(InvokesWriterProbe).ToArray();
            Assert.True(unexpectedParentExecutions.Length == 0,
                "Supervisor/Root executed the Writer probe instead of the Writer: " +
                string.Join("; ", unexpectedParentExecutions.Select(item =>
                    $"thread={item.ThreadId ?? "unknown"} ordinal={item.Ordinal} " +
                    $"arguments={item.ToolArguments}")));
            Assert.Contains(supervisor.Events, item => item.ToolName == "spawn_agent" &&
                HasArgument(item.ToolArguments, "task_name", "idd_factory_root_b0001") &&
                HasArgument(item.ToolArguments, "fork_turns", "none"));
            Assert.Contains(root.Events, item => item.ToolName == "spawn_agent" &&
                HasArgument(item.ToolArguments, "task_name", "idd_factory_worker_b0001_0001") &&
                HasArgument(item.ToolArguments, "fork_turns", "none"));

            // Compare exact native targets, not a substring: the Writer path
            // contains the Root path. The previous parent-only interrupt did
            // not stop this grandchild on the tested Codex host.
            Assert.False(string.IsNullOrWhiteSpace(root.AgentPath));
            Assert.False(string.IsNullOrWhiteSpace(writer.AgentPath));
            var writerInterrupt = Assert.Single(supervisor.Events, item =>
                item.ToolName == "interrupt_agent" &&
                HasArgument(item.ToolArguments, "target", writer.AgentPath!));
            var readyWait = Assert.Single(supervisor.Events, item =>
                item.ToolName is "exec" or "command_execution" &&
                item.ToolArguments?.Contains("native-wait-for-heartbeat.ps1",
                    StringComparison.OrdinalIgnoreCase) == true);
            Assert.True(readyWait.Ordinal < writerInterrupt.Ordinal,
                "Successful heartbeat wait must precede the Writer interruption.");
            var rootInterrupt = Assert.Single(supervisor.Events, item =>
                item.ToolName == "interrupt_agent" &&
                HasArgument(item.ToolArguments, "target", root.AgentPath!));
            Assert.True(writerInterrupt.Ordinal < rootInterrupt.Ordinal,
                "The Writer must be interrupted before its parent Root.");
            Assert.Contains(writer.Events, item => item.Type is
                "turn_aborted" or "turn.cancelled" or "turn_cancelled");
            Assert.Contains(root.Events, item => item.Type is
                "turn_aborted" or "turn.cancelled" or "turn_cancelled");
            Assert.True(File.Exists(heartbeatReady),
                "The prewritten heartbeat wait did not confirm three lines before cancellation.");
            var readinessText = (await File.ReadAllTextAsync(heartbeatReady)).Trim();
            Assert.True(int.TryParse(readinessText, out var readyCount) &&
                readyCount is >= 3 and < 40,
                $"Heartbeat readiness marker is invalid: '{readinessText}'.");
            Assert.True(File.Exists(guard), "One-shot Writer script did not start.");
            Assert.True(File.Exists(heartbeat), "Native Writer never wrote a heartbeat.");
            Assert.False(File.Exists(naturalCompletion),
                "Writer completed all 40 writes naturally; cancellation was not proven.");
            var before = await File.ReadAllTextAsync(heartbeat);
            var lines = before.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.InRange(lines.Length, 3, 39);
            await Task.Delay(TimeSpan.FromSeconds(8));
            var after = await File.ReadAllTextAsync(heartbeat);
            Assert.Equal(before, after);
            Assert.False(File.Exists(naturalCompletion),
                "Writer completed naturally during the observation window.");
            Assert.Contains(supervisor.Events, item => item.Role == "assistant" &&
                item.Text?.Contains("NATIVE_DESCENDANT_STOP_CONFIRMED",
                    StringComparison.Ordinal) == true);

            await File.WriteAllTextAsync(Path.Combine(artifacts, "stage0-writer-stop.json"),
                JsonSerializer.Serialize(new
                {
                    status = "confirmed",
                    supervisorThread = supervisor.ThreadId,
                    rootThread = root.ThreadId,
                    writerThread = writer.ThreadId,
                    writtenLinesBeforeStop = lines.Length,
                    observedReadyLines = readyCount,
                    stableObservationSeconds = 8
                }, new JsonSerializerOptions { WriteIndented = true }));
            confirmedQuiescence = true;
        }
        catch (Exception exception)
        {
            await TryWriteTextAsync(Path.Combine(artifacts, "stage0-writer-stop-blocker.txt"),
                exception.ToString());
            throw;
        }
        finally
        {
            await TrySaveWorkspaceArtifactsAsync(workspace, artifacts);
            await TrySaveReportSourceAsync(home, artifacts);
            // When quiescence is not established, preserve the disposable
            // folder for further diagnosis instead of deleting active files.
            if (confirmedQuiescence &&
                Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_KEEP_TEMP") != "1")
                try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    // Native CommandExecution.command can be a JSON array. Inspect the
    // executable command, not every mention of the script's filename.
    // A parent may legitimately read the script without executing it.
    private static bool InvokesWriterProbe(CodexEvent item)
    {
        if (item.ToolName is not ("exec" or "command_execution") ||
            string.IsNullOrWhiteSpace(item.ToolArguments))
            return false;

        var command = item.ToolArguments;
        try
        {
            using var document = JsonDocument.Parse(command);
            var value = document.RootElement;
            if (value.ValueKind == JsonValueKind.Object)
            {
                if (value.TryGetProperty("command", out var structured))
                    value = structured;
                else if (value.TryGetProperty("cmd", out structured))
                    value = structured;
            }
            command = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? "",
                JsonValueKind.Array => string.Join(" ", value.EnumerateArray()
                    .Select(part => part.ValueKind == JsonValueKind.String
                        ? part.GetString() : part.GetRawText())),
                _ => command
            };
        }
        catch (JsonException)
        {
            // A native -Command body is often already plain text.
        }

        return Regex.IsMatch(command,
            @"(?ix)
              (?: \b -File \s+ | (?<!\w) [&.] \s+ | (?<!\w) \.[\\/] )
              [^\s""';|]* native-writer-probe\.ps1
              (?= $ | [\s""';|] )",
            RegexOptions.CultureInvariant);
    }

    [Theory]
    [InlineData("""["pwsh.exe","-Command","Get-Content -LiteralPath ./native-writer-probe.ps1"]""", false)]
    [InlineData("""["pwsh.exe","-Command","Test-Path -LiteralPath ./native-writer-probe.ps1"]""", false)]
    [InlineData("""["pwsh.exe","-Command","pwsh -NoProfile -File ./native-writer-probe.ps1"]""", true)]
    [InlineData("""["pwsh.exe","-File","./native-writer-probe.ps1"]""", true)]
    [InlineData("""["pwsh.exe","-Command","& ./native-writer-probe.ps1"]""", true)]
    public void Factory_WriterProbeClassifierDistinguishesExecutionFromRead(
        string toolArguments, bool expected)
    {
        Assert.Equal(expected, InvokesWriterProbe(new CodexEvent
        {
            ToolName = "command_execution",
            ToolArguments = toolArguments
        }));
    }
}
