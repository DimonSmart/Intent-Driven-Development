using System.Text;
using Idd.Factory.Processes;

namespace Idd.Factory.Tests;

public sealed class ProcessExecutorEdgeTests
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    [Fact]
    public async Task TerminatingAlreadyExitedManagedProcessIsSafe()
    {
        await using var process = await ProcessExecutor.Shared.StartAsync(
            HelperRequest(["exit", "0"]),
            default);
        Assert.True(await process.WaitForExitAsync(TimeSpan.FromSeconds(10), default));

        var termination = await process.TerminateAsync();
        var result = await process.CompleteAsync();

        Assert.True(termination.Requested);
        Assert.True(termination.Succeeded);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task ObserverFailureDoesNotBlockProcessAndIsReported()
    {
        var result = await ProcessExecutor.Shared.RunAsync(
            HelperRequest(["lines", "3"]) with
            {
                StandardOutput = new()
                {
                    Capture = true,
                    LineObserver = _ => throw new InvalidOperationException("observer failed")
                }
            },
            default);

        Assert.Equal(ProcessCompletionReason.Exited, result.CompletionReason);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("line-2", result.StandardOutput);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Kind == ProcessExecutionDiagnosticKinds.OutputObserverFailure);
    }

    [Fact]
    public async Task NormalExitCompletesOutputDrainWithoutTermination()
    {
        var result = await ProcessExecutor.Shared.RunAsync(
            HelperRequest(["lines", "100"]),
            default);

        Assert.Equal(ProcessCompletionReason.Exited, result.CompletionReason);
        Assert.False(result.Termination.Requested);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Kind == ProcessExecutionDiagnosticKinds.DrainTimeout);
        Assert.Contains("line-99", result.StandardOutput);
    }

    private static ProcessExecutionRequest HelperRequest(IReadOnlyList<string> helperArguments) =>
        new(
            "dotnet",
            [HelperDll(), .. helperArguments],
            RepositoryRoot())
        {
            StandardOutputEncoding = StrictUtf8,
            StandardErrorEncoding = StrictUtf8
        };

    private static string HelperDll()
    {
        var root = RepositoryRoot();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
        return Path.Combine(
            root,
            "tests",
            "Idd.Factory.ProcessTestHelper",
            "bin",
            configuration,
            "net10.0",
            "Idd.Factory.ProcessTestHelper.dll");
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Intent-Driven-Development.slnx")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
