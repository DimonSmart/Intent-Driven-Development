namespace Idd.Factory.LiveTests.Models;

public sealed record FactoryEvalOptions(
    string Model,
    string ReasoningEffort,
    TimeSpan Timeout,
    string MethodologyVersion,
    bool PersistSessionRollouts = false,
    bool ReleaseCertification = false,
    string? PreviousFactoryVersion = null)
{
    public static bool ReleaseCertificationRequested => string.Equals(Environment.GetEnvironmentVariable("IDD_FACTORY_RELEASE_CERTIFICATION"), "1", StringComparison.Ordinal);
    public static FactoryEvalOptions FromEnvironment(string methodologyVersion)
    {
        var timeoutText = Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_TIMEOUT_MINUTES");
        var timeout = int.TryParse(timeoutText, out var minutes) && minutes > 0
            ? TimeSpan.FromMinutes(minutes)
            : TimeSpan.FromMinutes(20);
        return new(
            Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_MODEL") ?? "gpt-5.6-luna",
            Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_REASONING_EFFORT") ?? "low",
            timeout,
            methodologyVersion,
            ReleaseCertification: ReleaseCertificationRequested,
            PreviousFactoryVersion: Environment.GetEnvironmentVariable("IDD_FACTORY_PREVIOUS_VERSION"));
    }
}
