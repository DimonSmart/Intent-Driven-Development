using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed partial class NativeFactoryEndToEndLiveTests
{
    [Fact]
    public async Task FactoryEvalGitBaseline_TracksGeneratedAgentsDespiteIgnoredCodexDirectory()
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
            foreach (var name in FactoryEvalAgentRegistration.RequiredRoles)
                await File.WriteAllTextAsync(Path.Combine(agents, name + ".toml"),
                    "name = \"" + name + "\"\n");

            await InitializeGitRepositoryAsync(workspace, artifacts, includeProjectAgents: true);
            await VerifyGitBaselineAsync(workspace, artifacts);

            var status = await RunAsync("git", ["status", "--porcelain=v1"],
                workspace, null, null, artifacts, "06-git-status");
            Assert.True(string.IsNullOrWhiteSpace(status.Stdout),
                $"Expected a clean baseline, found: {status.Stdout}");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
