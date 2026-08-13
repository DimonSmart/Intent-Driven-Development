using System.Text.Json;

namespace Idd.Factory.LiveTests.Infrastructure;

public static class CodexHostLifecycleCertification
{
    public const string ReportEnvironmentVariable = "IDD_FACTORY_CODEX_LIFECYCLE_REPORT";

    public static CodexHostLifecycleReport RequireFromEnvironment() =>
        Validate(Environment.GetEnvironmentVariable(ReportEnvironmentVariable));

    public static CodexHostLifecycleReport Validate(string? reportPath)
    {
        if (string.IsNullOrWhiteSpace(reportPath) || !File.Exists(reportPath))
            throw new InvalidOperationException($"Release certification requires a real Codex process-tree lifecycle report via {ReportEnvironmentVariable}.");
        CodexHostLifecycleReport? report;
        try
        {
            report = JsonSerializer.Deserialize<CodexHostLifecycleReport>(File.ReadAllText(reportPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The Codex process-tree lifecycle report is invalid JSON.", exception);
        }
        if (report is null || report.SchemaVersion != 1 || report.ProbeKind != "process-tree-lifecycle")
            throw new InvalidOperationException("The Codex process-tree lifecycle report has an unsupported contract.");
        if (string.IsNullOrWhiteSpace(report.HostBuild))
            throw new InvalidOperationException("The Codex process-tree lifecycle report does not identify the tested host build.");
        if (!report.NormalInterruptNoDescendants || !report.HardKillNoDescendants || !report.FactoryStateResumable)
            throw new InvalidOperationException("The tested Codex host failed Factory process-tree cleanup or resumability certification.");
        return report;
    }
}

public sealed record CodexHostLifecycleReport(
    int SchemaVersion,
    string ProbeKind,
    string HostBuild,
    bool NormalInterruptNoDescendants,
    bool HardKillNoDescendants,
    bool FactoryStateResumable);
