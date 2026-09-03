using System.Text.Json;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed class CurrentIddArtifactBuilder(ProcessRunner processRunner)
{
    public async Task<InstalledIddArtifact> BuildAsync(string repositoryRoot, FactoryEvalWorkspace workspace, string version, CancellationToken cancellationToken, string? previousVersion = null)
    {
        var generator = Path.Combine(repositoryRoot, "tools", "generate", "bin", "Debug", "net10.0", "Generate.dll");
        if (!File.Exists(generator)) throw new FileNotFoundException("Current generator assembly was not built.", generator);
        PrepareIsolatedCodexHome(workspace.CodexHomeDirectory);
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["CODEX_HOME"] = workspace.CodexHomeDirectory };
        var codex = CodexExecutableResolver.Resolve();
        string? previousInstalledPath = null;
        if (!string.IsNullOrWhiteSpace(previousVersion))
        {
            await GenerateAsync(generator, repositoryRoot, workspace, previousVersion, "previous", cancellationToken);
            previousInstalledPath = await InstallAsync(codex, repositoryRoot, workspace, environment, "previous", cancellationToken);
            VerifyInstalledFactory(previousInstalledPath);
            if (ReadMethodologyVersion(previousInstalledPath) != previousVersion) throw new InvalidOperationException("Previous installed Factory methodology version is incorrect.");
            await RemoveAsync(codex, repositoryRoot, workspace, environment, cancellationToken);
        }
        await GenerateAsync(generator, repositoryRoot, workspace, version, "current", cancellationToken);
        var installedPath = await InstallAsync(codex, repositoryRoot, workspace, environment, "current", cancellationToken);
        if (previousInstalledPath is not null && Path.GetFullPath(previousInstalledPath) == Path.GetFullPath(installedPath))
            throw new InvalidOperationException("Factory reinstall reused the previous version's installedPath.");
        VerifyInstalledFactory(installedPath);
        MergeBootstrap(Path.Combine(installedPath, "assets", "bootstrap"), workspace.WorkspaceDirectory);
        var generated = ReadMethodologyVersion(installedPath);
        if (generated != version) throw new InvalidOperationException("Generated methodology-version.json does not match the requested methodology version.");
        return new(installedPath, version, previousInstalledPath);
    }

    private async Task GenerateAsync(string generator, string repositoryRoot, FactoryEvalWorkspace workspace, string version, string label, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync("dotnet", ["exec", generator, "--version", version, "--output", workspace.GeneratedMarketplaceDirectory], repositoryRoot,
            Path.Combine(workspace.VerificationDirectory, $"generator-{label}.stdout.log"), Path.Combine(workspace.VerificationDirectory, $"generator-{label}.stderr.log"), TimeSpan.FromMinutes(2), cancellationToken);
        if (result.ExitCode != 0) throw new InvalidOperationException($"Generator failed with exit code {result.ExitCode}. See {result.StderrPath}.");
    }

    private async Task<string> InstallAsync(CodexCommand codex, string repositoryRoot, FactoryEvalWorkspace workspace, IReadOnlyDictionary<string, string> environment, string label, CancellationToken cancellationToken)
    {
        var marketplace = await processRunner.RunAsync(codex.Executable, codex.PrefixArguments.Concat(["plugin", "marketplace", "add", workspace.GeneratedMarketplaceDirectory, "--json"]).ToArray(), repositoryRoot,
            Path.Combine(workspace.VerificationDirectory, $"plugin-marketplace-add-{label}.json"), Path.Combine(workspace.VerificationDirectory, $"plugin-marketplace-add-{label}.stderr.log"), TimeSpan.FromMinutes(2), cancellationToken, environmentOverrides: environment);
        RequireSuccess(marketplace, "Codex marketplace add");
        ProcessResult install;
        for (var attempt = 1; ; attempt++)
        {
            install = await processRunner.RunAsync(codex.Executable, codex.PrefixArguments.Concat(["plugin", "add", "idd-factory@intent-driven-development", "--json"]).ToArray(), repositoryRoot,
                Path.Combine(workspace.VerificationDirectory, $"plugin-add-{label}-{attempt}.json"), Path.Combine(workspace.VerificationDirectory, $"plugin-add-{label}-{attempt}.stderr.log"), TimeSpan.FromMinutes(2), cancellationToken, environmentOverrides: environment);
            if (install.ExitCode == 0) break;
            var stderr = await File.ReadAllTextAsync(install.StderrPath, cancellationToken);
            if (attempt >= 3 || !stderr.Contains("failed to activate plugin cache entry: Access is denied", StringComparison.OrdinalIgnoreCase))
                RequireSuccess(install, "Codex plugin add");
            await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
        }
        return ReadInstalledPath(install.StdoutPath);
    }

    private async Task RemoveAsync(CodexCommand codex, string repositoryRoot, FactoryEvalWorkspace workspace, IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken)
    {
        var removePlugin = await processRunner.RunAsync(codex.Executable, codex.PrefixArguments.Concat(["plugin", "remove", "idd-factory@intent-driven-development", "--json"]).ToArray(), repositoryRoot,
            Path.Combine(workspace.VerificationDirectory, "plugin-remove-previous.json"), Path.Combine(workspace.VerificationDirectory, "plugin-remove-previous.stderr.log"), TimeSpan.FromMinutes(2), cancellationToken, environmentOverrides: environment);
        RequireSuccess(removePlugin, "Codex plugin remove");
        var removeMarketplace = await processRunner.RunAsync(codex.Executable, codex.PrefixArguments.Concat(["plugin", "marketplace", "remove", "intent-driven-development", "--json"]).ToArray(), repositoryRoot,
            Path.Combine(workspace.VerificationDirectory, "plugin-marketplace-remove-previous.json"), Path.Combine(workspace.VerificationDirectory, "plugin-marketplace-remove-previous.stderr.log"), TimeSpan.FromMinutes(2), cancellationToken, environmentOverrides: environment);
        RequireSuccess(removeMarketplace, "Codex marketplace remove");
    }

    private static void PrepareIsolatedCodexHome(string codexHome)
    {
        Directory.CreateDirectory(codexHome);
        var configuredHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        var sourceHome = string.IsNullOrWhiteSpace(configuredHome)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex")
            : configuredHome;
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

    internal static void VerifyInstalledFactory(string installedPath)
    {
        var required = new[]
        {
            Path.Combine(".codex-plugin", "plugin.json"),
            ".mcp.json",
            Path.Combine("skills", "idd-factory-run", "SKILL.md"),
            Path.Combine("skills", "idd-factory-decompose-task", "SKILL.md"),
            Path.Combine("skills", "idd-factory-execute-subtask", "SKILL.md"),
            Path.Combine("runtime", "idd-factory.dll"),
            Path.Combine("runtime", "idd-factory.deps.json"),
            Path.Combine("runtime", "idd-factory.runtimeconfig.json")
        };
        var missing = required.Where(relative => !File.Exists(Path.Combine(installedPath, relative))).ToArray();
        if (missing.Length != 0) throw new InvalidOperationException($"Installed Factory plugin is incomplete: {string.Join(", ", missing)}");
        var appHost = Path.Combine(installedPath, "runtime", OperatingSystem.IsWindows() ? "idd-factory.exe" : "idd-factory");
        if (!File.Exists(appHost)) throw new InvalidOperationException($"Installed Factory runtime apphost is missing: {appHost}");
    }

    internal static string ReadMethodologyVersion(string installedPath)
    {
        var reference = Path.Combine(installedPath, "skills", "idd-factory-run", "references", "methodology-version.json");
        using var document = JsonDocument.Parse(File.ReadAllText(reference));
        return document.RootElement.GetProperty("methodologyVersion").GetString() ?? "unknown";
    }

    private static void RequireSuccess(ProcessResult result, string operation)
    {
        if (result.ExitCode != 0) throw new InvalidOperationException($"{operation} failed with exit code {result.ExitCode}. See {result.StderrPath}.");
    }

    private static void MergeBootstrap(string source, string workspace)
    {
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException($"Generated Factory bootstrap assets are missing: {source}");
        CopyDirectory(source, workspace, allowExisting: true, preserveExistingIntent: true);
    }

    private static void CopyDirectory(string source, string destination, bool allowExisting, bool preserveExistingIntent = false)
    {
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            if (File.Exists(target))
            {
                if (preserveExistingIntent && relative.Replace('\\', '/').StartsWith(".idd/intent/", StringComparison.Ordinal)) continue;
                if (!allowExisting) throw new InvalidOperationException($"Skill copy conflict: {target}");
                if (File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(target))) continue;
                throw new InvalidOperationException($"Unexpected bootstrap asset conflict: {target}");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}

public sealed record InstalledIddArtifact(string InstalledPath, string MethodologyVersion, string? PreviousInstalledPath);
