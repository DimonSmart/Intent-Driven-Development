namespace Idd.Intent.LiveTests.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class LiveIntentEvalFactAttribute : Xunit.FactAttribute
{
    public LiveIntentEvalFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("IDD_RUN_LIVE_INTENT_EVALS"), "1", StringComparison.Ordinal))
            Skip = "Set IDD_RUN_LIVE_INTENT_EVALS=1 to run this token-consuming Codex live eval.";
    }
}
