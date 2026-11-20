using System.Text;
using Idd.Factory.Report;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed partial class NativeFactoryEndToEndLiveTests
{
    [LiveFactoryMarketplaceFact]
    [Trait("Category", "LiveFactoryMarketplace")]
    public async Task Factory_NativeTenTasksRotatesOnlyAtPlanBoundariesWithLimitThree()
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
                The FIRST Planner must issue Step01 through Step04 as FOUR
                separate # Task sections in ONE Planner result, to test a plan
                larger than the target of three Workers. Subsequent Planner
                results must contain at most four # Task sections. Do not split
                a single plan across Roots merely to meet the target.
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
            Assert.InRange(report.Metrics.FactoryRootInvocations, 2, 10);
            Assert.Equal("completed", report.Run.Result);
            Assert.Equal("passed", report.Completion.ProjectVerification);
            Assert.False(report.FactoryProjectState.CurrentRequestPresent);

            var ordered = report.Batches.OrderBy(item => item.BatchOrdinal).ToArray();
            Assert.True(ordered.Select(item => item.BatchOrdinal).SequenceEqual(
                Enumerable.Range(1, ordered.Length).Select(n => (int?)n)));
            Assert.All(ordered.SkipLast(1), batch =>
            {
                Assert.Equal("CONTINUE", batch.Status);
                Assert.Equal("BATCH_LIMIT", batch.Reason);
                Assert.True(batch.CompletedWorkers >= 3);
            });
            Assert.Equal("COMPLETED", ordered[^1].Status);
            Assert.Equal(10, ordered.Sum(batch => batch.CompletedWorkers ?? 0));

            var reader = new CodexRolloutReader();
            var rollouts = Directory.EnumerateFiles(Path.Combine(home, "sessions"),
                "*.jsonl", SearchOption.AllDirectories).Select(path => reader.Read(path)).ToArray();
            var supervisor = Assert.Single(rollouts, item => item.ThreadId == report.Run.RootThreadId);
            AssertMultiAgentV2(supervisor);
            var roots = ordered.Select(batch =>
                Assert.Single(rollouts, item => item.ThreadId == batch.RootThreadId)).ToArray();
            CorrelateNativeSpawnsForAssertions(supervisor, home, report);
            foreach (var root in roots)
                CorrelateNativeSpawnsForAssertions(root, home, report);
            FactoryNativeEvidence.VerifyRoots(supervisor, roots);

            var confirmedWorkers = 0;
            for (var i = 0; i < roots.Length; i++)
            {
                var root = roots[i];
                var children = rollouts.Where(item => item.ParentThreadId == root.ThreadId &&
                    FactoryBatchTrace.ParseName(item.AgentPath?.Split('/').Last())?.Role
                        is "planner" or "worker").ToArray();
                confirmedWorkers += FactoryNativeEvidence.VerifyPlans(root, children);
                Assert.Equal(ordered[i].CompletedWorkers,
                    children.Count(item => FactoryBatchTrace.ParseName(
                        item.AgentPath?.Split('/').Last())?.Role == "worker"));
                if (i == roots.Length - 1)
                    Assert.Contains(children, child =>
                        FactoryBatchTrace.ParseName(child.AgentPath?.Split('/').Last())?.Role == "planner" &&
                        FactoryNativeEvidence.ConfirmedFinalText(child) == "# Done");
            }
            Assert.Equal(10, confirmedWorkers);
            Assert.Equal(10, report.Metrics.WorkerInvocations);
            Assert.Equal(10, report.Tasks.Count);
            Assert.All(report.Tasks, task => Assert.Equal("completed", task.Status));
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
