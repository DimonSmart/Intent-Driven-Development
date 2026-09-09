using System.Diagnostics;
using System.Text;
using Idd.Factory.Agents;
using Idd.Factory.Processes;

namespace Idd.Factory.Benchmark;

public sealed record ProcessResult(int ExitCode, TimeSpan Duration, string Stdout, string Stderr, bool TimedOut);

public static class ProcessExecution
{
    private static readonly ProcessSupervisor Supervisor = ProcessSupervisor.Shared;

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

        var stopwatch = Stopwatch.StartNew();
        using var process = Supervisor.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var stdout = Supervisor.CaptureAsync(process.StandardOutput, CancellationToken.None);
        var stderr = Supervisor.CaptureAsync(process.StandardError, CancellationToken.None);
        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput);
            await process.StandardInput.FlushAsync();
            process.StandardInput.Close();
        }

        var completed = await Supervisor.WaitForExitAsync(process, timeout, CancellationToken.None);
        if (!completed)
            await Supervisor.TerminateProcessTreeAsync(process, CancellationToken.None);

        stopwatch.Stop();
        return new(completed ? process.ExitCode : -1, stopwatch.Elapsed, await stdout, await stderr, !completed);
    }

    internal static string PrepareSandboxCompatiblePath(string path) =>
        CodexProcessEnvironment.PrepareSandboxCompatiblePath(path, isWindows: true).Path;
}
