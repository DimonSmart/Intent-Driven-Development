using System.Diagnostics;
using Xunit;

namespace Idd.Intent.LiveTests.Tests;

public sealed class IntentImportEndToEndLiveTests
{
    [LiveIntentEvalFact]
    [Trait("Category", "LiveIntentEval")]
    public async Task IntentImport_MigratesSuppliedEngineeringKnowledgeSafely()
    {
        var repo = FindRepositoryRoot();
        var tempRoot = ResolveTempRoot();
        var marketplace = Path.Combine(tempRoot, "marketplace");
        var codexHome = Path.Combine(tempRoot, "codex-home");
        var keepTemp = string.Equals(
            Environment.GetEnvironmentVariable("IDD_INTENT_EVAL_KEEP_TEMP"),
            "1",
            StringComparison.Ordinal);
        var timeoutMinutes =
            int.TryParse(Environment.GetEnvironmentVariable("IDD_INTENT_EVAL_TIMEOUT_MINUTES"), out var configuredTimeout)
                ? configuredTimeout
                : 20;
        var model = Environment.GetEnvironmentVariable("IDD_INTENT_EVAL_MODEL") ?? "gpt-5.6-luna";
        var reasoning = Environment.GetEnvironmentVariable("IDD_INTENT_EVAL_REASONING_EFFORT") ?? "low";
        var codex = Environment.GetEnvironmentVariable("IDD_INTENT_EVAL_CODEX") ?? "codex";

        try
        {
            Directory.CreateDirectory(tempRoot);
            PrepareCodexHome(codexHome);

            await RunAsync("dotnet", ["build", "tools/generate/Generate.csproj", "--nologo"], repo);

            var generator = Path.Combine(repo, "tools", "generate", "bin", "Debug", "net10.0", "Generate.dll");
            await RunAsync(
                "dotnet",
                ["exec", generator, "--version", "0.0.0-live", "--output", marketplace],
                repo);

            var generatedImport = Path.Combine(
                marketplace, "plugins", "codex", "idd-intent", "skills", "idd-intent-import");
            Assert.True(File.Exists(Path.Combine(generatedImport, "references", "engineering-guardrails.md")));
            Assert.True(File.Exists(Path.Combine(
                generatedImport, "assets", "bootstrap", ".idd", "engineering", "README.md")));
            Assert.True(File.Exists(Path.Combine(
                generatedImport, "assets", "bootstrap", ".idd", "engineering", "INDEX.md")));

            var env = new Dictionary<string, string?> { ["CODEX_HOME"] = codexHome };
            await RunAsync(codex, ["plugin", "marketplace", "add", marketplace, "--json"], repo, env);
            await RunAsync(
                codex,
                ["plugin", "add", "idd-intent@intent-driven-development", "--json"],
                repo,
                env);

            await MixedIntentAndEngineeringAsync(repo, tempRoot, codex, env, model, reasoning, timeoutMinutes);
            await ProductIntentOnlyAsync(repo, tempRoot, codex, env, model, reasoning, timeoutMinutes);
            await AmbiguousTechnicalChoiceAsync(repo, tempRoot, codex, env, model, reasoning, timeoutMinutes);
            await ExistingEquivalentRuleAsync(repo, tempRoot, codex, env, model, reasoning, timeoutMinutes);
            await ExistingConflictingRuleAsync(repo, tempRoot, codex, env, model, reasoning, timeoutMinutes);
            await ExplicitReplacementAsync(repo, tempRoot, codex, env, model, reasoning, timeoutMinutes);
        }
        finally
        {
            if (!keepTemp)
            {
                try { Directory.Delete(tempRoot, recursive: true); } catch { }
            }
        }
    }

