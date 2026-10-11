using System.Diagnostics;
using Xunit;

namespace Idd.Intent.LiveTests.Tests;

public sealed class IntentLintEndToEndLiveTests
{
    [LiveIntentEvalFact]
    [Trait("Category", "LiveIntentEval")]
    public async Task CheckIsReadOnly_AndFixPreservesIdentityAndUserData()
    {
        var repo = FindRepositoryRoot();
        var root = Path.Combine(Path.GetTempPath(), "idd-intent-lint-eval", Guid.NewGuid().ToString("N"));
        var keep = string.Equals(Environment.GetEnvironmentVariable("IDD_INTENT_EVAL_KEEP_TEMP"), "1",
            StringComparison.Ordinal);
        var model = Environment.GetEnvironmentVariable("IDD_INTENT_EVAL_MODEL") ?? "gpt-5.6-luna";
        var reasoning = Environment.GetEnvironmentVariable("IDD_INTENT_EVAL_REASONING_EFFORT") ?? "low";
        var codex = Environment.GetEnvironmentVariable("IDD_INTENT_EVAL_CODEX") ?? "codex";
        var timeout = TimeSpan.FromMinutes(
            int.TryParse(Environment.GetEnvironmentVariable("IDD_INTENT_EVAL_TIMEOUT_MINUTES"), out var value)
                ? value : 20);
        var marketplace = Path.Combine(root, "marketplace");
        var workspace = Path.Combine(root, "workspace");
        var home = Path.Combine(root, "codex-home");

        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(home);
            var currentHome = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (string.IsNullOrWhiteSpace(currentHome))
                currentHome = Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile), ".codex");
            var auth = Path.Combine(currentHome, "auth.json");
            if (File.Exists(auth))
                File.Copy(auth, Path.Combine(home, "auth.json"), overwrite: true);

            await RunAsync("dotnet", ["build", "tools/generate/Generate.csproj", "--nologo"], repo);
            var generator = Path.Combine(repo, "tools", "generate", "bin", "Debug", "net10.0", "Generate.dll");
            await RunAsync("dotnet", ["exec", generator, "--version", "0.0.0-live", "--output", marketplace], repo);

            var generated = Path.Combine(marketplace, "plugins", "codex", "idd-intent",
                "skills", "idd-intent-lint");
            foreach (var file in new[] { "README.md", "INDEX.md", "_templates/spec.md",
                                         "_templates/adr.md", "_templates/spike.md" })
                Assert.True(File.Exists(Path.Combine(generated, "assets", "bootstrap",
                    ".idd", "intent", file)));

            var env = new Dictionary<string, string?> { ["CODEX_HOME"] = home };
            await RunAsync(codex, ["plugin", "marketplace", "add", marketplace, "--json"], repo, env);
            await RunAsync(codex, ["plugin", "add", "idd-intent@intent-driven-development", "--json"],
                repo, env);

            var intent = Path.Combine(workspace, ".idd", "intent");
            Directory.CreateDirectory(intent);
            Directory.CreateDirectory(Path.Combine(intent, "_templates"));

            var canonical = Path.Combine(repo, "src", "canonical", "project-files", "intent");
            foreach (var file in new[] { "INDEX.md", "_templates/spec.md", "_templates/adr.md",
                                         "_templates/spike.md" })
            {
                var dest = Path.Combine(intent, file);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(Path.Combine(canonical, file), dest);
            }

            var idx = Path.Combine(intent, "INDEX.md");
            File.AppendAllText(idx,
                "| `0001.spec-timer.md` | Spec | Clock | Author note | — |\n" +
                "| IDD-0002 | Spec | Integration | Keep untouched | — |\n");

            File.WriteAllText(Path.Combine(intent, "0001.spec-timer.md"), """
                # 0001.spec-timer

                ## Intent

                A durable timer contract.

                ## Related Specifications

                None.

                ## Behavior

                The timer reports elapsed seconds.

                ## Non-Goals

                No time synchronization.

                ## Acceptance Criteria

                Elapsed seconds are monotonic.

                ## Verification

                Verify elapsed seconds never decrease.
                """);

            var dependent = Path.Combine(intent, "IDD-0002.spec-integration.md");
            File.WriteAllText(dependent, """
                # IDD-0002.spec-integration

                ## Intent

                Integration uses the timer.

                ## Related Specifications

                [Timer](0001.spec-timer.md#behavior)

                ## Behavior

                Timer results are displayed.

                ## Non-Goals

                No scheduling.

                ## Acceptance Criteria

                The timer is visible.

                ## Verification

                Verify the timer is visible.
                """);

            var glossary = Path.Combine(intent, "GLOSSARY.md");
            File.WriteAllText(glossary, "# Incorrect user glossary heading\n\n## Timer\nExplanation.\n");

            await RunAsync("git", ["init"], workspace);
            await RunAsync("git", ["config", "user.name", "IDD Lint Eval"], workspace);
            await RunAsync("git", ["config", "user.email", "idd-lint-eval@localhost"], workspace);
            await RunAsync("git", ["add", "--all"], workspace);
            await RunAsync("git", ["commit", "-m", "Initial invalid Intent state"], workspace);

            var before = Snapshot(intent);
            await InvokeLint(workspace, codex, env, model, reasoning, timeout, "check");
            Assert.Equal(before, Snapshot(intent));

            await InvokeLint(workspace, codex, env, model, reasoning, timeout, "fix");
            Assert.False(File.Exists(Path.Combine(intent, "0001.spec-timer.md")));
            var renamed = Path.Combine(intent, "IDD-0001.spec-timer.md");
            Assert.True(File.Exists(renamed));
            Assert.Contains("# IDD-0001.spec-timer", File.ReadAllText(renamed));
            Assert.Contains("IDD-0001 | Spec | Clock | Author note | —", File.ReadAllText(idx));
            Assert.Contains("[Timer](IDD-0001.spec-timer.md#behavior)", File.ReadAllText(dependent));
            Assert.True(File.Exists(Path.Combine(intent, "README.md")));
            Assert.Equal("# Incorrect user glossary heading\n\n## Timer\nExplanation.\n",
                File.ReadAllText(glossary));

            var fixedState = Snapshot(intent);
            await InvokeLint(workspace, codex, env, model, reasoning, timeout, "fix");
            Assert.Equal(fixedState, Snapshot(intent));
        }
        finally
        {
            if (!keep)
                try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static async Task InvokeLint(
        string workspace, string codex, IReadOnlyDictionary<string, string?> env,
        string model, string reasoning, TimeSpan timeout, string mode)
    {
        await RunAsync(codex,
            [
                "exec", "--json", "--ignore-rules",
                "--enable", "plugins", "--disable", "remote_plugin",
                "-c", "approval_policy=never",
                "-c", $"model_reasoning_effort={reasoning}",
                "--model", model,
                "--sandbox", "danger-full-access",
                "--cd", workspace, "-"
            ],
            workspace, env,
            $"Use $idd-intent-lint with scope .idd/intent/ and mode: {mode}. " +
            "Check all mechanical rules; obey read-only boundaries in check mode. " +
            "In fix mode apply only safe mechanical fixes, re-check, and report actual changes.",
            timeout);
    }

    private static string[] Snapshot(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/') + "\n" +
                            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                                File.ReadAllBytes(path))))
            .ToArray();

    private static async Task RunAsync(
        string executable, IReadOnlyList<string> args, string cwd,
        IReadOnlyDictionary<string, string?>? env = null, string? stdin = null,
        TimeSpan? timeout = null)
    {
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = cwd, RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        if (env is not null)
            foreach (var (key, value) in env) info.Environment[key] = value;

        using var process = Process.Start(info) ?? throw new InvalidOperationException(executable);
        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
        }
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(cancellation.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException(executable + " timed out.");
        }
        var output = await stdout;
        var error = await stderr;
        Assert.True(process.ExitCode == 0, executable + " failed:\n" + error + "\n" + output);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Environment.CurrentDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Intent-Driven-Development.slnx")))
                return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException("Could not find IDD repository.");
    }
}
