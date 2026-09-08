using Idd.Factory.Verification;

namespace Idd.Factory.Tests;

public sealed class VerificationPolicyTests
{
    [Fact]
    public async Task UnknownCheckIsRejected()
    {
        using var test = new VerificationTestContext().WithCheck("known", "exit 0");

        var error = await Assert.ThrowsAsync<VerificationException>(() => test.Engine().RunAsync(["missing"], default));

        Assert.Equal("UNKNOWN_VERIFICATION_CHECK", error.Code);
    }

    [Fact]
    public async Task FinalContextRunsItsAssignedChecks()
    {
        using var test = new VerificationTestContext().WithPolicy("""
            version: 1
            checks:
              default-check:
                run: exit 0
              final-check:
                run: exit 0
            default:
              use:
                - default-check
            final:
              use:
                - final-check
            """);

        var result = await test.Engine().RunContextAsync("final", default);

        Assert.Equal(VerificationStatus.Passed, result.Status);
        Assert.Equal("final-check", Assert.Single(result.Evidence).CheckId);
    }

    [Fact]
    public async Task PathRulesSelectFirstMatchingRule()
    {
        using var test = new VerificationTestContext().WithPolicy("""
            version: 1
            checks:
              backend:
                run: exit 0
              default-check:
                run: exit 1
            default:
              use:
                - default-check
            subtask:
              rules:
                - paths:
                    - src/backend/**
                  use:
                    - backend
                - fallback: true
                  use:
                    - default-check
            """);

        var result = await test.Engine().RunContextAsync("subtask", ["src\\backend\\A.cs"], default);

        Assert.Equal(VerificationStatus.Passed, result.Status);
        Assert.Equal("backend", Assert.Single(result.Evidence).CheckId);
    }

    [Theory]
    [InlineData("version: 2\nchecks: {}\ndefault:\n  use: []\n")]
    [InlineData("checks: {}\ndefault:\n  use: []\n")]
    [InlineData("version: 1\nchecks: {}\ndefault: {}\n")]
    [InlineData("version: 1\nchecks:\n  broken: command\ndefault:\n  use: []\n")]
    [InlineData("```yaml\nversion: 1\nchecks: {}\ndefault:\n  use: []\n```\n")]
    [InlineData("version: 1\nchecks: [\n")]
    public async Task MalformedPolicyNeverFallsBack(string policy)
    {
        using var test = new VerificationTestContext().WithPolicy(policy);
        test.Write("scripts/Check.ps1", "Set-Content -LiteralPath fallback-ran.txt -Value yes\n");

        var error = await Assert.ThrowsAsync<VerificationException>(() => test.Engine().RunContextAsync("final", default));

        Assert.Equal("INVALID_VERIFICATION_POLICY", error.Code);
        Assert.False(File.Exists(Path.Combine(test.WorkspacePath, "fallback-ran.txt")));
    }

    [Fact]
    public async Task ExistingPolicyIsValidatedEvenWithoutExplicitCheckIds()
    {
        using var test = new VerificationTestContext().WithPolicy("version: 1\nchecks: {}\n");

        var error = await Assert.ThrowsAsync<VerificationException>(() => test.Engine().RunSubtaskAsync([], default));

        Assert.Equal("INVALID_VERIFICATION_POLICY", error.Code);
    }
}
