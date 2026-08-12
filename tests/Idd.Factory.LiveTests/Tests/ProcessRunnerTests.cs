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
                "pwsh",
                ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30"],
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

    [Fact]
    public async Task RunAsync_PropagatesCallerCancellation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "idd-process-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ProcessRunner().RunAsync(
                "pwsh",
                ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30"],
                directory,
                Path.Combine(directory, "stdout.log"),
                Path.Combine(directory, "stderr.log"),
                TimeSpan.FromSeconds(30),
                cancellation.Token));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ReturnsAfterStableCompletionSignal()
    {
        var directory = Path.Combine(Path.GetTempPath(), "idd-process-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var signal = Path.Combine(directory, "last-message.json");
            var stopwatch = Stopwatch.StartNew();
            var result = await new ProcessRunner().RunAsync(
                "pwsh",
                ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", $"Set-Content -LiteralPath '{signal}' -Value '{{}}'; Start-Sleep -Seconds 30"],
                directory,
                Path.Combine(directory, "stdout.log"),
                Path.Combine(directory, "stderr.log"),
                TimeSpan.FromSeconds(30),
                CancellationToken.None,
                completionSignalPath: signal);

            Assert.True(result.CompletionSignaled);
            Assert.False(result.TimedOut);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
