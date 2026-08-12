using System.Text.RegularExpressions;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed record MethodologyVersion(string Value, string SourceRevision, bool SourceDirty);

public static class MethodologyVersionResolver
{
    public static async Task<MethodologyVersion> ResolveAsync(string repositoryRoot, CancellationToken cancellationToken, bool releaseCertification = false)
    {
        var overrideVersion = Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_VERSION");
        var revisionArguments = releaseCertification ? new[] { "rev-parse", "HEAD^{commit}" } : ["rev-parse", "--short=8", "HEAD"];
        var revision = await GitAsync(repositoryRoot, revisionArguments, cancellationToken) ?? "unknown";
        var dirty = !string.IsNullOrWhiteSpace(await GitAsync(repositoryRoot, ["status", "--porcelain"], cancellationToken));
        var tag = await GitAsync(repositoryRoot, ["describe", "--tags", "--exact-match", "HEAD"], cancellationToken);
        if (releaseCertification)
        {
            var version = ReleaseCertification.Validate(dirty, revision, tag, overrideVersion);
            return new(version, revision, false);
        }
        if (!string.IsNullOrWhiteSpace(overrideVersion)) return new(overrideVersion, revision, dirty);
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

public static class ReleaseCertification
{
    public static string Validate(bool sourceDirty, string sourceRevision, string? exactTag, string? requestedVersion)
    {
        if (sourceDirty) throw new InvalidOperationException("Release certification requires a clean source tree; commit or stash changes before the live model run.");
        if (!Regex.IsMatch(sourceRevision, "^[0-9a-fA-F]{40}$")) throw new InvalidOperationException("Release certification requires the full exact source commit SHA.");
        if (exactTag is null || !Regex.IsMatch(exactTag, "^v\\d+\\.\\d+\\.\\d+$")) throw new InvalidOperationException("Release certification requires HEAD to be the exact vMAJOR.MINOR.PATCH release tag.");
        var tagVersion = exactTag[1..];
        if (!string.IsNullOrWhiteSpace(requestedVersion) && !StringComparer.Ordinal.Equals(requestedVersion, tagVersion))
            throw new InvalidOperationException($"Requested methodology version '{requestedVersion}' does not match release tag '{exactTag}'.");
        return tagVersion;
    }
}
