using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed partial class NativeFactoryEndToEndLiveTests
{
    [Fact]
    public async Task FactoryEvalGitBaseline_DoesNotStageLegacyCodexAgentFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "idd-factory-git-baseline-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(root, "workspace");
        var artifacts = Path.Combine(root, "artifacts");
        try
        {
            Directory.CreateDirectory(workspace);
            Directory.CreateDirectory(artifacts);
            await File.WriteAllTextAsync(Path.Combine(workspace, ".gitignore"), ".codex/\n");
            await File.WriteAllTextAsync(Path.Combine(workspace, "README.md"), "Baseline test.\n");

            var agents = Path.Combine(workspace, ".codex", "agents");
            Directory.CreateDirectory(agents);
            await File.WriteAllTextAsync(Path.Combine(agents, "idd-internal-factory-planner.toml"), "legacy");
            await File.WriteAllTextAsync(Path.Combine(agents, "idd-internal-factory-worker.toml"), "legacy");
            await InitializeGitRepositoryAsync(workspace, artifacts);
            await VerifyGitBaselineAsync(workspace, artifacts);
            var tracked = await RunAsync("git", ["ls-files", "--", ".codex/agents"],
                workspace, null, null, artifacts, "06-git-agent-list");
            Assert.True(string.IsNullOrWhiteSpace(tracked.Stdout));

            var status = await RunAsync("git", ["status", "--porcelain=v1"],
                workspace, null, null, artifacts, "06-git-status");
            Assert.True(string.IsNullOrWhiteSpace(status.Stdout),
                $"Expected a clean baseline, found: {status.Stdout}");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                if (OperatingSystem.IsWindows())
                {
                    foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                        File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
                }
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
