using CodexLaunchExperiment;
using Idd.Factory.LiveTests.Infrastructure;
using Idd.Factory.LiveTests.Environments;
using Idd.Factory.LiveTests.Models;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class CodexLaunchExperimentTests
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
    public void CmdCommand_QuotesCommandPathAndEscapesMetacharacters()
    {
        var command = CommandFormatting.BuildCmdCommand(@"C:\Program Files\Codex\codex.cmd", ["exec", "a&b"]);

        Assert.Contains("C:\\Program Files\\Codex\\codex.cmd", command);
        Assert.Contains("a^&b", command);
    }

    [Fact]
    public void CandidateSelection_PrefersNativeThenCmdAndLimitsToTwo()
    {
        var selected = CodexDiscovery.ChooseModelCandidates(
        [new(LaunchKind.NodeScript, "node.exe", ["codex.js"], "node"), new(LaunchKind.CmdShim, "codex.cmd", [], "cmd"), new(LaunchKind.NativeExecutable, "codex.exe", [], "native")], @"C:\Windows\System32\cmd.exe");

        Assert.Equal([LaunchKind.NativeExecutable, LaunchKind.CmdShim], selected.Select(candidate => candidate.Kind));
        Assert.Equal(@"C:\Windows\System32\cmd.exe", selected[1].Executable);
    }

    [Fact]
    public void FileSnapshots_DetectsCreatedChangedAndDeletedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "changed.txt"), "before"); File.WriteAllText(Path.Combine(root, "deleted.txt"), "delete");
            var before = FileSnapshots.Take(root);
            File.WriteAllText(Path.Combine(root, "changed.txt"), "after"); File.Delete(Path.Combine(root, "deleted.txt")); File.WriteAllText(Path.Combine(root, "created.txt"), "created");
            var diff = FileSnapshots.Compare(before, FileSnapshots.Take(root));
            Assert.Equal(["created.txt"], diff.Created); Assert.Equal(["changed.txt"], diff.Changed); Assert.Equal(["deleted.txt"], diff.Deleted);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("CreateProcessAsUserW failed: 2", Outcome.SandboxInitializationFailed)]
    [InlineData("approval required", Outcome.ApprovalRequested)]
    public void Diagnostics_ClassifiesKnownFailures(string stderr, Outcome expected)
    {
        var process = new ProcessCapture(1, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
        Assert.Equal(expected, Diagnostics.Classify(process, stderr, false, false, false, true, true, false));
    }

    [Fact]
    public void Diagnostics_RedactsSecretValues()
    {
        var redacted = Diagnostics.Redact("OPENAI_API_KEY=secret-value PATH=C:\\tools");
        Assert.DoesNotContain("secret-value", redacted);
        Assert.Contains("[REDACTED]", redacted);
    }
}
