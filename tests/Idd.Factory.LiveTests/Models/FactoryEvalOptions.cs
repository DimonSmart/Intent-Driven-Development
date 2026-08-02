namespace Idd.Factory.LiveTests.Models;

public sealed record FactoryEvalOptions(
    string Model,
    string ReasoningEffort,
    TimeSpan Timeout,
    string MethodologyVersion)
{
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
            methodologyVersion);
    }
}
