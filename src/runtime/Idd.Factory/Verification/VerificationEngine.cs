using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Idd.Factory.Domain;

namespace Idd.Factory.Verification;

public sealed record VerificationEvidence(
    int SchemaVersion, string EvidenceId, string CheckId, string CheckDefinitionHash,
    string WorkspaceFingerprint, DateTimeOffset StartedAt, DateTimeOffset FinishedAt,
    int ExitCode, string Status, string Output);

public enum VerificationStatus { Passed, Failed, RequiresUserAction, InfrastructureFailure }

public sealed record VerificationResult(VerificationStatus Status, IReadOnlyList<VerificationEvidence> Evidence)
{
    public bool Passed => Status == VerificationStatus.Passed;
}

public sealed class WorkspaceFingerprinter
{
    public string Compute(string workspace)
    {
        using var incremental = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in Directory.EnumerateFiles(workspace, "*", SearchOption.AllDirectories)
            .Where(path => !Excluded(workspace, path)).OrderBy(path => Path.GetRelativePath(workspace, path), StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(workspace, path).Replace('\\', '/');
            incremental.AppendData(Encoding.UTF8.GetBytes(relative + "\0"));
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[81920]; int count; while ((count = stream.Read(buffer)) > 0) incremental.AppendData(buffer, 0, count);
        }
        return Convert.ToHexString(incremental.GetHashAndReset()).ToLowerInvariant();
    }

    private static bool Excluded(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        return relative.StartsWith(".git/", StringComparison.Ordinal) || relative.StartsWith(".idd/factory/", StringComparison.Ordinal) ||
            relative.StartsWith(".agents/", StringComparison.Ordinal) || relative.StartsWith(".codex/", StringComparison.Ordinal) ||
            relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal) || relative.StartsWith("artifacts/", StringComparison.Ordinal);
    }
}

public sealed class VerificationEngine(string workspace, string currentDirectory, WorkspaceFingerprinter fingerprinter)
{
    public void ValidateCheckIds(IEnumerable<string> checkIds)
    {
        var ids = checkIds.Distinct(StringComparer.Ordinal).ToArray(); if (ids.Length == 0) return;
        var policyPath = Path.Combine(workspace, ".idd", "verification.yaml");
        if (!File.Exists(policyPath)) throw new VerificationException("UNKNOWN_VERIFICATION_CHECK", "Explicit verification IDs require .idd/verification.yaml.");
        var known = VerificationPolicyParser.Parse(File.ReadAllText(policyPath));
        var unknown = ids.Where(id => !known.ContainsKey(id)).ToArray();
        if (unknown.Length > 0) throw new VerificationException("UNKNOWN_VERIFICATION_CHECK", $"Unknown check IDs: {string.Join(", ", unknown)}.");
    }

    public async Task<VerificationResult> RunContextAsync(string context, CancellationToken cancellationToken)
    {
        var policyPath = Path.Combine(workspace, ".idd", "verification.yaml");
        if (File.Exists(policyPath))
        {
            var yaml = await File.ReadAllTextAsync(policyPath, cancellationToken);
            var ids = VerificationPolicyParser.ResolveContext(yaml, context);
            return await RunAsync(ids, cancellationToken);
        }
        var fallback = RepositoryFallback();
        return fallback is null ? new(VerificationStatus.Passed, []) : await RunChecksAsync([new("repository-fallback", fallback)], cancellationToken);
    }

    public async Task<VerificationResult> RunAsync(IEnumerable<string> checkIds, CancellationToken cancellationToken)
    {
        var policyPath = Path.Combine(workspace, ".idd", "verification.yaml");
        if (!File.Exists(policyPath)) return new(VerificationStatus.Passed, []);
        var checks = VerificationPolicyParser.Parse(await File.ReadAllTextAsync(policyPath, cancellationToken));
        var selected = new List<(string Id, VerificationCheck Check)>();
        foreach (var id in checkIds.Distinct(StringComparer.Ordinal))
        {
            if (!checks.TryGetValue(id, out var check)) throw new VerificationException("UNKNOWN_VERIFICATION_CHECK", $"Unknown check ID {id}.");
            selected.Add((id, check));
        }
        return await RunChecksAsync(selected, cancellationToken);
    }

    private async Task<VerificationResult> RunChecksAsync(IEnumerable<(string Id, VerificationCheck Check)> checks, CancellationToken cancellationToken)
    {
        var evidence = new List<VerificationEvidence>();
        foreach (var (id, check) in checks) evidence.Add(await RunCheckAsync(id, check, cancellationToken));
        var status = evidence.Any(x => x.Status == "infrastructure-failure") ? VerificationStatus.InfrastructureFailure
            : evidence.Any(x => x.Status == "requires-user-action") ? VerificationStatus.RequiresUserAction
            : evidence.Any(x => x.Status == "failed") ? VerificationStatus.Failed
            : VerificationStatus.Passed;
        return new(status, evidence);
    }

