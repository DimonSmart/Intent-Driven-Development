using System.Text.Json;
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
    public async Task Factory_Stage0NativeRootCancellationStopsGrandchildWriter()
    {
        var repository = FindRepositoryRoot();
        var runId = "nested-writer-stop-" + Guid.NewGuid().ToString("N");
        var temp = Path.Combine(Path.GetTempPath(), "idd-factory-nested-stop", runId);
        var workspace = Path.Combine(temp, "workspace");
        var home = Path.Combine(temp, "codex-home");
        var artifacts = Path.Combine(repository, "artifacts", "factory-evals", runId);
        var heartbeat = Path.Combine(workspace, "native-worker-heartbeat.txt");
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
            await InitializeGitRepositoryAsync(workspace, artifacts);
            PrepareCodexHome(home, workspace);
            await RunAsync(codex, ["--version"], repository, environment, null,
                artifacts, "06-codex-version");

            // No installed Planner/Worker protocol is invoked here. This tests
            // the native descendant cancellation capability in isolation.
            const string prompt = """
                Native Codex Stage-0 descendant cancellation safety probe.
                Work only inside this disposable project and do not modify
                other folders. Do not invoke IDD Factory or any installed Skill.
                Use the native MultiAgentV2 generic child tools.

                1. Spawn ONE fresh generic ROOT agent task_name
                   idd_factory_root_b0001 with fork_turns "none".
                   Root spawns ONE fresh generic WRITER grandchild task_name
                   idd_factory_worker_b0001_0001 with fork_turns "none".
                2. The Writer runs exactly one local PowerShell command:
                   $p = Join-Path (Get-Location) 'native-worker-heartbeat.txt'
                   for ($i = 1; $i -le 40; $i++) {
                       Add-Content -LiteralPath $p -Value $i
                       Start-Sleep -Seconds 1
                   }
                   The Writer MUST issue this command before reporting its
                   terminal result. Root MUST NOT interrupt Writer directly.
                   Root waits natively for Writer.
                3. Supervisor must NOT wait for Root terminal result first.
                   After spawning Root, use ONE bounded local PowerShell
                   command to wait for at least three lines in
                   native-worker-heartbeat.txt (stop after 30 seconds if
                   the file never reaches three lines). Then use the NATIVE
                   interrupt_agent tool on Root ONLY while Writer is running.
                   Never use a shell command to kill any process.
                4. Confirm via native tools that Root has stopped, and that
                   Writer grandchild is also terminated/quiescent. Never
                   start a replacement writer. Do not claim stopped if the
                   host cannot confirm it. Return the terminal phrase
                   NATIVE_DESCENDANT_STOP_CONFIRMED only after native
                   descendant quiescence, otherwise return
                   BLOCKED_NATIVE_DESCENDANT_STOP_UNCONFIRMED.
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
            Assert.Contains(supervisor.Events, item => item.ToolName == "spawn_agent" &&
                HasArgument(item.ToolArguments, "task_name", "idd_factory_root_b0001") &&
                HasArgument(item.ToolArguments, "fork_turns", "none"));
            Assert.Contains(root.Events, item => item.ToolName == "spawn_agent" &&
                HasArgument(item.ToolArguments, "task_name", "idd_factory_worker_b0001_0001") &&
                HasArgument(item.ToolArguments, "fork_turns", "none"));
            Assert.Contains(supervisor.Events, item =>
                item.ToolName == "interrupt_agent" &&
                (item.ToolArguments?.Contains(root.ThreadId, StringComparison.Ordinal) == true ||
                 item.ToolArguments?.Contains("idd_factory_root_b0001", StringComparison.Ordinal) == true));
            Assert.Contains(supervisor.Events, item => item.Role == "assistant" &&
                item.Text?.Contains("NATIVE_DESCENDANT_STOP_CONFIRMED",
                    StringComparison.Ordinal) == true);

            Assert.True(File.Exists(heartbeat), "Native writer never started.");
            var before = await File.ReadAllTextAsync(heartbeat);
            var lines = before.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.InRange(lines.Length, 3, 39);
            await Task.Delay(TimeSpan.FromSeconds(5));
            var after = await File.ReadAllTextAsync(heartbeat);
            Assert.Equal(before, after);

            await File.WriteAllTextAsync(Path.Combine(artifacts, "stage0-writer-stop.json"),
                JsonSerializer.Serialize(new
                {
                    status = "confirmed",
                    supervisorThread = supervisor.ThreadId,
                    rootThread = root.ThreadId,
                    writerThread = writer.ThreadId,
                    writtenLinesBeforeStop = lines.Length,
                    stableObservationSeconds = 5
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
}
