namespace Idd.Factory.LiveTests.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class LiveFactoryEvalFactAttribute : Xunit.FactAttribute
{
    public LiveFactoryEvalFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("IDD_RUN_LIVE_FACTORY_EVALS"), "1", StringComparison.Ordinal))
            Skip = "Set IDD_RUN_LIVE_FACTORY_EVALS=1 to run this token-consuming Codex live eval.";
    }
}
