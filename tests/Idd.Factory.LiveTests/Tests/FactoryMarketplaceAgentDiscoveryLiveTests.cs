using Xunit;

namespace Idd.Factory.LiveTests.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class LiveFactoryMarketplaceFactAttribute : FactAttribute
{
    public LiveFactoryMarketplaceFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("IDD_RUN_LIVE_FACTORY_MARKETPLACE_EVALS") != "1")
            Skip = "Set IDD_RUN_LIVE_FACTORY_MARKETPLACE_EVALS=1 for this separate, token-consuming marketplace discovery test.";
    }
}

public sealed partial class NativeFactoryEndToEndLiveTests
{
    [LiveFactoryMarketplaceFact]
    [Trait("Category", "LiveFactoryMarketplace")]
    public async Task Factory_MarketplaceAloneDiscoversNativeAgents()
    {
        var repo = FindRepositoryRoot();
        var runId = "marketplace-agents-" + Guid.NewGuid().ToString("N");
        var tempRoot = Path.Combine(Path.GetTempPath(), "idd-factory-marketplace-eval", runId);
        var workspace = Path.Combine(tempRoot, "workspace");
        var marketplace = Path.Combine(tempRoot, "marketplace");
        var codexHome = Path.Combine(tempRoot, "codex-home");
        var artifacts = Path.Combine(repo, "artifacts", "factory-evals", runId);
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(artifacts);
        var settings = LiveEvalSettings.Resolve(
            repo,
            Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_MODEL"),
            Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_REASONING_EFFORT"),
            profileEnvironment: Environment.GetEnvironmentVariable);
        var codex = Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_CODEX") ?? "codex";
        var timeoutMinutes = int.TryParse(Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_TIMEOUT_MINUTES"), out var minutes)
            ? minutes : 10;
        var env = new Dictionary<string, string?> { ["CODEX_HOME"] = codexHome };

        try
        {
            await File.WriteAllTextAsync(Path.Combine(workspace, "README.md"),
                "Disposable Codex Marketplace native-agent discovery probe.\n");
            await InitializeGitRepositoryAsync(workspace, artifacts);
            await VerifyGitBaselineAsync(workspace, artifacts, requireProjectAgents: false);
            PrepareCodexHome(codexHome, workspace);

            await RunAsync("dotnet",
                ["build", "tools/generate/Generate.csproj", "--nologo"],
                repo, null, null, artifacts, "06-generator-build");
            await RunAsync("dotnet",
                ["exec", Path.Combine(repo, "tools", "generate", "bin", "Debug", "net10.0", "Generate.dll"),
                    "--version", "0.0.0-live", "--output", marketplace],
                repo, null, null, artifacts, "07-generate-marketplace");

            var generated = Path.Combine(marketplace, "plugins", "codex", "idd-factory");
            Assert.Equal(2, FactoryEvalAgentRegistration.ResolveGeneratedAgents(generated).Count);

            await RunAsync(codex,
                ["plugin", "marketplace", "add", marketplace, "--json"],
                repo, env, null, artifacts, "08-codex-marketplace-add");
            await RunAsync(codex,
                ["plugin", "add", "idd-factory@intent-driven-development", "--json"],
                repo, env, null, artifacts, "09-codex-plugin-add");

            // A workspace without .codex/agents isolates genuine plugin discovery.
            Assert.False(Directory.Exists(Path.Combine(workspace, ".codex", "agents")),
                "Marketplace-only test must not register project-local custom agents.");

            var result = await RunAsync(codex,
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
                workspace, env, NativeRoleProbePrompt, artifacts, "10-marketplace-agent-probe",
                timeout: TimeSpan.FromMinutes(timeoutMinutes));

            await File.WriteAllTextAsync(Path.Combine(artifacts, "codex-trace.jsonl"), result.Stdout);
            var root = FindRootRollout(codexHome, requireSpawn: false);
            VerifyRegisteredNativeRoleSpawns(root, artifacts, "marketplace-only");

            var status = await RunAsync("git", ["status", "--porcelain=v1"],
                workspace, null, null, artifacts, "11-marketplace-workspace-status");
            Assert.True(string.IsNullOrWhiteSpace(status.Stdout),
                "Marketplace discovery probe unexpectedly modified its workspace.");
        }
        catch (Exception ex)
        {
            await TryWriteTextAsync(Path.Combine(artifacts, "exception.txt"), ex.ToString());
            throw;
        }
        finally
        {
            await TrySaveWorkspaceArtifactsAsync(workspace, artifacts);
            await TrySaveReportSourceAsync(codexHome, artifacts);
            if (Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_KEEP_TEMP") != "1")
                try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

}
