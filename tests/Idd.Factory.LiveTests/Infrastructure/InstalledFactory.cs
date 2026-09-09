using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Idd.Factory.Agents;
using Idd.Factory.Processes;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed record InstalledFactoryInfo(string InstalledPath, string MethodologyVersion);

public sealed class InstalledFactory(ProcessRunner processRunner)
{
    private static readonly ProcessSupervisor Supervisor = ProcessSupervisor.Shared;

    public async Task<InstalledFactoryInfo> BuildAndInstallAsync(string repositoryRoot, LiveTestWorkspace workspace, CancellationToken cancellationToken)
    {
        var build = await processRunner.RunAsync("dotnet", ["build", "tools/generate/Generate.csproj", "--nologo"], repositoryRoot,
            Path.Combine(workspace.VerificationDirectory, "generator-build.log"), Path.Combine(workspace.VerificationDirectory, "generator-build.stderr.log"), TimeSpan.FromMinutes(3), cancellationToken);
        RequireSuccess(build, "Generator build");

        var version = await ResolveVersionAsync(repositoryRoot, cancellationToken);
        var generator = Path.Combine(repositoryRoot, "tools", "generate", "bin", "Debug", "net10.0", "Generate.dll");
        if (!File.Exists(generator)) throw new FileNotFoundException("Current generator assembly was not built.", generator);

        PrepareIsolatedCodexHome(workspace.CodexHomeDirectory);
        var generated = await processRunner.RunAsync("dotnet", ["exec", generator, "--version", version, "--output", workspace.GeneratedMarketplaceDirectory], repositoryRoot,
            Path.Combine(workspace.VerificationDirectory, "generator.log"), Path.Combine(workspace.VerificationDirectory, "generator.stderr.log"), TimeSpan.FromMinutes(3), cancellationToken);
        RequireSuccess(generated, "Factory plugin generation");

        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["CODEX_HOME"] = workspace.CodexHomeDirectory };
        var codex = CodexExecutableResolver.Resolve();
        var marketplace = await processRunner.RunAsync(codex.Executable, codex.PrefixArguments.Concat(["plugin", "marketplace", "add", workspace.GeneratedMarketplaceDirectory, "--json"]).ToArray(), repositoryRoot,
            Path.Combine(workspace.VerificationDirectory, "plugin-marketplace-add.json"), Path.Combine(workspace.VerificationDirectory, "plugin-marketplace-add.stderr.log"), TimeSpan.FromMinutes(2), cancellationToken, environmentOverrides: environment);
        RequireSuccess(marketplace, "Codex marketplace add");

        ProcessResult install;
        for (var attempt = 1; ; attempt++)
        {
            install = await processRunner.RunAsync(codex.Executable, codex.PrefixArguments.Concat(["plugin", "add", "idd-factory@intent-driven-development", "--json"]).ToArray(), repositoryRoot,
                Path.Combine(workspace.VerificationDirectory, $"plugin-add-{attempt}.json"), Path.Combine(workspace.VerificationDirectory, $"plugin-add-{attempt}.stderr.log"), TimeSpan.FromMinutes(2), cancellationToken, environmentOverrides: environment);
            if (install.ExitCode == 0) break;
            var stderr = await File.ReadAllTextAsync(install.StderrPath, cancellationToken);
            if (attempt >= 3 || !stderr.Contains("failed to activate plugin cache entry: Access is denied", StringComparison.OrdinalIgnoreCase))
                RequireSuccess(install, "Codex plugin add");
            await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
        }

