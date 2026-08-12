using Idd.Factory.Verification;

namespace Idd.Factory.Tests;

public sealed class VerificationTests
{
    [Fact] public void FingerprintChangesForTrackedStagedAndUntrackedContent()
    {
        using var temp = new TestWorkspace(); var fingerprint = new WorkspaceFingerprinter(); temp.Write("tracked.txt", "one"); var one = fingerprint.Compute(temp.Path);
        temp.Write("tracked.txt", "two"); var two = fingerprint.Compute(temp.Path); temp.Write("untracked.txt", "three"); var three = fingerprint.Compute(temp.Path); temp.Write("untracked.txt", "four"); var four = fingerprint.Compute(temp.Path);
        Assert.NotEqual(one, two); Assert.NotEqual(two, three); Assert.NotEqual(three, four);
    }

    [Fact] public void FingerprintIgnoresFactoryOperationalLock()
    {
        using var temp = new TestWorkspace(); temp.Write("product.txt", "one"); var lockPath = temp.Write(".idd/factory/runtime.lock", "");
        using var held = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.NotEmpty(new WorkspaceFingerprinter().Compute(temp.Path));
    }

    [Fact] public async Task UnknownCheckIsRejected()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  known:\n    run: exit 0\ndefault:\n  use:\n    - known\n");
        var engine = new VerificationEngine(temp.Path, System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"), new WorkspaceFingerprinter());
        Assert.Equal("UNKNOWN_VERIFICATION_CHECK", (await Assert.ThrowsAsync<VerificationException>(() => engine.RunAsync(["missing"], default))).Code);
    }

    [Fact] public async Task SuccessfulCheckCreatesEvidence()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  pass:\n    run: exit 0\n    timeout: 10s\ndefault:\n  use:\n    - pass\n");
        var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"); var evidence = await new VerificationEngine(temp.Path, current, new WorkspaceFingerprinter()).RunAsync(["pass"], default);
        Assert.Equal(VerificationStatus.Passed, evidence.Status); Assert.Single(evidence.Evidence); Assert.Equal("passed", evidence.Evidence[0].Status); Assert.True(File.Exists(System.IO.Path.Combine(current, "verification", evidence.Evidence[0].EvidenceId + ".json")));
    }

    [Fact] public async Task FinalContextRunsItsAssignedChecks()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  default-check:\n    run: exit 0\n  final-check:\n    run: exit 0\ndefault:\n  use:\n    - default-check\nfinal:\n  use:\n    - final-check\n");
        var evidence = await new VerificationEngine(temp.Path, System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"), new WorkspaceFingerprinter()).RunContextAsync("final", default);
        Assert.Equal(VerificationStatus.Passed, evidence.Status); Assert.Single(evidence.Evidence); Assert.Equal("final-check", evidence.Evidence[0].CheckId);
    }

    [Fact] public async Task FailedCheckReturnsStructuredResultAndPersistsEvidence()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  fail:\n    run: exit 7\ndefault:\n  use:\n    - fail\n");
        var current = System.IO.Path.Combine(temp.Path, ".idd", "factory", "current");
        var result = await new VerificationEngine(temp.Path, current, new WorkspaceFingerprinter()).RunAsync(["fail"], default);
        Assert.Equal(VerificationStatus.Failed, result.Status); Assert.Equal(7, Assert.Single(result.Evidence).ExitCode);
        Assert.True(File.Exists(System.IO.Path.Combine(current, "verification", result.Evidence[0].EvidenceId + ".json")));
    }

    [Fact] public async Task ManualCheckRequiresUserActionWithoutThrowing()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  manual:\n    instructions: Confirm behavior\ndefault:\n  use:\n    - manual\n");
        var result = await new VerificationEngine(temp.Path, System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"), new WorkspaceFingerprinter()).RunAsync(["manual"], default);
        Assert.Equal(VerificationStatus.RequiresUserAction, result.Status); Assert.Equal("requires-user-action", Assert.Single(result.Evidence).Status);
    }

    [Fact] public async Task RunnerTimeoutIsInfrastructureFailure()
    {
        using var temp = new TestWorkspace(); temp.Write(".idd/verification.yaml", "version: 1\nchecks:\n  timeout:\n    run: dotnet --info\n    timeout: 0s\ndefault:\n  use:\n    - timeout\n");
        var result = await new VerificationEngine(temp.Path, System.IO.Path.Combine(temp.Path, ".idd", "factory", "current"), new WorkspaceFingerprinter()).RunAsync(["timeout"], default);
        Assert.Equal(VerificationStatus.InfrastructureFailure, result.Status); Assert.Equal("infrastructure-failure", Assert.Single(result.Evidence).Status);
    }
}
