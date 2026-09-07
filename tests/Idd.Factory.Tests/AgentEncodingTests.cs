using System.Diagnostics;
using System.Text;
using Idd.Factory.Agents;

namespace Idd.Factory.Tests;

public sealed class AgentEncodingTests
{
    [Fact]
    public void ProcessTransportUsesStrictUtf8ForAllRedirectedStreams()
    {
        var start = CodexCliBackend.CreateProcessStartInfo("codex", "workspace");

        AssertStrictUtf8(start.StandardInputEncoding);
        AssertStrictUtf8(start.StandardOutputEncoding);
        AssertStrictUtf8(start.StandardErrorEncoding);
    }

    [Fact]
    public async Task CapturePreservesUnicodeInMemoryAndUtf8LogBytes()
    {
        const string text = "Устранена → готово\nstderr: ошибка\n";
        var utf8 = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);
        var expectedBytes = utf8.GetBytes(text);
        await using var stream = new MemoryStream(expectedBytes);
        using var reader = new StreamReader(stream, utf8, detectEncodingFromByteOrderMarks: false);
        var path = Path.Combine(Path.GetTempPath(), $"idd-factory-utf8-{Guid.NewGuid():N}.log");

        try
        {
            var captured = await CodexCliBackend.CaptureAsync(reader, path, CancellationToken.None);

            Assert.Equal(text, captured);
            Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ChildProcessUtf8StdoutAndStderrAreCapturedWithoutMojibake()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"idd-factory-child-utf8-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var stdoutPath = Path.Combine(directory, "stdout.log");
        var stderrPath = Path.Combine(directory, "stderr.log");

        try
        {
            var start = CreateUtf8WriterStartInfo(directory);
            using var process = new Process { StartInfo = start };
            Assert.True(process.Start());
            process.StandardInput.Close();

            var stdoutTask = CodexCliBackend.CaptureAsync(process.StandardOutput, stdoutPath, CancellationToken.None);
            var stderrTask = CodexCliBackend.CaptureAsync(process.StandardError, stderrPath, CancellationToken.None);
            await process.WaitForExitAsync();
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            Assert.Equal(0, process.ExitCode);
            Assert.Equal($"Устранена{Environment.NewLine}", stdout);
            Assert.Equal($"Ошибка{Environment.NewLine}", stderr);

            var utf8 = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true);
            Assert.Equal(utf8.GetBytes(stdout), await File.ReadAllBytesAsync(stdoutPath));
            Assert.Equal(utf8.GetBytes(stderr), await File.ReadAllBytesAsync(stderrPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ProcessStartInfo CreateUtf8WriterStartInfo(string workingDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            var start = CodexCliBackend.CreateProcessStartInfo(
                Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                workingDirectory);
            start.ArgumentList.Add("/d");
            start.ArgumentList.Add("/c");
            start.ArgumentList.Add("chcp 65001>nul & echo Устранена & echo Ошибка 1>&2");
            return start;
        }

        var shell = CodexCliBackend.CreateProcessStartInfo("/bin/sh", workingDirectory);
        shell.ArgumentList.Add("-c");
        shell.ArgumentList.Add("printf 'Устранена\\n'; printf 'Ошибка\\n' >&2");
        return shell;
    }

    private static void AssertStrictUtf8(Encoding? encoding)
    {
        Assert.NotNull(encoding);
        Assert.Equal(Encoding.UTF8.WebName, encoding.WebName);
        Assert.Empty(encoding.GetPreamble());
        Assert.Throws<DecoderFallbackException>(() => encoding.GetString(new byte[] { 0xff }));
    }
}
