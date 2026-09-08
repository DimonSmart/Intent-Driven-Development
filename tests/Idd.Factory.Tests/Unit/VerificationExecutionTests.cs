using Idd.Factory.Verification;

namespace Idd.Factory.Tests;

public sealed class VerificationExecutionTests
{
    [Theory]
    [InlineData(0, VerificationStatus.Passed, "passed")]
    [InlineData(7, VerificationStatus.Failed, "failed")]
    public async Task CheckExitCodeProducesStructuredEvidence(int exitCode, VerificationStatus expectedStatus, string evidenceStatus)
    {
        using var test = new VerificationTestContext().WithCheck("check", $"exit {exitCode}");

        var result = await test.Engine().RunAsync(["check"], default);

        Assert.Equal(expectedStatus, result.Status);
        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(exitCode, evidence.ExitCode);
        Assert.Equal(evidenceStatus, evidence.Status);
        Assert.True(File.Exists(test.EvidencePath(evidence)));
    }

    [Fact]
    public async Task ConfirmedCheckNeverRunsBeforeExplicitConfirmation()
    {
        using var test = new VerificationTestContext().WithCheck("expensive", "exit 0", confirmationRequired: true);
        var engine = test.Engine();

        var pending = await engine.RunAsync(["expensive"], default);
        Assert.Equal(VerificationStatus.ConfirmationRequired, pending.Status);
        Assert.Empty(pending.Evidence);

        var completed = await engine.RunCheckAsync("expensive", confirmed: true, manualPassed: null, default);
        Assert.Equal(VerificationStatus.Passed, completed.Status);
    }

    [Fact]
    public async Task DeclinedConfirmedCheckRecordsNotVerifiedEvidenceWithoutRunningCommand()
    {
        using var test = new VerificationTestContext().WithCheck("expensive", "throw 'must not run'", confirmationRequired: true);
        var engine = test.Engine();
        var pending = await engine.RunCheckAsync("expensive", confirmed: false, manualPassed: null, default);

        var declined = await engine.DeclineCheckAsync(
            "expensive",
            await engine.GetCheckDefinitionHashAsync("expensive", default),
            (await engine.ResolveContextAsync("subtask", [], default)).PolicyHash,
            default);

        Assert.Equal(VerificationStatus.Declined, declined.Status);
        Assert.Equal("not-verified", Assert.Single(declined.Evidence).Status);
        Assert.Equal(pending.PendingCheckId, declined.PendingCheckId);
    }

    [Fact]
    public async Task ManualCheckRequiresExplicitResultWithoutRunningAProcess()
    {
        using var test = new VerificationTestContext().WithPolicy("""
            version: 1
            checks:
              manual:
                instructions: Confirm behavior
            default:
              use:
                - manual
            """);

        var result = await test.Engine().RunAsync(["manual"], default);

        Assert.Equal(VerificationStatus.ResultRequired, result.Status);
        Assert.Empty(result.Evidence);
        Assert.Equal("manual", result.PendingCheckId);
    }
}
