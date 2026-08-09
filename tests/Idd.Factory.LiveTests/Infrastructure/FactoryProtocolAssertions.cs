namespace Idd.Factory.LiveTests.Infrastructure;

public static class FactoryProtocolAssertions
{
    public static void Assert(
        EvalAssertionCollector assertions,
        FactoryOutcomeTraceAnalysis analysis,
        ExecutionResponseReadResult executionResponse)
    {
        assertions.Require(
            analysis.PublicFactoryOutcomes.Count == 1,
            "Factory protocol",
            "Single terminal outcome",
            $"Expected exactly one public Factory outcome, found {analysis.PublicFactoryOutcomes.Count}.");
        assertions.Require(
            analysis.ActivityAfterOutcome.Count == 0,
            "Factory protocol",
            "No activity after terminal outcome",
            $"Factory performed execution after its terminal outcome: {string.Join(", ", analysis.ActivityAfterOutcome)}.");

        if (analysis.PublicFactoryOutcomes.Count != 1) return;

        var traceOutcome = analysis.PublicFactoryOutcomes[0].FactoryOutcome;
        var finalOutcome = executionResponse.Response?.FactoryOutcome;
        assertions.Require(
            finalOutcome is not null && traceOutcome == finalOutcome,
            "Factory protocol",
            "Outcome consistency",
            $"Factory outcome in events.jsonl ('{traceOutcome}') does not match last-message.json ('{finalOutcome ?? "unavailable"}').");
    }
}
