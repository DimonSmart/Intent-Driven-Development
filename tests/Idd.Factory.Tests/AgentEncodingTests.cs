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

    private static void AssertStrictUtf8(Encoding? encoding)
    {
        Assert.NotNull(encoding);
        Assert.Equal(Encoding.UTF8.WebName, encoding.WebName);
        Assert.Empty(encoding.GetPreamble());
        Assert.Throws<DecoderFallbackException>(() => encoding.GetString(new byte[] { 0xff }));
    }
}
