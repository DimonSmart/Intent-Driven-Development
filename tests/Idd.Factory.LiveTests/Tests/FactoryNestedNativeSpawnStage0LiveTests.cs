using Idd.Factory.Report;
using System.Text.Json;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed partial class NativeFactoryEndToEndLiveTests
{
    [LiveFactoryMarketplaceFact]
    [Trait("Category", "LiveFactoryMarketplace")]
    public async Task Factory_Stage0ProvesNestedNativeGenericAgents()
    {
        var repository = FindRepositoryRoot();
        var runId = "nested-stage0-" + Guid.NewGuid().ToString("N");
        var temporary = Path.Combine(Path.GetTempPath(), "idd-factory-nested-stage0", runId);
        var workspace = Path.Combine(temporary, "workspace");
        var marketplace = Path.Combine(temporary, "marketplace");
        var codexHome = Path.Combine(temporary, "codex-home");
        var artifacts = Path.Combine(repository, "artifacts", "factory-evals", runId);
        var settings = LiveEvalSettings.Resolve(repository,
            Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_MODEL"),
            Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_REASONING_EFFORT"),
            profileEnvironment: Environment.GetEnvironmentVariable);
        var worker = settings.ExecutionProfiles["standard"];
        var codex = Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_CODEX") ?? "codex";
        var timeout = int.TryParse(
            Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_TIMEOUT_MINUTES"), out var minutes)
            ? minutes : 10;
        var nonce = "supervisor-only-" + Guid.NewGuid().ToString("N");
        var environment = new Dictionary<string, string?> { ["CODEX_HOME"] = codexHome };

        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(artifacts);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(workspace, "README.md"),
                "Disposable, read-only IDD Factory stage-0 nested-spawn probe.\n");
            await InitializeGitRepositoryAsync(workspace, artifacts);
            await VerifyGitBaselineAsync(workspace, artifacts);
            PrepareCodexHome(codexHome, workspace);
            await RunAsync(codex, ["--version"], repository, environment, null,
                artifacts, "06-codex-version");
            await RunAsync("dotnet", ["build", "tools/generate/Generate.csproj", "--nologo"],
                repository, null, null, artifacts, "07-generator-build");
            await RunAsync("dotnet",
                ["exec", Path.Combine(repository, "tools", "generate", "bin", "Debug", "net10.0",
                    "Generate.dll"), "--version", "0.0.0-live", "--output", marketplace],
                repository, null, null, artifacts, "08-generate-marketplace");

            var packaged = Path.Combine(marketplace, "plugins", "codex", "idd-factory");
            Assert.False(Directory.Exists(Path.Combine(packaged, "agents")));
            foreach (var name in new[] { "idd-factory-run", "idd-internal-factory-planner",
                         "idd-internal-factory-worker" })
                Assert.True(File.Exists(Path.Combine(packaged, "skills", name, "SKILL.md")),
                    $"Missing packaged Skill: {name}");

            await RunAsync(codex, ["plugin", "marketplace", "add", marketplace, "--json"],
                repository, environment, null, artifacts, "09-marketplace-add");
            await RunAsync(codex, ["plugin", "add", "idd-factory@intent-driven-development", "--json"],
                repository, environment, null, artifacts, "10-plugin-add");

            // This is deliberately a technical generic-child probe, NOT an invocation
            // of the old one-level Factory Root or a simulation of full Factory execution.
            var prompt = $$"""
                Stage-0 native Codex nesting capability probe. Do not invoke $idd-factory-run
                and do not implement any product work. The repository is read-only.

                Use the currently installed idd-factory plugin, not its source checkout.
                Resolve the installed absolute path of idd-factory-run/SKILL.md and
                its sibling idd-internal-factory-planner/SKILL.md and
                idd-internal-factory-worker/SKILL.md from the SAME installed plugin.
                Do not register custom agent types or copy Skill text into child prompts.

                Launch exactly ONE generic native child with task_name
                idd_factory_root_b0001 and fork_turns "none". Give it the project
                cwd and installed Planner/Worker Skill paths. It is a technical
                Root probe, NOT an invocation of the Factory Skill. Inside this
                child, spawn exactly two GENERIC grandchildren SEQUENTIALLY:

                (1) idd_factory_planner_b0001_0001 with fork_turns "none".
                    Include the installed Planner SKILL.md absolute path, instruct
                    the grandchild to read it completely and provide an intentionally
                    incomplete Planner assignment. Require BLOCKED, no project writes.
                    Wait for terminal completion and close this grandchild before (2).
                (2) idd_factory_worker_b0001_0001 with fork_turns "none",
                    model "{{worker.Model}}" and reasoning_effort
                    "{{worker.ReasoningEffort}}" as explicit native spawn arguments.
                    Include the installed Worker SKILL.md absolute path, instruct
                    it to read it completely and provide an intentionally incomplete
                    Worker assignment. Require BLOCKED, no project writes.
                    Wait for terminal completion and close the grandchild.

                The Root probe then returns a short terminal result and closes.
                Supervisor waits natively for its terminal result and closes it.
                Never forward the Supervisor's private nonce to any child:
                {{nonce}}
                Do not retry rejected spawns, do not use CLI processes as subagents,
                do not poll status, and do not silently fall back to one-level work.
                """;

            var run = await RunAsync(codex,
                [
                    "exec", "--json", "--ignore-rules",
                    "--enable", "multi_agent", "--enable", "multi_agent_v2",
                    "--enable", "plugins", "--disable", "remote_plugin",
                    "-c", "agents.max_depth=2",
                    "-c", "agents.max_threads=4",
                    "-c", "approval_policy=never",
                    "-c", $"model_reasoning_effort={settings.ReasoningEffort}",
                    "--model", settings.Model,
                    "--sandbox", "read-only",
                    "--cd", workspace, "-"
                ],
                workspace, environment, prompt, artifacts, "11-nested-native-probe",
                timeout: TimeSpan.FromMinutes(timeout));
            await File.WriteAllTextAsync(Path.Combine(artifacts, "codex-trace.jsonl"), run.Stdout);

            var reader = new CodexRolloutReader();
            var sessions = Path.Combine(codexHome, "sessions");
            Assert.True(Directory.Exists(sessions), "No native Codex sessions captured.");
            var traces = Directory.EnumerateFiles(sessions, "*.jsonl", SearchOption.AllDirectories)
                .Select(path => reader.Read(path)).ToArray();
            var supervisor = Assert.Single(traces, trace =>
                !string.IsNullOrWhiteSpace(trace.ThreadId) &&
                string.IsNullOrWhiteSpace(trace.ParentThreadId));
            AssertMultiAgentV2(supervisor);
            Assert.Equal(4, traces.Length);
            Assert.Equal(4, traces.Select(trace => trace.ThreadId)
                .Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(0, traces.Count(trace => trace.ThreadId == ""));
            var root = Assert.Single(traces, trace =>
                trace.ParentThreadId == supervisor.ThreadId);
            var planner = Assert.Single(traces, trace =>
                trace.ParentThreadId == root.ThreadId &&
                trace.AgentPath?.Split('/').Last() == "idd_factory_planner_b0001_0001");
            var workerChild = Assert.Single(traces, trace =>
                trace.ParentThreadId == root.ThreadId &&
                trace.AgentPath?.Split('/').Last() == "idd_factory_worker_b0001_0001");

            AssertAcceptedSpawn(supervisor, root, "idd_factory_root_b0001", null, null);
            AssertAcceptedSpawn(root, planner, "idd_factory_planner_b0001_0001", null, null);
            AssertAcceptedSpawn(root, workerChild, "idd_factory_worker_b0001_0001",
                worker.Model, worker.ReasoningEffort);
            Assert.Single(supervisor.Events, item => item.ToolName == "spawn_agent");
            Assert.Equal(2, root.Events.Count(item => item.ToolName == "spawn_agent"));
            Assert.DoesNotContain(planner.Events, item => item.ToolName == "spawn_agent");
            Assert.DoesNotContain(workerChild.Events, item => item.ToolName == "spawn_agent");

            // A forked transcript would reveal this Supervisor-only canary.
            Assert.All(new[] { root, planner, workerChild }, child =>
                Assert.DoesNotContain(nonce, File.ReadAllText(child.Path), StringComparison.Ordinal));
            Assert.Contains(planner.Events, item => item.Role == "assistant" &&
                item.Text?.Contains("BLOCKED", StringComparison.OrdinalIgnoreCase) == true);
            Assert.Contains(workerChild.Events, item => item.Role == "assistant" &&
                item.Text?.Contains("BLOCKED", StringComparison.OrdinalIgnoreCase) == true);
            Assert.Contains(planner.Events, item => IsNativeSkillRead(item, "planner"));
            Assert.Contains(workerChild.Events, item => IsNativeSkillRead(item, "worker"));

            // Native terminal waits must be visible at both orchestration levels.
            Assert.Contains(supervisor.Events, item => item.ToolName == "wait_agent");
            Assert.Contains(root.Events, item => item.ToolName == "wait_agent");
            Assert.Contains(workerChild.Events, item =>
                item.ActualModel == worker.Model &&
                item.ActualReasoningEffort == worker.ReasoningEffort);

            var status = await RunAsync("git", ["status", "--porcelain=v1"],
                workspace, null, null, artifacts, "12-workspace-status");
            Assert.True(string.IsNullOrWhiteSpace(status.Stdout),
                "Nested capability probe changed its disposable repository.");
            await File.WriteAllTextAsync(Path.Combine(artifacts, "stage0-result.json"),
                JsonSerializer.Serialize(new
                {
                    status = "confirmed",
                    nativeRootSpawns = 1,
                    nativeGrandchildSpawns = 2,
                    threads = traces.Select(trace => new {
                        trace.ThreadId, trace.ParentThreadId, trace.AgentPath
                    }),
                    workerModel = worker.Model,
                    workerReasoningEffort = worker.ReasoningEffort
                }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception)
        {
            await TryWriteTextAsync(Path.Combine(artifacts, "stage0-blocker.txt"),
                exception.ToString());
            throw;
        }
        finally
        {
            await TrySaveWorkspaceArtifactsAsync(workspace, artifacts);
            await TrySaveReportSourceAsync(codexHome, artifacts);
            if (Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_KEEP_TEMP") != "1")
                try { Directory.Delete(temporary, recursive: true); } catch { }
        }
    }

    private static void AssertAcceptedSpawn(CodexRollout parent, CodexRollout child,
        string taskName, string? model, string? reasoningEffort)
    {
        var call = Assert.Single(parent.Events, item =>
            item.ToolName == "spawn_agent" &&
            HasArgument(item.ToolArguments, "task_name", taskName));
        Assert.True(HasArgument(call.ToolArguments, "fork_turns", "none"),
            $"Missing fork_turns=none: {taskName}");
        var spawn = Assert.Single(parent.SpawnRecords.Values, record =>
            record.CallId == call.ToolId);
        Assert.Equal("accepted", spawn.SpawnOutcome);
        Assert.False(spawn.CorrelationConflict);
        Assert.Equal(taskName, spawn.RequestedTaskName);
        Assert.Contains(child.ThreadId, spawn.Children);
        Assert.Equal(parent.ThreadId, child.ParentThreadId);
        if (model is not null)
        {
            Assert.Equal(model, spawn.RequestedModel);
            Assert.Equal(reasoningEffort, spawn.RequestedReasoningEffort);
        }
    }

    private static bool IsNativeSkillRead(CodexEvent item, string role)
    {
        var expected = "idd-internal-factory-" + role;
        return item.ToolName is not null &&
            item.ToolName != "spawn_agent" &&
            item.ToolArguments?.Replace('\\', '/').Contains(
                expected + "/SKILL.md", StringComparison.OrdinalIgnoreCase) == true;
    }
}
