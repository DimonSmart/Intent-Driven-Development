using System.Text.Json;
using Idd.Factory.Report;
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
            PrepareCodexHome(codexHome);

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

            var prompt = """
                This is an isolated host-native agent discovery probe, not a Factory run.
                Probe exactly these two registered native agent types, one after the other:
                idd-internal-factory-planner and idd-internal-factory-worker.
                Use native spawn_agent with the exact agent_type, a unique task_name,
                fork_turns "none", and a short message saying there is NO valid Factory
                assignment and the child must return BLOCKED without modifying files.
                Wait for each child to finish. Do not use generic agents, construct role
                prompts, or edit any project files. If a type is missing, report the
                native rejection without substituting a different type.
                """;
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
                workspace, env, prompt, artifacts, "10-marketplace-agent-probe",
                timeout: TimeSpan.FromMinutes(timeoutMinutes));

            await File.WriteAllTextAsync(Path.Combine(artifacts, "codex-trace.jsonl"), result.Stdout);
            var root = FindRootRollout(codexHome, requireSpawn: false);
            var nativeCalls = root.Events.Where(e => e.ToolName == "spawn_agent").ToArray();
            foreach (var role in FactoryEvalAgentRegistration.RequiredRoles)
            {
                var calls = nativeCalls.Where(e => GetRequestedAgentType(e.ToolArguments) == role).ToArray();
                Assert.True(calls.Length == 1,
                    $"Marketplace did not produce exactly one native spawn_agent attempt for {role}; " +
                    $"found {calls.Length}. Evidence: {artifacts}/codex-trace.jsonl");

                var call = calls[0];
                Assert.True(root.SpawnRecords.TryGetValue(call.ToolId ?? "", out var spawn),
                    $"No native spawn result for {role}: {call.ToolId}");
                Assert.True(spawn.SpawnOutcome == "accepted" && spawn.Children.Any(),
                    $"Marketplace did not register a callable {role}: " +
                    $"outcome={spawn.SpawnOutcome}, code={spawn.RejectionCode}, " +
                    $"reason={spawn.RejectionMessage}. Evidence: {artifacts}/codex-trace.jsonl");
            }

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

    private static string? GetRequestedAgentType(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("agent_type", out var type) &&
                   type.ValueKind == JsonValueKind.String
                ? type.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
