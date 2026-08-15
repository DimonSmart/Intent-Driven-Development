using System.Diagnostics;
using System.Text;

namespace Idd.Factory.Benchmark;

public sealed record ProcessResult(int ExitCode, TimeSpan Duration, string Stdout, string Stderr, bool TimedOut);

public static class ProcessExecution
{
    public static async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments, string workingDirectory, TimeSpan timeout, string? standardInput = null, IReadOnlyDictionary<string, string?>? environment = null)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = standardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (standardInput is not null) start.StandardInputEncoding = new UTF8Encoding(false);
        if (OperatingSystem.IsWindows())
            start.Environment["PATH"] = PrepareSandboxCompatiblePath(start.Environment["PATH"] ?? string.Empty);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (environment is not null)
            foreach (var pair in environment) start.Environment[pair.Key] = pair.Value;
        using var process = new Process { StartInfo = start };
        var stopwatch = Stopwatch.StartNew();
        if (!process.Start()) throw new InvalidOperationException($"Could not start {executable}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput);
            await process.StandardInput.FlushAsync();
            process.StandardInput.Close();
        }
        using var cancellation = new CancellationTokenSource(timeout);
        var timedOut = false;
        try { await process.WaitForExitAsync(cancellation.Token); }
        catch (OperationCanceledException)
        {
            timedOut = true;
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        stopwatch.Stop();
        return new(timedOut ? -1 : process.ExitCode, stopwatch.Elapsed, await stdout, await stderr, timedOut);
    }

    internal static string PrepareSandboxCompatiblePath(string path) => string.Join(
        Path.PathSeparator,
        path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(entry => !entry.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)));
}

public sealed record CodexCommand(string Executable, IReadOnlyList<string> PrefixArguments);

public static class CodexExecutableResolver
{
    public static CodexCommand Resolve()
    {
        var configured = Environment.GetEnvironmentVariable("IDD_FACTORY_CODEX_EXECUTABLE");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return new(Path.GetFullPath(configured), []);
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (!OperatingSystem.IsWindows()) return new("codex", []);
        var directories = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData)) directories.Add(Path.Combine(appData, "npm"));
        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var modules = Path.Combine(directory, "node_modules", "@openai", "codex", "node_modules");
            if (!Directory.Exists(modules)) continue;
            var native = Directory.EnumerateFiles(modules, "codex.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (native is not null) return new(native, []);
        }
        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var node = Path.Combine(directory, "node.exe");
            var script = Path.Combine(directory, "node_modules", "@openai", "codex", "bin", "codex.js");
            if (File.Exists(node) && File.Exists(script)) return new(node, [script]);
        }
        throw new FileNotFoundException("Could not resolve the Codex CLI executable from PATH or IDD_FACTORY_CODEX_EXECUTABLE.");
    }
}
