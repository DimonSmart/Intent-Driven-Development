using Idd.Factory.LiveTests.Infrastructure;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class ReleaseCertificationTests
{
    private const string Revision = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void DirtyDeveloperEvalRemainsAllowed()
    {
        Assert.Equal("1.2.3-eval", new MethodologyVersion("1.2.3-eval", "01234567", true).Value);
    }

    [Fact]
    public void DirtyReleaseCertificationIsRejectedEarly()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ReleaseCertification.Validate(true, Revision, "v1.2.3", null));
        Assert.Contains("clean source tree", exception.Message);
    }

    [Fact]
    public void CleanTaggedRevisionIsCertified()
    {
        Assert.Equal("1.2.3", ReleaseCertification.Validate(false, Revision, "v1.2.3", "1.2.3"));
    }

    [Theory]
    [InlineData("01234567", "v1.2.3", null)]
    [InlineData(Revision, null, null)]
    [InlineData(Revision, "v1.2.3", "1.2.4")]
    public void CertificationRejectsUnpinnedOrMismatchedIdentity(string revision, string? tag, string? version)
    {
        Assert.Throws<InvalidOperationException>(() => ReleaseCertification.Validate(false, revision, tag, version));
    }

    [Fact]
    public void SupportedCodexReleaseHostPasses()
    {
        CodexReleaseHostCompatibility.RequireSupported("codex-cli 0.148.0\r\n");
        CodexReleaseHostCompatibility.RequireSupported("codex-cli 0.149.0");
        CodexReleaseHostCompatibility.RequireSupported("codex-cli 1.0.0");
    }

    [Theory]
    [InlineData("codex-cli 0.147.0")]
    [InlineData("codex-cli 0.148")]
    [InlineData("codex-cli 0.148.0-alpha.15")]
    [InlineData("codex 0.149.0")]
    [InlineData("")]
    public void UnsupportedOrUnrecognizedCodexReleaseHostIsRejected(string version)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CodexReleaseHostCompatibility.RequireSupported(version));
        Assert.Contains("Release certification requires", exception.Message);
    }
}
