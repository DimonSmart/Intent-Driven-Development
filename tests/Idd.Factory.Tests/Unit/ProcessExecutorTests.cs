using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Idd.Factory.Processes;

namespace Idd.Factory.Tests;

public sealed class ProcessExecutorTests : IDisposable
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"idd-process-tests-{Guid.NewGuid():N}");

    public ProcessExecutorTests()
    {
        Directory.CreateDirectory(directory);
    }

    [Fact]
    public async Task RunTransfersArgumentsWorkingDirectoryEnvironmentStdinAndUnicode()
    {
        var working = Path.Combine(directory, "working directory");
        Directory.CreateDirectory(working);
        var arguments = new[] { "a b", "\"quoted\"", "semi;colon", "русский" };
        const string input = "stdin → Привет";
        var result = await ProcessExecutor.Shared.RunAsync(
            HelperRequest(["echo", .. arguments], working) with
            {
                EnvironmentOverrides = new Dictionary<string, string?>
                {
                    ["IDD_PROCESS_TEST_VALUE"] = "env → значение"
                },
                StandardInput = input,
                StandardInputEncoding = StrictUtf8,
                StandardOutputEncoding = StrictUtf8,
                StandardErrorEncoding = StrictUtf8
            },
            default);

        Assert.Equal(ProcessCompletionReason.Exited, result.CompletionReason);
        Assert.Equal(0, result.ExitCode);
        var payload = JsonSerializer.Deserialize<EchoPayload>(result.StandardOutput)!;
        Assert.Equal(arguments, payload.Arguments);
        Assert.Equal(Path.GetFullPath(working), Path.GetFullPath(payload.WorkingDirectory));
        Assert.Equal("env → значение", payload.EnvironmentValue);
        Assert.Equal(input, payload.StandardInput);
    }

    [Fact]
    public async Task ConfiguredUtf8PreservesStdoutAndStderr()
    {
        var result = await ProcessExecutor.Shared.RunAsync(
            HelperRequest(["unicode"]) with
            {
                StandardOutputEncoding = StrictUtf8,
                StandardErrorEncoding = StrictUtf8
            },
            default);

        Assert.Equal("Устранена → готово\n", result.StandardOutput);
        Assert.Equal("Ошибка → stderr\n", result.StandardError);
    }

    [Fact]
    public async Task LargeConcurrentStdoutAndStderrDoNotDeadlock()
    {
        var result = await ProcessExecutor.Shared.RunAsync(
            HelperRequest(["both", "128"]) with
            {
                Timeout = TimeSpan.FromSeconds(20)
            },
            default);

        Assert.Equal(ProcessCompletionReason.Exited, result.CompletionReason);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(128 * 4096, result.StandardOutput.Length);
        Assert.Equal(128 * 4096, result.StandardError.Length);
    }

    [Fact]
    public async Task CaptureFileLineAndChunkObserversCanRunTogether()
    {
        var path = Path.Combine(directory, "stdout.log");
        var lines = new List<string>();
        var observedCharacters = 0;
        var result = await ProcessExecutor.Shared.RunAsync(
            HelperRequest(["lines", "3"]) with
            {
                StandardOutputEncoding = StrictUtf8,
                StandardOutput = new()
                {
                    Capture = true,
                    FilePath = path,
                    FileEncoding = StrictUtf8,
                    LineObserver = lines.Add,
                    ChunkObserver = (chunk, _) =>
                    {
                        observedCharacters += chunk.Length;
                        return ValueTask.CompletedTask;
                    }
                }
            },
            default);

        Assert.Equal(ProcessCompletionReason.Exited, result.CompletionReason);
        Assert.Equal(new[] { "line-0", "line-1", "line-2" }, lines);
        Assert.Equal(result.StandardOutput.Length, observedCharacters);
        Assert.Equal(result.StandardOutput, await File.ReadAllTextAsync(path, StrictUtf8));
    }

    [Fact]
    public async Task OutputFileFailureIsReturnedAsDiagnostic()
    {
        var result = await ProcessExecutor.Shared.RunAsync(
            HelperRequest(["lines", "1"]) with
            {
                StandardOutput = new()
                {
                    Capture = true,
                    FilePath = directory
                }
            },
            default);

        Assert.Equal(ProcessCompletionReason.Exited, result.CompletionReason);
        Assert.Contains("line-0", result.StandardOutput);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Kind == ProcessExecutionDiagnosticKinds.OutputFileFailure
                && diagnostic.Stream == "stdout");
    }

    [Fact]
    public async Task MissingExecutableReturnsStartFailure()
    {
        var result = await ProcessExecutor.Shared.RunAsync(
            new(
                $"idd-missing-{Guid.NewGuid():N}",
                [],
                directory),
            default);

        Assert.Equal(ProcessCompletionReason.StartFailed, result.CompletionReason);
        Assert.Null(result.ProcessId);
        Assert.NotNull(result.Failure);
        Assert.False(result.Termination.Requested);
    }

    [Fact]
    public async Task TimeoutTerminatesProcessTreeAndKeepsTechnicalResult()
    {
        var childPidPath = Path.Combine(directory, "timeout-child.pid");
        var result = await ProcessExecutor.Shared.RunAsync(
            HelperRequest(["spawn-child", childPidPath, "30000"]) with
            {
                Timeout = TimeSpan.FromSeconds(2)
            },
            default);

        Assert.Equal(ProcessCompletionReason.TimedOut, result.CompletionReason);
        Assert.NotNull(result.ProcessId);
        Assert.True(result.Termination.Requested);
        Assert.True(result.Termination.Succeeded);
        Assert.True(File.Exists(childPidPath));
        await AssertStopsAsync(int.Parse(await File.ReadAllTextAsync(childPidPath)));
        await AssertStopsAsync(result.ProcessId!.Value);
    }

    [Fact]
    public async Task CancellationTerminatesProcessTreeAndKeepsTechnicalResult()
    {
        var childPidPath = Path.Combine(directory, "cancel-child.pid");
        using var cancellation = new CancellationTokenSource();
        var run = ProcessExecutor.Shared.RunAsync(
            HelperRequest(["spawn-child", childPidPath, "30000"]),
            cancellation.Token);

        for (var attempt = 0; attempt < 200 && !File.Exists(childPidPath); attempt++)
            await Task.Delay(25);
        Assert.True(File.Exists(childPidPath));
        cancellation.Cancel();
        var result = await run;

        Assert.Equal(ProcessCompletionReason.Cancelled, result.CompletionReason);
        Assert.NotNull(result.ProcessId);
        Assert.True(result.Termination.Requested);
        Assert.True(result.Termination.Succeeded);
        await AssertStopsAsync(int.Parse(await File.ReadAllTextAsync(childPidPath)));
        await AssertStopsAsync(result.ProcessId!.Value);
    }

    [Fact]
    public async Task RepeatedRacingTerminationIsSafe()
    {
        await using var process = await ProcessExecutor.Shared.StartAsync(
            HelperRequest(["sleep", "30000"]),
            default);

        var results = await Task.WhenAll(
            process.TerminateAsync(),
            process.TerminateAsync(),
            process.TerminateAsync());
        var completed = await process.CompleteAsync();

        Assert.All(results, termination => Assert.True(termination.Succeeded));
        Assert.True(completed.Termination.Requested);
        await AssertStopsAsync(process.ProcessId);
    }

    [Fact]
    public async Task DisposingManagedHandleDoesNotLeaveProcessRunning()
    {
        var process = await ProcessExecutor.Shared.StartAsync(
            HelperRequest(["sleep", "30000"]),
            default);
        var processId = process.ProcessId;

        await process.DisposeAsync();

        await AssertStopsAsync(processId);
    }

    [Fact]
    public async Task DrainAfterNormalExitIsBoundedWhenDescendantKeepsPipeOpen()
    {
        var result = await ProcessExecutor.Shared.RunAsync(
            HelperRequest(["hold-pipe", "1500"]) with
            {
                OutputDrainTimeout = TimeSpan.FromMilliseconds(150)
            },
            default);

        Assert.Equal(ProcessCompletionReason.Exited, result.CompletionReason);
        Assert.Contains("parent-exited", result.StandardOutput);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Kind == ProcessExecutionDiagnosticKinds.DrainTimeout);
    }

    private ProcessExecutionRequest HelperRequest(
        IReadOnlyList<string> helperArguments,
        string? workingDirectory = null) =>
        new(
            "dotnet",
            [HelperDll(), .. helperArguments],
            workingDirectory ?? directory)
        {
            StandardOutputEncoding = StrictUtf8,
            StandardErrorEncoding = StrictUtf8
        };

    private static string HelperDll()
    {
        var root = RepositoryRoot();
        var baseDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = baseDirectory.Parent?.Name ?? "Debug";
        var path = Path.Combine(
            root,
            "tests",
            "Idd.Factory.ProcessTestHelper",
            "bin",
            configuration,
            "net10.0",
            "Idd.Factory.ProcessTestHelper.dll");
        Assert.True(File.Exists(path), $"Process test helper was not built: {path}");
        return path;
    }

    private static async Task AssertStopsAsync(int processId)
    {
        for (var attempt = 0; attempt < 100 && IsRunning(processId); attempt++)
            await Task.Delay(25);
        Assert.False(IsRunning(processId), $"Process {processId} is still running.");
    }

    private static bool IsRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
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

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record EchoPayload(
        string[] Arguments,
        string WorkingDirectory,
        string? EnvironmentValue,
        string StandardInput);
}
