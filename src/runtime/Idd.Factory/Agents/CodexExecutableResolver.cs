namespace Idd.Factory.Agents;

public sealed record CodexCommand(string Executable, IReadOnlyList<string> PrefixArguments);

public static class CodexExecutableResolver
{
    public const string ExecutableEnvironmentVariable = "IDD_FACTORY_CODEX_EXECUTABLE";

    public static CodexCommand Resolve()
    {
        var configured = Environment.GetEnvironmentVariable(ExecutableEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!File.Exists(configured))
                throw new FileNotFoundException(
                    $"The executable configured by {ExecutableEnvironmentVariable} does not exist.",
                    configured);
            return new(Path.GetFullPath(configured), []);
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (OperatingSystem.IsWindows())
        {
            var candidates = new[]
            {
                Environment.GetEnvironmentVariable("APPDATA"),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE") ?? string.Empty, "AppData", "Roaming"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Roaming")
            };
            foreach (var applicationData in candidates
                         .Where(value => !string.IsNullOrWhiteSpace(value))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var npm = Path.Combine(applicationData!, "npm");
                if (Directory.Exists(npm)
                    && !path.Split(Path.PathSeparator).Contains(npm, StringComparer.OrdinalIgnoreCase))
                    path = string.IsNullOrEmpty(path) ? npm : path + Path.PathSeparator + npm;
            }
        }
        return ResolveFromPath(path, OperatingSystem.IsWindows());
    }

    public static CodexCommand ResolveFromPath(string path, bool isWindows)
    {
        if (!isWindows) return new("codex", []);
        var directories = path.Split(
            Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var directory in directories)
        {
            var direct = Path.Combine(directory, "codex.exe");
            if (File.Exists(direct)) return new(direct, []);
            var packages = Path.Combine(directory, "node_modules", "@openai", "codex", "node_modules");
            if (!Directory.Exists(packages)) continue;
            var native = Directory
                .EnumerateFiles(packages, "codex.exe", SearchOption.AllDirectories)
                .FirstOrDefault(candidate => candidate.Contains(
                    "@openai" + Path.DirectorySeparatorChar + "codex-win32-",
                    StringComparison.OrdinalIgnoreCase));
            if (native is not null) return new(native, []);
        }

        var nodes = directories
            .Select(directory => Path.Combine(directory, "node.exe"))
            .Where(File.Exists)
            .ToArray();
        foreach (var directory in directories)
        {
            var script = Path.Combine(directory, "node_modules", "@openai", "codex", "bin", "codex.js");
            if (!File.Exists(script)) continue;
            var node = File.Exists(Path.Combine(directory, "node.exe"))
                ? Path.Combine(directory, "node.exe")
                : nodes.FirstOrDefault();
            if (node is not null) return new(node, [script]);
        }

        throw new FileNotFoundException(
            "Could not locate a native Codex executable or npm Codex CLI with node.exe on PATH.");
    }
}
