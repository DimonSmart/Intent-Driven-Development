using Idd.Factory.LiveTests.Environments;
using Idd.Factory.LiveTests.Infrastructure;
using Idd.Factory.LiveTests.Models;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class CodexLaunchTests
{
    [Fact]
    public void RunArguments_PersistOnlyWhenRequested()
    {
        var workspace = new FactoryEvalWorkspace("run", "workspace", "marketplace", "verification", "case");
        var options = new FactoryEvalOptions("model", "low", TimeSpan.FromMinutes(1), "version");
        var defaultArguments = LocalFactoryEvalEnvironment.BuildRunCodexArguments(workspace, options);
        var persistentArguments = LocalFactoryEvalEnvironment.BuildRunCodexArguments(workspace, options with { PersistSessionRollouts = true });

        Assert.Contains("--ephemeral", defaultArguments);
        Assert.DoesNotContain("--ephemeral", persistentArguments);
        Assert.Equal(defaultArguments.Where(argument => argument != "--ephemeral"), persistentArguments);
    }

    [Fact]
    public void Resolver_PrefersTheNpmPackagedNativeExecutable()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var npmDirectory = Path.Combine(root, "npm");
        var nativeExecutable = Path.Combine(npmDirectory, "node_modules", "@openai", "codex", "node_modules", "@openai", "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "bin", "codex.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(nativeExecutable)!);
        File.WriteAllText(nativeExecutable, string.Empty);
        File.WriteAllText(Path.Combine(npmDirectory, "node.exe"), string.Empty);

        try
        {
            var command = CodexExecutableResolver.ResolveFromPath(npmDirectory, isWindows: true);

            Assert.Equal(nativeExecutable, command.Executable);
            Assert.Empty(command.PrefixArguments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReleaseEvalWithoutCodexFailsWithAConcretePrerequisiteError()
    {
        var exception = Assert.Throws<FileNotFoundException>(() =>
            CodexExecutableResolver.ResolveFromPath(string.Empty, isWindows: true));

        Assert.Contains("Could not locate the npm Codex CLI", exception.Message);
    }

    [Fact]
    public void EvalEnvironment_PropagatesWorkerExecutionConfigurationAndControlledCapabilities()
    {
        var options = new FactoryEvalOptions("gpt-test", "high", TimeSpan.FromMinutes(1), "version");
        var environment = LocalFactoryEvalEnvironment.BuildCodexEnvironment("path", isWindows: false, codexHome: "isolated", options: options);

        Assert.Equal("isolated", environment["CODEX_HOME"]);
        Assert.Equal("gpt-test", environment["IDD_FACTORY_MODEL"]);
        Assert.Equal("high", environment["IDD_FACTORY_REASONING_EFFORT"]);
        Assert.Equal("false", environment["IDD_FACTORY_INHERIT_USER_SKILLS"]);
        Assert.Equal("release-eval-controlled", environment["IDD_FACTORY_CAPABILITY_PROFILE"]);
    }
}
