using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexLaunchExperiment;

public enum LaunchKind { NativeExecutable, CmdShim, NodeScript, PathCommand }
public enum Outcome { ExecutableNotFound, ProcessStartFailed, CodexCliFailed, SandboxInitializationFailed, ChildCommandStartFailed, WorkspaceWriteDenied, OutsideWorkspaceWriteUnexpectedlyAllowed, ApprovalRequested, ModelExecutionFailed, JsonlInvalid, FinalMessageMissing, TimedOut, Success }

public sealed record CodexCandidate(LaunchKind Kind, string Executable, IReadOnlyList<string> PrefixArguments, string DisplayName, string? ScriptPath = null)
{
    public string CommandLine => CommandFormatting.Format(Executable, PrefixArguments);
}

public sealed record FileEntry(string RelativePath, long Length, string Sha256);
public sealed record FileDiff(IReadOnlyList<string> Created, IReadOnlyList<string> Changed, IReadOnlyList<string> Deleted);
public sealed record ProcessCapture(int? ExitCode, bool TimedOut, DateTimeOffset StartedAtUtc, DateTimeOffset EndedAtUtc, string? StartError)
{
    public TimeSpan Duration => EndedAtUtc - StartedAtUtc;
}

public static class CommandFormatting
{
    public static string Quote(string value) => value.Length == 0 ? "\"\"" : value.Any(char.IsWhiteSpace) || value.Contains('"') ? "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"" : value;
    public static string Format(string executable, IEnumerable<string> arguments) => string.Join(" ", new[] { Quote(executable) }.Concat(arguments.Select(Quote)));

    // cmd.exe receives one command string; quote the command path and escape cmd metacharacters in arguments.
    public static string BuildCmdCommand(string commandPath, IEnumerable<string> arguments) =>
        "\"" + commandPath.Replace("\"", "\"\"") + "\"" + string.Concat(arguments.Select(argument => " " + Quote(argument).Replace("^", "^^").Replace("&", "^&").Replace("|", "^|").Replace("<", "^<").Replace(">", "^>")));
}

public static class Diagnostics
{
    private static readonly string[] SecretNames = ["OPENAI_API_KEY", "CODEX_API_KEY", "TOKEN", "PASSWORD", "COOKIE", "AUTH"];
    public static string Redact(string value)
    {
        foreach (var name in SecretNames)
        {
            var marker = name + "=";
            var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                var end = value.IndexOfAny([' ', '\r', '\n', ';'], index + marker.Length);
                if (end < 0) end = value.Length;
                value = value[..(index + marker.Length)] + "[REDACTED]" + value[end..];
                index = value.IndexOf(marker, index + marker.Length, StringComparison.OrdinalIgnoreCase);
            }
        }
        return value;
    }

    public static Outcome Classify(ProcessCapture process, string stderr, bool expectedWrite, bool outsideWriteAttempt, bool outsideWriteExists, bool jsonlValid, bool finalMessageExists, bool modelRun)
    {
        if (process.TimedOut) return Outcome.TimedOut;
        if (process.StartError is not null) return process.StartError.Contains("No such file", StringComparison.OrdinalIgnoreCase) || process.StartError.Contains("cannot find", StringComparison.OrdinalIgnoreCase) ? Outcome.ExecutableNotFound : Outcome.ProcessStartFailed;
        if (stderr.Contains("approval", StringComparison.OrdinalIgnoreCase) && (stderr.Contains("request", StringComparison.OrdinalIgnoreCase) || stderr.Contains("required", StringComparison.OrdinalIgnoreCase))) return Outcome.ApprovalRequested;
        if (stderr.Contains("CreateProcessAsUserW failed", StringComparison.OrdinalIgnoreCase) || stderr.Contains("sandbox", StringComparison.OrdinalIgnoreCase) && stderr.Contains("failed", StringComparison.OrdinalIgnoreCase)) return Outcome.SandboxInitializationFailed;
        if (outsideWriteAttempt && outsideWriteExists) return Outcome.OutsideWorkspaceWriteUnexpectedlyAllowed;
        if (expectedWrite && !outsideWriteAttempt && process.ExitCode == 0 && !jsonlValid && modelRun) return Outcome.JsonlInvalid;
        if (expectedWrite && !outsideWriteAttempt && process.ExitCode == 0 && !finalMessageExists && modelRun) return Outcome.FinalMessageMissing;
        if (expectedWrite && !outsideWriteAttempt && process.ExitCode == 0) return Outcome.WorkspaceWriteDenied;
        if (process.ExitCode == 0) return Outcome.Success;
        return modelRun ? Outcome.ModelExecutionFailed : Outcome.CodexCliFailed;
    }
}

public static class FileSnapshots
{
    public static IReadOnlyList<FileEntry> Take(string root) => Directory.Exists(root)
        ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(path => new FileInfo(path)).Select(file => new FileEntry(Path.GetRelativePath(root, file.FullName).Replace('\\', '/'), file.Length, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file.FullName))))).OrderBy(entry => entry.RelativePath, StringComparer.Ordinal).ToArray()
        : [];
    public static FileDiff Compare(IReadOnlyList<FileEntry> before, IReadOnlyList<FileEntry> after)
    {
        var oldItems = before.ToDictionary(x => x.RelativePath, StringComparer.Ordinal);
        var newItems = after.ToDictionary(x => x.RelativePath, StringComparer.Ordinal);
        return new FileDiff(newItems.Keys.Except(oldItems.Keys).Order().ToArray(), newItems.Keys.Intersect(oldItems.Keys).Where(path => newItems[path] != oldItems[path]).Order().ToArray(), oldItems.Keys.Except(newItems.Keys).Order().ToArray());
    }
    public static Task WriteJsonAsync<T>(string path, T value) => File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, Json.Indented) + "\n");
}

public static class Json
{
    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static bool IsValidJson(string path) { try { using var _ = JsonDocument.Parse(File.ReadAllText(path)); return true; } catch { return false; } }
    public static bool IsValidJsonl(string path)
    {
        if (!File.Exists(path)) return false;
        var lines = File.ReadLines(path).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        return lines.Length > 0 && lines.All(line => { try { using var _ = JsonDocument.Parse(line); return true; } catch { return false; } });
    }
}
