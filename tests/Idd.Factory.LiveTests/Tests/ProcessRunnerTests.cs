using System.Diagnostics;
using Idd.Factory.LiveTests.Infrastructure;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_ReturnsPromptlyAfterTimeout()
    {
        var directory = Path.Combine(Path.GetTempPath(), "idd-process-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await new ProcessRunner().RunAsync(
                "cmd",
                ["/c", "ping -n 30 127.0.0.1 > nul"],
                directory,
                Path.Combine(directory, "stdout.log"),
                Path.Combine(directory, "stderr.log"),
                TimeSpan.FromMilliseconds(100),
                CancellationToken.None);

            Assert.True(result.TimedOut);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
