using Idd.Factory.Verification;

namespace Idd.Factory.Tests;

public sealed class VerificationTests
{
    [Fact] public async Task UnknownCheckIsRejected()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  known:\n    run: exit 0\ndefault:\n  use:\n    - known\n");
        var engine = new VerificationEngine(temp.Path, System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"));
        Assert.Equal("UNKNOWN_VERIFICATION_CHECK", (await Assert.ThrowsAsync<VerificationException>(() => engine.RunAsync(["missing"], default))).Code);
    }

    [Fact] public async Task SuccessfulCheckCreatesEvidence()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  pass:\n    run: exit 0\n    timeout: 10s\ndefault:\n  use:\n    - pass\n");
        var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"); var evidence = await new VerificationEngine(temp.Path, current).RunAsync(["pass"], default);
        Assert.Equal(VerificationStatus.Passed, evidence.Status); Assert.Single(evidence.Evidence); Assert.Equal("passed", evidence.Evidence[0].Status); Assert.Equal(2, evidence.Evidence[0].SchemaVersion); Assert.True(File.Exists(System.IO.Path.Combine(current, "verification", evidence.Evidence[0].EvidenceId + ".json")));
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(current, "verification", evidence.Evidence[0].EvidenceId + ".json"))); Assert.False(document.RootElement.TryGetProperty("workspaceFingerprint", out _));
    }

    [Fact] public async Task FinalContextRunsItsAssignedChecks()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  default-check:\n    run: exit 0\n  final-check:\n    run: exit 0\ndefault:\n  use:\n    - default-check\nfinal:\n  use:\n    - final-check\n");
        var evidence = await new VerificationEngine(temp.Path, System.IO.Path.Combine(temp.Path, ".idd", "factory", "current")).RunContextAsync("final", default);
        Assert.Equal(VerificationStatus.Passed, evidence.Status); Assert.Single(evidence.Evidence); Assert.Equal("final-check", evidence.Evidence[0].CheckId);
    }

    [Fact] public async Task FailedCheckReturnsStructuredResultAndPersistsEvidence()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  fail:\n    run: exit 7\ndefault:\n  use:\n    - fail\n");
        var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current");
        var result = await new VerificationEngine(temp.Path, current).RunAsync(["fail"], default);
        Assert.Equal(VerificationStatus.Failed, result.Status); Assert.Equal(7, Assert.Single(result.Evidence).ExitCode);
        Assert.True(File.Exists(System.IO.Path.Combine(current, "verification", result.Evidence[0].EvidenceId + ".json")));
    }

    [Fact] public async Task ManualCheckRequiresUserActionWithoutThrowing()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  manual:\n    instructions: Confirm behavior\ndefault:\n  use:\n    - manual\n");
        var result = await new VerificationEngine(temp.Path, System.IO.Path.Combine(temp.Path, ".idd", "factory", "current")).RunAsync(["manual"], default);
        Assert.Equal(VerificationStatus.RequiresUserAction, result.Status); Assert.Equal("requires-user-action", Assert.Single(result.Evidence).Status);
    }

    [Fact] public async Task RunnerTimeoutIsInfrastructureFailure()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  timeout:\n    run: dotnet --info\n    timeout: 0s\ndefault:\n  use:\n    - timeout\n");
        var result = await new VerificationEngine(temp.Path, System.IO.Path.Combine(temp.Path, ".idd", "factory", "current")).RunAsync(["timeout"], default);
        Assert.Equal(VerificationStatus.InfrastructureFailure, result.Status); Assert.Equal("infrastructure-failure", Assert.Single(result.Evidence).Status);
    }

    [Theory]
    [InlineData("version: 2\nchecks: {}\ndefault:\n  use: []\n")]
    [InlineData("checks: {}\ndefault:\n  use: []\n")]
    [InlineData("version: 1\nchecks: {}\ndefault: {}\n")]
    [InlineData("version: 1\nchecks:\n  broken: command\ndefault:\n  use: []\n")]
    [InlineData("```yaml\nversion: 1\nchecks: {}\ndefault:\n  use: []\n```\n")]
    [InlineData("version: 1\nchecks: [\n")]
    public async Task ExistingMalformedPolicyDoesNotFallback(string policy)
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/verification.yaml", policy);
        temp.Write("scripts/Check.ps1", "Set-Content -LiteralPath fallback-ran.txt -Value yes\n");
        var engine = new VerificationEngine(temp.Path, System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"));

        var exception = await Assert.ThrowsAsync<VerificationException>(() => engine.RunContextAsync("final", default));

        Assert.Equal("INVALID_VERIFICATION_POLICY", exception.Code);
        Assert.False(File.Exists(System.IO.Path.Combine(temp.Path, "fallback-ran.txt")));
    }

    [Fact] public async Task ExistingPolicyIsValidatedEvenWithNoExplicitIds()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/verification.yaml", "version: 1\nchecks: {}\n");
        var engine = new VerificationEngine(temp.Path, System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"));

        var exception = await Assert.ThrowsAsync<VerificationException>(() => engine.RunSubtaskAsync([], default));

        Assert.Equal("INVALID_VERIFICATION_POLICY", exception.Code);
    }
}