    private static async Task MixedIntentAndEngineeringAsync(
        string repo, string tempRoot, string codex, IReadOnlyDictionary<string, string?> env,
        string model, string reasoning, int timeoutMinutes)
    {
        var workspace = await CreateWorkspaceAsync(repo, tempRoot, "mixed-intent-engineering", """
            Product requirements:

            User uploads SVG.
            Application produces downloadable STL.
            Image fits inside configured printable region.

            Engineering decisions:

            Application uses Blazor WebAssembly.
            UI uses MudBlazor.
            Polygon operations use Clipper2.
            STL generation is performed client-side.

            Research/history:

            NetTopologySuite was also considered.
            A custom polygon engine might be investigated later.
            """);

        await RunImportAsync(workspace, codex, env, model, reasoning, timeoutMinutes);

        var intent = ReadCombinedIntent(workspace);
        Assert.Contains("SVG", intent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("STL", intent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("printable", intent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MudBlazor", intent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Clipper2", intent, StringComparison.OrdinalIgnoreCase);

        var engineering = ReadCombinedRules(workspace);
        Assert.Contains("Blazor WebAssembly", engineering, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MudBlazor", engineering, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Clipper2", engineering, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("client-side", engineering, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NetTopologySuite", engineering, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("custom polygon engine", engineering, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ProductIntentOnlyAsync(
        string repo, string tempRoot, string codex, IReadOnlyDictionary<string, string?> env,
        string model, string reasoning, int timeoutMinutes)
    {
        var workspace = await CreateWorkspaceAsync(repo, tempRoot, "intent-only", """
            Product requirements:

            User uploads SVG.
            Application produces downloadable STL.
            Image fits inside configured printable region.
            """);

        await RunImportAsync(workspace, codex, env, model, reasoning, timeoutMinutes);

        Assert.Contains("SVG", ReadCombinedIntent(workspace), StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(workspace, ".idd", "engineering")));
    }

    private static async Task AmbiguousTechnicalChoiceAsync(
        string repo, string tempRoot, string codex, IReadOnlyDictionary<string, string?> env,
        string model, string reasoning, int timeoutMinutes)
    {
        var workspace = await CreateWorkspaceAsync(repo, tempRoot, "ambiguous-choice", """
            Product requirements:

            User uploads SVG and downloads STL.

            Technical research:

            Use either Clipper2 or NetTopologySuite.
            Final choice has not been made.
            """);

        await RunImportAsync(workspace, codex, env, model, reasoning, timeoutMinutes);

        Assert.Contains("SVG", ReadCombinedIntent(workspace), StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(workspace, ".idd", "engineering")));
    }

    private static async Task ExistingEquivalentRuleAsync(
        string repo, string tempRoot, string codex, IReadOnlyDictionary<string, string?> env,
        string model, string reasoning, int timeoutMinutes)
    {
        var workspace = await CreateWorkspaceAsync(repo, tempRoot, "equivalent-rule", """
            Engineering decisions:

            Clipper2 is the canonical polygon engine.
            """, withPolygonRule: true);

        var before = File.ReadAllText(Path.Combine(workspace, ".idd", "engineering", "ENG-0002.rule-polygon-engine.md"));
        await RunImportAsync(workspace, codex, env, model, reasoning, timeoutMinutes);

        var rules = GetRuleFiles(workspace);
        Assert.Single(rules);
        Assert.Equal("ENG-0002.rule-polygon-engine.md", Path.GetFileName(rules[0]));
        Assert.Equal(before, File.ReadAllText(rules[0]));
        Assert.Contains(
            "Next ID: ENG-0003",
            File.ReadAllText(Path.Combine(workspace, ".idd", "engineering", "INDEX.md")),
            StringComparison.Ordinal);
    }

    private static async Task ExistingConflictingRuleAsync(
        string repo, string tempRoot, string codex, IReadOnlyDictionary<string, string?> env,
        string model, string reasoning, int timeoutMinutes)
    {
        var workspace = await CreateWorkspaceAsync(repo, tempRoot, "conflicting-rule", """
            Engineering decisions:

            NetTopologySuite is the canonical polygon engine.
            """, withPolygonRule: true);

        await RunImportAsync(workspace, codex, env, model, reasoning, timeoutMinutes);

        var rules = GetRuleFiles(workspace);
        Assert.Single(rules);
        var content = File.ReadAllText(rules[0]);
        Assert.Contains("Clipper2", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NetTopologySuite", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Next ID: ENG-0003",
            File.ReadAllText(Path.Combine(workspace, ".idd", "engineering", "INDEX.md")),
            StringComparison.Ordinal);
    }

    private static async Task ExplicitReplacementAsync(
        string repo, string tempRoot, string codex, IReadOnlyDictionary<string, string?> env,
        string model, string reasoning, int timeoutMinutes)
    {
        var workspace = await CreateWorkspaceAsync(repo, tempRoot, "explicit-replacement", """
            Engineering decisions:

            The previous Clipper2 decision is replaced.
            NetTopologySuite is now the canonical polygon engine.
            """, withPolygonRule: true);

        await RunImportAsync(workspace, codex, env, model, reasoning, timeoutMinutes);

        var rules = GetRuleFiles(workspace);
        Assert.Single(rules);
        Assert.True(
            Path.GetFileName(rules[0]).StartsWith("ENG-0002.rule-", StringComparison.Ordinal),
            "Explicit replacement must preserve ENG-0002 identity.");
        var content = File.ReadAllText(rules[0]);
        Assert.Contains("NetTopologySuite", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Next ID: ENG-0003",
            File.ReadAllText(Path.Combine(workspace, ".idd", "engineering", "INDEX.md")),
            StringComparison.Ordinal);
    }

    private static async Task<string> CreateWorkspaceAsync(
        string repo, string tempRoot, string caseName, string source, bool withPolygonRule = false)
    {
        var workspace = Path.Combine(tempRoot, "cases", caseName);
        Directory.CreateDirectory(workspace);

        CopyDirectory(
            Path.Combine(repo, "src", "canonical", "project-files", "intent"),
            Path.Combine(workspace, ".idd", "intent"));
        await File.WriteAllTextAsync(Path.Combine(workspace, "source.md"), source.Trim() + Environment.NewLine);

        if (withPolygonRule)
        {
            var engineering = Path.Combine(workspace, ".idd", "engineering");
            CopyDirectory(Path.Combine(repo, "src", "canonical", "project-files", "engineering"), engineering);
            await File.WriteAllTextAsync(Path.Combine(engineering, "INDEX.md"), """
                # Engineering Rules Index

                This index is a compact discovery projection for current Engineering Rules. Full rule documents are normative.

                The `Rule` column contains stable `ENG-NNNN` identifiers only.

                Next ID: ENG-0003

                | Rule | Applicability | Applies when | Summary |
                | --- | --- | --- | --- |
                | ENG-0002 | Always | Every implementation task | Clipper2 is the canonical polygon engine |
                """);
            await File.WriteAllTextAsync(Path.Combine(engineering, "ENG-0002.rule-polygon-engine.md"), """
                # ENG-0002.rule-polygon-engine

                ## Rule

                Clipper2 is the canonical polygon engine.

                ## Applicability

                Always

                ## Rationale

                Keep polygon processing consistent across implementations.

                ## Guidance

                Use Clipper2 for polygon operations.

                ## Verification

                Polygon operations use the canonical engine.
                """);
        }

        await RunAsync("git", ["init"], workspace);
        await RunAsync("git", ["config", "user.name", "IDD Intent Eval"], workspace);
        await RunAsync("git", ["config", "user.email", "idd-intent-eval@localhost"], workspace);
        await RunAsync("git", ["add", "--all"], workspace);
        await RunAsync("git", ["commit", "-m", "Initial eval workspace"], workspace);
        return workspace;
    }

    private static async Task RunImportAsync(
        string workspace, string codex, IReadOnlyDictionary<string, string?> env,
        string model, string reasoning, int timeoutMinutes)
    {
        const string prompt = """
            Use $idd-intent-import to import ./source.md with mode apply-safe.
            source.md is the supplied import source. Apply only changes permitted by
            the skill's source-authority, conflict, semantic-atomicity, allocator,
            Factory-safety, and validation rules. Do not ask for repeated confirmation
            of an explicit durable decision already established by source.md.
            Return the import report after completing validation.
            """;

        await RunAsync(
            codex,
            [
                "exec", "--json", "--ignore-rules",
                "--enable", "plugins", "--disable", "remote_plugin",
                "-c", "approval_policy=never",
                "-c", $"model_reasoning_effort={reasoning}",
                "--model", model,
                "--sandbox", "danger-full-access",
                "--cd", workspace,
                "-"
            ],
            workspace,
            env,
            prompt,
            TimeSpan.FromMinutes(timeoutMinutes));
    }

    private static string ReadCombinedIntent(string workspace) =>
        string.Join(
            Environment.NewLine,
            Directory.GetFiles(Path.Combine(workspace, ".idd", "intent"), "IDD-*.md")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));

    private static string ReadCombinedRules(string workspace) =>
        string.Join(Environment.NewLine, GetRuleFiles(workspace).Select(File.ReadAllText));

    private static string[] GetRuleFiles(string workspace)
    {
        var engineering = Path.Combine(workspace, ".idd", "engineering");
        Assert.True(Directory.Exists(engineering), "Engineering layer was not materialized.");
        return Directory.GetFiles(engineering, "ENG-*.rule-*.md")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static void PrepareCodexHome(string target)
    {
        Directory.CreateDirectory(target);
        var source = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(source))
            source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

        var auth = Path.Combine(source, "auth.json");
        if (File.Exists(auth))
            File.Copy(auth, Path.Combine(target, "auth.json"), overwrite: true);
    }

    private static string ResolveTempRoot()
    {
        var configured = Environment.GetEnvironmentVariable("IDD_INTENT_EVAL_TEMP_ROOT");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "idd-intent-import-eval", Guid.NewGuid().ToString("N"))
            : Path.GetFullPath(configured);
    }

    private static async Task<RunResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? stdin = null,
        TimeSpan? timeout = null)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        if (environment is not null)
            foreach (var (name, value) in environment)
                start.Environment[name] = value;

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(3));

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException ex)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"{executable} timed out.", ex);
        }

        var result = new RunResult(process.ExitCode, await stdoutTask, await stderrTask);
        Assert.True(
            result.ExitCode == 0,
            $"{executable} failed with exit code {result.ExitCode}.{Environment.NewLine}{result.Stderr}");
        return result;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
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

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed record RunResult(int ExitCode, string Stdout, string Stderr);
}
