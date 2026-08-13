using Idd.Factory.LiveTests.Infrastructure;
using System.Security.Cryptography;
using System.Text.Json;
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
    public void MatchingHostVersionAndFingerprintPass()
    {
        using var fixture = new LifecycleFixture();
        var identity = CodexHostLifecycleCertification.CreateHostIdentity(fixture.NativeCommand, "codex-cli 1.2.3");
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fixture.ExecutablePath))), identity.ExecutableSha256);
        fixture.WriteReport(identity with { Version = " \r\n codex-cli   1.2.3 \n", ExecutableSha256 = identity.ExecutableSha256.ToLowerInvariant() });

        var report = CodexHostLifecycleCertification.Load(fixture.ReportPath);
        CodexHostLifecycleCertification.ValidateHost(report, fixture.NativeCommand, "codex-cli 1.2.3\r\n");
        CodexHostLifecycleCertification.ValidateLifecycle(report);
    }

    [Fact]
    public void SameVersionFromDifferentExecutableIsRejectedBeforeReleaseEval()
    {
        using var fixture = new LifecycleFixture();
        var reportIdentity = CodexHostLifecycleCertification.CreateHostIdentity(fixture.NativeCommand, "codex-cli 1.2.3");
        fixture.WriteReport(reportIdentity);
        File.WriteAllText(fixture.ExecutablePath, "different Codex binary");

        var report = CodexHostLifecycleCertification.Load(fixture.ReportPath);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CodexHostLifecycleCertification.ValidateHost(report, fixture.NativeCommand, "codex-cli 1.2.3"));

        Assert.Contains("different Codex host", exception.Message);
        Assert.Contains("Expected:", exception.Message);
        Assert.Contains("Report:", exception.Message);
    }

    [Fact]
    public void DifferentVersionWithSameFingerprintIsRejected()
    {
        using var fixture = new LifecycleFixture();
        var identity = CodexHostLifecycleCertification.CreateHostIdentity(fixture.NativeCommand, "codex-cli 1.2.3");
        fixture.WriteReport(identity with { Version = "codex-cli 9.9.9" });
        var report = CodexHostLifecycleCertification.Load(fixture.ReportPath);

        Assert.Throws<InvalidOperationException>(() =>
            CodexHostLifecycleCertification.ValidateHost(report, fixture.NativeCommand, "codex-cli 1.2.3"));
    }

    [Fact]
    public void MissingFingerprintIsRejectedStructurally()
    {
        using var fixture = new LifecycleFixture();
        fixture.WriteRaw("""
            {"schemaVersion":2,"probeKind":"process-tree-lifecycle","host":{"version":"codex-cli 1.2.3"},"normalInterruptNoDescendants":true,"hardKillNoDescendants":true,"factoryStateResumable":true}
            """);

        var exception = Assert.Throws<InvalidOperationException>(() => CodexHostLifecycleCertification.Load(fixture.ReportPath));
        Assert.Contains("executableSha256", exception.Message);
    }

    [Fact]
    public void SchemaVersionOneCannotCertifyARelease()
    {
        using var fixture = new LifecycleFixture();
        fixture.WriteRaw("""
            {"schemaVersion":1,"probeKind":"process-tree-lifecycle","hostBuild":"source-build","normalInterruptNoDescendants":true,"hardKillNoDescendants":true,"factoryStateResumable":true}
            """);

        var exception = Assert.Throws<InvalidOperationException>(() => CodexHostLifecycleCertification.Load(fixture.ReportPath));
        Assert.Contains("Repeat the lifecycle probe", exception.Message);
    }

    [Theory]
    [InlineData(false, true, true, "normalInterruptNoDescendants")]
    [InlineData(true, false, true, "hardKillNoDescendants")]
    [InlineData(true, true, false, "factoryStateResumable")]
    public void FailedLifecycleResultIsRejected(bool normalInterrupt, bool hardKill, bool resumable, string expectedDiagnostic)
    {
        using var fixture = new LifecycleFixture();
        var identity = CodexHostLifecycleCertification.CreateHostIdentity(fixture.NativeCommand, "codex-cli 1.2.3");
        fixture.WriteReport(identity, normalInterrupt, hardKill, resumable);
        var report = CodexHostLifecycleCertification.Load(fixture.ReportPath);

        var exception = Assert.Throws<InvalidOperationException>(() => CodexHostLifecycleCertification.ValidateLifecycle(report));
        Assert.Contains(expectedDiagnostic, exception.Message);
    }

    [Fact]
    public void ScriptPrefixParticipatesInHostIdentity()
    {
        using var fixture = new LifecycleFixture();
        var firstScript = fixture.WriteArtifact("first-codex.js", "first installation");
        var secondScript = fixture.WriteArtifact("second-codex.js", "second installation");
        var first = CodexHostLifecycleCertification.CreateHostIdentity(new(fixture.ExecutablePath, [firstScript]), "codex-cli 1.2.3");
        var second = CodexHostLifecycleCertification.CreateHostIdentity(new(fixture.ExecutablePath, [secondScript]), "codex-cli 1.2.3");

        Assert.NotEqual(first.ExecutableSha256, second.ExecutableSha256);
        fixture.WriteReport(first);
        var report = CodexHostLifecycleCertification.Load(fixture.ReportPath);
        Assert.Throws<InvalidOperationException>(() =>
            CodexHostLifecycleCertification.ValidateHost(report, new(fixture.ExecutablePath, [secondScript]), "codex-cli 1.2.3"));
    }

    [Fact]
    public void MissingLifecycleReportIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => CodexHostLifecycleCertification.Load(null));
    }

    private sealed class LifecycleFixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "idd-lifecycle-" + Guid.NewGuid().ToString("N"));

        public LifecycleFixture()
        {
            Directory.CreateDirectory(root);
            ExecutablePath = WriteArtifact("codex-host.bin", "Codex binary");
            ReportPath = Path.Combine(root, "lifecycle.json");
        }

        public string ExecutablePath { get; }
        public string ReportPath { get; }
        public CodexCommand NativeCommand => new(ExecutablePath, []);

        public string WriteArtifact(string name, string content)
        {
            var path = Path.Combine(root, name);
            File.WriteAllText(path, content);
            return path;
        }

        public void WriteReport(CodexHostIdentity host, bool normalInterrupt = true, bool hardKill = true, bool resumable = true) =>
            WriteRaw(JsonSerializer.Serialize(new
            {
                schemaVersion = 2,
                probeKind = "process-tree-lifecycle",
                host,
                normalInterruptNoDescendants = normalInterrupt,
                hardKillNoDescendants = hardKill,
                factoryStateResumable = resumable
            }));

        public void WriteRaw(string content) => File.WriteAllText(ReportPath, content);

        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
