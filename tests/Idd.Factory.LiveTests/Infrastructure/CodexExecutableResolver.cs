namespace Idd.Factory.LiveTests.Infrastructure;

public sealed record CodexCommand(string Executable, IReadOnlyList<string> PrefixArguments);

public static class CodexExecutableResolver
{
    public static CodexCommand Resolve() => ResolveFromPath(Environment.GetEnvironmentVariable("PATH") ?? string.Empty, OperatingSystem.IsWindows());

    internal static CodexCommand ResolveFromPath(string path, bool isWindows)
    {
        if (!isWindows) return new("codex", []);

        var directories = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var directory in directories)
        {
            var packageDirectory = Path.Combine(directory, "node_modules", "@openai", "codex", "node_modules");
            if (!Directory.Exists(packageDirectory)) continue;

            var nativeExecutable = Directory.EnumerateFiles(packageDirectory, "codex.exe", SearchOption.AllDirectories)
                .FirstOrDefault(candidate => candidate.Contains("@openai" + Path.DirectorySeparatorChar + "codex-win32-", StringComparison.OrdinalIgnoreCase));
            if (nativeExecutable is not null) return new(nativeExecutable, []);
        }

        foreach (var directory in directories)
        {
            var script = Path.Combine(directory, "node_modules", "@openai", "codex", "bin", "codex.js");
            var node = Path.Combine(directory, "node.exe");
            if (File.Exists(script) && File.Exists(node)) return new(node, [script]);
        }

        var nodeFromPath = directories
            .Select(directory => Path.Combine(directory, "node.exe"))
            .FirstOrDefault(File.Exists);
        foreach (var directory in directories)
        {
            var script = Path.Combine(directory, "node_modules", "@openai", "codex", "bin", "codex.js");
            if (nodeFromPath is not null && File.Exists(script)) return new(nodeFromPath, [script]);
        }

        throw new FileNotFoundException("Could not locate the npm Codex CLI script and node.exe on PATH. The evaluator avoids the PowerShell and .cmd shims because ProcessStartInfo executes programs directly.");
    }
}
