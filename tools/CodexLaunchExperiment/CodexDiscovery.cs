using System.Runtime.InteropServices;

namespace CodexLaunchExperiment;

public sealed record DiscoveryResult(IReadOnlyList<CodexCandidate> Candidates, string? CmdExe, IReadOnlyList<string> PathEntries, IReadOnlyList<string> RelevantEnvironmentVariables);

public static class CodexDiscovery
{
    public static DiscoveryResult Discover()
    {
        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var candidates = new List<CodexCandidate>();
        var nodes = pathEntries.Select(directory => Path.Combine(directory, "node.exe")).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var scripts = new List<string>();
        foreach (var directory in pathEntries)
        {
            AddNative(candidates, Path.Combine(directory, "codex.exe"));
            var cmd = Path.Combine(directory, "codex.cmd");
            var ps1 = Path.Combine(directory, "codex.ps1");
            var script = Path.Combine(directory, "node_modules", "@openai", "codex", "bin", "codex.js");
            var node = Path.Combine(directory, "node.exe");
            if (File.Exists(cmd)) candidates.Add(new(LaunchKind.CmdShim, cmd, [], "codex.cmd"));
            if (File.Exists(script)) scripts.Add(script);
            if (File.Exists(script) && File.Exists(node)) candidates.Add(new(LaunchKind.NodeScript, node, [script], "node.exe + codex.js", script));
            if (File.Exists(ps1)) candidates.Add(new(LaunchKind.PathCommand, ps1, [], "codex.ps1"));
        }
        foreach (var script in scripts.Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (var node in nodes) candidates.Add(new(LaunchKind.NodeScript, node, [script], "node.exe + codex.js", script));
        candidates.Add(new(LaunchKind.PathCommand, "codex", [], "codex by PATH"));
        var cmdExe = OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "cmd.exe") : null;
        return new DiscoveryResult(candidates.Where(candidate => candidate.Kind == LaunchKind.PathCommand || File.Exists(candidate.Executable)).DistinctBy(candidate => (candidate.Kind, candidate.Executable), EqualityComparer<(LaunchKind, string)>.Default).ToArray(), File.Exists(cmdExe) ? cmdExe : null, pathEntries, Environment.GetEnvironmentVariables().Keys.Cast<object>().Select(x => x.ToString()!).Where(name => name.Contains("CODEX", StringComparison.OrdinalIgnoreCase) || name.Contains("OPENAI", StringComparison.OrdinalIgnoreCase) || name is "PATH" or "PATHEXT").Order().ToArray());
    }

    public static IReadOnlyList<CodexCandidate> ChooseModelCandidates(IEnumerable<CodexCandidate> candidates, string cmdExe) => candidates
        .Where(candidate => candidate.Kind is LaunchKind.NativeExecutable or LaunchKind.CmdShim or LaunchKind.NodeScript)
        .OrderBy(candidate => candidate.Kind switch { LaunchKind.NativeExecutable => 0, LaunchKind.CmdShim => 1, _ => 2 })
        .Take(2)
        .Select(candidate => candidate.Kind == LaunchKind.CmdShim
            ? new CodexCandidate(LaunchKind.CmdShim, cmdExe, [], candidate.DisplayName, candidate.Executable)
            : candidate).ToArray();

    private static void AddNative(List<CodexCandidate> candidates, string path) { if (File.Exists(path)) candidates.Add(new(LaunchKind.NativeExecutable, path, [], "codex.exe")); }
}
