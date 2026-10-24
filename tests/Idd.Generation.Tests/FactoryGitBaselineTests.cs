using System.Diagnostics;
using Xunit;

namespace Idd.Generation.Tests;

// Executable Git fixtures for the exact porcelain command specified by Factory.
// Factory itself is an agent protocol, so these tests validate Git edge cases,
// not the semantics of agent recovery or authorization.
public sealed class FactoryGitBaselineTests
{
    private sealed class Repo : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(), "idd-factory-git-" + Guid.NewGuid().ToString("N"));

        public Repo()
        {
            Directory.CreateDirectory(Root);
            Git("init", "-q");
            Git("config", "user.name", "IDD Test");
            Git("config", "user.email", "idd-test@example.invalid");
            Write(".idd/intent/IDD-0001.spec-sample.md", "initial");
            Write("src/code.cs", "initial");
            Git("add", ".");
            Git("commit", "-qm", "baseline");
        }

        public void Write(string path, string content)
        {
            var file = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, content);
        }

        public string Git(params string[] args)
        {
            using var p = new Process();
            p.StartInfo.FileName = "git";
            p.StartInfo.WorkingDirectory = Root;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;
            foreach (var arg in args)
                p.StartInfo.ArgumentList.Add(arg);
            Assert.True(p.Start(), "git did not start");
            var output = p.StandardOutput.ReadToEnd();
            var error = p.StandardError.ReadToEnd();
            Assert.True(p.WaitForExit(15000), "git did not exit in time");
            Assert.True(p.ExitCode == 0, $"git {string.Join(' ', args)} failed: {error}");
            return output;
        }

        public string ManagedStatus() =>
            Git("status", "--porcelain=v1", "-z", "--untracked-files=all",
                "--", ".idd/intent/", ".idd/engineering/");

        public void Dispose()
        {
            if (!Directory.Exists(Root))
                return;

            // Windows Git marks object files read-only. Normalize attributes
            // before recursively deleting this isolated test-only repository.
            foreach (var file in Directory.EnumerateFiles(
                         Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);

            foreach (var directory in Directory.EnumerateDirectories(
                         Root, "*", SearchOption.AllDirectories))
            {
                var attributes = File.GetAttributes(directory);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(directory, attributes & ~FileAttributes.ReadOnly);
            }

            Directory.Delete(Root, recursive: true);
        }
    }

    [Fact]
    public void CleanIdd_AllowsDirtyCodeButBlocksStagedUnstagedAndUntrackedIdd()
    {
        using var repo = new Repo();
        Assert.Equal(string.Empty, repo.ManagedStatus());

        repo.Write("src/code.cs", "edited code");
        Assert.Equal(string.Empty, repo.ManagedStatus());

        repo.Write(".idd/intent/IDD-0001.spec-sample.md", "unstaged change");
        Assert.Contains("IDD-0001", repo.ManagedStatus());
        repo.Git("add", ".idd/intent/IDD-0001.spec-sample.md");
        Assert.Contains("IDD-0001", repo.ManagedStatus());
        repo.Git("restore", "--staged", ".idd/intent/IDD-0001.spec-sample.md");
        repo.Git("restore", ".idd/intent/IDD-0001.spec-sample.md");

        repo.Write(".idd/engineering/new rule.txt", "not tracked");
        var status = repo.ManagedStatus();
        Assert.Contains("new rule.txt", status);
        Assert.Contains('\0', status);
    }

    [Fact]
    public void RenamesAndDeletesRemainVisibleWithWhitespaceInNames()
    {
        using var repo = new Repo();
        repo.Git("mv", ".idd/intent/IDD-0001.spec-sample.md",
            ".idd/intent/IDD-0001.spec-new name.md");
        var renamed = repo.ManagedStatus();
        Assert.Contains("IDD-0001.spec-new name.md", renamed);
        Assert.Contains("IDD-0001.spec-sample.md", renamed);
        Assert.Contains('\0', renamed);

        repo.Git("reset", "--hard", "HEAD"); // only the isolated fixture repo
        File.Delete(Path.Combine(repo.Root, ".idd", "intent", "IDD-0001.spec-sample.md"));
        Assert.Contains("IDD-0001.spec-sample.md", repo.ManagedStatus());
    }
}
