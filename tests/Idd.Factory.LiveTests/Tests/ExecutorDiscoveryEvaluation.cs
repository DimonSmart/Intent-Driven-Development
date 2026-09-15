using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.LiveTests.Infrastructure;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

internal static class ExecutorDiscoveryEvaluation
{
    private const string IgnoredNoiseMarker = "IDD_IGNORED_DISCOVERY_NOISE_";
    private const string UntrackedProbeRelativePath = "src/MiniCatalog/RepositoryDiscoveryProbe.md";
    private const int IgnoredNoiseFileCount = 600;
    private const int MaximumGitDiscoveryOutputChars = 12_000;

    public static async Task PrepareAsync(ProcessRunner runner, LiveTestWorkspace workspace)
    {
        var ignoredRoot = Path.Combine(
            workspace.WorkspaceDirectory,
            "src",
            "MiniCatalog",
            "obj",
            "idd-discovery-noise");

        for (var index = 0; index < IgnoredNoiseFileCount; index++)
        {
            var directory = Path.Combine(ignoredRoot, $"segment-{index / 50:D2}");
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, $"{IgnoredNoiseMarker}{index:D4}.tmp"),
                $"ignored benchmark noise {index:D4}{Environment.NewLine}");
        }

        var untrackedProbe = Path.Combine(
            workspace.WorkspaceDirectory,
            UntrackedProbeRelativePath.Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(
            untrackedProbe,
            "# Repository discovery probe\n\nThis file is intentionally untracked and non-ignored. It is benchmark data, not product intent.\n");

        var visible = await RunGitAsync(
            runner,
            workspace,
            ["ls-files", "--cached", "--others", "--exclude-standard", "--", "src/MiniCatalog"],
            "discovery-fixture-git-visible");
        var visibleOutput = File.ReadAllText(visible.StdoutPath).Replace('\\', '/');
        Assert.Contains(UntrackedProbeRelativePath, visibleOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(IgnoredNoiseMarker, visibleOutput, StringComparison.Ordinal);

        var ignoredProbe = Path.GetRelativePath(
                workspace.WorkspaceDirectory,
                Directory.GetFiles(ignoredRoot, $"{IgnoredNoiseMarker}*.tmp", SearchOption.AllDirectories)[0])
            .Replace('\\', '/');
        await RunGitAsync(
            runner,
            workspace,
            ["check-ignore", "-q", "--", ignoredProbe],
            "discovery-fixture-check-ignore");
    }

    public static void AssertEfficientDiscovery(string factoryResultPath, LiveTestWorkspace workspace)
    {
        var resultDirectory = Path.GetDirectoryName(factoryResultPath)
            ?? throw new InvalidOperationException("Factory result directory could not be resolved.");
        var commands = ReadExecutorCommands(resultDirectory);
        var gitDiscovery = commands.Where(command => IsGitVisibleDiscovery(command.Command)).ToArray();
        var fixtureVisibleOutputPath = Path.Combine(workspace.VerificationDirectory, "discovery-fixture-git-visible.log");
        var fixtureVisibleOutput = File.Exists(fixtureVisibleOutputPath)
            ? File.ReadAllText(fixtureVisibleOutputPath).Replace('\\', '/')
            : string.Empty;

        var evidence = new
        {
            ignoredNoiseFileCount = IgnoredNoiseFileCount,
            ignoredNoiseMarker = IgnoredNoiseMarker,
            untrackedProbe = UntrackedProbeRelativePath,
            untrackedProbeVisibleBeforeExecution = fixtureVisibleOutput.Contains(UntrackedProbeRelativePath, StringComparison.Ordinal),
            executorCommandCount = commands.Count,
            totalExecutorToolOutputChars = commands.Sum(command => command.Output.Length),
            gitVisibleDiscoveryCommandCount = gitDiscovery.Length,
            gitVisibleDiscoveryOutputChars = gitDiscovery.Sum(command => command.Output.Length),
            maxGitVisibleDiscoveryOutputChars = gitDiscovery.Length == 0 ? 0 : gitDiscovery.Max(command => command.Output.Length),
            commands = commands.Select(command => new
            {
                command.AttemptId,
                command.Command,
                command.ExitCode,
                outputChars = command.Output.Length,
                outputPreview = Preview(command.Output)
            }).ToArray()
        };
        File.WriteAllText(
            Path.Combine(workspace.RunDirectory, "executor-discovery-evidence.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));

        Assert.NotEmpty(gitDiscovery);
        Assert.Contains(gitDiscovery, command => IsScopedGitVisibleDiscovery(command.Command));
        Assert.DoesNotContain(commands, command => IsBroadPhysicalDiscovery(command.Command));
        Assert.DoesNotContain(commands, command => UsesIgnoreBypass(command.Command));
        Assert.DoesNotContain(commands, command => command.Output.Contains(IgnoredNoiseMarker, StringComparison.Ordinal));
        Assert.True(
            gitDiscovery.All(command => command.Output.Length <= MaximumGitDiscoveryOutputChars),
            $"Git-visible discovery produced more than {MaximumGitDiscoveryOutputChars:N0} characters in a single command. See executor-discovery-evidence.json.");
        Assert.Contains(UntrackedProbeRelativePath, fixtureVisibleOutput, StringComparison.Ordinal);
    }

    private static List<CommandEvidence> ReadExecutorCommands(string resultDirectory)
    {
        var attemptsDirectory = Path.Combine(resultDirectory, "attempts");
        var result = new List<CommandEvidence>();

        foreach (var invocationPath in Directory.GetFiles(attemptsDirectory, "invocation.json", SearchOption.AllDirectories))
        {
            var invocation = JsonSerializer.Deserialize<AgentInvocation>(
                                 File.ReadAllText(invocationPath),
                                 FactoryJson.Options)
                             ?? throw new InvalidDataException($"Invalid invocation artifact: {invocationPath}");
            if (invocation.Capability != "implementation") continue;

            var attemptDirectory = Path.GetDirectoryName(invocationPath)!;
            var stdoutPath = Path.Combine(attemptDirectory, "stdout.log");
            if (!File.Exists(stdoutPath)) continue;

            foreach (var line in File.ReadLines(stdoutPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(line);
                }
                catch (JsonException)
                {
                    continue;
                }

                using (document)
                {
                    var root = document.RootElement;
                    if (String(root, "type") != "item.completed" ||
                        !root.TryGetProperty("item", out var item) ||
                        String(item, "type") != "command_execution")
                    {
                        continue;
                    }

                    result.Add(new(
                        Path.GetFileName(attemptDirectory),
                        String(item, "command"),
                        Integer(item, "exit_code"),
                        String(item, "aggregated_output")));
                }
            }
        }

        return result;
    }

    private static bool IsGitVisibleDiscovery(string command) =>
        command.Contains("ls-files", StringComparison.OrdinalIgnoreCase) &&
        command.Contains("--cached", StringComparison.OrdinalIgnoreCase) &&
        command.Contains("--others", StringComparison.OrdinalIgnoreCase) &&
        command.Contains("--exclude-standard", StringComparison.OrdinalIgnoreCase);

    private static bool IsScopedGitVisibleDiscovery(string command)
    {
        if (!IsGitVisibleDiscovery(command)) return false;
        var normalized = command.Replace('\\', '/');
        var markerIndex = normalized.IndexOf("--exclude-standard", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0) return false;
        var tail = normalized[(markerIndex + "--exclude-standard".Length)..];
        return tail.Contains(" src", StringComparison.OrdinalIgnoreCase) ||
               tail.Contains(" tests", StringComparison.OrdinalIgnoreCase) ||
               tail.Contains(" .idd", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBroadPhysicalDiscovery(string command) =>
        (command.Contains("Get-ChildItem", StringComparison.OrdinalIgnoreCase) &&
         command.Contains("-Recurse", StringComparison.OrdinalIgnoreCase)) ||
        command.Contains("find .", StringComparison.OrdinalIgnoreCase) ||
        command.Contains("dir /s", StringComparison.OrdinalIgnoreCase) ||
        command.Contains("ls -R", StringComparison.OrdinalIgnoreCase);

    private static bool UsesIgnoreBypass(string command) =>
        command.Contains("--no-ignore", StringComparison.OrdinalIgnoreCase) ||
        command.Contains(" -uuu", StringComparison.OrdinalIgnoreCase);

    private static string String(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int? Integer(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var result)
            ? result
            : null;

    private static string Preview(string output)
    {
        const int maxLength = 500;
        var normalized = output.Replace("\r\n", "\n");
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "...";
    }

    private static async Task<ProcessResult> RunGitAsync(
        ProcessRunner runner,
        LiveTestWorkspace workspace,
        IReadOnlyList<string> arguments,
        string name)
    {
        var result = await runner.RunAsync(
            "git",
            arguments,
            workspace.WorkspaceDirectory,
            Path.Combine(workspace.VerificationDirectory, name + ".log"),
            Path.Combine(workspace.VerificationDirectory, name + ".stderr.log"),
            TimeSpan.FromMinutes(1),
            CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
        return result;
    }

    private sealed record CommandEvidence(string AttemptId, string Command, int? ExitCode, string Output);
}