    private async Task<VerificationEvidence> RunCheckAsync(string id, VerificationCheck check, CancellationToken cancellationToken)
    {
        var before = fingerprinter.Compute(workspace); var started = DateTimeOffset.UtcNow;
        if (check.Instructions is not null)
            return await PersistAsync(id, check.Instructions, before, started, -1, "requires-user-action", check.Instructions, cancellationToken);
        var shell = OperatingSystem.IsWindows() ? "powershell" : "/bin/sh";
        var info = new ProcessStartInfo(shell) { WorkingDirectory = workspace, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        if (OperatingSystem.IsWindows()) { info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-Command"); info.ArgumentList.Add(check.Run!); }
        else { info.ArgumentList.Add("-c"); info.ArgumentList.Add(check.Run!); }
        Process? startedProcess;
        try { startedProcess = Process.Start(info); }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        { return await PersistAsync(id, check.Run!, before, started, -1, "infrastructure-failure", exception.Message, cancellationToken); }
        if (startedProcess is null) return await PersistAsync(id, check.Run!, before, started, -1, "infrastructure-failure", $"Could not start check {id}.", cancellationToken);
        using var process = startedProcess;
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(check.Timeout);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited) process.Kill(true);
            return await PersistAsync(id, check.Run!, before, started, -1, "infrastructure-failure", $"Check {id} timed out.", CancellationToken.None);
        }
        var output = (await stdoutTask) + (await stderrTask);
        return await PersistAsync(id, check.Run!, before, started, process.ExitCode, process.ExitCode == 0 ? "passed" : "failed", output, cancellationToken);
    }

    private async Task<VerificationEvidence> PersistAsync(string id, string definition, string before, DateTimeOffset started, int exitCode, string status, string output, CancellationToken cancellationToken)
    {
        var definitionHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(definition))).ToLowerInvariant();
        var result = new VerificationEvidence(1, $"V{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..36], id, definitionHash, before, started, DateTimeOffset.UtcNow, exitCode, status, output);
        var directory = Path.Combine(currentDirectory, "verification"); Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, result.EvidenceId + ".json"), JsonSerializer.Serialize(result, FactoryJson.Options), cancellationToken);
        return result;
    }

    private VerificationCheck? RepositoryFallback()
    {
        if (File.Exists(Path.Combine(workspace, "scripts", "Check.ps1"))) return new("& './scripts/Check.ps1'", null, TimeSpan.FromMinutes(30));
        var solution = Directory.GetFiles(workspace, "*.sln").OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault();
        if (solution is not null) return new($"dotnet test '{Path.GetFileName(solution)}'", null, TimeSpan.FromMinutes(30));
        return null;
    }
}

public sealed record VerificationCheck(string? Run, string? Instructions, TimeSpan Timeout);

internal static class VerificationPolicyParser
{
    public static IReadOnlyDictionary<string, VerificationCheck> Parse(string yaml)
    {
        var checks = new Dictionary<string, VerificationCheck>(StringComparer.Ordinal); string? current = null, run = null, instructions = null; var timeout = TimeSpan.FromMinutes(10); var inChecks = false;
        void Commit() { if (current is null) return; if ((run is null) == (instructions is null)) throw new VerificationException("INVALID_VERIFICATION_POLICY", $"Check {current} must have exactly one of run or instructions."); checks.Add(current, new(run, instructions, timeout)); }
        foreach (var raw in yaml.Replace("\r\n", "\n").Split('\n'))
        {
            var text = raw.Trim(); if (text.Length == 0 || text.StartsWith('#')) continue; var indent = raw.Length - raw.TrimStart().Length;
            if (indent == 0 && text == "checks:") { inChecks = true; continue; }
            if (indent == 0) { inChecks = false; continue; }
            if (!inChecks) continue;
            if (indent == 2 && text.EndsWith(':')) { Commit(); current = text[..^1]; run = instructions = null; timeout = TimeSpan.FromMinutes(10); continue; }
            if (indent == 4 && current is not null)
            {
                var split = text.IndexOf(':'); if (split < 1) throw new VerificationException("INVALID_VERIFICATION_POLICY", text);
                var key = text[..split]; var value = text[(split + 1)..].Trim().Trim('"', '\'');
                if (key == "run") run = value; else if (key == "instructions") instructions = value; else if (key == "timeout") timeout = ParseTimeout(value);
            }
        }
        Commit(); return checks;
    }
    public static IReadOnlyList<string> ResolveContext(string yaml, string context)
    {
        var lines = yaml.Replace("\r\n", "\n").Split('\n'); var inContext = false; var inUse = false; var result = new List<string>();
        foreach (var raw in lines)
        {
            var text = raw.Trim(); if (text.Length == 0 || text.StartsWith('#')) continue; var indent = raw.Length - raw.TrimStart().Length;
            if (indent == 0) { if (inContext) break; inContext = text == context + ":"; inUse = false; continue; }
            if (!inContext) continue;
            if (indent == 2 && text == "use:") { inUse = true; continue; }
            if (indent == 2 && text != "use:") { inUse = false; continue; }
            if (inUse && indent == 4 && text.StartsWith("- ")) result.Add(text[2..].Trim().Trim('"', '\''));
        }
        if (result.Count > 0) return result;
        if (context != "default") return ResolveContext(yaml, "default");
        return [];
    }
    private static TimeSpan ParseTimeout(string value) => value.EndsWith('s') ? TimeSpan.FromSeconds(int.Parse(value[..^1])) : value.EndsWith('m') ? TimeSpan.FromMinutes(int.Parse(value[..^1])) : value.EndsWith('h') ? TimeSpan.FromHours(int.Parse(value[..^1])) : throw new VerificationException("INVALID_VERIFICATION_POLICY", $"Invalid timeout {value}.");
}

public sealed class VerificationException(string code, string message) : Exception(message) { public string Code { get; } = code; }
