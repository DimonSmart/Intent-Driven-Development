using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Idd.Factory.LiveTests.Infrastructure;

public static class CodexHostLifecycleCertification
{
    public const string ReportEnvironmentVariable = "IDD_FACTORY_CODEX_LIFECYCLE_REPORT";

    public static CodexHostLifecycleReport RequireFromEnvironment(CodexCommand actualCommand, string actualVersion)
    {
        var report = Load(Environment.GetEnvironmentVariable(ReportEnvironmentVariable));
        ValidateHost(report, actualCommand, actualVersion);
        ValidateLifecycle(report);
        return report;
    }

    public static CodexHostLifecycleReport Load(string? reportPath)
    {
        if (string.IsNullOrWhiteSpace(reportPath) || !File.Exists(reportPath))
            throw new InvalidOperationException($"Release certification requires a real Codex process-tree lifecycle report via {ReportEnvironmentVariable}.");

        CodexHostLifecycleReport? report;
        try
        {
            report = JsonSerializer.Deserialize<CodexHostLifecycleReport>(
                File.ReadAllText(reportPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The Codex process-tree lifecycle report is invalid JSON.", exception);
        }

        if (report is null)
            throw new InvalidOperationException("The Codex process-tree lifecycle report is empty.");
        if (report.SchemaVersion == 1)
            throw new InvalidOperationException("Codex lifecycle report schemaVersion 1 is unsupported for release certification. Repeat the lifecycle probe for the actual Codex host to produce schemaVersion 2.");
        if (report.SchemaVersion != 2 || !StringComparer.Ordinal.Equals(report.ProbeKind, "process-tree-lifecycle"))
            throw new InvalidOperationException("The Codex process-tree lifecycle report has an unsupported contract.");
        if (report.Host is null || string.IsNullOrWhiteSpace(report.Host.Version))
            throw new InvalidOperationException("The Codex process-tree lifecycle report does not identify the tested host version.");
        if (!IsSha256(report.Host.ExecutableSha256))
            throw new InvalidOperationException("The Codex process-tree lifecycle report does not contain a valid host executableSha256 fingerprint.");

        return report;
    }

    public static void ValidateHost(CodexHostLifecycleReport report, CodexCommand actualCommand, string actualVersion)
    {
        var reportHost = report.Host
            ?? throw new InvalidOperationException("The Codex process-tree lifecycle report does not identify the tested host.");
        var actualHost = CreateHostIdentity(actualCommand, actualVersion);
        if (StringComparer.Ordinal.Equals(NormalizeVersion(reportHost.Version), actualHost.Version)
            && StringComparer.OrdinalIgnoreCase.Equals(reportHost.ExecutableSha256, actualHost.ExecutableSha256))
            return;

        throw new InvalidOperationException(
            "Lifecycle report was produced for a different Codex host.\n\n" +
            $"Expected:\n  version = {actualHost.Version}\n  fingerprint = {actualHost.ExecutableSha256}\n\n" +
            $"Report:\n  version = {NormalizeVersion(reportHost.Version)}\n  fingerprint = {reportHost.ExecutableSha256}");
    }

    public static void ValidateLifecycle(CodexHostLifecycleReport report)
    {
        if (!report.NormalInterruptNoDescendants)
            throw new InvalidOperationException("The tested Codex host failed lifecycle certification: normalInterruptNoDescendants is false.");
        if (!report.HardKillNoDescendants)
            throw new InvalidOperationException("The tested Codex host failed lifecycle certification: hardKillNoDescendants is false.");
        if (!report.FactoryStateResumable)
            throw new InvalidOperationException("The tested Codex host failed lifecycle certification: factoryStateResumable is false.");
    }

    public static CodexHostIdentity CreateHostIdentity(CodexCommand command, string version)
    {
        var executablePath = ResolveExecutablePath(command.Executable);
        var executableHash = SHA256.HashData(File.ReadAllBytes(executablePath));
        var prefixArtifacts = command.PrefixArguments
            .Where(File.Exists)
            .Select(path => SHA256.HashData(File.ReadAllBytes(path)))
            .ToArray();

        var fingerprint = prefixArtifacts.Length == 0
            ? executableHash
            : CompositeFingerprint(executableHash, prefixArtifacts);
        return new(NormalizeVersion(version), Convert.ToHexString(fingerprint), null);
    }

    private static byte[] CompositeFingerprint(byte[] executableHash, IReadOnlyList<byte[]> prefixArtifactHashes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes("codex-command-fingerprint-v1\n"));
        hash.AppendData(Encoding.ASCII.GetBytes(Convert.ToHexString(executableHash)));
        foreach (var artifactHash in prefixArtifactHashes)
        {
            hash.AppendData([(byte)'\n']);
            hash.AppendData(Encoding.ASCII.GetBytes(Convert.ToHexString(artifactHash)));
        }
        return hash.GetHashAndReset();
    }

    private static string ResolveExecutablePath(string executable)
    {
        if (File.Exists(executable)) return Path.GetFullPath(executable);
        if (Path.IsPathFullyQualified(executable))
            throw new InvalidOperationException("The actual Codex executable could not be read for lifecycle fingerprinting.");

        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.COM").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [string.Empty];
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var extension in extensions.Prepend(string.Empty).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var candidate = Path.Combine(directory, executable + extension);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
        }

        throw new InvalidOperationException("The actual Codex executable could not be resolved for lifecycle fingerprinting.");
    }

    private static string NormalizeVersion(string version) =>
        string.Join(' ', version.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(character => char.IsAsciiHexDigit(character));
}

public sealed record CodexHostLifecycleReport(
    int SchemaVersion,
    string ProbeKind,
    CodexHostIdentity? Host,
    bool NormalInterruptNoDescendants,
    bool HardKillNoDescendants,
    bool FactoryStateResumable);

public sealed record CodexHostIdentity(string Version, string ExecutableSha256, string? Commit);
