using System.Text.RegularExpressions;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed record MethodologyVersion(string Value, string SourceRevision, bool SourceDirty);

public static class MethodologyVersionResolver
{
    public static async Task<MethodologyVersion> ResolveAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var overrideVersion = Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_VERSION");
        var revision = await GitAsync(repositoryRoot, ["rev-parse", "--short=8", "HEAD"], cancellationToken) ?? "unknown";
        var dirty = !string.IsNullOrWhiteSpace(await GitAsync(repositoryRoot, ["status", "--porcelain"], cancellationToken));
        if (!string.IsNullOrWhiteSpace(overrideVersion)) return new(overrideVersion, revision, dirty);
        var tag = await GitAsync(repositoryRoot, ["describe", "--tags", "--exact-match", "HEAD"], cancellationToken);
        if (!dirty && tag is not null && Regex.IsMatch(tag, "^v\\d+\\.\\d+\\.\\d+$")) return new(tag[1..], revision, false);
        var nearest = await GitAsync(repositoryRoot, ["describe", "--tags", "--abbrev=0"], cancellationToken);
        var baseVersion = nearest is not null && Regex.IsMatch(nearest, "^v\\d+\\.\\d+\\.\\d+$") ? nearest[1..] : "0.0.0";
        return new($"{baseVersion}-eval.{revision.ToLowerInvariant()}", revision, dirty);
    }

    private static async Task<string?> GitAsync(string root, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start);
        if (process is null) return null;
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode == 0 ? output.Trim() : null;
    }
}
