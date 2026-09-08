using System.Text;
using System.Text.Json;
using Idd.Factory.Verification;

namespace Idd.Factory.Tests;

public sealed class VerificationEvidenceTests
{
    [Fact]
    public async Task LargeOutputIsPersistedWhileInlineTailIsUtf8Bounded()
    {
        var command = OperatingSystem.IsWindows()
            ? "[Console]::Out.Write(('🙂' * 20000))"
            : "i=0; while [ $i -lt 20000 ]; do printf '🙂'; i=$((i+1)); done";
        using var test = new VerificationTestContext().WithCheck("large", command, timeout: "20s");

        var evidence = Assert.Single((await test.Engine().RunAsync(["large"], default)).Evidence);

        Assert.True(Encoding.UTF8.GetByteCount(evidence.StdoutTail) <= 4 * 1024);
        Assert.DoesNotContain('\uFFFD', evidence.StdoutTail);
        var logLength = new FileInfo(Path.Combine(test.WorkspacePath, evidence.StdoutPath!.Replace('/', Path.DirectorySeparatorChar))).Length;
        Assert.Equal(logLength, evidence.Stdout.ByteLength);
        Assert.True(evidence.Stdout.Truncated);
        Assert.True(logLength > 16 * 1024);
    }

    [Fact]
    public async Task PersistedV3EvidenceHasStableCompactStreamContract()
    {
        var command = OperatingSystem.IsWindows()
            ? "1..60 | ForEach-Object { Write-Output \"line-$_\" }"
            : "i=1; while [ $i -le 60 ]; do echo line-$i; i=$((i+1)); done";
        using var test = new VerificationTestContext().WithCheck("lines", command);

        var evidence = Assert.Single((await test.Engine().RunAsync(["lines"], default)).Evidence);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(test.EvidencePath(evidence)));
        var root = document.RootElement;

        Assert.Equal(3, root.GetProperty("schemaVersion").GetInt32());
        Assert.False(root.TryGetProperty("output", out _));
        Assert.False(root.TryGetProperty("stdoutPath", out _));
        Assert.False(root.TryGetProperty("stdoutTail", out _));
        Assert.False(root.TryGetProperty("primaryFailure", out _));
        Assert.False(root.TryGetProperty("secondaryIssues", out _));
        Assert.False(root.TryGetProperty("evidencePersisted", out _));
        Assert.True(root.TryGetProperty("durationMs", out _));
        Assert.True(root.TryGetProperty("timeoutMs", out _));
        Assert.False(root.TryGetProperty("durationMilliseconds", out _));
        Assert.False(root.TryGetProperty("timeoutMilliseconds", out _));
        Assert.Equal(new[] { "display", "workingDirectory" }, root.GetProperty("command").EnumerateObject().Select(x => x.Name).Order().ToArray());
        Assert.Equal(".", root.GetProperty("command").GetProperty("workingDirectory").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("failureKind").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("failureStage").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("summary").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("exception").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("termination").ValueKind);
        Assert.Equal(0, root.GetProperty("diagnosticIssues").GetArrayLength());
        var stdout = root.GetProperty("stdout");
        Assert.True(stdout.GetProperty("truncated").GetBoolean());
        Assert.True(stdout.GetProperty("tail").GetString()!.Split('\n').Length <= 40);
        Assert.Equal(
            new FileInfo(Path.Combine(test.WorkspacePath, stdout.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar))).Length,
            stdout.GetProperty("byteLength").GetInt64());
        var stderr = root.GetProperty("stderr");
        Assert.Equal(JsonValueKind.Null, stderr.GetProperty("path").ValueKind);
        Assert.Equal(0, stderr.GetProperty("byteLength").GetInt64());
        Assert.Equal("", stderr.GetProperty("tail").GetString());
        Assert.False(stderr.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task PersistedProcessStartFailureUsesFlatV3FailureContract()
    {
        using var test = new VerificationTestContext().WithCheck("check", "exit 0");
        var hooks = new VerificationRuntimeHooks
        {
            StartProcess = _ => throw new System.ComponentModel.Win32Exception("start exploded")
        };

        var evidence = Assert.Single((await test.Engine(hooks).RunAsync(["check"], default)).Evidence);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(test.EvidencePath(evidence)));
        var root = document.RootElement;

        Assert.Equal("process-start-failure", root.GetProperty("failureKind").GetString());
        Assert.Equal("start", root.GetProperty("failureStage").GetString());
        Assert.Equal("Could not start check check.", root.GetProperty("summary").GetString());
        Assert.Equal(typeof(System.ComponentModel.Win32Exception).FullName, root.GetProperty("exception").GetProperty("type").GetString());
        Assert.Equal("start exploded", root.GetProperty("exception").GetProperty("message").GetString());
        Assert.NotEmpty(root.GetProperty("exception").GetProperty("stackTrace").GetString()!);
        Assert.False(root.TryGetProperty("primaryFailure", out _));
        Assert.Equal("process-start-failure", VerificationEngine.Read(root.GetRawText()).PrimaryFailure?.Kind);
    }

    [Fact]
    public void LegacySchemaV2EvidenceRemainsReadable()
    {
        const string json = "{\"schemaVersion\":2,\"evidenceId\":\"V1\",\"checkId\":\"old\",\"checkDefinitionHash\":\"h\",\"startedAt\":\"2025-01-01T00:00:00Z\",\"finishedAt\":\"2025-01-01T00:00:01Z\",\"exitCode\":7,\"status\":\"failed\",\"output\":\"legacy\"}";

        var evidence = VerificationEngine.Read(json);

        Assert.Equal(2, evidence.SchemaVersion);
        Assert.Equal(7, evidence.ExitCode);
        Assert.Equal("legacy", evidence.Output);
    }
}
