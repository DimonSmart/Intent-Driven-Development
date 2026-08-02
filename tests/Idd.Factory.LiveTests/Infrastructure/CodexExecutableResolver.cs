namespace Idd.Factory.LiveTests.Infrastructure;

public sealed record CodexCommand(string Executable, IReadOnlyList<string> PrefixArguments);

public static class CodexExecutableResolver
{
    public static CodexCommand Resolve()
    {
        if (!OperatingSystem.IsWindows()) return new("codex", []);

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var script = Path.Combine(directory, "node_modules", "@openai", "codex", "bin", "codex.js");
            var node = Path.Combine(directory, "node.exe");
            if (File.Exists(script) && File.Exists(node)) return new(node, [script]);
        }

        var nodeFromPath = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directory => Path.Combine(directory, "node.exe"))
            .FirstOrDefault(File.Exists);
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var script = Path.Combine(directory, "node_modules", "@openai", "codex", "bin", "codex.js");
            if (nodeFromPath is not null && File.Exists(script)) return new(nodeFromPath, [script]);
        }

        throw new FileNotFoundException("Could not locate the npm Codex CLI script and node.exe on PATH. The evaluator avoids the PowerShell and .cmd shims because ProcessStartInfo executes programs directly.");
    }
}