        var installedPath = ReadInstalledPath(install.StdoutPath);
        VerifyInstalledFactory(installedPath);
        MergeBootstrap(Path.Combine(installedPath, "assets", "bootstrap"), workspace.WorkspaceDirectory);
        var installedVersion = ReadMethodologyVersion(installedPath);
        if (!StringComparer.Ordinal.Equals(installedVersion, version))
            throw new InvalidOperationException($"Installed methodology version '{installedVersion}' does not match generated version '{version}'.");
        return new(installedPath, version);
    }

    private static async Task<string> ResolveVersionAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var overrideVersion = Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_VERSION");
        if (!string.IsNullOrWhiteSpace(overrideVersion)) return overrideVersion;
        var exactTag = await GitTextAsync(repositoryRoot, ["describe", "--tags", "--exact-match", "HEAD"], cancellationToken);
        if (exactTag is not null && Regex.IsMatch(exactTag, "^v\\d+\\.\\d+\\.\\d+$")) return exactTag[1..];
        var revision = await GitTextAsync(repositoryRoot, ["rev-parse", "--short=8", "HEAD"], cancellationToken) ?? "unknown";
        return $"0.0.0-eval.{revision.ToLowerInvariant()}";
    }

    private static async Task<string?> GitTextAsync(string repositoryRoot, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        Process? process;
        try { process = Supervisor.Start(start); }
        catch (System.ComponentModel.Win32Exception) { return null; }
        if (process is null) return null;

        using (process)
        {
            var outputTask = Supervisor.CaptureAsync(process.StandardOutput, CancellationToken.None);
            var errorTask = Supervisor.CaptureAsync(process.StandardError, CancellationToken.None);
            try
            {
                await Supervisor.WaitForExitAsync(process, timeout: null, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await Supervisor.TerminateProcessTreeAsync(process, CancellationToken.None);
                await Task.WhenAll(outputTask, errorTask);
                throw;
            }

            var output = await outputTask;
            _ = await errorTask;
            return process.ExitCode == 0 ? output.Trim() : null;
        }
    }

    internal static void PrepareIsolatedCodexHome(string codexHome)
    {
        Directory.CreateDirectory(codexHome);
        var sourceHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(sourceHome)) sourceHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        var auth = Path.Combine(sourceHome, "auth.json");
        if (File.Exists(auth)) File.Copy(auth, Path.Combine(codexHome, "auth.json"), overwrite: true);
    }

    private static string ReadInstalledPath(string jsonPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
        if (!document.RootElement.TryGetProperty("installedPath", out var value) || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException("Codex plugin add JSON did not contain installedPath.");
        return Path.GetFullPath(value.GetString()!);
    }

    private static void VerifyInstalledFactory(string installedPath)
    {
        var required = new[]
        {
            Path.Combine(".codex-plugin", "plugin.json"), ".mcp.json",
            Path.Combine("skills", "idd-factory-run", "SKILL.md"),
            Path.Combine("runtime", "idd-factory.dll"), Path.Combine("runtime", "idd-factory.deps.json"), Path.Combine("runtime", "idd-factory.runtimeconfig.json")
        };
        var missing = required.Where(relative => !File.Exists(Path.Combine(installedPath, relative))).ToArray();
        if (missing.Length != 0) throw new InvalidOperationException($"Installed Factory plugin is incomplete: {string.Join(", ", missing)}");
        var appHost = Path.Combine(installedPath, "runtime", OperatingSystem.IsWindows() ? "idd-factory.exe" : "idd-factory");
        if (!File.Exists(appHost)) throw new InvalidOperationException($"Installed Factory runtime apphost is missing: {appHost}");
    }

    private static string ReadMethodologyVersion(string installedPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(installedPath, "skills", "idd-factory-run", "references", "methodology-version.json")));
        return document.RootElement.GetProperty("methodologyVersion").GetString() ?? "unknown";
    }

    private static void MergeBootstrap(string source, string workspace)
    {
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException($"Generated Factory bootstrap assets are missing: {source}");
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(workspace, relative);
            if (File.Exists(target))
            {
                if (relative.Replace('\\', '/').StartsWith(".idd/intent/", StringComparison.Ordinal)) continue;
                if (File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(target))) continue;
                throw new InvalidOperationException($"Unexpected bootstrap asset conflict: {target}");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static void RequireSuccess(ProcessResult result, string operation)
    {
        if (result.ExitCode != 0) throw new InvalidOperationException($"{operation} failed with exit code {result.ExitCode}. See {result.StderrPath}.");
    }
}
