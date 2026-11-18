using System.Text;
using Idd.Factory.Report;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed partial class NativeFactoryEndToEndLiveTests
{
    [LiveFactoryMarketplaceFact]
    [Trait("Category", "LiveFactoryMarketplace")]
    public async Task Factory_NativeTenTasksRotatesFourRootsWithLimitThree()
    {
        var repo = FindRepositoryRoot();
        var runId = "batch-ten-" + Guid.NewGuid().ToString("N");
        var temp = Path.Combine(Path.GetTempPath(), "idd-factory-batch-ten", runId);
        var workspace = Path.Combine(temp, "workspace");
        var home = Path.Combine(temp, "codex-home");
        var marketplace = Path.Combine(temp, "marketplace");
        var artifacts = Path.Combine(repo, "artifacts", "factory-evals", runId);
        var settings = LiveEvalSettings.Resolve(repo,
            Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_MODEL"),
            Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_REASONING_EFFORT"),
            profileEnvironment: Environment.GetEnvironmentVariable);
        var worker = settings.ExecutionProfiles["standard"];
        var codex = Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_CODEX") ?? "codex";
        var timeout = int.TryParse(Environment.GetEnvironmentVariable(
            "IDD_FACTORY_EVAL_TIMEOUT_MINUTES"), out var configured) ? configured : 60;
        var env = new Dictionary<string, string?> { ["CODEX_HOME"] = home };
        Directory.CreateDirectory(artifacts);

        try
        {
            PrepareTenTaskWorkspace(workspace, worker);
            await InitializeGitRepositoryAsync(workspace, artifacts);
            await VerifyGitBaselineAsync(workspace, artifacts);
            PrepareCodexHome(home, workspace);
            await RunAsync(codex, ["--version"], repo, env, null, artifacts, "06-codex-version");
            await RunAsync("dotnet", ["build", "tools/generate/Generate.csproj", "--nologo"],
                repo, null, null, artifacts, "07-generator-build");
            await RunAsync("dotnet", [
                    "exec", Path.Combine(repo, "tools", "generate", "bin", "Debug", "net10.0",
                        "Generate.dll"), "--version", "0.0.0-live", "--output", marketplace
                ], repo, null, null, artifacts, "08-marketplace-generate");
            var package = Path.Combine(marketplace, "plugins", "codex", "idd-factory");
            foreach (var role in new[] { "root", "planner", "worker" })
                Assert.True(File.Exists(Path.Combine(package, "skills",
                    "idd-internal-factory-" + role, "SKILL.md")));
            await RunAsync("dotnet", ["restore", "tests/TenTasks.Tests/TenTasks.Tests.csproj",
                "--nologo"], workspace, null, null, artifacts, "08-project-restore");
            await RunAsync(codex, ["plugin", "marketplace", "add", marketplace, "--json"],
                repo, env, null, artifacts, "09-marketplace-add");
            await RunAsync(codex, ["plugin", "add",
                "idd-factory@intent-driven-development", "--json"],
                repo, env, null, artifacts, "10-plugin-add");

            const string prompt = """
                $idd-factory-run
                Implement the current approved IDD-0001 product specification
                in this repository, in ONE user invocation, using the native
                two-level Factory Supervisor. Explicitly set
                max_workers_per_batch = 3 for this invocation.
                The ten independent Step01 through Step10 product requirements
                MUST be implemented by exactly ten sequential single-Task
                Workers, each creating only its one corresponding StepNN.cs file.
                Planner must issue one bounded task per step and not group them.
                The existing tests deliberately use reflection, so incomplete
                steps do not stop intermediate dotnet build checkpoints.
                Complete final project verification after Planner # Done.
                No shortcuts or direct Supervisor project writes. Don't start
                extra Workers for final verification or status checks.
                """;
            var result = await RunAsync(codex, [
                "exec", "--json", "--ignore-rules",
                "--enable", "multi_agent", "--enable", "multi_agent_v2",
                "--enable", "plugins", "--disable", "remote_plugin",
                "-c", "agents.max_depth=2", "-c", "agents.max_threads=18",
                "-c", "approval_policy=never",
                "-c", $"model_reasoning_effort={settings.ReasoningEffort}",
                "--model", settings.Model,
                "--sandbox", "danger-full-access",
                "--cd", workspace, "-"
            ], workspace, env, prompt, artifacts, "11-batched-native-run",
                timeout: TimeSpan.FromMinutes(timeout));
            await File.WriteAllTextAsync(Path.Combine(artifacts, "codex-trace.jsonl"), result.Stdout);

            var report = Assert.Single(new FactoryReportEngine().FindRuns(workspace, home));
            ReportWriters.WriteJson(report, Path.Combine(artifacts, "factory-report.json"));
            ReportWriters.WriteMarkdown(report, Path.Combine(artifacts, "factory-report.md"), verbose: false);

            Assert.Equal(1, report.Metrics.SupervisorInvocations);
            Assert.Equal(4, report.Metrics.FactoryRootInvocations);
            Assert.Equal("completed", report.Run.Result);
            var ordered = report.Batches.OrderBy(item => item.BatchOrdinal).ToArray();
            Assert.True(ordered.Select(item => item.BatchOrdinal).SequenceEqual(
                new int?[] { 1, 2, 3, 4 }), "Expected exactly four sequential Root batch ordinals.");
            Assert.Equal(new[] { "CONTINUE", "CONTINUE", "CONTINUE", "COMPLETED" },
                ordered.Select(item => item.Status).ToArray());
            Assert.True(ordered.Select(item => item.CompletedWorkers).SequenceEqual(
                new int?[] { 3, 3, 3, 1 }), "Expected exact 3/3/3/1 confirmed Worker checkpoints.");
            Assert.Equal(10, report.Metrics.WorkerInvocations);
            Assert.Equal(10, report.Tasks.Count);
            Assert.All(report.Tasks, item => Assert.Equal("completed", item.Status));
            Assert.Equal("passed", report.Completion.ProjectVerification);
            Assert.False(report.FactoryProjectState.CurrentRequestPresent);

            var readers = new CodexRolloutReader();
            var rollouts = Directory.EnumerateFiles(Path.Combine(home, "sessions"),
                "*.jsonl", SearchOption.AllDirectories).Select(path => readers.Read(path)).ToArray();
            var supervisor = Assert.Single(rollouts, item => item.ThreadId == report.Run.RootThreadId);
            AssertMultiAgentV2(supervisor);
            var roots = ordered.Select(batch =>
                Assert.Single(rollouts, item => item.ThreadId == batch.RootThreadId)).ToArray();
            Assert.All(roots, root => Assert.Equal(supervisor.ThreadId, root.ParentThreadId));
            foreach (var batch in ordered)
            {
                var root = Assert.Single(roots, candidate => candidate.ThreadId == batch.RootThreadId);
                Assert.Equal($"idd_factory_root_b{batch.BatchOrdinal:0000}",
                    root.AgentPath?.Split('/').Last());
                var workers = report.Agents.Where(item => item.Role == "worker" &&
                    item.ParentThreadId == root.ThreadId).OrderBy(item => item.StartedAt).ToArray();
                Assert.Equal(batch.CompletedWorkers, (int?)workers.Length);
                Assert.All(workers, item => Assert.Equal(batch.BatchOrdinal, item.BatchOrdinal));
                for (var i = 1; i < workers.Length; i++)
                    Assert.True(workers[i - 1].FinishedAt <= workers[i].StartedAt,
                        "Two writing Workers overlapped or native completion order was unknown.");
            }
            foreach (var step in Enumerable.Range(1, 10))
                Assert.True(File.Exists(Path.Combine(workspace, "src", "TenTasks",
                    $"Step{step:00}.cs")), $"Missing implementation Step{step:00}");

            var verification = await RunAsync("dotnet",
                ["test", "tests/TenTasks.Tests/TenTasks.Tests.csproj", "--no-restore",
                 "--logger", "trx;LogFileName=batch-ten-verification.trx",
                 "--results-directory", artifacts],
                workspace, null, null, artifacts, "12-independent-verification", requireSuccess: false);
            Assert.Equal(0, verification.ExitCode);
            ProjectTestEvidence.AssertPassed(
                Path.Combine(artifacts, "batch-ten-verification.trx"), expectedTests: 10);
        }
        catch (Exception exception)
        {
            await TryWriteTextAsync(Path.Combine(artifacts, "exception.txt"), exception.ToString());
            throw;
        }
        finally
        {
            await TrySaveWorkspaceArtifactsAsync(workspace, artifacts);
            await TrySaveReportSourceAsync(home, artifacts);
            if (Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_KEEP_TEMP") != "1")
                try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    private static void PrepareTenTaskWorkspace(string workspace, WorkerExecutionSettings worker)
    {
        static void Write(string root, string relative, string content)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content.ReplaceLineEndings("\n"));
        }

        Write(workspace, ".gitignore", ".idd/factory/\nbin/\nobj/\n**/bin/\n**/obj/\n");
        Write(workspace, ".idd/intent/README.md", """
            # IDD Intent
            Read INDEX.md for the current Product Intent owners. Numbered current
            documents define Product truth, not completion progress.
            """);
        Write(workspace, ".idd/intent/INDEX.md", """
            # IDD Intent Index
            ## Current documents
            | Document | Role | Area | Notes | Replaces |
            | --- | --- | --- | --- | --- |
            | IDD-0001 | spec | TenTasks | Ten independent named steps | |
            """);
        var intent = new StringBuilder("""
            # IDD-0001 spec: Ten independent steps

            The TenTasks library exposes ten independently implemented named steps.
            Each step is a public static class named StepNN in namespace TenTasks
            with a public static string Execute() method. Its exact return value
            is "STEP-NN", where NN is the two-digit step number.

            ## Required steps

            """);
        foreach (var n in Enumerable.Range(1, 10))
            intent.AppendLine($"- Step{n:00}.Execute() returns STEP-{n:00}.");
        Write(workspace, ".idd/intent/IDD-0001.spec-ten-steps.md", intent.ToString());
        Write(workspace, ".idd/verification.yaml", """
            version: 1

            checks:
              build:
                run: dotnet build tests/TenTasks.Tests/TenTasks.Tests.csproj --no-restore
              all-tests:
                run: dotnet test tests/TenTasks.Tests/TenTasks.Tests.csproj --no-restore

            default:
              use:
                - build
            checkpoint:
              use:
                - build
            final:
              use:
                - build
                - all-tests
            """);
        Write(workspace, "src/TenTasks/TenTasks.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """);
        Write(workspace, "src/TenTasks/AssemblyInfo.cs", "namespace TenTasks; public static class AssemblyInfo { }\n");
        Write(workspace, "tests/TenTasks.Tests/TenTasks.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <IsTestProject>true</IsTestProject>
                <IsPackable>false</IsPackable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
                <PackageReference Include="xunit" Version="2.9.3" />
                <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
              </ItemGroup>
              <ItemGroup><ProjectReference Include="../../src/TenTasks/TenTasks.csproj" /></ItemGroup>
            </Project>
            """);
        Write(workspace, "tests/TenTasks.Tests/TenStepsTests.cs", """
            using Xunit;

            namespace TenTasks.Tests;

            public sealed class TenStepsTests
            {
                [Theory]
                [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
                [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
                [InlineData(9)] [InlineData(10)]
                public void StepProducesExactProductValue(int number)
                {
                    var type = Type.GetType($"TenTasks.Step{number:00}, TenTasks");
                    Assert.NotNull(type);
                    var method = type.GetMethod("Execute", System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Static);
                    Assert.NotNull(method);
                    Assert.Equal($"STEP-{number:00}", method.Invoke(null, null));
                }
            }
            """);
        var modelScalar = worker.Model.Replace("'", "''", StringComparison.Ordinal);
        var effortScalar = worker.ReasoningEffort.Replace("'", "''", StringComparison.Ordinal);
        Write(workspace, ".idd/execution.yaml",
            "version: 1\nfactory:\n  executionProfiles:\n    standard:\n" +
            $"      codex:\n        model: '{modelScalar}'\n        reasoningEffort: '{effortScalar}'\n");
    }
}
